using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

/// <summary>
/// F-04.2 후보물질 선택 패널.
/// StreamingAssets/compounds/*.json 을 로드해 화합물들을 2x2 그리드로 3D 시각화한다.
/// 칸을 둘러싸는 와이어프레임 박스는 없고, 대신 각 칸 위에서 비추는 스포트라이트와
/// 바닥 글로우가 "빛으로 자리를 표시한 전시대" 느낌을 낸다(CompoundSlot 참고).
///
/// 배치(기본, placeRelativeToCamera=true): 카메라 기준 고정 오프셋(cameraOffset)으로
/// "화면 왼쪽 중앙" 근처에 사선으로 놓는다(PlaceBesideUser 참고). 단백질 구조를 실측해
/// "구조 옆"에 붙이는 방식도 코드는 남아 있지만(placeRelativeToCamera=false), 사건마다
/// 단백질 크기·아미노산 단계에서 선택한 구간의 위치가 달라 화면상 패널 위치가 들쭉날쭉하고
/// 화면 밖으로 잘리는 문제가 반복돼 기본값에서 뺐다 — [[compound-panel-placement-final]].
/// 사용자 중앙 시선 쪽으로 diagonalYaw만큼 틀어 사선 배치하는 것은 두 방식 모두 동일하다.
///
/// 표시 시점: levelController가 연결돼 있으면 아미노산(원자) 레벨에서만 패널이 보인다.
///
/// 원자 디자인: hologramShellMaterial(Custom/Hologram)을 지정하면
/// "홀로-오브" 스타일(CPK 발광 코어 + 프레넬 림 셸)로 렌더링된다.
///
/// 선택 입력: PC는 마우스 클릭, XR은 인터랙터에서 SelectSlot() 호출.
/// Quest(Android)의 jar:file:// 경로 문제 때문에 ProteinLoader와 동일하게 UnityWebRequest를 쓴다.
/// </summary>
public class CompoundSelectionPanel : MonoBehaviour
{
    [Header("데이터 소스")]
    [Tooltip("StreamingAssets 기준 상대 폴더")]
    public string compoundsFolder = "compounds";
    [Tooltip("로드할 파일 이름 순서대로")]
    public string[] compoundFiles = { "compound_A.json", "compound_B.json", "compound_C.json", "compound_D.json" };
    [Tooltip("씬 시작 시 위 목록 자동 로드. QuestCatalog가 퀘스트별로 로드하는 씬에서는 꺼진다")]
    public bool autoLoadOnStart = true;

    [Header("렌더링 (ProteinLoader와 같은 프리팹 재사용)")]
    public GameObject atomPrefab;
    public GameObject bondPrefab;
    public float atomScale = 0.18f;
    public float bondRadiusScale = 0.035f;
    [Tooltip("홀로-오브 원자 스타일용 셸 머티리얼 (Custom/Hologram). 비우면 Shader.Find로 자동 생성 시도")]
    public Material hologramShellMaterial;
    [Tooltip("홀로-오브 스타일 사용 여부 (끄면 기존 단색 구체)")]
    public bool useHoloOrbStyle = true;

    [Header("그리드 레이아웃 (columns x rows 칸을 격자로 배치)")]
    [Tooltip("가로 칸 수 — 화합물 4개면 2로 두어 2x2")]
    public int columns = 2;
    [Tooltip("칸 한 변 크기 (unit)")]
    public float boxSize = 0.45f;
    [Tooltip("칸 간 간격")]
    public float spacing = 0.06f;
    [Tooltip("칸 사이 여백(배치 계산에 사용, 시각적 박스는 없음)")]
    public float outerPadding = 0.07f;
    [Tooltip("칸의 스포트라이트/바닥 글로우 색 (옅게 — 호버/결과 시 강조색으로 덮임)")]
    public Color cellFrameColor = new Color(0.2f, 0.35f, 0.45f);
    [Tooltip("박스 안 분자 자전 속도 (도/초)")]
    public float moleculeSpinSpeed = 25f;

    [Header("자동 배치 (단백질 실측 경계 기준, 시선 왼쪽 옆 + 상단 정렬 + 사선)")]
    [Tooltip("켜면 단백질 로드/레벨 전환 시 자동 배치")]
    public bool autoPlace = true;
    [Tooltip("켜면 단백질 경계 대신 사용자 시야(카메라) 기준 고정 위치(화면 왼쪽 중앙)에 놓는다. " +
             "구조 옆 사선 배치는 사건마다 단백질 크기/선택 구간이 달라 화면 위치가 들쭉날쭉하고 " +
             "화면 밖으로 잘리는 문제가 반복돼, 2026-08-27부로 이 카메라 기준 배치를 기본값으로 " +
             "바꿨다(사용자 승인). PlaceBesideUser() 참고 — [[compound-panel-placement-final]]")]
    public bool placeRelativeToCamera = true;
    [Tooltip("카메라 기준 오프셋 (x=오른쪽, y=위, z=앞, 단위 m). 음수 x로 시야 왼쪽에 둔다. " +
             "수평 방향(yaw)만 따르므로 고개를 숙여도 패널 높이는 눈높이 기준을 유지한다")]
    public Vector3 cameraOffset = new Vector3(-0.68f, 0f, 1.5f);
    [Tooltip("패널 전체 크기 배율. 구조 옆/카메라 상대 어느 배치에서든 적용된다")]
    public float panelScale = 0.6f;
    [Tooltip("배치 기준이 되는 단백질 로더. 비우면 levelController에서 자동 획득, 그래도 없으면 placementAnchor 사용")]
    public ProteinLoader proteinLoader;
    [Tooltip("proteinLoader가 없을 때의 폴백 앵커 (예: ProteinAnchor_Main)")]
    public Transform placementAnchor;
    [Tooltip("단백질 왼쪽 가장자리와 패널 사이 추가 간격 (unit)")]
    public float sideGap = 0.12f;
    [Tooltip("배치 후 카메라 쪽(시선 방향)으로 끌어당기는 거리 (unit). 화면상 같은 자리에서 더 가깝고 크게 보인다")]
    public float pullTowardCamera = 0.35f;
    [Tooltip("상단 정렬 후 추가 높이 미세조정 (unit, +위/-아래)")]
    public float topOffset = 0f;
    [Tooltip("사선 각도 (도). 양수면 패널 정면이 사용자 중앙 시선(단백질) 쪽, 즉 안쪽을 향해 틀어진다")]
    public float diagonalYaw = 25f;
    [Tooltip("화면 가장자리 안전 여백 (화면 높이 대비 비율). 위 배치 공식 결과가 이 여백보다 " +
             "바깥을 가리키면 안쪽으로 당긴다 — 사건마다 남는 오차에 대한 최후 방어선.")]
    public float edgeClampPadding = 0.03f;
    [Tooltip("켜면 카메라와의 실제 거리에 관계없이 화면에서 차지하는 크기가 항상 비슷하게 " +
             "panelScale을 보정한다. 끄면 panelScale이 월드 크기 그대로 적용돼, 계산된 위치가 " +
             "카메라에 가까운 사건에서는 패널이 화면을 가득 채울 만큼 커 보인다.")]
    public bool keepConstantApparentSize = true;
    [Tooltip("panelScale이 '원래 크기'로 보이는 기준 거리(카메라~패널, unit). " +
             "사건 2(EGFR)처럼 잘 보이던 사건을 기준으로 Play 중 이 값을 조절해 맞춘다.")]
    public float apparentSizeReferenceDistance = 1.2f;
    [Tooltip("보정에 쓰는 거리의 하한/상한. 너무 가깝거나 멀어도 배율이 폭주하지 않게 막는다.")]
    public Vector2 apparentSizeDistanceClamp = new Vector2(0.5f, 4f);

    [Header("줌인 오버라이드 (예: 사건 5 열안정성 카메라 클로즈업)")]
    [Tooltip("켜져 있는 동안은 '구조 옆 사선' 대신 카메라 바로 옆에 고정한다 — 카메라가 구조 " +
             "전체가 아니라 좁은 부위로 확 당겨지면 구조 기준 배치가 의미가 없어지기 때문이다. " +
             "ThermalStabilityController처럼 클로즈업 연출을 트는 쪽이 SetZoomOverride()로 켜고 끈다. " +
             "placeRelativeToCamera가 기본값(켜짐)이면 배치 방식이 이미 같으므로 실질적인 차이는 없다.")]
    public bool zoomOverrideActive;
    [Tooltip("줌인 오버라이드 중 사용할 크기 배율. 0 이하면 panelScale을 그대로 쓴다(권장). " +
             "이 값은 '구조 옆 사선'이 기본이던 시절, 클로즈업 동안만 카메라 옆으로 옮기면서 " +
             "거리가 달라지는 것을 보정하려고 크게 잡아 둔 것이다. placeRelativeToCamera가 " +
             "기본값이 된 지금은 클로즈업 중에도 카메라와의 거리가 그대로라, 여기에 별도 배율을 " +
             "주면 사건 5에서만 판넬이 다른 사건보다 훨씬 크게 보인다 — " +
             "[[compound-panel-placement-final]]")]
    public float zoomOverridePanelScale;

    [Header("표시 레벨 연동")]
    [Tooltip("아미노산(원자) 레벨에서만 패널이 표시됨. 비우면 씬에서 자동 탐색")]
    public StructureLevelController levelController;

    [Header("라벨")]
    [Tooltip("한글 표시를 위해 한글 글리프가 포함된 폰트를 지정 (비우면 내장 LegacyRuntime — 한글 미지원)")]
    public Font labelFont;
    [Tooltip("화합물 이름/부제 글씨 색. 배경판 없이 어두운 씬 위에 바로 얹히므로 흰색이 기본")]
    public Color labelColor = Color.white;
    public float nameLabelSize = 0.045f;
    public float resultLabelSize = 0.05f;
    [Tooltip("이름표가 칸 너비를 넘으면 자동으로 줄을 바꾼다. TextMesh는 자체 줄바꿈이 없어서, " +
             "끄면 긴 이름이 한 줄로 뻗어 옆 칸 이름표와 겹친다.")]
    public bool wrapLabels = true;
    [Tooltip("이름표가 쓸 수 있는 가로 폭. 칸 너비(boxSize) 대비 배율이며, " +
             "1보다 크면 칸 사이 간격(spacing)까지 조금 빌려 쓴다.")]
    [Range(0.6f, 1.6f)] public float labelWidthRatio = 1.15f;
    [Tooltip("도킹 결과 문구(빨강/초록)를 화면에도 띄울지. 비서가 같은 내용을 말하고 읽어주므로 기본은 끔.")]
    public bool showResultLabels;

    [Header("입력 (PC 폴백)")]
    [Tooltip("비워두면 Camera.main")]
    public Camera targetCamera;
    public float maxRayDistance = 50f;

    /// <summary>사용자가 화합물 박스를 선택했을 때 발생. DockingQuestController가 구독.</summary>
    public event Action<CompoundSlot> OnCompoundChosen;

    public IReadOnlyList<CompoundSlot> Slots => _slots;
    /// <summary>도킹 진행 중 등, 퀘스트 진행 쪽이 잠그는 스위치.</summary>
    public bool Interactable { get; set; } = true;

    /// <summary>
    /// 비서가 말하는 동안 잠그는 스위치. <see cref="Interactable"/>과 따로 둔다.
    ///
    /// 한 스위치를 둘이 나눠 쓰면 서로 덮어쓴다. 도킹이 끝나며 Interactable=true로 풀어주는
    /// 순간 비서가 아직 말하는 중인데 잠금이 풀리고, 반대로 비서가 말을 마치며 풀어주면
    /// 도킹 연출 도중인데 다음 물질을 고를 수 있게 된다. 둘 다 만족해야 누를 수 있다.
    /// </summary>
    public bool SpeechLocked { get; set; }

    /// <summary>실제로 입력을 받을 수 있는 상태인지.</summary>
    private bool AcceptsInput => Interactable && !SpeechLocked;

    private readonly List<CompoundSlot> _slots = new List<CompoundSlot>();
    private Transform _contentRoot;   // 슬롯/라벨이 모두 이 아래 — 레벨 연동 표시 토글용
    private CompoundSlot _hovered;
    private TextMesh _resultText;
    private TextMesh _affinityText;
    private Coroutine _loadRoutine;
    private Vector3 _outerSize;       // 칸 4개가 차지하는 전체 크기 — 배치/라벨 위치 계산에 사용

    // 사건 2(EGFR, structures/P00533.json)를 실측해 고정한 배치 기준값(씬 단위) — 모든 퀘스트가
    // 구조 크기와 무관하게 이 값을 그대로 쓴다. PlaceNow()의 주석 참고.
    private const float ReferenceLateralExtent = 2.9637f;
    private const float ReferenceTopExtent = 2.0004f;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (labelFont == null)
            labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (useHoloOrbStyle && hologramShellMaterial == null)
        {
            Shader holo = Shader.Find("Custom/Hologram");
            if (holo != null) hologramShellMaterial = new Material(holo);
        }

        var contentGo = new GameObject("Content");
        contentGo.transform.SetParent(transform, false);
        _contentRoot = contentGo.transform;
    }

    private void OnEnable()
    {
        // 씬에서 참조를 안 걸어도 레벨 연동(아미노산 단계에서만 표시)이 되도록 자동 탐색
        if (levelController == null)
            levelController = FindFirstObjectByType<StructureLevelController>();
        if (levelController != null) levelController.OnLevelChanged += HandleLevelChanged;
        if (proteinLoader == null && levelController != null)
            proteinLoader = levelController.GetComponent<ProteinLoader>();
        if (proteinLoader != null) proteinLoader.OnLoaded += HandleProteinLoaded;
    }

    private void OnDisable()
    {
        if (levelController != null) levelController.OnLevelChanged -= HandleLevelChanged;
        if (proteinLoader != null) proteinLoader.OnLoaded -= HandleProteinLoaded;
    }

    private void Start()
    {
        if (autoLoadOnStart) LoadCompounds(compoundsFolder, compoundFiles);

        // 레벨 연동: 아미노산 레벨이 아니면 숨긴 상태로 시작
        if (levelController != null)
            HandleLevelChanged(levelController.CurrentLevel);
        else if (autoPlace)
            StartCoroutine(PlaceNextFrame());
    }

    // --- 표시 레벨 연동 ---

    private void HandleLevelChanged(StructureLevelController.ViewLevel level)
    {
        bool visible = level == StructureLevelController.ViewLevel.AminoAcid;
        if (_contentRoot != null) _contentRoot.gameObject.SetActive(visible);
        if (visible && autoPlace) PlaceNow();
    }

    private void HandleProteinLoaded(ProteinLoader.ProteinData data)
    {
        if (autoPlace) StartCoroutine(PlaceNextFrame());
    }

    // 원자 인스턴스화가 끝난 다음 프레임에 실측 배치
    private IEnumerator PlaceNextFrame()
    {
        yield return null;
        PlaceNow();
    }

    // ThermalStabilityController.CameraTransitionRoutine()처럼 레벨 전환 "이후"에도
    // 카메라가 계속 움직이는 연출이 있으면(p53 변이 자리 클로즈업 등), 레벨 전환 시점에
    // 한 번만 배치해서는 카메라가 멀어진 뒤 판넬이 화면 밖/구석에 남는다.
    // AIAssistantFollower와 같은 패턴으로 카메라가 갱신된 뒤(LateUpdate) 매 프레임
    // 다시 배치해 항상 현재 카메라 기준을 따라가게 한다. PlaceNow()는 원자 경계를
    // 재측정하지 않고 상수+벡터 연산만 하므로 매 프레임 호출해도 비용이 작다.
    private void LateUpdate()
    {
        if (!autoPlace) return;
        if (_contentRoot == null || !_contentRoot.gameObject.activeSelf) return;
        PlaceNow();
    }

    // --- 자동 배치 ---

    /// <summary>단백질 원자들의 월드 경계를 측정해 "왼쪽 옆 + 상단 정렬 + 사선"으로 즉시 배치.
    /// placeRelativeToCamera가 켜져 있으면 경계 대신 시야 기준 왼쪽 가까이에 사선으로 놓는다.</summary>
    public void PlaceNow()
    {
        if (targetCamera == null) return;

        if (zoomOverrideActive)
        {
            // 0 이하면 평소와 같은 크기로 둔다. 클로즈업 중에도 카메라 기준 오프셋은 그대로라
            // 화면에서 보이는 크기가 달라질 이유가 없다 — 여기에 별도 배율을 주면 그 사건에서만
            // 판넬이 유독 커 보인다(사건 5에서 실제로 그렇게 보였다).
            PlaceBesideUser(zoomOverridePanelScale > 0f ? zoomOverridePanelScale : panelScale);
            return;
        }

        if (placeRelativeToCamera)
        {
            PlaceBesideUser(panelScale);
            return;
        }

        Vector3 camRight = targetCamera.transform.right;
        camRight.y = 0f;
        if (camRight.sqrMagnitude < 1e-6f) camRight = Vector3.right;
        camRight.Normalize();

        float scale = Mathf.Max(panelScale, 0.01f);
        float panelHalfWidth = (_outerSize.x > 0f ? _outerSize.x * 0.5f : boxSize + spacing * 0.5f + outerPadding) * scale;
        float panelHalfHeight = (_outerSize.y > 0f ? _outerSize.y * 0.5f : boxSize + spacing * 0.5f + outerPadding) * scale;

        Vector3 center;
        if (levelController != null) center = levelController.AminoAcidFocusWorldPosition;
        else if (proteinLoader != null) center = proteinLoader.transform.position;
        else if (placementAnchor != null) center = placementAnchor.position;
        else return; // 배치 기준 없음

        // 단백질 실측 경계 대신, 배치를 확정할 때 기준으로 삼은 사건 2(EGFR, P00533) 구조의
        // 실측값을 고정해서 쓴다. 퀘스트마다 단백질 크기가 다르면(KRAS/ABL1/CFTR/p53 등) 이 값을
        // 구조별로 다시 재는 순간 판넬이 퀘스트마다 다른 자리에 놓인다 — "구조 옆 사선"이라는
        // 배치 규칙 자체는 모든 퀘스트에서 똑같이 보여야 하므로 상수로 고정했다.
        //
        // 중심(center)은 levelController.AminoAcidFocusWorldPosition(카메라가 실제로 보는 지점)을
        // 우선 쓴다. proteinLoader.transform.position(앵커 원점)은 아미노산 단계에서
        // ApplyAminoAcidCentering이 선택 구간의 무게중심을 화면 중앙으로 옮기느라 통째로
        // 이동시키는데, 그 이동량은 "어떤 구간을 골랐느냐"에 따라 사건마다 달라져서 원점 자체는
        // 더 이상 화면 중앙이 아니게 된다. 원점 기준으로 패널을 붙이면 사건마다 화면에서
        // 제각각 다른 자리(심하면 화면 밖)에 나타나는 원인이 됐다.
        Vector3 pos = center - camRight * (ReferenceLateralExtent + panelHalfWidth + sideGap);
        pos.y = center.y + ReferenceTopExtent - panelHalfHeight + topOffset;

        // 카메라→패널 시선 방향을 따라 당기면 화면상 위치는 유지한 채 가깝고 크게 보인다.
        // 상한은 "카메라 앞 0.6m는 남긴다" — 이전의 dist*0.5 상한은 구조가 멀수록 당김을 깎아
        // 판넬이 구조 깊이에 뒤처져 보이는 원인이었다.
        Vector3 toCam = targetCamera.transform.position - pos;
        float dist = toCam.magnitude;
        if (dist > 1e-4f)
            pos += toCam / dist * Mathf.Min(pullTowardCamera, Mathf.Max(dist - 0.6f, 0f));

        // 최후 방어선: 위 계산이 그래도 화면 밖을 가리키면(사건마다 다른 구조 형태 등으로
        // 여전히 오차가 남을 수 있으므로) 뷰포트로 투영해 가장자리 안쪽으로 당긴다.
        // center를 실제 포커스 지점으로 고친 뒤에는 거의 안 걸리겠지만, 안 걸리는 경우에도
        // 비용이 거의 없어 상시 켜둔다.
        pos = ClampToViewport(pos, panelHalfWidth, panelHalfHeight);

        // panelScale은 "월드 크기" 배율이라, 계산된 pos가 카메라에서 얼마나 떨어져 있느냐에
        // 따라 화면에서 차지하는 크기가 달라진다. center를 AminoAcidFocusWorldPosition으로
        // 바꾼 뒤로 이 거리 자체가 사건마다(선택 구간이 구조 어디 있느냐에 따라) 크게 달라질
        // 수 있어서, 보정 없이 두면 카메라에 가까워진 사건에서 패널이 화면을 가득 채울 만큼
        // 커 보인다 — apparentSizeReferenceDistance 기준으로 실제 거리에 반비례하게 다시
        // 줄여, "화면에서 보이는 크기"는 사건과 무관하게 비슷하게 유지한다.
        float finalScale = scale;
        if (keepConstantApparentSize)
        {
            float finalDist = Mathf.Clamp(
                Vector3.Distance(targetCamera.transform.position, pos),
                apparentSizeDistanceClamp.x, apparentSizeDistanceClamp.y);
            finalScale = scale * (finalDist / Mathf.Max(apparentSizeReferenceDistance, 0.01f));
        }

        transform.position = pos;
        transform.localScale = Vector3.one * finalScale;

        // TextMesh는 +Z가 뒤통수 — forward가 카메라 반대편을 향해야 글이 바로 보인다.
        // 패널이 시선 왼쪽에 있으므로 음(-)의 yaw를 줘야 정면이 중앙 시선(안쪽)을 향한다.
        Vector3 away = pos - targetCamera.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-6f) return;
        transform.rotation = Quaternion.LookRotation(away.normalized) * Quaternion.Euler(0f, -diagonalYaw, 0f);
    }

    /// <summary>
    /// 계산된 위치가 카메라 뷰포트 밖(또는 가장자리 안전 여백 안쪽)을 가리키면 안으로 당긴다.
    /// AIAssistantFollower.TryComputeAnchorOnScreen과 같은 방식 — 현재 거리(depth)에서
    /// 뷰포트 1.0이 덮는 실제 월드 크기를 구해, 패널의 절반 크기(panelHalfWidth/Height)만큼의
    /// 여백을 두고 뷰포트 좌표를 [padding, 1-padding] 안으로 clamp한 뒤 다시 월드 좌표로 되돌린다.
    /// </summary>
    private Vector3 ClampToViewport(Vector3 pos, float panelHalfWidth, float panelHalfHeight)
    {
        Vector3 viewportPos = targetCamera.WorldToViewportPoint(pos);
        if (viewportPos.z <= 0f) return pos; // 카메라 뒤쪽 — 이 공식으로는 다룰 수 없는 상황

        float depth = viewportPos.z;
        float viewHeight = 2f * depth * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float viewWidth = viewHeight * targetCamera.aspect;
        if (viewHeight <= 1e-4f || viewWidth <= 1e-4f) return pos;

        float marginX = panelHalfWidth / viewWidth + edgeClampPadding;
        float marginY = panelHalfHeight / viewHeight + edgeClampPadding;

        float clampedX = Mathf.Clamp(viewportPos.x, marginX, 1f - marginX);
        float clampedY = Mathf.Clamp(viewportPos.y, marginY, 1f - marginY);

        if (Mathf.Approximately(clampedX, viewportPos.x) && Mathf.Approximately(clampedY, viewportPos.y))
            return pos; // 이미 안쪽 — 그대로 둔다

        return targetCamera.ViewportToWorldPoint(new Vector3(clampedX, clampedY, depth));
    }

    /// <summary>
    /// AI 비서의 사용자 기준 배치(AIAssistantFollower.localOffset)와 같은 방식으로,
    /// 시야 왼쪽 가까이에 사선으로 놓는다. 단백질 크기/거리와 무관하게 항상 손 닿는 거리에 온다.
    /// 비서와 마찬가지로 수평 방향(yaw)만 기준축으로 써서 고개 각도에 흔들리지 않는다.
    /// </summary>
    private void PlaceBesideUser(float scale)
    {
        Transform cam = targetCamera.transform;

        Vector3 flatForward = cam.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
        {
            // 정수리/발밑을 보는 중 — forward가 수직이라 못 쓰고 up이 수평을 가리킨다
            flatForward = cam.up;
            flatForward.y = 0f;
        }
        if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;

        Quaternion basis = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        Vector3 pos = cam.position + basis * cameraOffset;

        // cameraOffset은 카메라 기준 고정 오프셋이라 매 사건 동일한 화면 위치에 오는 게
        // 보장되지만, 화면비/FOV가 달라지는 예외적인 경우까지 대비해 clamp도 한 번 더 건다.
        float finalScale = Mathf.Max(scale, 0.01f);
        float panelHalfWidth = (_outerSize.x > 0f ? _outerSize.x * 0.5f : boxSize + spacing * 0.5f + outerPadding) * finalScale;
        float panelHalfHeight = (_outerSize.y > 0f ? _outerSize.y * 0.5f : boxSize + spacing * 0.5f + outerPadding) * finalScale;
        pos = ClampToViewport(pos, panelHalfWidth, panelHalfHeight);

        transform.position = pos;
        transform.localScale = Vector3.one * finalScale;

        // 패널이 시선 왼쪽에 있으므로 음(-)의 yaw로 정면을 중앙 시선(안쪽)으로 틀어 사선 배치
        Vector3 away = pos - cam.position;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-6f) away = basis * Vector3.forward;
        transform.rotation = Quaternion.LookRotation(away.normalized) * Quaternion.Euler(0f, -diagonalYaw, 0f);
    }

    /// <summary>
    /// ThermalStabilityController처럼 카메라를 구조의 좁은 부위로 클로즈업시키는 연출을 트는 쪽이
    /// 연출 시작/종료에 맞춰 호출한다. true면 "구조 옆 사선" 배치를 잠시 멈추고 카메라 옆에
    /// 고정하며, false면 원래 배치 규칙으로 되돌린다. LateUpdate가 매 프레임 재배치하므로
    /// 카메라가 계속 움직이는 클로즈업 도중에도 계속 따라간다.
    ///
    /// 크기는 건드리지 않는다(zoomOverridePanelScale 기본값 0 = panelScale 그대로).
    /// placeRelativeToCamera가 기본값이 된 뒤로는 이 스위치가 켜지든 말든 카메라와의 거리가
    /// 같아서, 크기까지 바꾸면 그 사건에서만 판넬이 커 보이는 문제가 된다.
    /// </summary>
    public void SetZoomOverride(bool active)
    {
        zoomOverrideActive = active;
    }

    // --- 로딩 / 그리드 구성 ---

    /// <summary>화합물 목록을 교체 로드 (퀘스트 전환용). 기존 슬롯/결과 표시는 모두 제거된다.</summary>
    public void LoadCompounds(string folder, IList<string> files)
    {
        compoundsFolder = folder;
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        ClearSlots();
        _loadRoutine = StartCoroutine(LoadAllRoutine(files));
    }

    private void ClearSlots()
    {
        foreach (var slot in _slots)
            if (slot != null) Destroy(slot.gameObject);
        _slots.Clear();
        _hovered = null;
        ClearResult();
        Interactable = true;
    }

    private IEnumerator LoadAllRoutine(IList<string> files)
    {
        int cols = Mathf.Max(1, columns);
        int rows = Mathf.CeilToInt(files.Count / (float)cols);
        float step = boxSize + spacing;
        float startX = -step * (cols - 1) * 0.5f;
        float startY = step * (rows - 1) * 0.5f;

        for (int i = 0; i < files.Count; i++)
        {
            // Path.Combine은 Windows에서 '\'를 붙여 Android jar:file:// URL을 깨뜨리므로 '/'로 직접 연결
            string url = $"{Application.streamingAssetsPath}/{compoundsFolder}/{files[i]}";
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[CompoundSelectionPanel] 로딩 실패: {req.error} ({url})");
                    continue;
                }

                CompoundData data = JsonUtility.FromJson<CompoundData>(req.downloadHandler.text);
                int col = i % cols, row = i / cols;
                CreateSlot(data, new Vector3(startX + step * col, startY - step * row, 0f));
            }
        }

        // 시각적 외곽 박스는 없지만, 배치(PlaceNow)와 결과 라벨 위치 계산에 쓸
        // "칸 4개가 차지하는 전체 크기"는 여전히 필요하다.
        _outerSize = new Vector3(
            cols * step - spacing + outerPadding * 2f,
            rows * step - spacing + outerPadding * 2f,
            boxSize + outerPadding * 2f);

        if (_resultText == null) CreateResultLabels(); // 재로드 시 기존 라벨 재사용
        PositionResultLabels();

        if (autoPlace) PlaceNow();
    }

    private void CreateSlot(CompoundData data, Vector3 localPos)
    {
        var slotGo = new GameObject($"Slot_{data.id}");
        slotGo.transform.SetParent(_contentRoot, false);
        slotGo.transform.localPosition = localPos;

        Material shell = useHoloOrbStyle ? hologramShellMaterial : null;
        GameObject molecule = CompoundMoleculeBuilder.Build(
            data, atomPrefab, bondPrefab, atomScale, bondRadiusScale, slotGo.transform,
            warheadAtoms: null, shellMaterial: shell);

        var slot = slotGo.AddComponent<CompoundSlot>();
        slot.Init(data, molecule, boxSize, cellFrameColor, moleculeSpinSpeed);

        CreateLabel(slotGo.transform, data.display_name, data.subtitle,
                    new Vector3(0f, -(boxSize * 0.5f - 0.01f), -boxSize * 0.5f));

        _slots.Add(slot);
    }

    private void CreateLabel(Transform parent, string title, string subtitle, Vector3 localPos)
    {
        // 이름표에 배경판은 두지 않는다. 씬 배경이 어두워서 흰 글씨만으로 충분히 읽히고,
        // 하늘색 판을 깔면 그 자체가 화면에서 눈에 띄는 색면이 돼 분자보다 먼저 시선을 끈다.
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        // 부제는 <size=28>로 작게 찍히므로(본문 48) 같은 폭에 더 많은 글자가 들어간다.
        const float SubtitleFontRatio = 28f / 48f;

        string titleText = Wrap(title, nameLabelSize);
        string subtitleText = Wrap(subtitle, nameLabelSize * SubtitleFontRatio);

        var tm = go.AddComponent<TextMesh>();
        tm.text = string.IsNullOrEmpty(subtitleText)
            ? titleText
            : $"{titleText}\n<size=28>{subtitleText}</size>";
        tm.font = labelFont;
        tm.fontSize = 48;
        tm.characterSize = nameLabelSize * 10f / 48f;
        tm.anchor = TextAnchor.UpperCenter;
        tm.alignment = TextAlignment.Center;
        tm.richText = true;
        tm.color = labelColor;
        var mr = go.GetComponent<MeshRenderer>();
        if (labelFont != null) mr.sharedMaterial = labelFont.material;
    }

    /// <summary>
    /// 이름표를 칸 너비에 맞게 줄바꿈한다.
    ///
    /// TextMesh에는 자동 줄바꿈이 없어서 긴 이름이 한 줄로 뻗고, 2열 격자에서
    /// 옆 칸 이름표와 그대로 겹친다("Generic Cys-reactive Warhead" 같은 것들).
    /// 폰트 메트릭을 직접 재는 대신 글자 폭을 근사한다 — 한글·한자는 라틴 문자의
    /// 약 두 배 폭이므로 2, 나머지는 1로 세고 반각(半角) 단위로 한 줄 예산을 잡는다.
    /// </summary>
    private string Wrap(string text, float emSize)
    {
        if (!wrapLabels || string.IsNullOrWhiteSpace(text)) return text;

        float available = boxSize * Mathf.Max(labelWidthRatio, 0.1f);
        float halfEm = Mathf.Max(emSize * 0.5f, 1e-4f);
        int budget = Mathf.Max(6, Mathf.FloorToInt(available / halfEm));

        var result = new System.Text.StringBuilder(text.Length + 8);
        int lineUnits = 0;

        foreach (string word in text.Split(' '))
        {
            if (word.Length == 0) continue;

            int wordUnits = Units(word);

            if (lineUnits > 0 && lineUnits + 1 + wordUnits > budget)
            {
                result.Append('\n');
                lineUnits = 0;
            }
            else if (lineUnits > 0)
            {
                result.Append(' ');
                lineUnits += 1;
            }

            // 한 단어가 통째로 예산을 넘으면(긴 화합물명) 글자 단위로 끊는다.
            if (wordUnits > budget)
            {
                foreach (char c in word)
                {
                    int cu = Units(c.ToString());
                    if (lineUnits > 0 && lineUnits + cu > budget)
                    {
                        result.Append('\n');
                        lineUnits = 0;
                    }
                    result.Append(c);
                    lineUnits += cu;
                }
                continue;
            }

            result.Append(word);
            lineUnits += wordUnits;
        }

        return result.ToString();
    }

    /// <summary>글자 폭을 반각 단위로 센다. 한글·한자·전각 기호는 2, 나머지는 1.</summary>
    private static int Units(string s)
    {
        int n = 0;
        foreach (char c in s)
            n += (c >= 0x1100 && c <= 0x11FF) ||   // 한글 자모
                 (c >= 0x3000 && c <= 0x303F) ||   // CJK 문장부호
                 (c >= 0x3130 && c <= 0x318F) ||   // 호환 자모
                 (c >= 0x4E00 && c <= 0x9FFF) ||   // 한자
                 (c >= 0xAC00 && c <= 0xD7A3) ||   // 한글 음절
                 (c >= 0xFF00 && c <= 0xFF60)      // 전각 영숫자
                 ? 2 : 1;
        return n;
    }

    private void CreateResultLabels()
    {
        _resultText = CreateTextMesh("ResultMessage");
        _affinityText = CreateTextMesh("AffinityLabel");
    }

    private TextMesh CreateTextMesh(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_contentRoot, false);
        var tm = go.AddComponent<TextMesh>();
        tm.font = labelFont;
        tm.fontSize = 48;
        tm.characterSize = resultLabelSize * 10f / 48f;
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.text = "";
        var mr = go.GetComponent<MeshRenderer>();
        if (labelFont != null) mr.sharedMaterial = labelFont.material;
        return tm;
    }

    private void PositionResultLabels()
    {
        float halfH = _outerSize.y * 0.5f;
        if (_resultText != null)
            _resultText.transform.localPosition = new Vector3(0f, halfH + 0.17f, 0f);
        if (_affinityText != null)
            _affinityText.transform.localPosition = new Vector3(0f, halfH + 0.05f, 0f);
    }

    /// <summary>
    /// 도킹 결과 메시지 + 친화도 수치를 패널 상단에 표시.
    /// messageOverride/affinityOverride를 주면 data의 값 대신 그 문구를 그대로 쓴다 —
    /// 예: 순서 오류(prerequisite 미충족)처럼 화합물 고유의 결과가 아니라 상황에 따라
    /// 다른 안내가 필요한 경우.
    /// </summary>
    public void ShowResult(CompoundData data, Color color, string messageOverride = null, string affinityOverride = null)
    {
        // 결과는 AI 비서가 말풍선으로 말하고 소리로도 읽어준다. 같은 내용을 화면에 한 번 더
        // 띄우면 시선이 갈리고, 색만 다른 글자가 분자 위에 겹쳐 보인다.
        // 대본 문구(result_message)는 비서가 항상 말하므로 백엔드가 없어도 정보가 사라지지 않는다.
        if (!showResultLabels)
        {
            ClearResult();
            return;
        }

        if (_resultText != null)
        {
            _resultText.text = messageOverride ?? data.result_message;
            _resultText.color = color;
        }
        if (_affinityText != null)
        {
            if (affinityOverride != null)
            {
                _affinityText.text = affinityOverride;
            }
            else
            {
                string sign = data.affinity > 0 ? "+" : "";
                _affinityText.text = data.Outcome == DockingOutcome.StericClash
                    ? "ΔG = 측정 불가 (진입 실패)"
                    : $"ΔG = {sign}{data.affinity:0.0} kcal/mol";
            }
            _affinityText.color = color;
        }
    }

    public void ClearResult()
    {
        if (_resultText != null) _resultText.text = "";
        if (_affinityText != null) _affinityText.text = "";
    }

    // --- PC 폴백 입력: 마우스 호버/클릭 (MouseWorldSelector와 동일 패턴) ---

    private void Update()
    {
        if (!AcceptsInput || targetCamera == null || Mouse.current == null) return;
        if (_contentRoot != null && !_contentRoot.gameObject.activeSelf) return; // 숨김 상태에선 입력 무시

        Ray ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        CompoundSlot hit = null;
        if (Physics.Raycast(ray, out RaycastHit hitInfo, maxRayDistance))
            hit = hitInfo.collider.GetComponentInParent<CompoundSlot>();

        if (hit != _hovered)
        {
            if (_hovered != null) _hovered.SetHovered(false);
            _hovered = hit;
            if (_hovered != null) _hovered.SetHovered(true);
        }

        bool overUI = UnityEngine.EventSystems.EventSystem.current != null &&
                      UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        if (hit != null && !overUI && Mouse.current.leftButton.wasPressedThisFrame)
        {
            SelectSlot(hit);
        }
    }

    /// <summary>선택 확정. XR 컨트롤러/핸드 트래킹 인터랙터에서도 이 메서드를 호출하면 된다.</summary>
    public void SelectSlot(CompoundSlot slot)
    {
        if (!AcceptsInput || slot == null) return;
        OnCompoundChosen?.Invoke(slot);
    }
}
