#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 메뉴 [Tools/Taming Mutants/카메라 레벨 리그 생성]으로
/// 설계서의 Level 0~5 무대와 카메라 앵커를 씬에 만든다.
///
/// 레벨 무대를 서로 100m씩 떨어뜨려 놓는다. 실제 축척(인체 1m ~ 원자 1e-10m)을
/// 좌표로 재현하지 않는 이유는 <see cref="LevelStage"/> 주석에 적어두었다 —
/// 요약하면 float32 정밀도가 그 범위를 감당하지 못한다.
///
/// 인체·세포·DNA 3D 에셋은 아직 프로젝트에 없다. 지금은 각 레벨에 빈 컨텐츠 루트만
/// 만들어 두므로, 에셋이 준비되면 그 아래에 넣기만 하면 된다.
/// Level2(단백질)만 씬의 ProteinAnchor에 자동 연결된다.
/// </summary>
public static class LevelRigSetupMenu
{
    private const float LevelSpacing = 100f;

    [MenuItem("Tools/Taming Mutants/카메라 레벨 리그 생성")]
    public static void Setup()
    {
        GameObject rigGo = GameObject.Find("LevelRig");
        if (rigGo == null)
        {
            rigGo = new GameObject("LevelRig");
            Undo.RegisterCreatedObjectUndo(rigGo, "Create LevelRig");
        }

        var director = GetOrAdd<CameraTransitionDirector>(rigGo);
        var effects = GetOrAdd<TransitionEffects>(rigGo);
        var binder = GetOrAdd<QuestLevelBinder>(rigGo);

        director.targetCamera = Camera.main;

        var stages = new LevelStage[6];
        for (int i = 0; i < 6; i++)
            stages[i] = BuildStage(rigGo.transform, (QuestLevel)i, i);

        director.stages = stages;
        director.transitions = BuildTransitions();

        // 트랜지션 중에는 마우스 회전/줌/선택을 잠근다.
        // 잠그지 않으면 이동 도중 사용자가 대상을 돌려 도착 그림이 흐트러진다.
        var fallbackController = Object.FindFirstObjectByType<DesktopFallbackController>();
        var selector = Object.FindFirstObjectByType<MouseWorldSelector>();
        director.disableDuringTransition = new MonoBehaviour[] { fallbackController, selector };

        // Level2는 씬에 이미 있는 단백질을 그대로 쓴다.
        var loader = Object.FindFirstObjectByType<ProteinLoader>();
        if (loader != null)
        {
            stages[(int)QuestLevel.Level2_Protein].contentRoot = loader.gameObject;
            // 원자 로딩 코루틴이 도중에 끊기지 않도록 렌더러만 끈다.
            stages[(int)QuestLevel.Level2_Protein].hideMode = LevelStage.HideMode.DisableRenderers;
        }

        effects.postProcessVolume = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
        effects.audioSource = GetOrAdd<AudioSource>(rigGo);
        effects.audioSource.playOnAwake = false;
        effects.audioSource.spatialBlend = 0f; // 트랜지션 음향은 화면 전체를 덮으므로 2D

        binder.director = director;
        binder.session = Object.FindFirstObjectByType<QuestSession>();
        binder.assistant = Object.FindFirstObjectByType<AIAssistantBrain>();

        EditorUtility.SetDirty(director);
        EditorUtility.SetDirty(effects);
        EditorUtility.SetDirty(binder);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Selection.activeGameObject = rigGo;
        Debug.Log("[LevelRigSetup] Level 0~5 무대를 만들었습니다. " +
                  "각 Content 루트에 인체/세포/DNA 에셋을 넣고, 카메라 앵커 위치를 조정하세요.");
    }

    private static LevelStage BuildStage(Transform parent, QuestLevel level, int index)
    {
        string name = level.ToString();

        Transform existing = parent.Find(name);
        GameObject stageGo = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null)
        {
            stageGo.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(stageGo, "Create LevelStage");
        }

        // 레벨끼리 겹치지 않게 일정 간격으로 늘어놓는다.
        stageGo.transform.localPosition = new Vector3(0f, 0f, index * LevelSpacing);

        var stage = GetOrAdd<LevelStage>(stageGo);
        stage.level = level;
        stage.fieldOfView = 60f;

        // 카메라 앵커: 무대 원점에서 조금 뒤로 물러난 자리.
        Transform anchor = FindOrCreateChild(stageGo.transform, "CameraAnchor");
        anchor.localPosition = new Vector3(0f, 1.6f, -3f);
        anchor.localRotation = Quaternion.identity;
        stage.cameraAnchor = anchor;

        // 컨텐츠 루트: 여기에 각 레벨의 3D 에셋을 넣는다.
        if (stage.contentRoot == null)
            stage.contentRoot = FindOrCreateChild(stageGo.transform, "Content").gameObject;

        // Pan & Focus용 경유점. Level1에서만 실제로 쓰인다.
        FindOrCreateChild(stageGo.transform, "ViaPoint").localPosition = new Vector3(0f, 3f, 6f);

        EditorUtility.SetDirty(stage);
        return stage;
    }

    /// <summary>설계서 "카메라 설계 요약 표"의 4개 전환.</summary>
    private static LevelTransition[] BuildTransitions()
    {
        return new[]
        {
            // Step 0 -> 1: 흉부 암 조직 -> 폐세포 -> 세포막 통과 -> 세포핵
            new LevelTransition
            {
                from = QuestLevel.Level0_Body, to = QuestLevel.Level1_DNA,
                style = CameraMotionStyle.DollyIn,
                duration = 3.2f, fovPunch = 24f,
                revealAt = 0.55f, hidePreviousAt = 0.7f,
            },
            // Step 1 -> 2: DNA를 따라 훑다가 리보솜 단백질 사슬로 초점 전환
            new LevelTransition
            {
                from = QuestLevel.Level1_DNA, to = QuestLevel.Level2_Protein,
                style = CameraMotionStyle.PanAndFocus,
                duration = 3.6f, fovPunch = 6f,
                revealAt = 0.45f, hidePreviousAt = 0.75f,
            },
            // Step 2 -> 3: 리본 표면을 뚫고 Switch-II Pocket 내부로
            new LevelTransition
            {
                from = QuestLevel.Level2_Protein, to = QuestLevel.Level3_Pocket,
                style = CameraMotionStyle.MicroZoomIn,
                duration = 3f, fovPunch = 10f,
                revealAt = 0.5f, hidePreviousAt = 0.8f,
            },
            // Step 3 -> 4: 포켓 안에서 도킹 자리로. 같은 공간이라 짧게.
            new LevelTransition
            {
                from = QuestLevel.Level3_Pocket, to = QuestLevel.Level4_Docking,
                style = CameraMotionStyle.MicroZoomIn,
                duration = 1.6f, fovPunch = 4f,
                revealAt = 0.4f, hidePreviousAt = 0.6f,
            },
            // Step 4 -> 5: 분자 -> 세포 -> 인체를 뚫고 연구실로 후퇴
            new LevelTransition
            {
                from = QuestLevel.Level4_Docking, to = QuestLevel.Level5_Dashboard,
                style = CameraMotionStyle.SpatialZoomOut,
                duration = 4f, fovPunch = -14f, // 음수: 좁히며 빨려 나가는 느낌
                revealAt = 0.6f, hidePreviousAt = 0.5f,
            },
        };
    }

    private static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        return go.transform;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }
}
#endif
