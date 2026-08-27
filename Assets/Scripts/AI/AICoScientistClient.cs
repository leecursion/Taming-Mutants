using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// F-06 AI Co-Scientist — 자체 백엔드 프록시를 거쳐 LLM을 호출하는 구현체.
///
/// 배포용 경로다. LLM API 키를 APK 내부에 넣으면 안 된다 —
/// Quest 앱은 디컴파일로 리소스/문자열이 쉽게 노출된다.
/// 서버가 키를 보관하고 system prompt를 주입하며, Unity는 이 서버만 부른다.
///
///   Unity -> 내 서버 -> LLM
///
/// 개발 중에 서버 없이 바로 붙여보려면 <see cref="SolarChatClient"/>로 컴포넌트를 교체하면 된다.
/// <see cref="AIAssistantBrain"/>은 <see cref="AIChatBackend"/>만 알고 있어 코드 수정이 필요 없다.
///
/// 기대하는 응답 JSON:
///   { "reply": "...", "quizChoices": ["...","..."], "correctChoiceIndex": 0 }
/// </summary>
public class AICoScientistClient : AIChatBackend
{
    /// <summary>엔드포인트가 이 문자열을 포함하면 아직 설정되지 않은 것으로 본다.</summary>
    private const string PlaceholderMarker = "your-backend.example.com";

    [Header("백엔드 프록시 설정")]
    [Tooltip("직접 LLM 제공자를 호출하지 말고 반드시 자체 백엔드 URL을 넣으세요.")]
    public string backendEndpoint = "https://your-backend.example.com/api/co-scientist";
    [Tooltip("응답을 기다리는 최대 시간(초). 넘기면 실패로 처리한다.")]
    public int timeoutSeconds = 30;
    [Tooltip("백엔드가 요구하는 공유 토큰. 서버의 APP_TOKEN과 같은 값을 넣습니다.\n" +
             "URL이 알려졌을 때 아무나 호출하지 못하게 막는 최소한의 문턱입니다.")]
    public string proxyToken = "";

    [Header("디버그")]
    [Tooltip("켜면 백엔드가 설정돼 있어도 호출하지 않고 항상 실패시킨다. 오프라인 대사 확인용.")]
    public bool forceOfflineMode;

    public override bool IsConfigured =>
        !forceOfflineMode
        && !string.IsNullOrWhiteSpace(backendEndpoint)
        && !backendEndpoint.Contains(PlaceholderMarker);

    [Serializable]
    private class RequestPayload
    {
        public string sessionId;
        public string userMessage;
        public string context;      // 현재 퀘스트/단계/선택된 잔기 등을 한 덩어리 문자열로
        public string questId;
        public string stage;
    }

    [Serializable]
    private class ResponsePayload
    {
        public string reply;
        public string[] quizChoices;   // F-06.3 퀴즈 기능 사용 시
        public int correctChoiceIndex;
    }

    private string _sessionId;

    private void Awake()
    {
        _sessionId = Guid.NewGuid().ToString();
    }

    public override void Ask(string userMessage, AIRequestContext context,
                             Action<string> onReply = null, Action<string> onFailed = null)
    {
        if (RejectIfUnusable(userMessage, onFailed)) return;

        StartCoroutine(AskRoutine(userMessage, context, onReply, onFailed));
    }

    private IEnumerator AskRoutine(string userMessage, AIRequestContext context,
                                   Action<string> onReply, Action<string> onFailed)
    {
        var payload = new RequestPayload
        {
            sessionId = _sessionId,
            userMessage = userMessage,
            context = context != null ? context.Compose() : string.Empty,
            questId = context != null ? context.questId : string.Empty,
            stage = context != null ? context.stage : string.Empty,
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));

        PendingRequests++;
        try
        {
            using (var request = new UnityWebRequest(backendEndpoint.Trim(), UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrWhiteSpace(proxyToken))
                    request.SetRequestHeader("X-App-Token", proxyToken.Trim());
                request.timeout = Mathf.Max(timeoutSeconds, 1);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string reason = $"{request.result}: {request.error}";
                    Debug.LogWarning($"[AICoScientistClient] 요청 실패 — {reason}", this);
                    RaiseError(reason);
                    onFailed?.Invoke(reason);
                    yield break;
                }

                string reply = ParseReply(request.downloadHandler.text, out string parseError);
                if (reply == null)
                {
                    Debug.LogWarning($"[AICoScientistClient] 응답 해석 실패 — {parseError}", this);
                    RaiseError(parseError);
                    onFailed?.Invoke(parseError);
                    yield break;
                }

                RaiseReply(reply);
                onReply?.Invoke(reply);
            }
        }
        finally
        {
            PendingRequests--;
        }
    }

    /// <summary>
    /// 응답 본문에서 reply를 꺼낸다. 실패하면 null과 사유를 돌려준다.
    ///
    /// JsonUtility는 형식이 어긋나면 예외를 던지고, 필드가 없으면 조용히 null을 남긴다.
    /// 둘 다 걸러내야 비서가 빈 말풍선을 띄우는 일이 없다.
    /// </summary>
    private static string ParseReply(string json, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "응답 본문이 비어 있습니다.";
            return null;
        }

        ResponsePayload response;
        try
        {
            response = JsonUtility.FromJson<ResponsePayload>(json);
        }
        catch (Exception e)
        {
            error = $"JSON 형식이 올바르지 않습니다 ({e.Message})";
            return null;
        }

        if (response == null || string.IsNullOrWhiteSpace(response.reply))
        {
            error = "응답에 reply 필드가 없습니다.";
            return null;
        }

        return response.reply;
    }
}
