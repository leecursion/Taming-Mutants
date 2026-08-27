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
    [Tooltip("리본/Helix 세그먼트에 쓸 머티리얼. 비우면 URP Lit 기본 머티리얼 자동 생성")]
    public Material segmentMaterial;

    [Header("Helix 구간 (미리 입력, 계산하지 않음)")]
    public List<HelixRegion> helixRegions = new List<HelixRegion>();

    [Header("표시 색상")]
    [Tooltip("리본 단계의 이차구조 색상. PyMOL/ChimeraX 기본 배색과 같다 — " +
             "알파나선 마젠타, 베타가닥 노랑, 루프/코일 흰색·회색. " +
             "SecondaryStructureAssigner가 주쇄 수소결합(DSSP)으로 판정한 이차구조를 이 색으로 칠한다.")]
    public Color ssHelixColor = new Color(0.78f, 0.2f, 0.85f);
    public Color ssStrandColor = new Color(1f, 0.92f, 0.15f);
    public Color ssLoopColor = new Color(0.82f, 0.82f, 0.85f);
    public Color helixColor = new Color(1f, 0.6f, 0.1f);

    [Header("리본 굵기 기준값 (unit)")]
    [Tooltip("RibbonMeshBuilder.Style이 이 값 하나로 나선 띠 폭·가닥 판 폭·화살촉·루프 관 굵기를 " +
             "실제 카툰 표현의 비율대로 정한다. 0.08이면 나선 폭 ≈ 2.2 Å로 PyMOL 기본과 같다.")]
    public float ribbonRadius = 0.08f;
    [Tooltip("Helix 단계에서 쓰는 굵기 기준값. 한 구간만 크게 보는 단계라 리본보다 살짝 굵게 둔다.")]
    public float helixRadius = 0.1f;

    [Header("클릭 유도 효과")]
    [Tooltip("다음 단계로 내려갈 수 있는 세그먼트(리본의 Helix 구간, Helix 전체)를 점멸시켜 클릭 지점을 안내")]
    public bool pulseClickableSegments = true;
    [Tooltip("점멸 강조색 — 세그먼트 기본색과 이 색 사이를 오간다")]
    public Color clickHintColor = new Color(0.9f, 1f, 0.45f);
    [Tooltip("점멸 속도 (높을수록 빠르게 깜빡임)")]
    public float clickHintPulseSpeed = 2.5f;
    [Tooltip("세그먼트마다 위상을 어긋나게 해 구간을 따라 흐르는 파동처럼 보이게 하는 간격")]
    public float clickHintPhaseStep = 0.35f;

    [Header("아미노산 단계 표시 범위")]
    [Tooltip("켜면 아미노산 단계에서 선택한 Helix 구간(+여유 잔기, +항상 표시 잔기)만 원자 표시. " +
             "퀘스트와 무관한 원자를 지워 필요한 부분만 남기며, ProteinLoader.SetVisibleResidues가 " +
             "결합이 끊긴 홀로 남는 원자까지 함께 숨겨 남은 원자가 모두 연결되게 한다. " +
             "끄면 전체 원자 표시")]
    public bool showOnlyRegionAtoms = true;
    [Tooltip("Helix 구간 앞뒤로 함께 표시할 여유 잔기 수 (맥락 파악용)")]
    public int regionResiduePadding = 2;
    [Tooltip("아미노산 단계 진입 시 선택 구간이 전체 구조 중심 자리(화면 중앙)에 오도록 이동. 리본/Helix로 돌아가면 원복")]
    public bool centerRegionOnAminoAcid = true;

    [Header("레이캐스트")]
    public float maxRayDistance = 100f;

    public ViewLevel CurrentLevel { get; private set; } = ViewLevel.Ribbon;

    /// <summary>
    /// true인 동안 클릭으로 단계를 내려가지 못한다.
    ///
    /// 비서가 설명을 끝내기 전에 사용자가 구조를 눌러 다음 단계로 넘어가면, 방금 시작한 해설이
    /// 곧바로 다음 단계 해설로 덮여 아무것도 못 듣게 된다. 누가 이 값을 켜고 끄는지는
    /// 여기서 알 필요가 없다 — 이 컴포넌트는 "지금 입력을 받는가"만 본다.
    /// (지금은 <see cref="AIAssistantBrain"/>이 말하는 동안 켠다.)
    /// </summary>
    public bool InputLocked
    {
        get => _inputLocked;
        set
        {
            if (_inputLocked == value) return;

            _inputLocked = value;
            // 잠긴 동안에는 클릭 유도 점멸을 멈춘다. 눌러도 안 되는데 계속 반짝이면
            // 사용자는 클릭이 먹지 않는 걸 고장으로 받아들인다.
            ClickHintPulse.Suppressed = value;
        }
    }

    private bool _inputLocked;
    public event Action<ViewLevel> OnLevelChanged;

    /// <summary>
    /// 아미노산 단계에서 카메라가 실제로 향하는 월드 지점.
    ///
    /// <see cref="ApplyAminoAcidCentering"/>이 선택 구간의 무게중심을 이 지점(전체 구조의
    /// 원래 중심 자리)으로 옮겨오므로, 아미노산 단계에서는 이 값이 곧 화면 중앙이다.
    /// transform.position(앵커 원점)과는 다르다 — 원점은 구조의 피벗일 뿐 카메라가 보는
    /// 자리가 아니고, 둘 사이 거리는 어떤 구간을 골랐느냐에 따라 사건마다 달라진다.
    /// CompoundSelectionPanel처럼 "지금 화면에 보이는 구조 옆"에 뭔가를 붙이려면
    /// transform.position이 아니라 이 프로퍼티를 기준으로 삼아야 한다.
    /// </summary>
    public Vector3 AminoAcidFocusWorldPosition => transform.TransformPoint(_fullCenterLocal);

    private ProteinLoader _proteinLoader;
    private Transform _ribbonRoot;
    private readonly List<Transform> _helixRegionRoots = new List<Transform>();
    // 런타임 생성 메시는 GameObject를 지워도 함께 사라지지 않는다 — 구조를 전환할 때마다
    // 수백 개씩 새로 만들므로 직접 해제하지 않으면 퀘스트를 오갈수록 메모리가 계속 늘어난다.
    private readonly List<Mesh> _builtMeshes = new List<Mesh>();
    private BackboneChain _chain;
    private SecondaryStructureAssigner.Type[] _secondaryStructure;
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

        // 주쇄(N/CA/C/O)를 한 번만 뽑아 이차구조 판정과 리본 지오메트리가 같은 데이터를 쓰게 한다.
        _chain = BackboneChain.Extract(data, _proteinLoader.CenterOffset);
        _secondaryStructure = SecondaryStructureAssigner.Assign(_chain);

        BuildRibbon();
        BuildHelixRegions();

        // 아미노산 단계 중앙 정렬에 쓸 전체 구조 중심(CA 평균) 캐시
        _caTrace = ExtractCaTrace();
        _fullCenterLocal = Vector3.zero;
        foreach (var entry in _caTrace) _fullCenterLocal += entry.Value;
        if (_caTrace.Count > 0) _fullCenterLocal /= _caTrace.Count;

        _proteinLoader.SetAtomsVisible(false); // 아미노산 단계로 가기 전까지 원자 표시는 숨김
        SetLevel(ViewLevel.Ribbon);

        // 방금 리본/Helix를 통째로 새로 만들었으니, 이 오브젝트를 contentRoot로 쓰는
        // LevelStage(Level2~4)가 렌더러 캐시를 확실히 다시 잡도록 직접 알린다.
        LevelStage.InvalidateSharedContent(gameObject);
    }

    private void ClearBuilt()
    {
        if (_ribbonRoot != null) Destroy(_ribbonRoot.gameObject);
        _ribbonRoot = null;
        foreach (var root in _helixRegionRoots)
            if (root != null) Destroy(root.gameObject);
        _helixRegionRoots.Clear();

        foreach (var mesh in _builtMeshes)
            if (mesh != null) Destroy(mesh);
        _builtMeshes.Clear();

        _activeHelixIndex = -1;
    }

    private void OnDestroy() => ClearBuilt();

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

    /// <summary>
    /// 아미노산 단계 중앙 정렬에만 쓰는 Cα 목록. 리본 자체는 <see cref="_chain"/>에서 직접 만든다.
    /// </summary>
    private List<KeyValuePair<int, Vector3>> ExtractCaTrace()
    {
        var trace = new List<KeyValuePair<int, Vector3>>();
        if (_chain == null) return trace;

        foreach (var res in _chain.Residues)
            trace.Add(new KeyValuePair<int, Vector3>(res.resId, res.ca));

        return trace;
    }

    private void BuildRibbon()
    {
        GameObject rootGo = new GameObject("RibbonView");
        rootGo.transform.SetParent(transform, false);
        _ribbonRoot = rootGo.transform;

        // 리본은 서열이 이어진 조각별로 따로 만들어진다 — 구조에 없는 잔기(cryo-EM에서 못 잡은 loop,
        // F508del처럼 아예 결실된 자리, CFTR처럼 서열상 멀리 떨어진 두 구간만 골라 담은 경우)를
        // 가로지르는 가짜 연결이 생기지 않도록 BackboneChain.Fragments가 경계를 잡아 준다.
        var pieces = RibbonMeshBuilder.Build(_chain, _secondaryStructure,
                                             RibbonMeshBuilder.Style.FromRadius(ribbonRadius));

        for (int i = 0; i < pieces.Count; i++)
        {
            RibbonMeshBuilder.Piece piece = pieces[i];
            Color segColor = ColorForSecondaryStructure(piece.type);

            GameObject seg = CreatePiece(piece.mesh, _ribbonRoot, segColor, $"Ribbon_{piece.resId}");
            seg.AddComponent<RibbonSegmentInfo>().residueId = piece.resId;

            // 클릭 시 Helix로 내려갈 수 있는 구간만 점멸 — 클릭해도 반응 없는 곳은 그대로 둔다
            if (pulseClickableSegments && FindHelixRegionIndex(piece.resId) >= 0)
                seg.AddComponent<ClickHintPulse>()
                   .Init(segColor, clickHintColor, clickHintPulseSpeed, i * clickHintPhaseStep);
        }
    }

    private Color ColorForSecondaryStructure(SecondaryStructureAssigner.Type type)
    {
        switch (type)
        {
            case SecondaryStructureAssigner.Type.Helix: return ssHelixColor;
            case SecondaryStructureAssigner.Type.Strand: return ssStrandColor;
            default: return ssLoopColor;
        }
    }

    private void BuildHelixRegions()
    {
        RibbonMeshBuilder.Style style = RibbonMeshBuilder.Style.FromRadius(helixRadius);

        for (int r = 0; r < helixRegions.Count; r++)
        {
            HelixRegion region = helixRegions[r];

            GameObject regionGo = new GameObject($"HelixView_{region.label}");
            regionGo.transform.SetParent(transform, false);

            var ids = new HashSet<int>();
            for (int id = region.startResId; id <= region.endResId; id++) ids.Add(id);

            // 스플라인은 사슬 전체로 계산하고 메시만 구간 안쪽에서 뽑는다 — 구간 좌표만 떼어
            // 따로 스플라인을 그리면 경계에서 접선이 달라져, 리본 단계에서 보던 모양과
            // 미묘하게 다른 곡선이 나온다.
            var pieces = RibbonMeshBuilder.Build(_chain, _secondaryStructure, style, ids);

            for (int i = 0; i < pieces.Count; i++)
            {
                GameObject seg = CreatePiece(pieces[i].mesh, regionGo.transform, helixColor,
                                             $"Helix_{pieces[i].resId}");
                seg.AddComponent<HelixSegmentInfo>().helixRegionIndex = r;

                // Helix 단계에서는 어느 세그먼트를 눌러도 아미노산으로 내려가므로 전체가 점멸 대상
                if (pulseClickableSegments)
                    seg.AddComponent<ClickHintPulse>()
                       .Init(helixColor, clickHintColor, clickHintPulseSpeed, i * clickHintPhaseStep);
            }

            regionGo.SetActive(false);
            _helixRegionRoots.Add(regionGo.transform);
        }
    }

    /// <summary>
    /// 리본 조각 하나를 씬에 올린다. 메시 정점이 이미 앵커 로컬 좌표라 트랜스폼은 기본값 그대로 둔다
    /// (부모가 회전/이동한 상태에서 빌드돼도 원자 표시와 어긋나지 않는다).
    /// </summary>
    private GameObject CreatePiece(Mesh mesh, Transform parent, Color color, string name)
    {
        _builtMeshes.Add(mesh);

        var seg = new GameObject(name);
        seg.transform.SetParent(parent, false);

        seg.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = seg.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = segmentMaterial != null ? segmentMaterial : RuntimeMaterials.Solid;

        // 클릭으로 단계를 내려가는 조작이 레이캐스트라 조각마다 콜라이더가 필요하다.
        // 클릭 대상이 아닌 루프에도 달아야 앞에 있는 루프를 눌렀을 때 뒤쪽 나선이 대신 잡히지 않는다.
        seg.AddComponent<MeshCollider>().sharedMesh = mesh;

        TintSegment(seg, color);
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
        if (InputLocked) return;
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

            int regionIndex = FindHelixRegionIndex(ribbonInfo.residueId);
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

    /// <summary>이미 최상위(Ribbon)에서 한 번 더 나가려 할 때 발생 — 구조를 벗어나 퀘스트 선택으로
    /// 돌아가는 건 이 컨트롤러의 책임이 아니라서, 듣고 싶은 쪽(IntroDirector)에 맡긴다.</summary>
    public event Action OnExitRequested;

    public void GoBack()
    {
        if (CurrentLevel == ViewLevel.AminoAcid) SetLevel(ViewLevel.Helix);
        else if (CurrentLevel == ViewLevel.Helix) SetLevel(ViewLevel.Ribbon);
        else OnExitRequested?.Invoke();
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

/// <summary>리본 조각 클릭 판별용 — 어느 잔기의 조각인지 표시.</summary>
public class RibbonSegmentInfo : MonoBehaviour
{
    public int residueId;
}

/// <summary>Helix 세그먼트 클릭 판별용 — 어느 HelixRegion에 속하는지 표시.</summary>
public class HelixSegmentInfo : MonoBehaviour
{
    public int helixRegionIndex;
}

/// <summary>
/// 다음 단계로 내려갈 수 있는(클릭 가능한) 세그먼트를 기본색과 강조색 사이에서 점멸시킨다.
/// 위상(phase)을 세그먼트마다 어긋나게 주면 구간을 따라 흐르는 파동처럼 보여 시선을 끈다.
/// 리본/Helix 루트가 켜져 있을 때만 Update가 돌므로 레벨 전환 시 따로 켜고 끌 필요가 없다.
/// </summary>
public class ClickHintPulse : MonoBehaviour
{
    /// <summary>
    /// true인 동안 점멸을 멈추고 기본색으로 가라앉는다.
    ///
    /// 세그먼트마다 따로 켜고 끄지 않고 정적 플래그로 둔 이유: 화면에 떠 있는 세그먼트는
    /// 수백 개인데 잠금은 전부 같은 순간에 걸린다. 하나씩 순회하면 매번 수백 번 접근하게 된다.
    /// </summary>
    public static bool Suppressed;

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private Color _baseColor;
    private Color _hintColor;
    private float _speed;
    private float _phase;

    public void Init(Color baseColor, Color hintColor, float speed, float phase)
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
        _baseColor = baseColor;
        _hintColor = hintColor;
        _speed = speed;
        _phase = phase;
    }

    private void Update()
    {
        if (_renderer == null) return;

        // 잠긴 동안에는 t=0으로 고정해 기본색으로 눕힌다. Update를 그냥 건너뛰면
        // 잠기기 직전 프레임의 밝기에서 멈춰 어중간하게 빛난 채로 남는다.
        float t = ClickHintPulse.Suppressed ? 0f : (Mathf.Sin(Time.time * _speed + _phase) + 1f) * 0.5f;
        Color c = Color.Lerp(_baseColor, _hintColor, t);

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_BaseColor", c);
        // 머티리얼에 _EMISSION 키워드가 켜져 있으면(RuntimeMaterials.Solid) 발광까지 얹힌다
        _mpb.SetColor("_EmissionColor", c * (0.3f + t * 1.2f));
        _renderer.SetPropertyBlock(_mpb);
    }
}
