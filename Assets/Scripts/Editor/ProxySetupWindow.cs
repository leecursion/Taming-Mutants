#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 메뉴 [Tools/Taming Mutants/배포용 프록시 배선]으로 씬의 AI 컴포넌트를
/// 자체 백엔드(Cloudflare Worker)만 바라보도록 한 번에 바꾼다.
///
/// 배포본에 API 키를 넣지 않기 위한 마지막 단계다. 이 창에서 URL과 공유 토큰을 넣으면:
///   - 채팅: SolarChatClient(직접 호출)를 떼고 AICoScientistClient(프록시)로 교체
///   - 음성: OpenAiWhisperClient / OpenAiTtsClient의 proxyEndpoint를 채움
///   - 세 컴포넌트의 apiKey 칸을 모두 비움
///
/// 입력한 값은 EditorPrefs(내 PC)에만 기억되고 저장소에는 남지 않는다.
/// 다만 <b>적용하면 토큰이 씬 파일에 저장된다</b> — 클라이언트가 헤더로 보내야 하므로
/// 피할 수 없다. 토큰은 인증이 아니라 문턱이고, 유출되면 서버에서 값만 바꾸면 된다.
/// </summary>
public class ProxySetupWindow : EditorWindow
{
    private const string UrlPrefKey = "TamingMutants.ProxyBaseUrl";
    private const string TokenPrefKey = "TamingMutants.ProxyAppToken";

    private string _baseUrl = "";
    private string _appToken = "";
    private Vector2 _scroll;
    private string _report = "";

    [MenuItem("Tools/Taming Mutants/배포용 프록시 배선")]
    public static void Open()
    {
        var window = GetWindow<ProxySetupWindow>(true, "배포용 프록시 배선");
        window.minSize = new Vector2(520, 380);
    }

    private void OnEnable()
    {
        _baseUrl = EditorPrefs.GetString(UrlPrefKey, "");
        _appToken = EditorPrefs.GetString(TokenPrefKey, "");
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.HelpBox(
            "Cloudflare Worker의 주소와 APP_TOKEN을 넣고 [적용]을 누르세요.\n" +
            "적용하면 씬의 AI 컴포넌트가 이 서버만 부르게 되고, API 키는 빌드에 실리지 않습니다.",
            MessageType.Info);

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Worker 주소", EditorStyles.boldLabel);
        _baseUrl = EditorGUILayout.TextField("Base URL", _baseUrl);
        EditorGUILayout.LabelField(" ", "예: https://taming-mutants-proxy.pacasim.workers.dev",
                                   EditorStyles.miniLabel);

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("공유 토큰", EditorStyles.boldLabel);
        _appToken = EditorGUILayout.PasswordField("APP_TOKEN", _appToken);
        EditorGUILayout.LabelField(" ", "wrangler secret put APP_TOKEN 으로 넣은 값과 같아야 합니다.",
                                   EditorStyles.miniLabel);

        EditorGUILayout.Space();

        if (!string.IsNullOrWhiteSpace(_baseUrl))
        {
            string root = Normalize(_baseUrl);
            EditorGUILayout.LabelField("적용될 엔드포인트", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("채팅", root + "/api/co-scientist");
            EditorGUILayout.LabelField("음성 인식", root + "/api/stt");
            EditorGUILayout.LabelField("음성 합성", root + "/api/tts");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("적용", GUILayout.Height(30))) Apply();
            if (GUILayout.Button("빌드 전 점검만 실행")) _report = Inspect();
        }

        if (EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Play 모드에서는 실행할 수 없습니다. Play를 멈추고 다시 시도하세요.",
                                    MessageType.Warning);
        }

        if (!string.IsNullOrEmpty(_report))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(_report, GUILayout.MinHeight(120));
        }

        EditorGUILayout.EndScrollView();
    }

    private void Apply()
    {
        string root = Normalize(_baseUrl);

        if (string.IsNullOrWhiteSpace(root) || !root.StartsWith("https://"))
        {
            EditorUtility.DisplayDialog("배선 실패",
                "Base URL이 비어 있거나 https://로 시작하지 않습니다.\n\n" +
                "wrangler deploy가 출력한 주소를 그대로 넣으세요.", "확인");
            return;
        }

        if (string.IsNullOrWhiteSpace(_appToken) &&
            !EditorUtility.DisplayDialog("토큰이 비어 있습니다",
                "APP_TOKEN 없이 배선하면 서버가 401로 거절합니다.\n\n" +
                "서버에 APP_TOKEN을 설정하지 않은 경우에만 계속하세요.", "계속", "취소"))
        {
            return;
        }

        EditorPrefs.SetString(UrlPrefKey, _baseUrl);
        EditorPrefs.SetString(TokenPrefKey, _appToken);

        string token = _appToken != null ? _appToken.Trim() : "";
        var log = new List<string>();

        WireChat(root, token, log);
        WireVoice(root, token, log);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        log.Add("");
        log.Add("씬을 저장하세요 (Ctrl+S). 저장해야 빌드에 반영됩니다.");
        _report = string.Join("\n", log);
        Debug.Log("[ProxySetup] 배선 완료\n" + _report);
    }

    /// <summary>
    /// 채팅 백엔드를 프록시 구현으로 갈아끼운다.
    ///
    /// SolarChatClient를 남겨두지 않고 지우는 이유: AIAssistantBrain은 참조가 비면
    /// FindFirstObjectByType&lt;AIChatBackend&gt;()로 아무거나 집어온다. 둘 다 씬에 있으면
    /// 어느 쪽이 잡힐지 보장할 수 없어, 키를 직접 쓰는 쪽이 배포본에서 살아날 수 있다.
    /// </summary>
    private static void WireChat(string root, string token, List<string> log)
    {
        var brain = Object.FindFirstObjectByType<AIAssistantBrain>(FindObjectsInactive.Include);

        // 프록시 클라이언트를 놓을 자리는 지금 백엔드가 있는 오브젝트를 그대로 쓴다.
        var existing = Object.FindObjectsByType<AIChatBackend>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        GameObject host = null;
        var solars = new List<SolarChatClient>();
        AICoScientistClient proxy = null;

        foreach (AIChatBackend backend in existing)
        {
            if (host == null) host = backend.gameObject;
            if (backend is SolarChatClient solar) solars.Add(solar);
            else if (backend is AICoScientistClient p) proxy = p;
        }

        if (host == null)
        {
            if (brain == null)
            {
                log.Add("채팅: 씬에서 AI 백엔드도 비서도 찾지 못했습니다. 건너뜁니다.");
                return;
            }
            host = brain.gameObject;
        }

        if (proxy == null)
        {
            proxy = Undo.AddComponent<AICoScientistClient>(host);
            log.Add("채팅: " + host.name + "에 AICoScientistClient를 추가했습니다.");
        }
        else
        {
            log.Add("채팅: " + host.name + "의 기존 AICoScientistClient를 갱신했습니다.");
        }

        proxy.backendEndpoint = root + "/api/co-scientist";
        proxy.proxyToken = token;
        proxy.forceOfflineMode = false;
        EditorUtility.SetDirty(proxy);

        // 비서가 새 백엔드를 보게 한다. 참조가 낡은 채로 남으면 지워진 컴포넌트를 가리킨다.
        if (brain != null)
        {
            brain.client = proxy;
            EditorUtility.SetDirty(brain);
        }

        foreach (SolarChatClient solar in solars)
        {
            Undo.DestroyObjectImmediate(solar);
            log.Add("채팅: SolarChatClient(직접 호출)를 제거했습니다.");
        }
    }

    private static void WireVoice(string root, string token, List<string> log)
    {
        var stt = Object.FindFirstObjectByType<OpenAiWhisperClient>(FindObjectsInactive.Include);
        if (stt != null)
        {
            stt.proxyEndpoint = root + "/api/stt";
            stt.proxyToken = token;
            stt.apiKey = "";
            EditorUtility.SetDirty(stt);
            log.Add("음성 인식: " + stt.gameObject.name + "의 OpenAiWhisperClient를 프록시로 돌렸습니다.");
        }
        else
        {
            log.Add("음성 인식: OpenAiWhisperClient가 씬에 없습니다. 건너뜁니다.");
        }

        var tts = Object.FindFirstObjectByType<OpenAiTtsClient>(FindObjectsInactive.Include);
        if (tts != null)
        {
            tts.proxyEndpoint = root + "/api/tts";
            tts.proxyToken = token;
            tts.apiKey = "";
            EditorUtility.SetDirty(tts);
            log.Add("음성 합성: " + tts.gameObject.name + "의 OpenAiTtsClient를 프록시로 돌렸습니다.");
        }
        else
        {
            log.Add("음성 합성: OpenAiTtsClient가 씬에 없습니다. 건너뜁니다.");
        }
    }

    /// <summary>
    /// 빌드 직전에 확인할 것들. 키가 씬에 남아 있거나 프록시가 안 걸린 곳을 잡아낸다.
    /// </summary>
    private static string Inspect()
    {
        var lines = new List<string>();
        bool clean = true;

        var solars = Object.FindObjectsByType<SolarChatClient>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (SolarChatClient solar in solars)
        {
            clean = false;
            lines.Add("[위험] " + solar.gameObject.name + "에 SolarChatClient가 남아 있습니다 " +
                      "— Upstage를 직접 호출하며 키가 필요합니다.");
        }

        var chat = Object.FindFirstObjectByType<AICoScientistClient>(FindObjectsInactive.Include);
        if (chat == null)
        {
            clean = false;
            lines.Add("[위험] AICoScientistClient가 씬에 없습니다 — 채팅이 프록시를 쓰지 않습니다.");
        }
        else if (!chat.IsConfigured)
        {
            clean = false;
            lines.Add("[위험] AICoScientistClient의 backendEndpoint가 비어 있거나 placeholder입니다.");
        }

        var stt = Object.FindFirstObjectByType<OpenAiWhisperClient>(FindObjectsInactive.Include);
        if (stt != null)
        {
            if (!stt.UsingProxy)
            {
                clean = false;
                lines.Add("[위험] OpenAiWhisperClient가 OpenAI를 직접 호출합니다.");
            }
            if (!string.IsNullOrWhiteSpace(stt.apiKey))
            {
                clean = false;
                lines.Add("[위험] OpenAiWhisperClient의 apiKey가 채워져 있습니다 — 빌드에 실려 나갑니다.");
            }
        }

        var tts = Object.FindFirstObjectByType<OpenAiTtsClient>(FindObjectsInactive.Include);
        if (tts != null)
        {
            if (!tts.UsingProxy)
            {
                clean = false;
                lines.Add("[위험] OpenAiTtsClient가 OpenAI를 직접 호출합니다.");
            }
            if (!string.IsNullOrWhiteSpace(tts.apiKey))
            {
                clean = false;
                lines.Add("[위험] OpenAiTtsClient의 apiKey가 채워져 있습니다 — 빌드에 실려 나갑니다.");
            }
        }

        if (clean) lines.Add("이상 없습니다. 키를 쓰는 컴포넌트가 씬에 없고, 세 경로 모두 프록시를 봅니다.");

        return string.Join("\n", lines);
    }

    /// <summary>끝의 슬래시를 떼고 공백을 정리한다. 붙여넣기한 주소에 자주 섞인다.</summary>
    private static string Normalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        return url.Trim().TrimEnd('/');
    }
}
#endif
