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

    [Header("Helix 구간 (미리 입력, 계산하지 않음)")]
    public List<HelixRegion> helixRegions = new List<HelixRegion>();

    [Header("표시 색상")]
    public Color ribbonColor = new Color(0.2f, 0.6f, 1f);
    public Color helixColor = new Color(1f, 0.6f, 0.1f);

    [Header("두께 (실제 반지름, unit 단위 — segmentPrefab의 기본 스케일과 무관)")]
    public float ribbonRadius = 0.08f;
    public float helixRadius = 0.1f;

    [Header("레이캐스트")]
    public float maxRayDistance = 100f;

    public ViewLevel CurrentLevel { get; private set; } = ViewLevel.Ribbon;
    public event Action<ViewLevel> OnLevelChanged;

    private ProteinLoader _proteinLoader;
    private Transform _ribbonRoot;
    private readonly List<Transform> _helixRegionRoots = new List<Transform>();
    private int _activeHelixIndex = -1;

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
        BuildRibbon(data);
        BuildHelixRegions(data);
        _proteinLoader.SetAtomsVisible(false); // 아미노산 단계로 가기 전까지 원자 표시는 숨김
        SetLevel(ViewLevel.Ribbon);
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

        _proteinLoader.SetAtomsVisible(level == ViewLevel.AminoAcid);

        OnLevelChanged?.Invoke(level);
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
