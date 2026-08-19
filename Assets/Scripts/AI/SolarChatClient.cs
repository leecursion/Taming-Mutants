using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Solar의 추론 강도. None이면 요청 본문에서 아예 뺀다.</summary>
public enum SolarReasoningEffort
{
    None,
    Low,
    Medium,
    High,
}

/// <summary>
/// Upstage Solar를 직접 호출하는 LLM 백엔드.
///
/// Solar는 OpenAI 호환 스펙이라 <c>POST https://api.upstage.ai/v1/chat/completions</c>에
/// <c>Authorization: Bearer &lt;key&gt;</c>를 붙여 보내면 된다. 파이썬 SDK가 하는 일과 같다.
///
/// <b>키를 빌드에 넣지 마세요.</b> Quest 앱(Android)은 디컴파일로 문자열이 그대로 노출되고,
/// 씬이나 프리팹에 직렬화된 값은 저장소에도 그대로 커밋됩니다. 개발 중에는 인스펙터나
/// 환경변수로 쓰고, 배포할 때는 <see cref="AICoScientistClient"/>(자체 프록시)로 갈아끼우세요.
/// 컴포넌트만 교체하면 <see cref="AIAssistantBrain"/>은 고칠 필요가 없습니다.
/// </summary>
public class SolarChatClient : AIChatBackend
{
    [Header("엔드포인트")]
    public string endpoint = "https://api.upstage.ai/v1/chat/completions";
    public string model = "solar-pro4";

    [Header("인증")]
    [Tooltip("개발용으로만 채우세요. 여기 넣은 값은 씬 파일에 그대로 저장되고 빌드에도 포함됩니다.")]
    public string apiKey = "";
    [Tooltip("위 칸이 비어 있으면 이 환경변수에서 키를 읽습니다. 커밋 사고를 피하려면 이쪽을 권장합니다.")]
    public string apiKeyEnvironmentVariable = "UPSTAGE_API_KEY";

    [Header("생성 설정")]
    [Tooltip("모델에게 주는 역할 지시문. 백엔드 프록시를 쓸 때는 서버가 넣어주던 부분이다.")]
    [TextArea(4, 12)]
    public string systemPrompt =
        "당신은 VR 신약개발 교육 게임 '돌연변이 길들이기'의 AI 공동연구자입니다.\n" +
        "플레이어는 유전자 변이를 관찰하고 표적을 찾아 후보물질을 도킹하는 퀘스트를 진행합니다.\n" +
        "\n" +
        "규칙:\n" +
        "- 반드시 한국어로, 존댓말로 답합니다.\n" +
        "- 말풍선에 들어가야 하므로 2~3문장, 200자 이내로 짧게 답합니다.\n" +
        "- 마크다운(**, #, 목록 기호)을 쓰지 않습니다. 평문으로만 씁니다.\n" +
        "- 함께 주어지는 '현재 상황'을 벗어난 단계를 미리 설명하지 않습니다.\n" +
        "- 정답을 통째로 알려주지 말고, 플레이어가 스스로 찾도록 한 걸음만 이끕니다.\n" +
        "- 확실하지 않은 수치나 사실은 지어내지 말고 모른다고 말합니다.";

    [Tooltip("추론 강도. 높일수록 응답이 좋아지지만 느려집니다. None이면 요청에서 생략합니다.")]
    public SolarReasoningEffort reasoningEffort = SolarReasoningEffort.Medium;
    [Tooltip("응답 최대 토큰. 0이면 지정하지 않습니다.")]
    public int maxTokens = 400;
    [Tooltip("응답을 기다리는 최대 시간(초). 추론 강도를 올리면 넉넉히 잡아야 합니다.")]
    public int timeoutSeconds = 40;

    [Header("디버그")]
    [Tooltip("켜면 키가 있어도 호출하지 않고 항상 실패시킵니다. 대본 대사 확인용.")]
    public bool forceOfflineMode;
    [Tooltip("주고받은 본문을 Console에 남깁니다. 키는 찍지 않습니다.")]
    public bool logTraffic;

    public override bool IsConfigured =>
        !forceOfflineMode
        && !string.IsNullOrWhiteSpace(endpoint)
        && !string.IsNullOrWhiteSpace(ResolveApiKey());

    private void Awake()
    {
        // 에디터 밖에서 인스펙터 키가 살아 있다는 건 그 키가 빌드에 실려 나갔다는 뜻이다.
        if (!Application.isEditor && !string.IsNullOrWhiteSpace(apiKey))
        {
            Debug.LogWarning("[SolarChatClient] API 키가 빌드에 포함된 채 실행 중입니다. " +
                             "배포본에서는 자체 프록시(AICoScientistClient)로 교체하세요.", this);
        }
    }

    /// <summary>인스펙터 값이 우선이고, 비어 있으면 환경변수에서 읽는다.</summary>
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
            // 플랫폼에 따라 환경변수 접근이 막혀 있을 수 있다. 그때는 키가 없는 것으로 본다.
            return null;
        }
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
        string situation = context != null ? context.Compose() : string.Empty;
        string body = BuildRequestJson(situation, userMessage);

        if (logTraffic) Debug.Log($"[SolarChatClient] 요청\n{body}", this);

        PendingRequests++;
        try
        {
            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + ResolveApiKey());
                request.timeout = Mathf.Max(timeoutSeconds, 1);

                yield return request.SendWebRequest();

                string responseText = request.downloadHandler != null ? request.downloadHandler.text : null;
                if (logTraffic) Debug.Log($"[SolarChatClient] 응답 ({request.responseCode})\n{responseText}", this);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // 본문에 원인이 적혀 오는 경우가 많다 (잘못된 키, 없는 모델 이름 등).
                    string detail = ExtractApiError(responseText) ?? request.error;
                    string reason = $"HTTP {request.responseCode} — {detail}";

                    Debug.LogWarning($"[SolarChatClient] 요청 실패: {reason}", this);
                    RaiseError(reason);
                    onFailed?.Invoke(reason);
                    yield break;
                }

                string reply = ParseReply(responseText, out string parseError);
                if (reply == null)
                {
                    Debug.LogWarning($"[SolarChatClient] 응답 해석 실패: {parseError}", this);
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

    // --- 요청 만들기 ---

    /// <summary>
    /// 요청 본문을 직접 조립한다.
    ///
    /// JsonUtility로 만들지 않는 이유: JsonUtility는 값이 비어 있어도 필드를 반드시 써넣는다.
    /// reasoning_effort나 max_tokens에 빈 값이 실려 가면 서버가 400으로 거절한다.
    /// 넣을 것만 골라 쓰려면 손으로 조립하는 편이 확실하다.
    /// </summary>
    private string BuildRequestJson(string situation, string userMessage)
    {
        var builder = new StringBuilder(512);
        builder.Append('{');

        builder.Append("\"model\":").Append(Quote(model));

        builder.Append(",\"messages\":[");
        AppendMessage(builder, "system", systemPrompt);

        // 상황 설명은 사용자 발화와 섞지 않고 별도 system 메시지로 넣는다.
        // 사용자 질문 안에 붙여 보내면 모델이 그것까지 "사용자가 한 말"로 취급해
        // 배경 정보를 되읊는 답이 나온다.
        if (!string.IsNullOrWhiteSpace(situation))
        {
            builder.Append(',');
            AppendMessage(builder, "system", "현재 상황:\n" + situation);
        }

        builder.Append(',');
        AppendMessage(builder, "user", userMessage);
        builder.Append(']');

        string effort = ReasoningEffortValue();
        if (effort != null) builder.Append(",\"reasoning_effort\":").Append(Quote(effort));

        if (maxTokens > 0) builder.Append(",\"max_tokens\":").Append(maxTokens);

        builder.Append(",\"stream\":false");
        builder.Append('}');

        return builder.ToString();
    }

    private static void AppendMessage(StringBuilder builder, string role, string content)
    {
        builder.Append("{\"role\":").Append(Quote(role))
               .Append(",\"content\":").Append(Quote(content ?? string.Empty))
               .Append('}');
    }

    private string ReasoningEffortValue()
    {
        switch (reasoningEffort)
        {
            case SolarReasoningEffort.Low: return "low";
            case SolarReasoningEffort.Medium: return "medium";
            case SolarReasoningEffort.High: return "high";
            default: return null; // None — 필드를 아예 넣지 않는다
        }
    }

    /// <summary>
    /// JSON 문자열 리터럴로 감싼다. 대사에 따옴표나 줄바꿈이 들어가면
    /// 이스케이프 없이는 본문 전체가 깨진 JSON이 된다.
    /// 한글 같은 비ASCII 문자는 UTF-8 그대로 보내도 유효하므로 건드리지 않는다.
    /// </summary>
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

    // --- 응답 읽기 ---

    [Serializable]
    private class ChatResponse
    {
        public Choice[] choices;
        public ApiError error;
    }

    [Serializable]
    private class Choice
    {
        public ResponseMessage message;
        public string finish_reason;
    }

    [Serializable]
    private class ResponseMessage
    {
        public string role;
        public string content;
        public string reasoning; // 추론 과정. 플레이어에게 보여줄 내용은 아니라 쓰지 않는다.
    }

    [Serializable]
    private class ApiError
    {
        public string message;
        public string type;
        public string code;
    }

    private static string ParseReply(string json, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "응답 본문이 비어 있습니다.";
            return null;
        }

        ChatResponse response;
        try
        {
            response = JsonUtility.FromJson<ChatResponse>(json);
        }
        catch (Exception e)
        {
            error = $"JSON 형식이 올바르지 않습니다 ({e.Message})";
            return null;
        }

        if (response == null || response.choices == null || response.choices.Length == 0)
        {
            error = "응답에 choices가 없습니다.";
            return null;
        }

        ResponseMessage message = response.choices[0].message;
        if (message == null || string.IsNullOrWhiteSpace(message.content))
        {
            // 추론에만 토큰을 다 쓰고 본문이 비는 경우가 있다. max_tokens를 올리면 해결된다.
            error = "응답 본문(content)이 비어 있습니다. max_tokens를 늘려보세요.";
            return null;
        }

        return message.content.Trim();
    }

    /// <summary>오류 응답에서 서버가 알려준 사유를 꺼낸다. 못 꺼내면 null.</summary>
    private static string ExtractApiError(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            ChatResponse response = JsonUtility.FromJson<ChatResponse>(json);
            if (response?.error != null && !string.IsNullOrWhiteSpace(response.error.message))
                return response.error.message;
        }
        catch (Exception)
        {
            // 오류 본문이 JSON이 아닐 수도 있다. 그때는 아래에서 원문 일부를 돌려준다.
        }

        return json.Length > 200 ? json.Substring(0, 200) : json;
    }
}
