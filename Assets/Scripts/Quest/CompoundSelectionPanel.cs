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
/// 배치: 단백질 원자들의 실제 월드 경계(bounds)를 측정해서
/// 카메라 시선 기준 "구조 바로 왼쪽 옆 + 상단 높이 일치" 위치에 놓고,
/// 사용자 중앙 시선 쪽으로 diagonalYaw만큼 틀어 사선 배치한다.
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
    [Tooltip("켜면 단백질 경계 대신 사용자 시야(카메라) 기준으로 배치. " +
             "확정된 배치는 구조 옆 사선(끔) — 카메라 상대 배치로 되돌리지 말 것")]
    public bool placeRelativeToCamera = false;
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

    [Header("줌인 오버라이드 (예: 사건 5 열안정성 카메라 클로즈업)")]
    [Tooltip("켜져 있는 동안은 '구조 옆 사선' 대신 카메라 바로 옆에 크게 고정한다 — 카메라가 구조 " +
             "전체가 아니라 좁은 부위로 확 당겨지면 구조 기준 배치가 의미가 없어지기 때문이다. " +
             "ThermalStabilityController처럼 클로즈업 연출을 트는 쪽이 SetZoomOverride()로 켜고 끈다. " +
             "인스펙터 기본값(구조 옆 사선)에는 영향을 주지 않는다.")]
    public bool zoomOverrideActive;
    [Tooltip("줌인 오버라이드 중 사용할 크기 배율 (panelScale 대신 사용)")]
    public float zoomOverridePanelScale = 1.1f;

    [Header("표시 레벨 연동")]
    [Tooltip("아미노산(원자) 레벨에서만 패널이 표시됨. 비우면 씬에서 자동 탐색")]
    public StructureLevelController levelController;

    [Header("라벨")]
    [Tooltip("한글 표시를 위해 한글 글리프가 포함된 폰트를 지정 (비우면 내장 LegacyRuntime — 한글 미지원)")]
    public Font labelFont;
    [Tooltip("화합물 이름/부제 글씨 색")]
    public Color labelColor = Color.black;
    public float nameLabelSize = 0.045f;
    public float resultLabelSize = 0.05f;

    [Header("입력 (PC 폴백)")]
    [Tooltip("비워두면 Camera.main")]
    public Camera targetCamera;
    public float maxRayDistance = 50f;

    /// <summary>사용자가 화합물 박스를 선택했을 때 발생. DockingQuestController가 구독.</summary>
    public event Action<CompoundSlot> OnCompoundChosen;

    public IReadOnlyList<CompoundSlot> Slots => _slots;
    public bool Interactable { get; set; } = true;

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
            PlaceBesideUser(zoomOverridePanelScale);
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
        if (proteinLoader != null) center = proteinLoader.transform.position;
        else if (placementAnchor != null) center = placementAnchor.position;
        else return; // 배치 기준 없음

        // 단백질 실측 경계 대신, 배치를 확정할 때 기준으로 삼은 사건 2(EGFR, P00533) 구조의
        // 실측값을 고정해서 쓴다. 퀘스트마다 단백질 크기가 다르면(KRAS/ABL1/CFTR/p53 등) 이 값을
        // 구조별로 다시 재는 순간 판넬이 퀘스트마다 다른 자리에 놓인다 — "구조 옆 사선"이라는
        // 배치 규칙 자체는 모든 퀘스트에서 똑같이 보여야 하므로 상수로 고정했다.
        //
        // 중심(center)은 여전히 앵커의 실시간 위치를 쓴다 — 아미노산 단계에서 관심 구간만
        // 화면 중앙에 오도록 StructureLevelController.ApplyAminoAcidCentering이 앵커 자체를
        // 미리 옮겨두므로, 앵커 위치를 그대로 따르면 구간 정렬도 자동으로 따라온다.
        Vector3 pos = center - camRight * (ReferenceLateralExtent + panelHalfWidth + sideGap);
        pos.y = center.y + ReferenceTopExtent - panelHalfHeight + topOffset;

        // 카메라→패널 시선 방향을 따라 당기면 화면상 위치는 유지한 채 가깝고 크게 보인다.
        // 상한은 "카메라 앞 0.6m는 남긴다" — 이전의 dist*0.5 상한은 구조가 멀수록 당김을 깎아
        // 판넬이 구조 깊이에 뒤처져 보이는 원인이었다.
        Vector3 toCam = targetCamera.transform.position - pos;
        float dist = toCam.magnitude;
        if (dist > 1e-4f)
            pos += toCam / dist * Mathf.Min(pullTowardCamera, Mathf.Max(dist - 0.6f, 0f));

        transform.position = pos;
        transform.localScale = Vector3.one * scale;

        // TextMesh는 +Z가 뒤통수 — forward가 카메라 반대편을 향해야 글이 바로 보인다.
        // 패널이 시선 왼쪽에 있으므로 음(-)의 yaw를 줘야 정면이 중앙 시선(안쪽)을 향한다.
        Vector3 away = pos - targetCamera.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-6f) return;
        transform.rotation = Quaternion.LookRotation(away.normalized) * Quaternion.Euler(0f, -diagonalYaw, 0f);
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

        transform.position = pos;
        transform.localScale = Vector3.one * Mathf.Max(scale, 0.01f);

        // 패널이 시선 왼쪽에 있으므로 음(-)의 yaw로 정면을 중앙 시선(안쪽)으로 틀어 사선 배치
        Vector3 away = pos - cam.position;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-6f) away = basis * Vector3.forward;
        transform.rotation = Quaternion.LookRotation(away.normalized) * Quaternion.Euler(0f, -diagonalYaw, 0f);
    }

    /// <summary>
    /// ThermalStabilityController처럼 카메라를 구조의 좁은 부위로 클로즈업시키는 연출을 트는 쪽이
    /// 연출 시작/종료에 맞춰 호출한다. true면 "구조 옆 사선" 배치를 잠시 멈추고 카메라 옆에
    /// 크게 고정하며, false면 원래 배치 규칙으로 되돌린다. LateUpdate가 매 프레임 재배치하므로
    /// 카메라가 계속 움직이는 클로즈업 도중에도 계속 따라간다.
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
        // 이름표 전용 배경판. 바닥 글로우(스포트라이트 효과, CompoundSlot.BuildLightPresentation)는
        // 눕혀진 원반이라 카메라 각도에 따라 글자와 어긋나 보인다 — 텍스트와 같은 방향을
        // 향하는(같은 부모 회전을 그대로 따르는) 별도 배경판이라야 어느 각도에서도 글자 뒤에
        // 정확히 붙어 보인다. anchor가 UpperCenter라 텍스트가 localPos에서 아래로 자라므로,
        // 배경판 중심도 그만큼 아래로 내려서 잡는다.
        const float backgroundHeight = 0.11f;
        Vector3 backgroundLocalPos = localPos + new Vector3(0f, -backgroundHeight * 0.5f, 0.015f); // +Z = 텍스트보다 살짝 뒤

        var bgGo = new GameObject("LabelBackground");
        bgGo.transform.SetParent(parent, false);
        bgGo.transform.localPosition = backgroundLocalPos;

        var bgRenderer = bgGo.AddComponent<SpriteRenderer>();
        bgRenderer.sprite = HoloSpriteFactory.Panel();
        bgRenderer.drawMode = SpriteDrawMode.Sliced;
        bgRenderer.size = new Vector2(boxSize * 0.92f, backgroundHeight);
        bgRenderer.color = new Color(0.65f, 0.87f, 1f, 0.55f); // 파스텔 하늘색

        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        var tm = go.AddComponent<TextMesh>();
        tm.text = string.IsNullOrEmpty(subtitle) ? title : $"{title}\n<size=28>{subtitle}</size>";
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
        if (!Interactable || targetCamera == null || Mouse.current == null) return;
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
        if (!Interactable || slot == null) return;
        OnCompoundChosen?.Invoke(slot);
    }
}
