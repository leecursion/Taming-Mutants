#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 메뉴 [Tools/Taming Mutants/음성 입출력(STT/TTS) 추가]로
/// 음성 질문(Whisper)과 비서 낭독(TTS)에 필요한 컴포넌트를 씬에 붙인다.
///
/// 여러 번 실행해도 안전하다. 이미 있는 컴포넌트는 다시 만들지 않고 참조만 다시 잇는다.
///
/// 키는 넣지 않는다 — 환경변수 <c>OPENAI_API_KEY</c>에서 읽도록만 설정한다.
/// 인스펙터에 적으면 씬 파일에 그대로 저장되고 빌드에도 실려 나간다.
/// </summary>
public static class VoiceSetupMenu
{
    [MenuItem("Tools/Taming Mutants/음성 입출력(STT/TTS) 추가")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("음성 구성 실패",
                "Play 모드 중에는 실행할 수 없습니다.\n\n" +
                "Play를 멈춘 뒤(편집 모드) 다시 실행하세요 — Play 중에 만든 오브젝트는 " +
                "Play를 멈추는 순간 사라져 씬에 남지 않습니다.", "확인");
            return;
        }

        AIAssistantBrain brain = Object.FindFirstObjectByType<AIAssistantBrain>(FindObjectsInactive.Include);
        if (brain == null)
        {
            EditorUtility.DisplayDialog("음성 구성 실패",
                "씬에서 AI 비서(AIAssistantBrain)를 찾지 못했습니다.\n\n" +
                "먼저 [Tools/Taming Mutants/AI 비서 생성]을 실행하세요.", "확인");
            return;
        }

        // 백엔드는 다른 LLM 백엔드와 같은 자리에 둔다. 없으면 비서 오브젝트에 붙인다.
        GameObject backendHost = ResolveBackendHost(brain);

        OpenAiWhisperClient stt = GetOrAdd<OpenAiWhisperClient>(backendHost);
        stt.apiKey = "";                                  // 키는 절대 씬에 저장하지 않는다
        stt.apiKeyEnvironmentVariable = "OPENAI_API_KEY";
        EditorUtility.SetDirty(stt);

        OpenAiTtsClient tts = GetOrAdd<OpenAiTtsClient>(backendHost);
        tts.apiKey = "";
        tts.apiKeyEnvironmentVariable = "OPENAI_API_KEY";
        EditorUtility.SetDirty(tts);

        // 말풍선이 이 TTS로 대사를 읽게 한다.
        if (brain.bubble != null)
        {
            brain.bubble.tts = tts;
            brain.bubble.speakAloud = true;
            EditorUtility.SetDirty(brain.bubble);
        }
        else
        {
            Debug.LogWarning("[VoiceSetup] 비서의 말풍선을 찾지 못해 TTS를 연결하지 못했습니다. " +
                             "AIAssistantBrain의 Bubble 참조를 확인하세요.");
        }

        BuildMicButton(brain, stt);
        EnsureEventSystem();

        EditorUtility.SetDirty(brain);
        MarkSceneDirty();

        Debug.Log("[VoiceSetup] 음성 입출력 구성을 완료했습니다.\n" +
                  "환경변수 OPENAI_API_KEY를 설정한 뒤 Unity Hub를 다시 시작하세요 " +
                  "(Hub가 Editor에 환경을 물려줍니다).");
    }

    /// <summary>
    /// 음성 백엔드를 붙일 오브젝트. 이미 LLM 백엔드가 있는 곳에 모아둔다 —
    /// 키를 쓰는 컴포넌트가 흩어져 있으면 배포 전에 확인할 곳이 늘어난다.
    /// </summary>
    private static GameObject ResolveBackendHost(AIAssistantBrain brain)
    {
        AIChatBackend chat = Object.FindFirstObjectByType<AIChatBackend>(FindObjectsInactive.Include);
        return chat != null ? chat.gameObject : brain.gameObject;
    }

    /// <summary>비서를 따라다니는 마이크 버튼을 만든다.</summary>
    private static void BuildMicButton(AIAssistantBrain brain, SpeechToTextBackend stt)
    {
        VoiceInputController existing = Object.FindFirstObjectByType<VoiceInputController>(FindObjectsInactive.Include);

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject("VoiceInput");
            Undo.RegisterCreatedObjectUndo(go, "Create VoiceInput");
            // 비서의 자식으로 두면 비서가 움직일 때 따로 계산하지 않아도 함께 따라간다.
            go.transform.SetParent(brain.transform, false);
        }

        VoiceInputController controller = GetOrAdd<VoiceInputController>(go);
        controller.assistant = brain;
        controller.speechToText = stt;
        controller.followTarget = brain.transform;
        if (Camera.main != null) controller.lookTarget = Camera.main.transform;

        EditorUtility.SetDirty(controller);
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;

        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }

    private static void MarkSceneDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
}
#endif
