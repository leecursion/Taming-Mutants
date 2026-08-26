using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// OpenAI 음성 인식(Whisper)을 쓰는 <see cref="SpeechToTextBackend"/> 구현.
///
/// 흐름: 마이크 녹음 → 실제 녹음된 만큼만 잘라내기 → WAV로 인코딩 →
/// <c>POST /v1/audio/transcriptions</c>에 multipart로 업로드 → 받은 글을 이벤트로 흘린다.
///
/// <b>키를 빌드에 넣지 마세요.</b> <see cref="SolarChatClient"/>와 같은 이유입니다 —
/// 인스펙터에 적은 값은 씬 파일에 그대로 저장되고 빌드에도 실려 나갑니다.
/// 개발 중에는 환경변수를 쓰고, 배포할 때는 자체 프록시로 갈아끼우세요.
/// </summary>
public class OpenAiWhisperClient : SpeechToTextBackend
{
    [Header("엔드포인트")]
    public string endpoint = "https://api.openai.com/v1/audio/transcriptions";
    [Tooltip("whisper-1이 가장 널리 열려 있습니다. 계정에서 쓸 수 있다면 gpt-4o-transcribe 계열이 더 정확합니다.")]
    public string model = "whisper-1";
    [Tooltip("인식할 언어(ISO-639-1). 비워두면 모델이 알아서 판별하지만, 정해주면 짧은 말도 덜 틀립니다.")]
    public string language = "ko";

    [Header("인증")]
    [Tooltip("개발용으로만 채우세요. 여기 넣은 값은 씬 파일에 그대로 저장되고 빌드에도 포함됩니다.")]
    public string apiKey = "";
    [Tooltip("위 칸이 비어 있으면 이 환경변수에서 키를 읽습니다.")]
    public string apiKeyEnvironmentVariable = "OPENAI_API_KEY";

    [Header("녹음")]
    [Tooltip("비워두면 기본 마이크를 씁니다.")]
    public string microphoneDevice = "";
    [Tooltip("녹음 표본율(Hz). Whisper는 어차피 16kHz로 낮추므로 더 올려도 업로드만 커집니다.")]
    public int sampleRate = 16000;
    [Tooltip("한 번에 녹음할 수 있는 최대 길이(초). 이 시간이 지나면 자동으로 끊고 전송합니다.")]
    public int maxRecordSeconds = 20;
    [Tooltip("이보다 짧게 녹음하면 잘못 눌렀다고 보고 전송하지 않습니다(초).")]
    public float minRecordSeconds = 0.4f;
    [Tooltip("녹음 내내 이 크기를 한 번도 넘지 않으면 소리가 안 들어온 것으로 보고 전송하지 않습니다. " +
             "마이크 음소거·장치 오선택을 요금 쓰기 전에 걸러냅니다. 0으로 두면 검사하지 않습니다.")]
    [Range(0f, 0.2f)] public float silenceThreshold = 0.015f;

    [Header("네트워크")]
    public int timeoutSeconds = 30;

    [Header("디버그")]
    [Tooltip("켜면 키가 있어도 호출하지 않습니다.")]
    public bool forceOfflineMode;
    public bool logTraffic;

    /// <summary>마이크가 하나도 없으면 키가 있어도 쓸 수 없다.</summary>
    public override bool IsConfigured =>
        !forceOfflineMode
        && !string.IsNullOrWhiteSpace(endpoint)
        && !string.IsNullOrWhiteSpace(ResolveApiKey())
        && Microphone.devices != null && Microphone.devices.Length > 0;

    /// <summary>녹음이 시작된 시각. 길이를 재고 최대 시간을 넘겼는지 보는 데 쓴다.</summary>
    public float ElapsedRecordSeconds => IsListening ? Time.time - _recordStartedAt : 0f;

    /// <summary>실제로 잡힌 마이크 이름. 비워두고 시작하면 OS 기본 장치가 잡힌다.</summary>
    public override string ActiveDeviceName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_activeDevice)) return _activeDevice;
            var devices = Microphone.devices;
            return devices != null && devices.Length > 0 ? devices[0] : null;
        }
    }

    private const int LevelWindowSamples = 512;

    private AudioClip _clip;
    private float _recordStartedAt;
    private string _activeDevice;
    private readonly float[] _levelBuffer = new float[LevelWindowSamples];

    private void Awake()
    {
        if (!Application.isEditor && !string.IsNullOrWhiteSpace(apiKey))
        {
            Debug.LogWarning("[OpenAiWhisperClient] API 키가 빌드에 포함된 채 실행 중입니다. " +
                             "배포본에서는 자체 프록시로 교체하세요.", this);
        }
    }

    private void Update()
    {
        if (IsListening) UpdateInputLevel();

        // 최대 길이를 넘기면 스스로 끊는다. 버튼에서 손을 떼는 걸 잊어도
        // 마이크가 무한정 돌지 않게 하는 안전장치다.
        if (IsListening && ElapsedRecordSeconds >= maxRecordSeconds) StopListening();
    }

    /// <summary>
    /// 방금 녹음된 짧은 구간을 읽어 소리 크기를 잰다.
    ///
    /// 클립 전체를 매 프레임 읽으면 20초치 배열을 60번씩 복사하게 되므로,
    /// 녹음 헤드 바로 뒤의 한 창(약 512샘플 = 32ms)만 본다.
    /// </summary>
    private void UpdateInputLevel()
    {
        if (_clip == null)
        {
            InputLevel = 0f;
            return;
        }

        int position = Microphone.GetPosition(_activeDevice);
        int start = position - LevelWindowSamples;

        // 녹음 시작 직후에는 창을 채울 만큼 쌓이지 않았다.
        if (start < 0)
        {
            InputLevel = 0f;
            return;
        }

        _clip.GetData(_levelBuffer, start);

        float peak = 0f;
        foreach (float sample in _levelBuffer)
        {
            float magnitude = Mathf.Abs(sample);
            if (magnitude > peak) peak = magnitude;
        }

        // 표시가 튀지 않게 살짝 눕힌다. 올라갈 때는 즉시 따라가야 말하는 순간이 바로 보인다.
        InputLevel = peak > InputLevel ? peak : Mathf.Lerp(InputLevel, peak, Time.deltaTime * 8f);
        if (peak > PeakLevel) PeakLevel = peak;
    }

    private void OnDisable()
    {
        if (IsListening) Cancel();
    }

    public string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKeyEnvironmentVariable)) return null;

        try
        {
            return Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public override void StartListening()
    {
        if (IsListening) return;

        if (!RequestMicrophonePermission())
        {
            RaiseError("마이크 권한이 없습니다.");
            return;
        }

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            RaiseError("마이크를 찾지 못했습니다.");
            return;
        }

        _activeDevice = string.IsNullOrWhiteSpace(microphoneDevice) ? null : microphoneDevice;
        _clip = Microphone.Start(_activeDevice, false, Mathf.Max(maxRecordSeconds, 1), sampleRate);

        if (_clip == null)
        {
            RaiseError("마이크 녹음을 시작하지 못했습니다.");
            return;
        }

        _recordStartedAt = Time.time;
        InputLevel = 0f;
        PeakLevel = 0f;
        IsListening = true;

        if (logTraffic)
            Debug.Log($"[OpenAiWhisperClient] 녹음 시작 — 장치: {ActiveDeviceName ?? "(기본)"}", this);
    }

    public override void StopListening()
    {
        if (!IsListening) return;

        // 길이를 먼저 재둔다. EndRecording이 IsListening을 내리는데,
        // ElapsedRecordSeconds는 녹음 중이 아니면 0을 돌려주기 때문이다.
        // 순서를 바꾸면 매번 0초로 읽혀 "너무 짧음"에 걸리고 전송이 아예 일어나지 않는다.
        float elapsed = ElapsedRecordSeconds;

        float[] samples = EndRecording(out int channels);
        if (samples == null) return;

        float peak = PeakLevel;
        InputLevel = 0f;

        if (elapsed < minRecordSeconds || samples.Length == 0)
        {
            // 잘못 눌렀다고 보고 조용히 버린다. 오류로 처리하면 실수할 때마다 경고가 뜬다.
            if (logTraffic) Debug.Log("[OpenAiWhisperClient] 녹음이 너무 짧아 전송하지 않습니다.", this);
            return;
        }

        // 통째로 조용했으면 올리지 않는다. 올려봐야 빈 결과가 돌아오는데, 그때는
        // "인식된 말이 없다"고만 알게 돼 마이크 문제인지 발음 문제인지 구분할 수 없다.
        if (silenceThreshold > 0f && peak < silenceThreshold)
        {
            string device = ActiveDeviceName ?? "(기본 장치)";
            string reason = $"마이크로 소리가 들어오지 않았습니다 (최대 입력 {peak:0.000}). " +
                            $"잡힌 장치: {device}. 마이크 음소거와 Windows 마이크 권한을 확인하세요.";

            Debug.LogWarning($"[OpenAiWhisperClient] {reason}", this);
            RaiseError(reason);
            return;
        }

        if (logTraffic)
            Debug.Log($"[OpenAiWhisperClient] {elapsed:0.0}초 녹음, 최대 입력 {peak:0.000}", this);

        byte[] wav = WavCodec.Encode(samples, channels, sampleRate);
        if (wav == null)
        {
            RaiseError("녹음을 WAV로 만들지 못했습니다.");
            return;
        }

        StartCoroutine(TranscribeRoutine(wav));
    }

    public override void Cancel()
    {
        if (!IsListening) return;

        EndRecording(out _);
        InputLevel = 0f;
    }

    /// <summary>
    /// 녹음을 멈추고 실제로 채워진 만큼만 잘라 돌려준다.
    ///
    /// <see cref="Microphone.Start"/>가 만든 클립은 maxRecordSeconds 길이로 통째로 잡혀 있고
    /// 나머지는 무음이다. 그대로 올리면 20초짜리 파일을 매번 보내게 되고, 뒤의 긴 침묵 때문에
    /// 인식 품질도 떨어진다. <see cref="Microphone.GetPosition"/>이 어디까지 찼는지 알려준다.
    /// </summary>
    private float[] EndRecording(out int channels)
    {
        channels = _clip != null ? _clip.channels : 1;

        int position = Microphone.GetPosition(_activeDevice);
        Microphone.End(_activeDevice);
        IsListening = false;

        if (_clip == null) return null;

        AudioClip clip = _clip;
        _clip = null;

        if (position <= 0)
        {
            Destroy(clip);
            return null;
        }

        var samples = new float[position * clip.channels];
        clip.GetData(samples, 0);
        Destroy(clip);

        return samples;
    }

    private IEnumerator TranscribeRoutine(byte[] wav)
    {
        IsTranscribing = true;
        try
        {
            List<IMultipartFormSection> form = BuildForm(wav);

            // 경계 문자열을 직접 만들어 넘긴다. UnityWebRequest.Post의 자동 생성본을
            // 그대로 쓰면 Content-Type 헤더를 우리가 덮어쓸 때 경계가 어긋나는 경우가 있다.
            byte[] boundary = UnityWebRequest.GenerateBoundary();

            using (UnityWebRequest request = UnityWebRequest.Post(endpoint, form, boundary))
            {
                request.SetRequestHeader("Authorization", "Bearer " + ResolveApiKey());
                request.timeout = Mathf.Max(timeoutSeconds, 1);

                if (logTraffic) Debug.Log($"[OpenAiWhisperClient] 업로드 {wav.Length / 1024}KB", this);

                yield return request.SendWebRequest();

                string body = request.downloadHandler != null ? request.downloadHandler.text : null;
                if (logTraffic) Debug.Log($"[OpenAiWhisperClient] 응답 ({request.responseCode})\n{body}", this);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string reason = $"HTTP {request.responseCode} — {ExtractError(body) ?? request.error}";
                    Debug.LogWarning($"[OpenAiWhisperClient] 음성 인식 실패: {reason}", this);
                    RaiseError(reason);
                    yield break;
                }

                string text = ParseText(body, out string parseError);
                if (text == null)
                {
                    Debug.LogWarning($"[OpenAiWhisperClient] 응답 해석 실패: {parseError}", this);
                    RaiseError(parseError);
                    yield break;
                }

                RaiseTranscribed(text);
            }
        }
        finally
        {
            IsTranscribing = false;
        }
    }

    private List<IMultipartFormSection> BuildForm(byte[] wav)
    {
        var form = new List<IMultipartFormSection>
        {
            // 파일 이름의 확장자로 형식을 판별하므로 .wav를 반드시 붙인다.
            new MultipartFormFileSection("file", wav, "speech.wav", "audio/wav"),
            new MultipartFormDataSection("model", model),
            new MultipartFormDataSection("response_format", "json"),
        };

        if (!string.IsNullOrWhiteSpace(language))
            form.Add(new MultipartFormDataSection("language", language.Trim()));

        return form;
    }

    // --- 응답 읽기 ---

    [Serializable]
    private class TranscriptionResponse
    {
        public string text;
        public ApiError error;
    }

    [Serializable]
    private class ApiError
    {
        public string message;
        public string type;
        public string code;
    }

    private static string ParseText(string json, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "응답 본문이 비어 있습니다.";
            return null;
        }

        TranscriptionResponse response;
        try
        {
            response = JsonUtility.FromJson<TranscriptionResponse>(json);
        }
        catch (Exception e)
        {
            error = $"JSON 형식이 올바르지 않습니다 ({e.Message})";
            return null;
        }

        if (response == null || string.IsNullOrWhiteSpace(response.text))
        {
            // 말을 안 하고 버튼만 눌렀다 뗀 경우도 여기로 온다.
            error = "인식된 말이 없습니다.";
            return null;
        }

        return response.text;
    }

    private static string ExtractError(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            TranscriptionResponse response = JsonUtility.FromJson<TranscriptionResponse>(json);
            if (response?.error != null && !string.IsNullOrWhiteSpace(response.error.message))
                return response.error.message;
        }
        catch (Exception)
        {
            // 오류 본문이 JSON이 아닐 수도 있다.
        }

        return json.Length > 200 ? json.Substring(0, 200) : json;
    }

    /// <summary>Android(Quest)는 실행 중에 마이크 권한을 따로 받아야 한다.</summary>
    private static bool RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Microphone)) return true;

        Permission.RequestUserPermission(Permission.Microphone);
        // 권한 창은 비동기라 이번 시도는 실패한다. 사용자가 허용하면 다음 누름부터 된다.
        return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
        return true;
#endif
    }
}
