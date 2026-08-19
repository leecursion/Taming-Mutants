using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 표시 레벨을 리본(전체 개관) -> Helix(관심 구간) -> 아미노산(원자 단위, 기존 ProteinLoader 표시)
/// 순서로 전환한다.
///
/// Helix 구간은 DSSP 등으로 계산하지 않고 <see cref="helixRegions"/>에 미리 입력해둔
/// 잔기(res_id) 범위를 그대로 사용한다 — 돌연변이 부위와 마찬가지로 "탐색"이 아니라
/// 사전에 정해진 정보를 데이터로 저장해두는 방식.
/// </summary>
[RequireComponent(typeof(ProteinLoader))]
public class StructureLevelController : MonoBehaviour
{
    public enum ViewLevel { Ribbon, Helix, AminoAcid }

    [Serializable]
    public class HelixRegion
    {
        public string label;
        public int startResId;
        public int endResId;
    }

    [Header("참조")]
    [Tooltip("비워두면 Camera.main 사용")]
    public Camera targetCamera;
    [Tooltip("리본/Helix 세그먼트로 재사용할 프리팹. Bond.prefab(Cylinder+Collider)을 그대로 써도 된다.")]
    public GameObject segmentPrefab;
    [Tooltip("세그먼트를 홀로그램이 아닌 불투명(실제) 재질로 표시. Bond.prefab이 Hologram_Blue.mat을 쓰므로 기본 켜짐")]
    public bool solidSegments = true;
    [Tooltip("세그먼트에 덮어씌울 머티리얼. 비우면 URP Lit 기본 머티리얼 자동 생성")]
    public Material segmentMaterial;

    [Header("Helix 구간 (미리 입력, 계산하지 않음)")]
    public List<HelixRegion> helixRegions = new List<HelixRegion>();

    [Header("표시 색상")]
    public Color ribbonColor = new Color(0.2f, 0.6f, 1f);
    public Color helixColor = new Color(1f, 0.6f, 0.1f);

    [Header("두께 (실제 반지름, unit 단위 — segmentPrefab의 기본 스케일과 무관)")]
    public float ribbonRadius = 0.08f;
    public float helixRadius = 0.1f;

    [Header("아미노산 단계 표시 범위")]
    [Tooltip("켜면 아미노산 단계에서 선택한 Helix 구간(+여유 잔기, +항상 표시 잔기)만 원자 표시. 끄면 전체 원자 표시")]
    public bool showOnlyRegionAtoms = true;
    [Tooltip("Helix 구간 앞뒤로 함께 표시할 여유 잔기 수 (맥락 파악용)")]
    public int regionResiduePadding = 2;
    [Tooltip("아미노산 단계 진입 시 선택 구간이 전체 구조 중심 자리(화면 중앙)에 오도록 이동. 리본/Helix로 돌아가면 원복")]
    public bool centerRegionOnAminoAcid = true;

    [Header("레이캐스트")]
    public float maxRayDistance = 100f;

    public ViewLevel CurrentLevel { get; private set; } = ViewLevel.Ribbon;
    public event Action<ViewLevel> OnLevelChanged;

    private ProteinLoader _proteinLoader;
    private Transform _ribbonRoot;
    private readonly List<Transform> _helixRegionRoots = new List<Transform>();
    private int _activeHelixIndex = -1;
    // 구간 필터와 무관하게 아미노산 단계에서 항상 표시할 잔기 (도킹 타깃/포켓 등 — QuestCatalog가 주입)
    private readonly HashSet<int> _alwaysVisibleResidues = new HashSet<int>();
    // 아미노산 단계 중앙 정렬용: CA 트레이스 캐시 + 전체 구조의 로컬 중심 + 현재 적용된 이동량
    private List<KeyValuePair<int, Vector3>> _caTrace;
    private Vector3 _fullCenterLocal;
    private Vector3 _aminoAcidShift;

    private void Awake()
    {
        _proteinLoader = GetComponent<ProteinLoader>();
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void OnEnable()
    {
        _proteinLoader.OnLoaded += HandleLoaded;
    }

    private void OnDisable()
    {
        _proteinLoader.OnLoaded -= HandleLoaded;
    }

    private void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryClickAtMouse();
        }
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            GoBack();
        }
    }

    private void HandleLoaded(ProteinLoader.ProteinData data)
    {
        ClearBuilt(); // 구조 재로드(퀘스트 전환) 시 이전 리본/Helix 제거
        BuildRibbon(data);
        BuildHelixRegions(data);

        // 아미노산 단계 중앙 정렬에 쓸 전체 구조 중심(CA 평균) 캐시
        _caTrace = ExtractCaTrace(data);
        _fullCenterLocal = Vector3.zero;
        foreach (var entry in _caTrace) _fullCenterLocal += entry.Value;
        if (_caTrace.Count > 0) _fullCenterLocal /= _caTrace.Count;

        _proteinLoader.SetAtomsVisible(false); // 아미노산 단계로 가기 전까지 원자 표시는 숨김
        SetLevel(ViewLevel.Ribbon);
    }

    private void ClearBuilt()
    {
        if (_ribbonRoot != null) Destroy(_ribbonRoot.gameObject);
        _ribbonRoot = null;
        foreach (var root in _helixRegionRoots)
            if (root != null) Destroy(root.gameObject);
        _helixRegionRoots.Clear();
        _activeHelixIndex = -1;
    }

    /// <summary>
    /// 퀘스트 정의 등 외부 데이터로 Helix 구간을 교체한다.
    /// 다음 ProteinLoader.OnLoaded 시점(=LoadStructure 완료)에 새 구간으로 빌드된다.
    /// </summary>
    public void SetHelixRegions(IEnumerable<HelixRegion> regions)
    {
        helixRegions.Clear();
        if (regions != null) helixRegions.AddRange(regions);
    }

    /// <summary>
    /// 아미노산 단계의 구간 필터와 무관하게 항상 표시할 잔기 목록을 교체한다.
    /// 도킹 퀘스트의 타깃/포켓 잔기는 Helix 구간 밖에 있어도 보여야 연출이 성립한다.
    /// </summary>
    public void SetAlwaysVisibleResidues(IEnumerable<int> residues)
    {
        _alwaysVisibleResidues.Clear();
        if (residues != null)
            foreach (int id in residues) _alwaysVisibleResidues.Add(id);
    }

    // --- 빌드 ---

    private List<KeyValuePair<int, Vector3>> ExtractCaTrace(ProteinLoader.ProteinData data)
    {
        var trace = new List<KeyValuePair<int, Vector3>>();
        foreach (var atom in data.atoms)
        {
            if (atom.name != "CA") continue;
            Vector3 pos = new Vector3(atom.x, atom.y, atom.z) * 0.1f; // ProteinLoader.SpawnStructure와 동일한 스케일
            trace.Add(new KeyValuePair<int, Vector3>(atom.res_id, pos));
        }
        trace.Sort((a, b) => a.Key.CompareTo(b.Key));
        return trace;
    }

    private void BuildRibbon(ProteinLoader.ProteinData data)
    {
        var trace = ExtractCaTrace(data);

        GameObject rootGo = new GameObject("RibbonView");
        rootGo.transform.SetParent(transform, false);
        _ribbonRoot = rootGo.transform;

        for (int i = 0; i < trace.Count - 1; i++)
        {
            GameObject seg = CreateSegment(trace[i].Value, trace[i + 1].Value, _ribbonRoot, ribbonRadius);
            TintSegment(seg, ribbonColor);
            var info = seg.AddComponent<RibbonSegmentInfo>();
            info.residueIdA = trace[i].Key;
            info.residueIdB = trace[i + 1].Key;
        }
    }

    private void BuildHelixRegions(ProteinLoader.ProteinData data)
    {
        var trace = ExtractCaTrace(data);

        for (int r = 0; r < helixRegions.Count; r++)
        {
            HelixRegion region = helixRegions[r];

            GameObject regionGo = new GameObject($"HelixView_{region.label}");
            regionGo.transform.SetParent(transform, false);

            var subset = new List<KeyValuePair<int, Vector3>>();
            foreach (var entry in trace)
            {
                if (entry.Key >= region.startResId && entry.Key <= region.endResId)
                    subset.Add(entry);
            }

            for (int i = 0; i < subset.Count - 1; i++)
            {
                GameObject seg = CreateSegment(subset[i].Value, subset[i + 1].Value, regionGo.transform, helixRadius);
                TintSegment(seg, helixColor);
                var info = seg.AddComponent<HelixSegmentInfo>();
                info.helixRegionIndex = r;
            }

            regionGo.SetActive(false);
            _helixRegionRoots.Add(regionGo.transform);
        }
    }

    private GameObject CreateSegment(Vector3 a, Vector3 b, Transform parent, float radius)
    {
        GameObject seg = Instantiate(segmentPrefab, parent);
        if (solidSegments)
        {
            var renderer = seg.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = segmentMaterial != null ? segmentMaterial : RuntimeMaterials.Solid;
        }
        Vector3 mid = (a + b) / 2f;
        seg.transform.localPosition = mid;
        seg.transform.up = (b - a).normalized;
        float length = Vector3.Distance(a, b);
        seg.transform.localScale = new Vector3(radius, length / 2f, radius);
        return seg;
    }

    private void TintSegment(GameObject seg, Color color)
    {
        Renderer renderer = seg.GetComponent<Renderer>();
        if (renderer == null) return;
        var mpb = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(mpb);
    }

    // --- 레벨 전환 ---

    private void TryClickAtMouse()
    {
        if (targetCamera == null || Mouse.current == null) return;
        // UI 버튼(예: StructureLevelBackButton) 위 클릭은 3D 선택으로 처리하지 않음
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = targetCamera.ScreenPointToRay(mousePos);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance)) return;

        if (CurrentLevel == ViewLevel.Ribbon)
        {
            var ribbonInfo = hit.collider.GetComponent<RibbonSegmentInfo>();
            if (ribbonInfo == null) return;

            int regionIndex = FindHelixRegionIndex(ribbonInfo.residueIdA);
            if (regionIndex < 0) return; // 이 구간엔 미리 지정된 Helix가 없음

            _activeHelixIndex = regionIndex;
            SetLevel(ViewLevel.Helix);
        }
        else if (CurrentLevel == ViewLevel.Helix)
        {
            var helixInfo = hit.collider.GetComponent<HelixSegmentInfo>();
            if (helixInfo == null) return;

            SetLevel(ViewLevel.AminoAcid);
        }
    }

    private int FindHelixRegionIndex(int residueId)
    {
        for (int i = 0; i < helixRegions.Count; i++)
        {
            if (residueId >= helixRegions[i].startResId && residueId <= helixRegions[i].endResId)
                return i;
        }
        return -1;
    }

    public void GoBack()
    {
        if (CurrentLevel == ViewLevel.AminoAcid) SetLevel(ViewLevel.Helix);
        else if (CurrentLevel == ViewLevel.Helix) SetLevel(ViewLevel.Ribbon);
    }

    public void SetLevel(ViewLevel level)
    {
        CurrentLevel = level;

        if (_ribbonRoot != null)
            _ribbonRoot.gameObject.SetActive(level == ViewLevel.Ribbon);

        for (int i = 0; i < _helixRegionRoots.Count; i++)
        {
            bool active = level == ViewLevel.Helix && i == _activeHelixIndex;
            _helixRegionRoots[i].gameObject.SetActive(active);
        }

        if (level == ViewLevel.AminoAcid)
            _proteinLoader.SetVisibleResidues(BuildAminoAcidResidueSet()); // null이면 전체 표시
        else
            _proteinLoader.SetAtomsVisible(false);

        ApplyAminoAcidCentering(level);

        OnLevelChanged?.Invoke(level);
    }

    // 아미노산 단계에서는 선택 구간만 남아 전체 구조의 한쪽 구석(예: 우하단)에 치우쳐 보인다.
    // 구간의 CA 평균 위치가 전체 구조 중심이 있던 자리로 오도록 루트를 이동하고,
    // 리본/Helix로 돌아가면 원복한다.
    private void ApplyAminoAcidCentering(ViewLevel level)
    {
        // 이전에 적용한 이동 원복
        transform.position -= _aminoAcidShift;
        _aminoAcidShift = Vector3.zero;

        if (level != ViewLevel.AminoAcid || !centerRegionOnAminoAcid) return;
        if (_activeHelixIndex < 0 || _activeHelixIndex >= helixRegions.Count) return;
        if (_caTrace == null || _caTrace.Count == 0) return;

        HelixRegion region = helixRegions[_activeHelixIndex];
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var entry in _caTrace)
        {
            if (entry.Key >= region.startResId && entry.Key <= region.endResId)
            {
                sum += entry.Value;
                count++;
            }
        }
        if (count == 0) return;

        // TransformPoint 차분이라 현재 회전/줌 상태에서도 올바른 월드 이동량이 나온다
        Vector3 regionCenterLocal = sum / count;
        _aminoAcidShift = transform.TransformPoint(_fullCenterLocal) - transform.TransformPoint(regionCenterLocal);
        transform.position += _aminoAcidShift;
    }

    // 아미노산 단계에서 보여줄 잔기 집합: 선택된 Helix 구간 ± 여유 + 항상 표시 잔기.
    // 필터를 끄거나 선택된 구간이 없으면 null(전체 표시).
    private HashSet<int> BuildAminoAcidResidueSet()
    {
        if (!showOnlyRegionAtoms) return null;
        if (_activeHelixIndex < 0 || _activeHelixIndex >= helixRegions.Count) return null;

        var set = new HashSet<int>(_alwaysVisibleResidues);
        HelixRegion region = helixRegions[_activeHelixIndex];
        for (int id = region.startResId - regionResiduePadding; id <= region.endResId + regionResiduePadding; id++)
            set.Add(id);
        return set;
    }
}

/// <summary>리본 세그먼트 클릭 판별용 — 어느 잔기 구간인지 표시.</summary>
public class RibbonSegmentInfo : MonoBehaviour
{
    public int residueIdA;
    public int residueIdB;
}

/// <summary>Helix 세그먼트 클릭 판별용 — 어느 HelixRegion에 속하는지 표시.</summary>
public class HelixSegmentInfo : MonoBehaviour
{
    public int helixRegionIndex;
}
