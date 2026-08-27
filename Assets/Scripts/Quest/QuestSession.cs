using System;
using UnityEngine;

/// <summary>
/// 진행 중인 퀘스트의 유일한 상태 보관소.
///
/// 인트로(<see cref="IntroDirector"/>), 비서(<see cref="AIAssistantBrain"/>),
/// 퀘스트 패널(<see cref="QuestManagerSpatialUI"/>)이 서로를 직접 참조하면
/// 셋 중 하나만 바꿔도 나머지가 딸려 온다. 전부 여기만 보게 해서 한 방향으로 흐르게 한다.
///
///   IntroDirector ──StartQuest()──▶ QuestSession ──OnStageEntered──▶ AIAssistantBrain
///                                        │                          (대사 재생)
///                                        └──────────────────────────▶ QuestManagerSpatialUI
///                                                                    (진행률 표시)
///
/// QuestManagerSpatialUI는 표시만 담당하고 상태를 갖지 않는다 —
/// 원래 자기 안에 currentStage를 들고 있었는데, 그러면 진행 상태가 두 군데에 생겨
/// 어느 쪽이 진짜인지 모르게 된다. 여기서 매 전환마다 밀어 넣어 한쪽으로 몰아준다.
/// </summary>
public class QuestSession : MonoBehaviour
{
    [Header("표시 연결 (선택)")]
    [Tooltip("퀘스트 진행률 홀로그램 패널. 비워두면 표시만 생략된다.")]
    public QuestManagerSpatialUI questPanel;

    [Header("퀘스트 적용 대상 (선택)")]
    [Tooltip("퀘스트를 시작할 때 이 로더에 구조 데이터 경로를 밀어 넣는다.")]
    public ProteinLoader proteinLoader;
    [Tooltip("퀘스트의 변이 잔기를 이 하이라이터에 등록한다.")]
    public MutationHighlighter mutationHighlighter;
    [Tooltip("나선 단계에서 변이 자리를 띠로 짚어주려면 연결한다. 비우면 proteinLoader와 같은 " +
             "오브젝트에서 찾는다.")]
    public StructureLevelController levelController;
    [Tooltip("퀘스트를 고르기 전에는 숨겨둘 오브젝트들(실험 테이블 등). " +
             "proteinLoader의 오브젝트는 따로 지정하지 않아도 함께 처리된다.")]
    public GameObject[] extraStageObjects;

    /// <summary>진행 중인 퀘스트. 아직 고르지 않았으면 null.</summary>
    public QuestDefinition CurrentQuest { get; private set; }

    public QuestManagerSpatialUI.QuestStage CurrentStage { get; private set; }

    /// <summary>현재 단계의 진행률 0~1.</summary>
    public float CurrentProgress { get; private set; }

    /// <summary>현재 단계의 브리핑. 퀘스트나 단계 정의가 없으면 null.</summary>
    public QuestStageBriefing CurrentBriefing =>
        CurrentQuest != null ? CurrentQuest.FindStage(CurrentStage) : null;

    public bool IsRunning => CurrentQuest != null && !IsCompleted;
    public bool IsCompleted { get; private set; }

    public event Action<QuestDefinition> OnQuestStarted;
    public event Action<QuestStageBriefing> OnStageEntered;
    public event Action<QuestManagerSpatialUI.QuestStage> OnStageCompleted;
    public event Action<QuestDefinition> OnQuestCompleted;

    private void OnEnable()
    {
        // 씬 참조가 비어 있어도 조용히 죽지 않게 한 번 더 찾아본다. 인트로 동안 무대가 꺼져
        // 있으므로 비활성까지 뒤진다 — DockingQuestController.Awake와 같은 이유, 같은 방식.
        if (proteinLoader == null)
            proteinLoader = FindFirstObjectByType<ProteinLoader>(FindObjectsInactive.Include);
        // 하이라이터는 아예 씬에 저장돼 있지 않았다. 없으면 만들어 붙인다 — 없으면 변이 부위
        // 펄스와 번호표, 도입 시나리오의 "여기가 문제야" 연출이 전부 사라진다.
        if (mutationHighlighter == null)
            mutationHighlighter = MutationHighlighter.EnsureFor(proteinLoader);
        if (levelController == null && proteinLoader != null)
            levelController = proteinLoader.GetComponent<StructureLevelController>();

        if (proteinLoader != null) proteinLoader.OnLoaded += HandleStructureLoaded;
    }

    private void OnDisable()
    {
        if (proteinLoader != null) proteinLoader.OnLoaded -= HandleStructureLoaded;
    }

    private const QuestManagerSpatialUI.QuestStage FirstStage =
        QuestManagerSpatialUI.QuestStage.Quest1_DiseaseAnalysis;
    private const QuestManagerSpatialUI.QuestStage LastStage =
        QuestManagerSpatialUI.QuestStage.Quest5_Verification;

    /// <summary>퀘스트를 시작하고 1단계로 들어간다.</summary>
    public void StartQuest(QuestDefinition quest)
    {
        if (quest == null)
        {
            Debug.LogError("[QuestSession] 시작할 퀘스트가 null입니다.", this);
            return;
        }

        CurrentQuest = quest;
        IsCompleted = false;

        ApplyQuestToScene(quest);
        OnQuestStarted?.Invoke(quest);

        EnterStage(FirstStage);
    }

    /// <summary>현재 단계의 진행률을 갱신한다. 예: 변이 부위 3개 중 1개 확인 -> 0.33</summary>
    public void SetProgress(float progress01)
    {
        CurrentProgress = Mathf.Clamp01(progress01);
        PushToPanel();
    }

    /// <summary>현재 단계를 완료 처리하고 다음 단계로 넘어간다. 마지막이면 퀘스트를 끝낸다.</summary>
    public void CompleteCurrentStage()
    {
        if (!IsRunning) return;

        QuestManagerSpatialUI.QuestStage finished = CurrentStage;
        CurrentProgress = 1f;
        PushToPanel();
        OnStageCompleted?.Invoke(finished);

        if (finished >= LastStage)
        {
            IsCompleted = true;
            OnQuestCompleted?.Invoke(CurrentQuest);
            return;
        }

        EnterStage(finished + 1);
    }

    /// <summary>단계를 건너뛰거나 되돌릴 때 쓴다. (디버그, 또는 퀘스트 재개)</summary>
    public void JumpToStage(QuestManagerSpatialUI.QuestStage stage)
    {
        if (CurrentQuest == null) return;
        EnterStage(stage);
    }

    private void EnterStage(QuestManagerSpatialUI.QuestStage stage)
    {
        CurrentStage = stage;
        CurrentProgress = 0f;
        PushToPanel();

        // 브리핑이 없어도 이벤트는 발생시킨다. 데이터가 덜 채워진 퀘스트에서도
        // 진행 자체는 막히지 않아야 하고, 구독자는 null을 보고 넘어갈 수 있다.
        OnStageEntered?.Invoke(CurrentBriefing);
    }

    private void PushToPanel()
    {
        if (questPanel == null) return;

        questPanel.currentStage = CurrentStage;
        questPanel.currentStageProgress = CurrentProgress;
    }

    /// <summary>
    /// 퀘스트가 가리키는 구조와 변이 부위를 씬 컴포넌트에 밀어 넣는다.
    ///
    /// ProteinLoader는 Start()에서 로드를 시작하므로, 이미 로드가 끝난 뒤에 경로만 바꾸면
    /// 아무 일도 일어나지 않는다. 그래서 경로가 실제로 달라졌을 때만 다시 로드한다.
    /// </summary>
    private void ApplyQuestToScene(QuestDefinition quest)
    {
        if (mutationHighlighter != null && quest.mutationResidueIds != null)
        {
            mutationHighlighter.mutationSites.Clear();
            foreach (int residueId in quest.mutationResidueIds)
            {
                mutationHighlighter.mutationSites.Add(new MutationHighlighter.MutationSite
                {
                    residueId = residueId,
                    description = $"{quest.gene} {quest.mutation}",
                    alias = quest.mutationSiteAlias,
                });
            }
        }

        // 나선 단계에서 변이 자리를 띠로 짚어준다. 반드시 Reload() 앞에서 — 띠는 구조를 읽은 뒤
        // 나선을 만들 때 함께 얹히므로, 로드가 끝난 다음에 넣으면 이번 구조에는 반영되지 않는다.
        if (levelController != null)
            levelController.SetTargetResidues(quest.mutationResidueIds);

        if (proteinLoader == null) return;

        // 켜는 것이 먼저다. 꺼진 오브젝트에서는 코루틴을 시작할 수 없어 Reload가 그냥 실패한다.
        SetStageVisible(true);

        if (!string.IsNullOrEmpty(quest.structureStreamingPath))
            proteinLoader.streamingAssetsRelativePath = quest.structureStreamingPath;

        proteinLoader.Reload();
    }

    /// <summary>
    /// 퀘스트 무대(분자, 테이블)를 켜고 끈다.
    ///
    /// 인트로에서는 아직 아무것도 고르지 않았으므로 무대가 보이면 안 된다.
    /// 렌더러만 끄지 않고 오브젝트째 끄는 이유: ProteinLoader가 Start에서 로딩을 시작하는데,
    /// 꺼져 있으면 그 시점이 미뤄져서 <b>고른 구조만</b> 읽게 된다.
    /// 렌더러만 껐다면 인트로 동안 쓰지도 않을 구조를 먼저 읽느라
    /// 원자 2천 개의 O(n²) 결합 계산이 헛돌았을 것이다.
    /// </summary>
    public void SetStageVisible(bool visible)
    {
        // 순서가 중요하다. ProteinAnchor는 보통 ExperimentTableRoot의 자식이라,
        // 테이블이 꺼진 채로 앵커만 켜면 계층상 여전히 비활성이고
        // ProteinLoader.Reload가 코루틴을 시작하지 못해 조용히 실패한다.
        // 켤 때는 부모(테이블)부터, 끌 때는 자식부터.
        if (visible) SetExtrasVisible(true);

        if (proteinLoader != null && proteinLoader.gameObject.activeSelf != visible)
            proteinLoader.gameObject.SetActive(visible);

        if (!visible) SetExtrasVisible(false);
    }

    private void SetExtrasVisible(bool visible)
    {
        if (extraStageObjects == null) return;

        foreach (GameObject go in extraStageObjects)
            if (go != null && go.activeSelf != visible) go.SetActive(visible);
    }

    /// <summary>
    /// 구조 로딩이 끝나면 생성된 원자를 하이라이터에 넘긴다.
    ///
    /// 이 연결이 없으면 <see cref="MutationHighlighter"/>는 자기가 어떤 원자를 맡았는지 몰라
    /// 변이 부위 강조도, 잔기 선택도 전혀 동작하지 않는다. 원자는 로딩이 끝나야 생기므로
    /// 이 시점에만 넘길 수 있다.
    /// </summary>
    private void HandleStructureLoaded(ProteinLoader.ProteinData data)
    {
        if (mutationHighlighter == null || proteinLoader == null) return;

        mutationHighlighter.IndexAtoms(proteinLoader.GetComponentsInChildren<AtomInfo>(includeInactive: true));
    }
}
