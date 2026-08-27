using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F-04.3 후보물질 도킹 퀘스트 (KRAS G12C 시나리오).
/// CompoundSelectionPanel에서 화합물을 선택하면:
///   1. 단백질의 Switch-II Pocket 잔기들이 펄스 발광(시각 효과)
///   2. 화합물 분자가 포켓을 향해 날아가는 도킹 연출
///   3. 결과별 이펙트:
///      - Success     : Cys 황(S) 원자 섬광 + 공유결합 실린더 생성 + 포켓 녹색 락인
///      - NoWarhead   : 포켓 진입 후 고정 실패, 튕겨 나옴 (주황)
///      - StericClash : 포켓 입구에서 충돌, 빨간 충돌 셸 이펙트
///      - OffTarget   : 접근 중 자석 반발처럼 밀려남 (적색)
/// 잔기 번호는 로드된 구조 JSON의 res_id 기준이며 Inspector에서 조정한다.
/// (KRAS P01116 구조라면 targetResidueId=12(G12C의 Cys), 포켓은 Switch-II 주변 잔기)
/// </summary>
public class DockingQuestController : MonoBehaviour
{
    [Header("참조")]
    public ProteinLoader proteinLoader;
    public CompoundSelectionPanel selectionPanel;
    [Tooltip("있으면 도킹 시작 시 아미노산(원자) 표시 레벨로 강제 전환")]
    public StructureLevelController levelController;
    [Tooltip("있으면 정답 도킹 성공 시 현재 퀘스트 단계 완료 처리")]
    public QuestManagerSpatialUI questUI;
    [Tooltip("공유결합 연출용 실린더 프리팹 (비우면 ProteinLoader.bondPrefab 재사용)")]
    public GameObject covalentBondPrefab;
    [Tooltip("p53 Y220C 열안정성 퀘스트에서만 쓰인다. 있으면 정답(안정화제) 도킹 성공 시 " +
             "wobble을 가라앉히고, 부분 정답/오답 결과에 맞는 HUD 지표를 갱신한다. 비우면 무시된다.")]
    public ThermalStabilityController thermal;
    [Tooltip("위와 같은 조건 — 결과별 p53 총량/DNA 결합능/독성 경고 HUD를 갱신한다.")]
    public ThermalStabilityHUD hud;
    [Tooltip("CFTR F508del(사건 4) corrector/potentiator 퀘스트에서만 쓰인다. 있으면 화합물별 " +
             "성공/실패/순서오류 결과를 넘겨줘 8EJ1→8EIQ 구조 스왑, wobble 완화, gate/Cl- 연출, " +
             "HUD(Surface CFTR/Channel activity) 갱신을 맡긴다. 비우면 무시된다.")]
    public CftrRescueController cftr;

    [Header("타깃 부위 (res_id는 로드된 구조 JSON 기준 — QuestCatalog 사용 시 퀘스트 JSON이 덮어씀)")]
    [Tooltip("공유결합 대상 잔기 — KRAS G12C의 Cys12")]
    public int targetResidueId = 12;
    [Tooltip("공유결합 대상 원자 이름. 없으면 element S → CA 순으로 폴백")]
    public string targetAtomName = "SG";
    [Tooltip("Switch-II Pocket을 구성하는 잔기 목록 (하이라이트 대상)")]
    public List<int> pocketResidueIds = new List<int> { 12, 62, 68, 95, 96, 99 };

    [Header("연출 설정")]
    [Tooltip("포켓 중심에서 입구까지의 거리 (unit)")]
    public float entranceOffset = 0.8f;
    public float approachDuration = 1.4f;
    public Color pocketHighlightColor = new Color(0.2f, 0.9f, 1f);
    public Color successColor = new Color(0.25f, 1f, 0.35f);
    public Color noWarheadColor = new Color(1f, 0.6f, 0.1f);
    public Color failColor = new Color(1f, 0.18f, 0.12f);
    [Tooltip("도킹을 시작하기 전, 포켓/타깃 잔기 덩어리가 리본 구간과 서열상 떨어져 있어도 " +
             "'끊긴 조각'이 아니라 의도된 관심 부위임을 알 수 있도록 항상 입히는 은은한 표시색")]
    public Color pocketMarkerColor = new Color(0.85f, 0.35f, 0.95f);

    /// <summary>도킹 연출이 끝날 때 발생. Success면 퀘스트 통과.</summary>
    /// <summary>
    /// 도킹 시도가 끝날 때마다 발생한다. 성공·실패·순서 오류를 모두 싣는다.
    /// <see cref="AIAssistantBrain"/>이 이걸 받아 결과를 설명한다.
    /// </summary>
    public event Action<DockingResult> OnDockingFinished;

    private readonly List<AtomInfo> _pocketAtoms = new List<AtomInfo>();
    private readonly List<GameObject> _questSpawned = new List<GameObject>(); // 락인된 클론·공유결합 등 퀘스트 산출물
    // 여러 화합물이 순서대로 필요한 퀘스트(예: CFTR corrector -> potentiator)에서
    // "이미 Success한 화합물 id"를 기억해, 아직 순서가 안 된 화합물의 requires_prior_success_id를 검사한다.
    private readonly HashSet<string> _succeededCompoundIds = new HashSet<string>();
    private AtomInfo _targetSulfur;   // Cys12의 SG (없으면 해당 잔기 CA로 폴백)
    private bool _indexed;
    private bool _questCompleted;
    private Coroutine _pulseRoutine;

    /// <summary>
    /// 퀘스트 정의(JSON)를 적용하고 진행 상태를 초기화한다.
    /// QuestCatalog가 구조/화합물 로드 직전에 호출한다. 실행 중인 연출은 중단·정리된다.
    /// </summary>
    public void ApplyDefinition(DockingQuestDefinition def)
    {
        StopAllCoroutines();
        _pulseRoutine = null;
        RestorePocketColors(); // 이전 구조가 아직 살아있는 동안 하이라이트 원복
        foreach (var go in _questSpawned)
            if (go != null) Destroy(go);
        _questSpawned.Clear();

        targetResidueId = def.target_residue_id;
        if (!string.IsNullOrEmpty(def.target_atom_name)) targetAtomName = def.target_atom_name;
        if (def.pocket_residue_ids != null && def.pocket_residue_ids.Count > 0)
            pocketResidueIds = new List<int>(def.pocket_residue_ids);
        if (def.entrance_offset > 0f) entranceOffset = def.entrance_offset;

        _questCompleted = false;
        _indexed = false;
        _succeededCompoundIds.Clear();

        if (thermal != null) thermal.SetStabilized(false);
        if (hud != null) { hud.SetDnaBindingCompetent(false); hud.HideWarning(); hud.SetP53Quantity(0f); }
    }

    private void Awake()
    {
        // 씬 참조가 끊겨 있어도(프리팹 재직렬화로 stripped 참조가 깨진 사고가 실제로 있었다)
        // 도킹이 조용히 죽지 않도록 복구한다. 인트로 동안 무대가 꺼져 있으므로 비활성까지 탐색.
        if (proteinLoader == null)
            proteinLoader = FindFirstObjectByType<ProteinLoader>(FindObjectsInactive.Include);
        if (selectionPanel == null)
            selectionPanel = FindFirstObjectByType<CompoundSelectionPanel>(FindObjectsInactive.Include);
        if (levelController == null && proteinLoader != null)
            levelController = proteinLoader.GetComponent<StructureLevelController>();
        // 열안정성 퀘스트가 아닌 씬에는 이 둘이 아예 없을 수 있다 — 못 찾아도 조용히 null로 둔다.
        if (thermal == null) thermal = FindFirstObjectByType<ThermalStabilityController>(FindObjectsInactive.Include);
        if (hud == null) hud = FindFirstObjectByType<ThermalStabilityHUD>(FindObjectsInactive.Include);
        if (cftr == null) cftr = FindFirstObjectByType<CftrRescueController>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        if (selectionPanel != null) selectionPanel.OnCompoundChosen += HandleCompoundChosen;
        if (proteinLoader != null) proteinLoader.OnLoaded += HandleProteinLoaded;
        if (levelController != null) levelController.OnLevelChanged += HandleLevelChanged;
    }

    private void OnDisable()
    {
        if (selectionPanel != null) selectionPanel.OnCompoundChosen -= HandleCompoundChosen;
        if (proteinLoader != null) proteinLoader.OnLoaded -= HandleProteinLoaded;
        if (levelController != null) levelController.OnLevelChanged -= HandleLevelChanged;
    }

    private void HandleProteinLoaded(ProteinLoader.ProteinData data)
    {
        _indexed = false; // 재로드 시 다시 인덱싱
    }

    // 이전 버튼으로 Ribbon/Helix로 돌아가면 도킹 연출로 생성된 화합물 클론·공유결합 실린더는
    // 원자 단계에서만 의미가 있으므로 함께 숨긴다. 다시 아미노산 단계로 내려오면 그대로 복원된다 —
    // 성공 락인 상태(포켓에 남은 클론)는 지우지 않고 유지한다.
    //
    // 아미노산 단계로 내려올 때 포켓/타깃 잔기를 바로 인덱싱해 표시색을 입힌다. 이 잔기들은
    // 리본에서 고른 Helix 구간과 서열상 멀리 떨어져 있어(StructureLevelController의
    // "항상 표시" 잔기) 결합으로 이어지지 않는 별개 덩어리로 보인다 — 표시색이 없으면
    // 도킹을 시작하기도 전에 "끊어진 원자"처럼 보인다.
    private void HandleLevelChanged(StructureLevelController.ViewLevel level)
    {
        bool visible = level == StructureLevelController.ViewLevel.AminoAcid;
        foreach (var go in _questSpawned)
            if (go != null) go.SetActive(visible);

        if (visible && proteinLoader != null)
        {
            if (!_indexed) IndexPocketAtoms();
            if (!_questCompleted && _pulseRoutine == null) ApplyPocketMarker();
        }
    }

    // 씬에 생성된 원자들 중 포켓 잔기/타깃 황 원자를 찾아둔다.
    private void IndexPocketAtoms()
    {
        _pocketAtoms.Clear();
        _targetSulfur = null;
        AtomInfo byName = null, byElement = null, targetCA = null;

        foreach (var atom in proteinLoader.GetComponentsInChildren<AtomInfo>(true))
        {
            if (pocketResidueIds.Contains(atom.ResidueId) || atom.ResidueId == targetResidueId)
                _pocketAtoms.Add(atom);

            if (atom.ResidueId == targetResidueId)
            {
                if (atom.AtomName == targetAtomName) byName = atom;
                if (atom.Element == "S" && byElement == null) byElement = atom;
                if (atom.AtomName == "CA") targetCA = atom;
            }
        }

        _targetSulfur = byName != null ? byName : (byElement != null ? byElement : targetCA);
        _indexed = true;

        if (_pocketAtoms.Count == 0)
            Debug.LogWarning($"[DockingQuest] 포켓 잔기({string.Join(",", pocketResidueIds)})에 해당하는 원자를 찾지 못했습니다. " +
                             "로드된 구조의 res_id 범위를 확인하세요.");
    }

    /// <summary>도킹 시도 전/후 대기 상태의 은은한 표시색. 펄스나 결과색과 달리 고정된 톤이다.</summary>
    private void ApplyPocketMarker()
    {
        TintPocket(pocketMarkerColor, emission: 0.5f);
    }

    private void HandleCompoundChosen(CompoundSlot slot)
    {
        if (_questCompleted) return;
        StartCoroutine(DockingRoutine(slot));
    }

    private IEnumerator DockingRoutine(CompoundSlot slot)
    {
        // Interactable을 잠그기 전에 확인한다 — 여기서 죽으면 패널이 영영 클릭 불능이 된다
        if (proteinLoader == null)
        {
            Debug.LogError("[DockingQuest] ProteinLoader 참조가 없어 도킹을 진행할 수 없습니다. " +
                           "DockingQuest 오브젝트의 Protein Loader 참조를 확인하세요.", this);
            yield break;
        }

        selectionPanel.Interactable = false;
        selectionPanel.ClearResult();
        if (hud != null) hud.HideWarning(); // 새 시도마다 이전 경고 배너를 지운다

        // 원자 단위 표시로 전환 (도킹은 원자 레벨에서만 의미가 있다)
        if (levelController != null && levelController.CurrentLevel != StructureLevelController.ViewLevel.AminoAcid)
            levelController.SetLevel(StructureLevelController.ViewLevel.AminoAcid);
        else if (levelController == null)
            proteinLoader.SetAtomsVisible(true);

        if (!_indexed) IndexPocketAtoms();

        // --- 1. 선택 즉시: 해당 단백질 부위(포켓) 펄스 하이라이트 ---
        Vector3 pocketCenter = GetPocketCenterWorld();
        _pulseRoutine = StartCoroutine(PulsePocket(pocketHighlightColor));

        // --- 2. 화합물 클론 생성, 슬롯 → 포켓 입구로 비행 ---
        GameObject clone = UnityEngine.Object.Instantiate(slot.MoleculeRoot, proteinLoader.transform);
        clone.transform.position = slot.transform.position;
        clone.transform.localScale = Vector3.one; // 단백질과 같은 0.1/Å 스케일

        // Instantiate는 MaterialPropertyBlock(CPK 색/홀로 셸 색)을 복제하지 않으므로 원본에서 복사
        var srcRenderers = slot.MoleculeRoot.GetComponentsInChildren<Renderer>(true);
        var dstRenderers = clone.GetComponentsInChildren<Renderer>(true);
        var copyMpb = new MaterialPropertyBlock();
        for (int i = 0; i < Mathf.Min(srcRenderers.Length, dstRenderers.Length); i++)
        {
            srcRenderers[i].GetPropertyBlock(copyMpb);
            dstRenderers[i].SetPropertyBlock(copyMpb);
        }

        // PulseHighlight의 내부 참조는 복제 시 초기화되지 않으므로 재-Init
        foreach (var pulse in clone.GetComponentsInChildren<PulseHighlight>())
            pulse.Init(new Color(1f, 0.55f, 0.1f), 3f);
        _questSpawned.Add(clone); // 퀘스트 전환 시 일괄 정리 대상

        Vector3 outward = pocketCenter - proteinLoader.transform.position;
        if (outward.sqrMagnitude < 1e-6f) outward = slot.transform.position - pocketCenter;
        Vector3 entrance = pocketCenter + outward.normalized * entranceOffset;

        // 이 화합물이 먼저 성공해야 할 다른 화합물(requires_prior_success_id)을 아직 못 채웠다면,
        // 실제 outcome 대신 "순서 오류" 연출로 대체한다 — 예: potentiator를 corrector보다 먼저 고른 경우.
        // 오답 취급(StericClash/OffTarget)이 아니라 "맞는 방향이지만 아직 때가 아니다"라는 신호다.
        if (!string.IsNullOrEmpty(slot.Data.requires_prior_success_id) &&
            !_succeededCompoundIds.Contains(slot.Data.requires_prior_success_id))
        {
            yield return MoveTo(clone.transform, entrance, approachDuration, spin: true);
            yield return OrderErrorSequence(slot, clone, pocketCenter, outward);
            yield break;
        }

        DockingOutcome outcome = slot.Data.Outcome;

        switch (outcome)
        {
            case DockingOutcome.Success:
                yield return MoveTo(clone.transform, entrance, approachDuration, spin: true);
                yield return MoveTo(clone.transform, pocketCenter, 0.6f, spin: false);
                yield return SuccessSequence(slot, clone, pocketCenter);
                break;

            case DockingOutcome.NoWarhead:
                yield return MoveTo(clone.transform, entrance, approachDuration, spin: true);
                yield return MoveTo(clone.transform, pocketCenter, 0.6f, spin: false);
                yield return Shake(clone.transform, 0.5f, 0.02f); // 고정되지 않고 흔들림
                yield return MoveTo(clone.transform, entrance + outward.normalized * 1.2f, 0.5f, spin: true); // 튕겨 나옴
                FinishFailure(slot, clone, noWarheadColor, outcome);
                break;

            case DockingOutcome.StericClash:
                yield return MoveTo(clone.transform, entrance, approachDuration, spin: true);
                // 입구에서 걸림: 충돌 셸 + 흔들림
                StartCoroutine(BurstEffect(entrance, failColor, 0.7f, 0.9f));
                yield return Shake(clone.transform, 0.6f, 0.035f);
                yield return MoveTo(clone.transform, entrance + outward.normalized * 0.9f, 0.4f, spin: false);
                FinishFailure(slot, clone, failColor, outcome);
                break;

            case DockingOutcome.OffTarget:
                // 접근 도중 자석 반발: 입구 60% 지점에서 감속 후 밀려남
                Vector3 repelPoint = Vector3.Lerp(clone.transform.position, entrance, 0.6f);
                yield return MoveTo(clone.transform, repelPoint, approachDuration * 0.7f, spin: true);
                yield return Repel(clone.transform, (repelPoint - pocketCenter).normalized, 1.5f, 0.8f);
                FinishFailure(slot, clone, failColor, outcome);
                break;

            // --- p53 Y220C 열안정성 퀘스트 전용 (같은 Snap 판정 + 결과별 짧은 VFX/HUD만 다르다) ---

            case DockingOutcome.FragmentHit:
                // 포켓엔 들어가 잠깐 안정화 효과가 보이지만, 오래 붙어있지 못하고 이탈한다.
                yield return MoveTo(clone.transform, entrance, approachDuration, spin: true);
                yield return MoveTo(clone.transform, pocketCenter, 0.6f, spin: false);
                if (hud != null) hud.SetStability(0.35f, "낮음 (잠깐 붙었다 떨어짐)");
                yield return new WaitForSeconds(0.8f);
                yield return MoveTo(clone.transform, entrance + outward.normalized * 1.1f, 0.6f, spin: true);
                FinishFailure(slot, clone, noWarheadColor, outcome);
                if (thermal != null) thermal.SetTemperature(thermal.CurrentCelsius); // HUD를 온도 기준값으로 되돌림
                break;

            case DockingOutcome.WrongStrategy:
                // 이 포켓과 무관한 전략(예: MDM2 억제제) — 애초에 포켓에 들어가지 않고 입구에서 흩어진다.
                yield return MoveTo(clone.transform, entrance, approachDuration, spin: true);
                yield return Shake(clone.transform, 0.5f, 0.03f);
                if (hud != null) hud.SetP53Quantity(0.8f); // p53 총량은 늘지만
                yield return MoveTo(clone.transform, entrance + outward.normalized * 1.3f, 0.5f, spin: true);
                FinishFailure(slot, clone, failColor, outcome); // stability/wobble/DNA 결합능은 그대로 — 회복되지 않는다
                break;

            case DockingOutcome.NoStabilization:
                // 표적 원자 근처까지는 닿는다(proximity effect) — 그러나 안정화 상호작용은 형성되지 않는다.
                yield return MoveTo(clone.transform, entrance, approachDuration, spin: true);
                yield return MoveTo(clone.transform, pocketCenter, 0.6f, spin: false);
                Vector3 proximityPos = _targetSulfur != null ? _targetSulfur.transform.position : pocketCenter;
                yield return BurstEffect(proximityPos, new Color(1f, 0.7f, 0.3f), 0.3f, 0.4f);
                yield return new WaitForSeconds(0.4f);
                yield return MoveTo(clone.transform, entrance + outward.normalized * 1.0f, 0.5f, spin: true);
                FinishFailure(slot, clone, noWarheadColor, outcome); // wobble 유지 = 안정화 실패
                break;

            case DockingOutcome.NonSelective:
                // 이 포켓뿐 아니라 주변 다른 자리에도 동시에 비특이적 결합 마커가 나타난다.
                yield return MoveTo(clone.transform, entrance, approachDuration, spin: true);
                StartCoroutine(BurstEffect(pocketCenter + Vector3.up * 0.6f, failColor, 0.28f, 0.5f));
                StartCoroutine(BurstEffect(pocketCenter - proteinLoader.transform.right * 0.8f, failColor, 0.28f, 0.5f));
                if (hud != null) hud.ShowWarning("표적이 아닌 곳에도 마구 붙었어요 — 부작용 위험");
                yield return Shake(clone.transform, 0.4f, 0.02f);
                yield return MoveTo(clone.transform, entrance + outward.normalized * 0.9f, 0.4f, spin: true);
                FinishFailure(slot, clone, failColor, outcome);
                break;
        }
    }

    // --- 성공 연출: 섬광 → 공유결합 → 락인 ---
    private IEnumerator SuccessSequence(CompoundSlot slot, GameObject clone, Vector3 pocketCenter)
    {
        Vector3 sulfurPos = _targetSulfur != null ? _targetSulfur.transform.position : pocketCenter;

        // Cys12 황(S) 원자 섬광
        yield return BurstEffect(sulfurPos, Color.white, 0.45f, 0.5f);

        // Warhead 원자 ↔ S 원자 공유결합 실린더 생성
        Transform warhead = FindWarhead(clone);
        if (warhead != null && _targetSulfur != null)
        {
            GameObject prefab = covalentBondPrefab != null ? covalentBondPrefab : proteinLoader.bondPrefab;
            if (prefab != null)
            {
                Transform parent = proteinLoader.transform;
                GameObject bond = UnityEngine.Object.Instantiate(prefab, parent);
                Vector3 la = parent.InverseTransformPoint(warhead.position);
                Vector3 lb = parent.InverseTransformPoint(_targetSulfur.transform.position);
                bond.transform.localPosition = (la + lb) * 0.5f;
                // 위치/길이와 같은 로컬 좌표계(la/lb)로 방향도 맞춘다 — 앵커가 회전해 있어도 정확히 잇는다
                bond.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (lb - la).normalized);
                bond.transform.localScale = new Vector3(0.05f, Vector3.Distance(la, lb) * 0.5f, 0.05f);
                RuntimeMaterials.ApplySolid(bond); // 홀로그램 재질은 녹색 틴트를 무시
                CompoundMoleculeBuilder.Tint(bond, successColor);
                _questSpawned.Add(bond);
            }
        }

        // 포켓 락인: 펄스 중지 후 녹색 고정
        StopPocketPulse();
        TintPocket(successColor, emission: 0.8f);
        slot.SetResultColor(successColor);
        selectionPanel.ShowResult(slot.Data, successColor);

        // p53 Y220C 퀘스트: 안정화제가 포켓에 락인되면 wobble이 가라앉고
        // DNA 결합능이 회복된 것으로 표시한다(HUD Before/After 비교의 "After" 상태).
        if (thermal != null) thermal.SetStabilized(true);
        if (hud != null) hud.SetDnaBindingCompetent(true);

        _succeededCompoundIds.Add(slot.Data.id);
        if (cftr != null) cftr.HandleCompoundSuccess(slot.Data.id);

        // completes_stage가 false인 화합물(예: CFTR corrector)은 그 자체로는 충분하지 않다 —
        // 단계를 끝내지 않고 패널을 다시 열어, 뒤이어 필요한 화합물(potentiator 등)을 고를 수 있게 한다.
        if (slot.Data.completes_stage)
        {
            _questCompleted = true;
            if (questUI != null) questUI.CompleteCurrentStageAndAdvance();
        }
        else
        {
            selectionPanel.Interactable = true;
        }

        OnDockingFinished?.Invoke(new DockingResult
        {
            Outcome = DockingOutcome.Success,
            Compound = slot.Data,
            Message = slot.Data.result_message,
        });
        // 성공 시 clone은 포켓에 그대로 남긴다 (KRAS OFF 락인 상태 / CFTR 락인 상태)
    }

    /// <summary>
    /// "순서 오류" 연출: 오답도 성공도 아니다 — 표적 자리에는 닿았지만 아직 준비가 안 된 상태라
    /// 짧게 반응만 하고 물러난다. requires_prior_success_id 조건이 충족되지 않았을 때만 호출된다.
    /// </summary>
    private IEnumerator OrderErrorSequence(CompoundSlot slot, GameObject clone, Vector3 pocketCenter, Vector3 outward)
    {
        StartCoroutine(BurstEffect(pocketCenter, pocketHighlightColor, 0.4f, 0.5f));
        yield return Shake(clone.transform, 0.35f, 0.015f);
        yield return MoveTo(clone.transform, pocketCenter + outward.normalized * (entranceOffset + 0.3f), 0.4f, spin: true);
        UnityEngine.Object.Destroy(clone);

        StopPocketPulse();
        ApplyPocketMarker();

        Color orderColor = new Color(1f, 0.85f, 0.25f); // 경고색 — 실패(빨강)도 성공(초록)도 아니다
        slot.SetResultColor(orderColor);
        selectionPanel.ShowResult(slot.Data, orderColor,
            messageOverride: slot.Data.order_error_message,
            affinityOverride: "먼저 다른 후보물질이 필요해요");
        selectionPanel.Interactable = true;
        if (cftr != null) cftr.HandleOrderError(slot.Data.id);

        OnDockingFinished?.Invoke(new DockingResult
        {
            Outcome = DockingOutcome.NoWarhead, // 연출 계열은 NoWarhead와 같지만 판정은 다르다
            Compound = slot.Data,
            IsOrderError = true,
            Message = slot.Data.order_error_message,
        });
    }

    private void FinishFailure(CompoundSlot slot, GameObject clone, Color color, DockingOutcome outcome)
    {
        StopPocketPulse();
        ApplyPocketMarker(); // 완전히 원래 색으로 되돌리면 다시 "끊긴 원자"처럼 보이므로 표시색을 유지한다
        UnityEngine.Object.Destroy(clone);

        slot.SetResultColor(color);
        selectionPanel.ShowResult(slot.Data, color);
        selectionPanel.Interactable = true; // 재도전 허용
        if (cftr != null) cftr.HandleCompoundFailure(slot.Data.id, outcome);
        OnDockingFinished?.Invoke(new DockingResult
        {
            Outcome = outcome,
            Compound = slot.Data,
            Message = slot.Data.result_message,
        });
    }

    // --- 이펙트/헬퍼 ---

    private Vector3 GetPocketCenterWorld()
    {
        if (_pocketAtoms.Count == 0)
            return _targetSulfur != null ? _targetSulfur.transform.position : proteinLoader.transform.position;

        Vector3 sum = Vector3.zero;
        foreach (var a in _pocketAtoms) sum += a.transform.position;
        return sum / _pocketAtoms.Count;
    }

    private static Transform FindWarhead(GameObject clone)
    {
        var pulses = clone.GetComponentsInChildren<PulseHighlight>();
        return pulses.Length > 0 ? pulses[0].transform : null;
    }

    private IEnumerator MoveTo(Transform t, Vector3 target, float duration, bool spin)
    {
        Vector3 from = t.position;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (t == null) yield break;
            elapsed += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            t.position = Vector3.Lerp(from, target, k);
            if (spin) t.Rotate(Vector3.up, 90f * Time.deltaTime, Space.Self);
            yield return null;
        }
        if (t != null) t.position = target;
    }

    private IEnumerator Shake(Transform t, float duration, float amplitude)
    {
        Vector3 origin = t.position;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (t == null) yield break;
            elapsed += Time.deltaTime;
            t.position = origin + UnityEngine.Random.insideUnitSphere * amplitude;
            yield return null;
        }
        if (t != null) t.position = origin;
    }

    private IEnumerator Repel(Transform t, Vector3 direction, float distance, float duration)
    {
        Vector3 from = t.position;
        Vector3 target = from + direction * distance;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (t == null) yield break;
            elapsed += Time.deltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / duration), 3f); // ease-out: 처음에 강하게 밀림
            t.position = Vector3.Lerp(from, target, k);
            t.Rotate(Vector3.one, 180f * Time.deltaTime, Space.Self);
            yield return null;
        }
    }

    /// <summary>구체가 커지며 사라지는 섬광/충돌 이펙트.</summary>
    private IEnumerator BurstEffect(Vector3 worldPos, Color color, float maxScale, float duration)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        UnityEngine.Object.Destroy(sphere.GetComponent<Collider>());
        sphere.transform.position = worldPos;

        var renderer = sphere.GetComponent<Renderer>();
        var mpb = new MaterialPropertyBlock();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            sphere.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, maxScale, k);
            Color c = Color.Lerp(color, color * 0.1f, k); // 점점 어두워지며 소멸
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_EmissionColor", c * 2.5f);
            renderer.SetPropertyBlock(mpb);
            yield return null;
        }
        UnityEngine.Object.Destroy(sphere);
    }

    private IEnumerator PulsePocket(Color color)
    {
        while (true)
        {
            float t = (Mathf.Sin(Time.time * 4f) + 1f) * 0.5f;
            TintPocket(Color.Lerp(color * 0.5f, color, t), emission: 1.2f);
            yield return null;
        }
    }

    private void StopPocketPulse()
    {
        if (_pulseRoutine != null)
        {
            StopCoroutine(_pulseRoutine);
            _pulseRoutine = null;
        }
    }

    private void TintPocket(Color color, float emission)
    {
        foreach (var atom in _pocketAtoms)
        {
            if (atom == null) continue;
            var renderer = atom.GetComponent<Renderer>();
            if (renderer == null) continue;
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", color);
            mpb.SetColor("_EmissionColor", color * emission);
            renderer.SetPropertyBlock(mpb);
        }
    }

    // 하이라이트 해제: pLDDT 신뢰도 색으로 복원
    private void RestorePocketColors()
    {
        foreach (var atom in _pocketAtoms)
        {
            if (atom == null) continue;
            var renderer = atom.GetComponent<Renderer>();
            if (renderer == null) continue;
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", PLDDTColorizer.GetColorForPLDDT(atom.PLDDT));
            mpb.SetColor("_EmissionColor", Color.black);
            renderer.SetPropertyBlock(mpb);
        }
    }
}
