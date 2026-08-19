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

    /// <summary>도킹 연출이 끝날 때 발생. Success면 퀘스트 통과.</summary>
    public event Action<DockingOutcome, CompoundData> OnDockingFinished;

    private readonly List<AtomInfo> _pocketAtoms = new List<AtomInfo>();
    private readonly List<GameObject> _questSpawned = new List<GameObject>(); // 락인된 클론·공유결합 등 퀘스트 산출물
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
    }

    private void OnEnable()
    {
        if (selectionPanel != null) selectionPanel.OnCompoundChosen += HandleCompoundChosen;
        if (proteinLoader != null) proteinLoader.OnLoaded += HandleProteinLoaded;
    }

    private void OnDisable()
    {
        if (selectionPanel != null) selectionPanel.OnCompoundChosen -= HandleCompoundChosen;
        if (proteinLoader != null) proteinLoader.OnLoaded -= HandleProteinLoaded;
    }

    private void HandleProteinLoaded(ProteinLoader.ProteinData data)
    {
        _indexed = false; // 재로드 시 다시 인덱싱
    }

    // 씬에 생성된 원자들 중 포켓 잔기/타깃 황 원자를 찾아둔다.
    private void IndexPocketAtoms()
    {
        _pocketAtoms.Clear();
        _targetSulfur = null;
        AtomInfo byName = null, byElement = null, targetCA = null;

        foreach (var atom in proteinLoader.GetComponentsInChildren<AtomInfo>(true))
        {
            if (pocketResidueIds.Contains(atom.ResidueId))
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

    private void HandleCompoundChosen(CompoundSlot slot)
    {
        if (_questCompleted) return;
        StartCoroutine(DockingRoutine(slot));
    }

    private IEnumerator DockingRoutine(CompoundSlot slot)
    {
        selectionPanel.Interactable = false;
        selectionPanel.ClearResult();

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
                bond.transform.up = (_targetSulfur.transform.position - warhead.position).normalized;
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

        _questCompleted = true;
        if (questUI != null) questUI.CompleteCurrentStageAndAdvance();
        OnDockingFinished?.Invoke(DockingOutcome.Success, slot.Data);
        // 성공 시 clone은 포켓에 그대로 남긴다 (KRAS OFF 락인 상태)
    }

    private void FinishFailure(CompoundSlot slot, GameObject clone, Color color, DockingOutcome outcome)
    {
        StopPocketPulse();
        RestorePocketColors();
        UnityEngine.Object.Destroy(clone);

        slot.SetResultColor(color);
        selectionPanel.ShowResult(slot.Data, color);
        selectionPanel.Interactable = true; // 재도전 허용
        OnDockingFinished?.Invoke(outcome, slot.Data);
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
