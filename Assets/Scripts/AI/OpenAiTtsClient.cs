using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// OpenAI가 제공하는 목소리. 인스펙터에서 오타 없이 고르라고 열거형으로 둔다.
///
/// 교육용 비서로는 Coral·Shimmer·Sage가 부드럽게 들리고, Alloy는 가장 중립적이라
/// 설명체로 읽으면 딱딱하게 느껴진다. Fable·Ballad는 이야기하듯 읽어 도입 시나리오에 어울린다.
/// </summary>
public enum OpenAiVoice
{
    Alloy,
    Ash,
    Ballad,
    Coral,
    Echo,
    Fable,
    Nova,
    Onyx,
    Sage,
    Shimmer,
    Verse,
}

/// <summary>
/// OpenAI 음성 합성을 쓰는 <see cref="TextToSpeechBackend"/> 구현.
///
/// <c>POST /v1/audio/speech</c>에 문장을 보내면 오디오 바이트가 돌아온다.
/// 형식을 wav로 요청해 <see cref="WavCodec"/>으로 직접 디코딩한다 — 이유는 그쪽 주석 참고.
///
/// 같은 문장을 다시 읽는 일이 잦으므로(단계 브리핑, 반복되는 안내 문구) 마지막 몇 개를
/// 캐시해 둔다. 같은 대사에 매번 돈과 대기 시간을 쓸 이유가 없다.
/// </summary>
public class OpenAiTtsClient : TextToSpeechBackend
{
    [Header("엔드포인트")]
    public string endpoint = "https://api.openai.com/v1/audio/speech";
    public string model = "gpt-4o-mini-tts";

    [Header("목소리")]
    [Tooltip("교육용으로는 Coral(따뜻함) / Shimmer(부드러움) / Sage(차분함)를 권합니다. " +
             "Alloy는 가장 중립적이라 딱딱하게 들리고, Onyx·Echo는 낮은 남성 목소리입니다.")]
    public OpenAiVoice voice = OpenAiVoice.Coral;
    [Tooltip("목록에 없는 목소리를 쓰려면 여기 이름을 적으세요. 비어 있으면 위 드롭다운을 씁니다.")]
    public string voiceOverride = "";

    [TextArea(3, 6)]
    [Tooltip("말투 지시. gpt-4o-mini-tts 계열이 지원하며, 목소리를 바꾸는 것보다 이쪽이 훨씬 크게 바뀝니다. " +
             "비워두면 보내지 않습니다(구형 tts-1 모델은 이 항목을 받지 않습니다).")]
    public string instructions =
        "중학생에게 과학을 설명해 주는, 친근하지만 정중한 연구원처럼 말하세요. " +
        "밝고 다정한 톤으로, 살짝 들뜬 호기심이 느껴지게 읽습니다. " +
        "또박또박 천천히 읽되 기계적으로 끊지 말고, 문장 끝을 부드럽게 내려주세요. " +
        "중요한 낱말은 살짝 힘주어 강조합니다. 뉴스 아나운서처럼 딱딱하게 읽지 마세요.";

    [Tooltip("읽는 속도. 1이 기본입니다. 한국어를 처음 듣는 중학생에게는 살짝 느린 편이 알아듣기 좋습니다.")]
    [Range(0.25f, 4f)] public float speed = 0.95f;

    [Header("인증")]
    [Tooltip("개발용으로만 채우세요. 여기 넣은 값은 씬 파일에 그대로 저장되고 빌드에도 포함됩니다.")]
    public string apiKey = "";
    [Tooltip("위 칸이 비어 있으면 이 환경변수에서 키를 읽습니다.")]
    public string apiKeyEnvironmentVariable = "OPENAI_API_KEY";

    [Header("재생")]
    [Tooltip("비워두면 이 오브젝트에 AudioSource를 만들어 씁니다.")]
    public AudioSource audioSource;
    [Range(0f, 1f)] public float volume = 1f;

    [Header("캐시")]
    [Tooltip("최근 읽은 문장을 몇 개까지 기억할지. 같은 대사를 다시 읽을 때 요청을 건너뜁니다.")]
    public int cacheSize = 24;

    [Header("네트워크")]
    public int timeoutSeconds = 30;
    [Tooltip("한 번에 보낼 수 있는 글자 수 상한. 넘으면 잘라서 보냅니다.")]
    public int maxCharacters = 400;

    [Header("디버그")]
    [Tooltip("켜면 키가 있어도 호출하지 않습니다. 음성 없이 진행을 확인할 때 씁니다.")]
    public bool forceOfflineMode;
    public bool logTraffic;

    public override bool IsConfigured =>
        !forceOfflineMode
        && !string.IsNullOrWhiteSpace(endpoint)
        && !string.IsNullOrWhiteSpace(ResolveApiKey());

    // 문장 → 이미 받아둔 소리. 순서를 알아야 오래된 것부터 버릴 수 있어 목록을 따로 둔다.
    private readonly System.Collections.Generic.Dictionary<string, AudioClip> _cache =
        new System.Collections.Generic.Dictionary<string, AudioClip>();
    private readonly System.Collections.Generic.List<string> _cacheOrder =
        new System.Collections.Generic.List<string>();

    private readonly System.Collections.Generic.HashSet<string> _prewarming =
        new System.Collections.Generic.HashSet<string>();

    private Coroutine _running;
    private Action _pendingComplete;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;

        // instructions는 gpt-4o 계열 음성 모델만 받는다. 구형 모델에 보내면 400으로 거절되거나
        // 조용히 무시돼, 말투를 아무리 고쳐도 결과가 그대로인 채로 헤매게 된다.
        if (!string.IsNullOrWhiteSpace(instructions) && !SupportsInstructions())
        {
            Debug.LogWarning($"[OpenAiTtsClient] 모델 '{model}'은 말투 지시(instructions)를 지원하지 않을 수 있습니다. " +
                             "말투가 반영되지 않으면 gpt-4o-mini-tts로 바꾸세요.", this);
        }
        // 비서 목소리는 방향감이 필요 없다. 3D로 두면 비서가 화면 가장자리로 갈 때 한쪽 귀에서만 들린다.
        audioSource.spatialBlend = 0f;

        if (!Application.isEditor && !string.IsNullOrWhiteSpace(apiKey))
        {
            Debug.LogWarning("[OpenAiTtsClient] API 키가 빌드에 포함된 채 실행 중입니다. " +
                             "배포본에서는 자체 프록시로 교체하세요.", this);
        }
    }

    /// <summary>말투 지시를 받는 모델인지. 이름으로만 판단하므로 새 모델이 나오면 여기 추가한다.</summary>
    private bool SupportsInstructions()
    {
        return !string.IsNullOrEmpty(model) && model.StartsWith("gpt-4o");
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

    public override void Speak(string text, Action onComplete = null)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsConfigured)
        {
            // 못 읽어도 콜백은 불러야 한다. 안 그러면 말풍선이 소리를 기다리다 멈춘다.
            onComplete?.Invoke();
            return;
        }

        Stop();

        string trimmed = text.Length > maxCharacters ? text.Substring(0, maxCharacters) : text;
        _pendingComplete = onComplete;
        IsSpeaking = true;
        _running = StartCoroutine(SpeakRoutine(trimmed));
    }

    public override void Stop()
    {
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
        }

        if (audioSource != null && audioSource.isPlaying) audioSource.Stop();

        // 중단된 재생의 콜백은 부르지 않는다. 부르면 말풍선이 "다 읽었다"고 보고 넘어간다.
        _pendingComplete = null;
        IsSpeaking = false;
    }

    /// <summary>
    /// 아직 캐시에 없으면 미리 합성해 담아둔다. 재생 중인 소리에는 손대지 않는다.
    ///
    /// <see cref="Speak"/>와 코루틴을 공유하지 않는 이유: Speak은 _running/_pendingComplete로
    /// "지금 읽는 문장"을 관리하는데, 미리 받기가 그 자리를 차지하면 재생 중인 소리가
    /// 중간에 끊기거나 콜백이 엉뚱한 시점에 불린다.
    /// </summary>
    public override void Prewarm(string text)
    {
        if (!IsConfigured || cacheSize <= 0 || string.IsNullOrWhiteSpace(text)) return;

        string trimmed = text.Length > maxCharacters ? text.Substring(0, maxCharacters) : text;
        if (GetCached(trimmed) != null) return;

        string key = CacheKey(trimmed);
        if (_prewarming.Contains(key)) return; // 같은 문장을 두 번 받지 않는다

        _prewarming.Add(key);
        StartCoroutine(PrewarmRoutine(trimmed, key));
    }

    private IEnumerator PrewarmRoutine(string text, string key)
    {
        AudioClip clip = null;
        yield return SynthesizeRoutine(text, result => clip = result);

        _prewarming.Remove(key);

        // 미리 받기가 실패해도 조용히 넘어간다. 실제로 읽을 때 다시 시도하고,
        // 그때 실패하면 그 경로에서 이미 경고를 남긴다.
        if (clip != null) StoreCached(text, clip);
    }

    private IEnumerator SpeakRoutine(string text)
    {
        AudioClip clip = GetCached(text);

        if (clip == null)
        {
            yield return SynthesizeRoutine(text, result => clip = result);

            if (clip == null)
            {
                // 합성에 실패해도 진행은 막지 않는다. 글은 이미 말풍선에 떠 있다.
                Finish();
                yield break;
            }

            StoreCached(text, clip);
        }

        audioSource.volume = volume;
        audioSource.clip = clip;
        audioSource.Play();

        while (audioSource.isPlaying) yield return null;

        Finish();
    }

    private void Finish()
    {
        Action callback = _pendingComplete;
        _pendingComplete = null;
        _running = null;
        IsSpeaking = false;

        callback?.Invoke();
    }

    private IEnumerator SynthesizeRoutine(string text, Action<AudioClip> onReady)
    {
        string body = BuildRequestJson(text);
        if (logTraffic) Debug.Log($"[OpenAiTtsClient] 요청\n{body}", this);

        using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + ResolveApiKey());
            request.timeout = Mathf.Max(timeoutSeconds, 1);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // 오류일 때는 본문이 오디오가 아니라 JSON이라 그대로 읽어도 된다.
                string detail = request.downloadHandler != null ? request.downloadHandler.text : request.error;
                if (!string.IsNullOrEmpty(detail) && detail.Length > 200) detail = detail.Substring(0, 200);

                string reason = $"HTTP {request.responseCode} — {detail}";
                Debug.LogWarning($"[OpenAiTtsClient] 음성 합성 실패: {reason}", this);
                RaiseError(reason);
                onReady(null);
                yield break;
            }

            byte[] audio = request.downloadHandler.data;
            if (logTraffic) Debug.Log($"[OpenAiTtsClient] 응답 {audio.Length / 1024}KB", this);

            AudioClip clip = WavCodec.Decode(audio, "TTS", out string decodeError);
            if (clip == null)
            {
                Debug.LogWarning($"[OpenAiTtsClient] 오디오 해석 실패: {decodeError}", this);
                RaiseError(decodeError);
            }

            onReady(clip);
        }
    }

    private string BuildRequestJson(string text)
    {
        var builder = new StringBuilder(256);
        builder.Append('{');
        builder.Append("\"model\":").Append(Quote(model));
        builder.Append(",\"voice\":").Append(Quote(ResolveVoice()));
        builder.Append(",\"input\":").Append(Quote(text));
        // wav로 받아야 WavCodec이 그대로 읽는다. mp3는 플랫폼별 디코딩 지원이 갈린다.
        builder.Append(",\"response_format\":\"wav\"");
        if (!Mathf.Approximately(speed, 1f)) builder.Append(",\"speed\":").Append(speed.ToString("0.00"));

        // 말투 지시. 구형 모델(tts-1 등)은 이 항목을 모르고 400으로 거절하므로,
        // 비워두면 아예 넣지 않는다.
        if (!string.IsNullOrWhiteSpace(instructions) && SupportsInstructions())
            builder.Append(",\"instructions\":").Append(Quote(instructions.Trim()));
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>직접 적은 이름이 있으면 그쪽을, 없으면 드롭다운 값을 쓴다.</summary>
    public string ResolveVoice()
    {
        if (!string.IsNullOrWhiteSpace(voiceOverride)) return voiceOverride.Trim();
        return voice.ToString().ToLowerInvariant();
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (char c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                    else builder.Append(c);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    // --- 캐시 ---

    /// <summary>
    /// 캐시 키. 문장만으로는 안 된다.
    ///
    /// 목소리나 말투를 바꿔도 키가 같으면 예전 목소리로 녹음해 둔 소리가 그대로 재생된다.
    /// 인스펙터에서 목소리를 바꿔놓고 "왜 안 바뀌지" 하며 헤매게 되는 지점이다.
    /// </summary>
    private string CacheKey(string text)
    {
        return $"{ResolveVoice()}|{speed:0.00}|{(instructions ?? string.Empty).GetHashCode()}|{text}";
    }

    private AudioClip GetCached(string text)
    {
        if (cacheSize <= 0) return null;
        return _cache.TryGetValue(CacheKey(text), out AudioClip clip) ? clip : null;
    }

    private void StoreCached(string text, AudioClip clip)
    {
        if (cacheSize <= 0) return;

        string key = CacheKey(text);
        _cache[key] = clip;
        _cacheOrder.Add(key);

        while (_cacheOrder.Count > cacheSize)
        {
            string oldest = _cacheOrder[0];
            _cacheOrder.RemoveAt(0);

            if (_cache.TryGetValue(oldest, out AudioClip stale))
            {
                _cache.Remove(oldest);
                // 클립은 런타임에 만든 것이라 직접 지워주지 않으면 메모리에 계속 쌓인다.
                if (stale != null) Destroy(stale);
            }
        }
    }
}
