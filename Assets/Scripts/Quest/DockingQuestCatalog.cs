using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// F-04 도킹 퀘스트 카탈로그 — 퀘스트 진행의 단일 진입점.
///
/// StreamingAssets/quests/index.json 에 나열된 퀘스트 정의(JSON)를 순서대로 읽고,
/// StartQuest() 호출 한 번으로 아래를 일괄 배선한다:
///   1. DockingQuestController.ApplyDefinition() — 타깃 잔기/포켓/상태 초기화
///   2. StructureLevelController.SetHelixRegions() — 구조별 Helix 구간 교체 (있을 때)
///   3. ProteinLoader.LoadStructure() — 단백질 구조 교체 로드
///   4. CompoundSelectionPanel.LoadCompounds() — 후보물질 박스 교체
///
/// 퀘스트 추가 절차 (코드/씬 수정 불필요):
///   ① 구조 JSON 생성: python pdb_parser_script.py <UniProtID>  (+필요 시 변이 후처리)
///   ② 후보물질 JSON들을 compounds/ 아래에 추가
///   ③ 퀘스트 정의 JSON 1개를 quests/ 아래에 작성
///   ④ quests/index.json 목록에 파일명 추가
/// </summary>
public class DockingQuestCatalog : MonoBehaviour
{
    [Header("데이터 소스")]
    public string questsFolder = "quests";
    public string indexFile = "index.json";

    [Header("참조")]
    public ProteinLoader proteinLoader;
    public CompoundSelectionPanel selectionPanel;
    public DockingQuestController dockingController;
    [Tooltip("있으면 퀘스트별 helix_regions를 주입")]
    public StructureLevelController levelController;
    [Tooltip("인트로에서 퀘스트를 고르는 세션. 있으면 자동 시작 대신 세션이 고른 퀘스트에 맞는 " +
             "도킹 정의를 적용한다. 비우면 씬에서 자동 탐색")]
    public QuestSession questSession;

    [Header("진행 설정")]
    [Tooltip("카탈로그 로드 완료 시 첫 퀘스트 자동 시작")]
    public bool autoStartFirstQuest = true;
    [Tooltip("정답 도킹 성공 시 일정 시간 후 다음 퀘스트로 자동 전환")]
    public bool autoAdvanceOnSuccess = true;
    public float autoAdvanceDelay = 6f;

    public IReadOnlyList<DockingQuestDefinition> Quests => _quests;
    public int CurrentIndex { get; private set; } = -1;
    public DockingQuestDefinition CurrentQuest =>
        CurrentIndex >= 0 && CurrentIndex < _quests.Count ? _quests[CurrentIndex] : null;

    public event Action<DockingQuestDefinition> OnQuestStarted;
    public event Action OnAllQuestsCompleted;

    private readonly List<DockingQuestDefinition> _quests = new List<DockingQuestDefinition>();

    private void Awake()
    {
        // levelController 참조 누락 시 퀘스트별 helix_regions 주입이 통째로 빠져
        // 리본 클릭 → Helix 전환이 안 되는 사고를 방지 — ProteinLoader와 같은 GO에서 자동 획득
        if (levelController == null && proteinLoader != null)
            levelController = proteinLoader.GetComponent<StructureLevelController>();
        if (levelController == null)
            levelController = FindFirstObjectByType<StructureLevelController>();

        // 인트로(퀘스트 보드)가 있는 씬에서는 세션이 퀘스트 선택을 주도한다
        if (questSession == null)
            questSession = FindFirstObjectByType<QuestSession>(FindObjectsInactive.Include);

        // 카탈로그가 로드를 주도하므로 개별 컴포넌트의 자동 로드는 끈다 (이중 로드 방지).
        if (autoStartFirstQuest)
        {
            if (proteinLoader != null) proteinLoader.loadOnStart = false;
            if (selectionPanel != null) selectionPanel.autoLoadOnStart = false;
        }

        // 패널의 배치/표시 연동 참조가 비어 있으면 카탈로그 참조로 자동 배선
        if (selectionPanel != null)
        {
            if (selectionPanel.proteinLoader == null) selectionPanel.proteinLoader = proteinLoader;
            if (selectionPanel.levelController == null) selectionPanel.levelController = levelController;
            if (selectionPanel.placementAnchor == null && proteinLoader != null)
                selectionPanel.placementAnchor = proteinLoader.transform;
        }
        if (dockingController != null && dockingController.levelController == null)
            dockingController.levelController = levelController;
    }

    private void OnEnable()
    {
        if (dockingController != null) dockingController.OnDockingFinished += HandleDockingFinished;
        if (questSession != null) questSession.OnQuestStarted += ApplyForSessionQuest;
    }

    private void OnDisable()
    {
        if (dockingController != null) dockingController.OnDockingFinished -= HandleDockingFinished;
        if (questSession != null) questSession.OnQuestStarted -= ApplyForSessionQuest;
    }

    private IEnumerator Start()
    {
        string baseUrl = $"{Application.streamingAssetsPath}/{questsFolder}";

        QuestCatalogData index = null;
        yield return Fetch($"{baseUrl}/{indexFile}",
            text => index = JsonUtility.FromJson<QuestCatalogData>(text));
        if (index == null || index.quests == null || index.quests.Count == 0)
        {
            Debug.LogError($"[DockingQuestCatalog] {indexFile}을 읽지 못했거나 퀘스트 목록이 비어 있습니다.");
            yield break;
        }

        foreach (string file in index.quests)
        {
            DockingQuestDefinition def = null;
            yield return Fetch($"{baseUrl}/{file}",
                text => def = JsonUtility.FromJson<DockingQuestDefinition>(text));
            if (def != null) _quests.Add(def);
        }
        Debug.Log($"[DockingQuestCatalog] 퀘스트 {_quests.Count}개 로드 완료");

        if (questSession != null)
        {
            // 인트로가 퀘스트 선택을 주도한다. 정의 로딩보다 먼저 골랐다면 지금 적용한다.
            if (questSession.CurrentQuest != null) ApplyForSessionQuest(questSession.CurrentQuest);
        }
        else if (autoStartFirstQuest && _quests.Count > 0)
        {
            StartQuest(0);
        }
    }

    private IEnumerator Fetch(string url, Action<string> onSuccess)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogError($"[DockingQuestCatalog] 로딩 실패: {req.error} ({url})");
            else
                onSuccess(req.downloadHandler.text);
        }
    }

    public void StartQuest(string questId)
    {
        int idx = _quests.FindIndex(q => q.id == questId);
        if (idx < 0) { Debug.LogError($"[DockingQuestCatalog] 퀘스트 id를 찾을 수 없음: {questId}"); return; }
        StartQuest(idx);
    }

    public void StartQuest(int index)
    {
        ApplyQuest(index, loadStructure: true);
    }

    /// <summary>
    /// 인트로(QuestSession)에서 고른 퀘스트에 맞는 도킹 정의를 찾아 적용한다.
    /// 구조 로드는 QuestSession이 이미 수행하므로 여기서는 다시 로드하지 않는다.
    /// 매칭되는 정의가 없으면 아무것도 덮어쓰지 않는다 — 특히 Helix 구간을 다른 구조의
    /// 값으로 덮으면 리본의 클릭 유도 펄스와 단계 전환이 통째로 죽는다(프리팹 기본값 유지).
    /// </summary>
    private void ApplyForSessionQuest(QuestDefinition quest)
    {
        if (quest == null) return;
        if (_quests.Count == 0) return; // 정의 로딩이 끝나면 Start 끝에서 CurrentQuest로 재시도된다

        int idx = _quests.FindIndex(q =>
            q.id == quest.questId ||
            (!string.IsNullOrEmpty(q.protein_json) && q.protein_json == quest.structureStreamingPath));
        if (idx < 0)
        {
            Debug.Log($"[DockingQuestCatalog] '{quest.questId}'에 해당하는 도킹 정의가 없어 기본 설정을 유지합니다.");
            return;
        }

        ApplyQuest(idx, loadStructure: false);
    }

    private void ApplyQuest(int index, bool loadStructure)
    {
        if (index < 0 || index >= _quests.Count) return;
        CurrentIndex = index;
        DockingQuestDefinition def = _quests[index];
        Debug.Log($"[DockingQuestCatalog] 퀘스트 시작: {def.title} ({def.id})");

        // 순서 중요: 컨트롤러 정리(이전 구조 원복) → Helix 구간 교체 → 구조 로드 → 화합물 로드
        if (dockingController != null) dockingController.ApplyDefinition(def);

        if (levelController != null)
        {
            if (def.helix_regions != null)
            {
                var regions = new List<StructureLevelController.HelixRegion>();
                foreach (var r in def.helix_regions)
                    regions.Add(new StructureLevelController.HelixRegion
                    {
                        label = r.label,
                        startResId = r.start_res_id,
                        endResId = r.end_res_id
                    });
                levelController.SetHelixRegions(regions);
            }

            // 아미노산 단계 구간 필터가 켜져 있어도 도킹 타깃/포켓 잔기는 항상 보이게
            var alwaysVisible = new List<int>();
            if (def.pocket_residue_ids != null) alwaysVisible.AddRange(def.pocket_residue_ids);
            if (def.target_residue_id > 0) alwaysVisible.Add(def.target_residue_id);
            levelController.SetAlwaysVisibleResidues(alwaysVisible);
        }

        if (loadStructure && proteinLoader != null && !string.IsNullOrEmpty(def.protein_json))
            proteinLoader.LoadStructure(def.protein_json);

        if (selectionPanel != null && def.compound_files != null && def.compound_files.Count > 0)
            selectionPanel.LoadCompounds(def.compounds_folder, def.compound_files);

        OnQuestStarted?.Invoke(def);
    }

    private void HandleDockingFinished(DockingOutcome outcome, CompoundData data)
    {
        // 세션(인트로 선택)이 있는 씬에서는 진행 순서를 세션이 소유한다 —
        // 카탈로그가 멋대로 다음 도킹 퀘스트로 구조를 갈아치우면 안 된다.
        if (questSession != null) return;
        if (outcome != DockingOutcome.Success || !autoAdvanceOnSuccess) return;

        if (CurrentIndex + 1 < _quests.Count)
            StartCoroutine(AdvanceAfterDelay(CurrentIndex + 1));
        else
            OnAllQuestsCompleted?.Invoke();
    }

    private IEnumerator AdvanceAfterDelay(int nextIndex)
    {
        yield return new WaitForSeconds(autoAdvanceDelay);
        StartQuest(nextIndex);
    }
}
