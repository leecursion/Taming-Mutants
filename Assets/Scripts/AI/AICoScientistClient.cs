using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// F-06.1~F-06.3 AI Co-Scientist 시스템
///
/// 보안 주의: GPT-4o API 키를 APK 내부에 넣지 마세요. Quest 앱은 디컴파일로
/// 리소스/문자열이 쉽게 노출됩니다. 반드시 본인 서버(백엔드 프록시)를 하나 두고
/// Unity -> 내 서버 -> OpenAI 순서로 요청해야 합니다.
///
/// 이 스크립트는 예시로 "https://your-backend.example.com/api/co-scientist"
/// 라는 자체 엔드포인트를 호출하는 구조입니다. 서버 쪽에서 실제 GPT-4o 키를 보관하고
/// system prompt(생명과학/AlphaFold 특화 지시문)를 주입하세요.
/// </summary>
public class AICoScientistClient : MonoBehaviour
{
    [Header("백엔드 프록시 설정")]
    [Tooltip("직접 OpenAI를 호출하지 말고 반드시 자체 백엔드 URL을 넣으세요")]
    public string backendEndpoint = "https://your-backend.example.com/api/co-scientist";

    [Serializable]
    private class RequestPayload
    {
        public string sessionId;
        public string userMessage;
        public string context; // 현재 퀘스트 단계, 선택된 잔기/변이 정보 등
    }

    [Serializable]
    private class ResponsePayload
    {
        public string reply;
        public string[] quizChoices; // F-06.3 퀴즈 기능 사용 시
        public int correctChoiceIndex;
    }

    public event Action<string> OnReplyReceived;
    public event Action<string> OnError;

    private string _sessionId;

    private void Awake()
    {
        _sessionId = Guid.NewGuid().ToString();
    }

    // F-02.4 상황 맥락 브리핑: 변이 부위 선택 시 호출
    public void RequestMutationBriefing(string mutationDescription)
    {
        SendRequest($"이 변이 부위에 대해 설명해줘: {mutationDescription}", context: "quest1_dna");
    }

    // F-06.2 퀘스트 가이드 및 추천
    public void RequestGuidance(string currentQuestStage, string extraContext)
    {
        SendRequest($"현재 단계({currentQuestStage})에서 다음에 뭘 해야 할지 가이드해줘.", context: extraContext);
    }

    // F-06.3 학습 피드백 및 퀴즈
    public void RequestQuiz(string topic)
    {
        SendRequest($"'{topic}' 주제로 이해도 확인 퀴즈를 하나 만들어줘.", context: "quiz_request");
    }

    private void SendRequest(string userMessage, string context)
    {
        StartCoroutine(SendRequestRoutine(userMessage, context));
    }

    private IEnumerator SendRequestRoutine(string userMessage, string context)
    {
        var payload = new RequestPayload
        {
            sessionId = _sessionId,
            userMessage = userMessage,
            context = context
        };
        string json = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(backendEndpoint, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AICoScientistClient] 요청 실패: {req.error}");
                OnError?.Invoke(req.error);
                yield break;
            }

            ResponsePayload response = JsonUtility.FromJson<ResponsePayload>(req.downloadHandler.text);
            OnReplyReceived?.Invoke(response.reply);
        }
    }
}
