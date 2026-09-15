using System;
using System.Collections;
using UnityEngine;

using Stage = QuestManagerSpatialUI.QuestStage;

/// <summary>
/// 퀘스트 단계와 카메라 레벨을 잇는다.
///
/// <see cref="QuestSession"/>은 "지금 몇 단계인가"만 알고,
/// <see cref="CameraTransitionDirector"/>는 "레벨 사이를 어떻게 건너는가"만 안다.
/// 둘을 서로 모르게 두고 이 컴포넌트가 짝을 지어준다 — 그래야 카메라 없이도
/// 퀘스트 로직을 테스트할 수 있고, 퀘스트 없이도 트랜지션을 확인할 수 있다.
///
/// 설계서 대응:
///   Quest1 질병 원인 분석  -> Level1 DNA        (Step 0->1 Rapid Dolly-In)
///   Quest2 단백질 구조 분석 -> Level2 Protein    (Step 1->2 Pan &amp; Focus Shift)
///   Quest3 치료 표적 발굴   -> Level3 Pocket     (Step 2->3 Micro Zoom-In)
///   Quest4 후보물질 평가    -> Level4 Docking
///   Quest5 치료 효과 검증   -> Level5 Dashboard  (Step 4->5 Spatial Zoom-Out)
/// </summary>
public class QuestLevelBinder : MonoBehaviour
{
    [Serializable]
    public class StageLevel
    {
        public Stage stage;
        public QuestLevel level;
    }

    [Header("연결 (비워두면 씬에서 찾는다)")]
    public QuestSession session;
    public CameraTransitionDirector director;
    public AIAssistantBrain assistant;

    [Header("단계 -> 레벨 대응")]
    // Quest1(질병 원인 분석)은 원래 Level1_DNA(이중나선)로 보냈지만, 그 레벨에는 실제 구조가
    // 없어 빈 자리로 갔다가 다시 Level2로 되돌아오는 "줌인 후 줌아웃" 왕복만 생겼다. DNA 관련
    // 설명은 비서 대사로만 하고, 화면은 처음부터 리본이 있는 Level2_Protein으로 곧장 빨려
    // 들어가게 한다.
    public StageLevel[] mapping =
    {
        new StageLevel { stage = Stage.Quest1_DiseaseAnalysis,     level = QuestLevel.Level2_Protein },
        new StageLevel { stage = Stage.Quest2_ProteinStructure,    level = QuestLevel.Level2_Protein },
        new StageLevel { stage = Stage.Quest3_TargetDiscovery,     level = QuestLevel.Level3_Pocket },
        new StageLevel { stage = Stage.Quest4_CandidateEvaluation, level = QuestLevel.Level4_Docking },
        new StageLevel { stage = Stage.Quest5_Verification,        level = QuestLevel.Level5_Dashboard },
    };

    [Header("퀘스트 시작 연출")]
    [Tooltip("Level0(인체)에는 아직 실제 홀로그램 콘텐츠가 없어, 켜두면 3.5초간 빈 화면만 " +
             "보여주다 Level1로 다시 이동하는 헛걸음이 생긴다. 인체/DNA 콘텐츠가 생기기 전까지는 꺼둔다.")]
    public bool openFromBodyLevel = false;
    [Tooltip("인체 홀로그램을 보여주는 시간(초). openFromBodyLevel이 켜져 있을 때만 쓰인다.")]
    public float bodyDwellSeconds = 3.5f;

    [Header("비서")]
    [Tooltip("트랜지션 중에는 비서를 숨긴다. 카메라가 빠르게 움직이면 " +
             "lazy-follow가 뒤늦게 쫓아오며 화면을 가로질러 날아간다.")]
    public bool hideAssistantDuringTransition = true;

    private Coroutine _opening;

    private void Awake()
    {
        if (session == null) session = FindFirstObjectByType<QuestSession>();
        if (director == null) director = FindFirstObjectByType<CameraTransitionDirector>();
        if (assistant == null) assistant = FindFirstObjectByType<AIAssistantBrain>();
    }

    private void OnEnable()
    {
        if (session != null)
        {
            session.OnQuestStarted += HandleQuestStarted;
            session.OnStageEntered += HandleStageEntered;
        }

        if (director != null)
        {
            director.OnTransitionStarted += HandleTransitionStarted;
            director.OnTransitionCompleted += HandleTransitionCompleted;
        }
    }

    private void OnDisable()
    {
        if (session != null)
        {
            session.OnQuestStarted -= HandleQuestStarted;
            session.OnStageEntered -= HandleStageEntered;
        }

        if (director != null)
        {
            director.OnTransitionStarted -= HandleTransitionStarted;
            director.OnTransitionCompleted -= HandleTransitionCompleted;
        }
    }

    // --- 퀘스트 이벤트 ---

    private void HandleQuestStarted(QuestDefinition quest)
    {
        if (director == null || !openFromBodyLevel) return;

        // 인체 레벨에 세워두고, 1단계 이동은 잠시 미룬다.
        // QuestSession.StartQuest는 시작하자마자 1단계에 들어가므로,
        // 여기서 붙잡지 않으면 인체 홀로그램이 한 프레임도 보이지 않는다.
        director.SnapTo(QuestLevel.Level0_Body);

        // IntroDirector.MoveAssistantToQuestAnchor()가 이 시점에 이미 follower.anchorTarget을
        // questAnchor(보통 ProteinAnchor_Main, Level2 자리에 있음)로 옮겨둔 상태다. 카메라는
        // 아직 인체 레벨(Level0)에 있으니 비서가 그 먼 자리를 향해 화면을 가로질러 날아가
        // "AI 창이 저 멀리 있다"는 인상을 준다. 첫 트랜지션이 Level2에 도착해 SnapToAnchor로
        // 자리를 바로잡을 때까지(HandleTransitionCompleted) 숨겨서 그 이동 자체가 안 보이게 한다.
        if (hideAssistantDuringTransition) SetAssistantVisible(false);
    }

    private void HandleStageEntered(QuestStageBriefing briefing)
    {
        if (director == null || session == null) return;

        QuestLevel level = ResolveLevel(session.CurrentStage);

        // 첫 단계만 인체 레벨을 잠깐 보여준 뒤에 이동한다.
        bool isFirstStage = session.CurrentStage == Stage.Quest1_DiseaseAnalysis;
        if (openFromBodyLevel && isFirstStage && bodyDwellSeconds > 0f)
        {
            if (_opening != null) StopCoroutine(_opening);
            _opening = StartCoroutine(OpenAfterDwell(level));
            return;
        }

        director.GoTo(level);
    }

    private IEnumerator OpenAfterDwell(QuestLevel level)
    {
        yield return new WaitForSeconds(bodyDwellSeconds);

        director.GoTo(level);
        _opening = null;
    }

    // --- 트랜지션 이벤트 ---

    private void HandleTransitionStarted(QuestLevel from, QuestLevel to, LevelTransition settings)
    {
        if (!hideAssistantDuringTransition || assistant == null) return;

        // 재생을 멈추고 비서를 숨긴다. 이동이 끝나면 멈춘 지점부터 이어서 말한다.
        //
        // Hide()로 지우면 안 된다. QuestSession.OnStageEntered는 이 바인더와 AIAssistantBrain이
        // 함께 구독하는데 호출 순서가 보장되지 않는다. 바인더가 먼저면 브리핑이 쌓이기도 전에
        // 큐가 비워지고, 브레인이 먼저면 방금 쌓은 브리핑이 통째로 지워진다.
        // 어느 쪽이든 "설명과 화면이 따로 노는" 결과가 된다.
        if (assistant.bubble != null) assistant.bubble.Pause();
        SetAssistantVisible(false);
    }

    private void HandleTransitionCompleted(QuestLevel level)
    {
        if (!hideAssistantDuringTransition || assistant == null) return;

        SetAssistantVisible(true);

        // 새 레벨의 카메라 자리 옆으로 즉시 옮긴다.
        // 스냅하지 않으면 이전 레벨 위치에서 화면을 가로질러 날아온다.
        if (assistant.follower != null) assistant.follower.SnapToAnchor();

        // 자리를 잡은 다음에 다시 말하게 한다. 순서가 반대면 이동 중인 비서 옆에서
        // 말풍선이 먼저 열려 화면을 가로지른다.
        if (assistant.bubble != null) assistant.bubble.Resume();
    }

    private void SetAssistantVisible(bool visible)
    {
        if (assistant == null) return;

        // 오브젝트를 끄면 비서의 코루틴(말풍선 재생)까지 멈추므로 렌더러만 끈다.
        foreach (Renderer r in assistant.GetComponentsInChildren<Renderer>(includeInactive: true))
            r.enabled = visible;

        foreach (Canvas c in assistant.GetComponentsInChildren<Canvas>(includeInactive: true))
            c.enabled = visible;

        foreach (Light l in assistant.GetComponentsInChildren<Light>(includeInactive: true))
            l.enabled = visible;
    }

    // --- 보조 ---

    private QuestLevel ResolveLevel(Stage stage)
    {
        if (mapping != null)
        {
            foreach (StageLevel entry in mapping)
                if (entry != null && entry.stage == stage) return entry.level;
        }

        Debug.LogWarning($"[QuestLevelBinder] {stage}에 대응하는 레벨이 없어 Level2를 씁니다.", this);
        return QuestLevel.Level2_Protein;
    }
}
