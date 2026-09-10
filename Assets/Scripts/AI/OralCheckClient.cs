using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>대화와 다른 응답 계약을 사용하는 서술퀴즈 전용 프록시.</summary>
public class OralCheckClient : MonoBehaviour
{
    public string endpoint = "";
    public string proxyToken = "";
    public int timeoutSeconds = 30;

    private Coroutine _running;
    private UnityWebRequest _request;

    private AICoScientistClient Source =>
        GetComponent<AICoScientistClient>() ?? FindFirstObjectByType<AICoScientistClient>();

    private string ResolvedEndpoint
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(endpoint)) return endpoint.Trim();
            var source = Source;
            if (source == null || !source.IsConfigured ||
                !Uri.TryCreate(source.backendEndpoint.Trim(), UriKind.Absolute, out var uri)) return "";
            // 호스트 이름이나 쿼리 안의 문자열을 바꾸지 않고 마지막 경로만 교체한다.
            var builder = new UriBuilder(uri);
            string path = builder.Path.TrimEnd('/');
            builder.Path = path.Substring(0, path.LastIndexOf('/') + 1) + "oral-check";
            return builder.Uri.AbsoluteUri;
        }
    }

    public bool IsConfigured => isActiveAndEnabled &&
        Uri.TryCreate(ResolvedEndpoint, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "http" || uri.Scheme == "https") &&
        !uri.Host.Equals("your-backend.example.com", StringComparison.OrdinalIgnoreCase);

    public static OralCheckClient Ensure()
    {
        var existing = FindFirstObjectByType<OralCheckClient>();
        if (existing != null) return existing;
        var source = FindFirstObjectByType<AICoScientistClient>();
        return source != null ? source.gameObject.AddComponent<OralCheckClient>() : null;
    }

    [Serializable]
    private class Payload
    {
        public string questId;
        public string criteria;
        public string answer;
        public string[] conceptKeys;
        public string previousAnswer;
    }

    public void Grade(string questId, string criteria, string answer,
                      Action<OralGrade> onResult, Action<string> onFailed) =>
        Grade(questId, criteria, answer, null, onResult, onFailed);

    public void Grade(string questId, string criteria, string answer, OralConcept[] concepts,
                      Action<OralGrade> onResult, Action<string> onFailed, string previousAnswer = "")
    {
        Cancel();
        if (!IsConfigured || string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(criteria))
        {
            onFailed?.Invoke("채점 설정 또는 답변이 없습니다.");
            return;
        }
        var keys = new System.Collections.Generic.List<string>();
        if (concepts != null)
            foreach (var concept in concepts)
                if (concept != null && !string.IsNullOrWhiteSpace(concept.key)) keys.Add(concept.key);
        _running = StartCoroutine(GradeRoutine(new Payload {
            questId = questId, criteria = criteria, answer = answer,
            conceptKeys = keys.ToArray(), previousAnswer = previousAnswer
        }, onResult, onFailed));
    }

    // 중단된 퀴즈의 요청은 네트워크에서도 취소한다.
    public void Cancel()
    {
        if (_request != null) _request.Abort();
        if (_running != null) StopCoroutine(_running);
        _running = null;
        if (_request != null) _request.Dispose();
        _request = null;
    }

    private void OnDisable() => Cancel();

    private IEnumerator GradeRoutine(Payload payload, Action<OralGrade> onResult, Action<string> onFailed)
    {
        using (var request = new UnityWebRequest(ResolvedEndpoint, UnityWebRequest.kHttpVerbPOST))
        {
            _request = request;
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            var source = Source;
            string token = string.IsNullOrWhiteSpace(proxyToken) ? source?.proxyToken : proxyToken;
            if (!string.IsNullOrWhiteSpace(token)) request.SetRequestHeader("X-App-Token", token.Trim());
            request.timeout = Mathf.Clamp(timeoutSeconds, 1, 60);
            yield return request.SendWebRequest();
            _request = null;
            _running = null;
            if (request.result != UnityWebRequest.Result.Success)
            {
                onFailed?.Invoke(request.error);
                yield break;
            }
            OralGrade grade = ParseGrade(request.downloadHandler.text);
            if (grade == null) onFailed?.Invoke("채점 응답 형식이 올바르지 않습니다.");
            else onResult?.Invoke(grade);
        }
    }

    public static OralGrade ParseGrade(string json) => OralGradeProtocol.ParseGrade(json);
}
