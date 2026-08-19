using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 리본 → Helix → 아미노산으로 내려간 표시 레벨을 버튼으로 되돌린다.
/// (StructureLevelController.GoBack()의 UI 버전 — Esc 키와 동일 동작)
/// Canvas/Button을 런타임에 직접 생성하므로 씬에는 빈 GameObject에 이 스크립트만 붙이면 된다.
///
/// 버튼 위치: 단백질 로드 완료 시 1회만 전체 구조(리본)의 화면상 하단 아래로 계산해 "고정"한다.
/// 이후 레벨 전환(Ribbon/Helix/AminoAcid)이나 회전·줌에도 움직이지 않는다.
/// 다른 퀘스트로 구조가 교체 로드되면 그때만 위치를 다시 잡는다.
/// 리본(최상위) 레벨에서는 버튼이 비활성(회색)으로 표시된다.
/// </summary>
public class StructureLevelBackButton : MonoBehaviour
{
    [Header("참조")]
    public StructureLevelController levelController;

    [Header("표시")]
    [Tooltip("한글 표시를 원하면 한글 폰트 지정. 내장 폰트는 'Back'만 렌더링됨")]
    public Font labelFont;
    public string buttonLabel = "◀ Back (이전 단계)";
    public Vector2 buttonSize = new Vector2(240f, 56f);
    [Tooltip("단백질 화면상 하단과 버튼 사이 간격 (캔버스 기준 px). 로드 시 1회만 반영")]
    public float proteinGap = 20f;
    [Tooltip("단백질 위치를 아직 못 잡았을 때(로딩 전) 화면 하단에서의 여백 (px)")]
    public float bottomMargin = 36f;
    public Color buttonColor = new Color(0.06f, 0.28f, 0.45f, 0.85f);
    public Color textColor = new Color(0.75f, 0.95f, 1f);

    private GameObject _buttonRoot;
    private RectTransform _buttonRect;
    private Button _button;
    private Image _buttonImage;
    private Text _levelText;
    private Canvas _canvas;

    private ProteinLoader _proteinLoader;
    private bool _positioned; // true면 이번 구조에 대한 고정 위치 확정 — 더 이상 갱신하지 않음

    private void Awake()
    {
        // 참조 누락으로 버튼이 아예 동작하지 않는 실수를 방지 — 씬에서 자동 탐색
        if (levelController == null)
        {
            levelController = FindFirstObjectByType<StructureLevelController>();
            if (levelController == null)
                Debug.LogWarning("[StructureLevelBackButton] StructureLevelController를 찾지 못했습니다. " +
                                 "ProteinAnchor_Main에 컴포넌트가 붙어 있는지 확인하세요.");
        }
        if (labelFont == null)
            labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();
    }

    private void OnEnable()
    {
        if (levelController != null)
        {
            levelController.OnLevelChanged += HandleLevelChanged;
            _proteinLoader = levelController.GetComponent<ProteinLoader>();
            if (_proteinLoader != null) _proteinLoader.OnLoaded += HandleProteinLoaded;
        }
    }

    private void OnDisable()
    {
        if (levelController != null) levelController.OnLevelChanged -= HandleLevelChanged;
        if (_proteinLoader != null) _proteinLoader.OnLoaded -= HandleProteinLoaded;
    }

    private void Start()
    {
        HandleLevelChanged(levelController != null
            ? levelController.CurrentLevel
            : StructureLevelController.ViewLevel.Ribbon);
    }

    // 구조가 (재)로드되면 다음 프레임에 위치를 한 번만 다시 계산한다.
    private void HandleProteinLoaded(ProteinLoader.ProteinData data)
    {
        _positioned = false;
    }

    private void LateUpdate()
    {
        if (!_positioned) TryPositionOnce();
    }

    private void HandleLevelChanged(StructureLevelController.ViewLevel level)
    {
        // 버튼은 항상 표시하되, 최상위(리본)에서는 비활성(회색) — 존재를 알 수 있게 한다
        bool canGoBack = level != StructureLevelController.ViewLevel.Ribbon;
        if (_button != null) _button.interactable = canGoBack;
        if (_buttonImage != null)
        {
            Color c = buttonColor;
            if (!canGoBack) { c.a *= 0.35f; }
            _buttonImage.color = c;
        }

        if (_levelText != null)
        {
            switch (level)
            {
                case StructureLevelController.ViewLevel.Ribbon: _levelText.text = "Ribbon"; break;
                case StructureLevelController.ViewLevel.Helix: _levelText.text = "Ribbon ▸ Helix"; break;
                case StructureLevelController.ViewLevel.AminoAcid: _levelText.text = "Ribbon ▸ Helix ▸ Amino Acid"; break;
            }
        }
    }

    // 현재 보이는 렌더러(로드 직후에는 전체 리본)의 화면상 하단 아래로 버튼을 1회 배치.
    // 성공하면 _positioned = true 로 고정, 실패(아직 로딩 전)하면 폴백 위치에 두고 다음 프레임 재시도.
    private void TryPositionOnce()
    {
        if (_buttonRect == null || _canvas == null) return;

        float sf = Mathf.Max(_canvas.scaleFactor, 1e-4f);
        Vector2 canvasSize = new Vector2(Screen.width / sf, Screen.height / sf);

        Camera cam = levelController != null && levelController.targetCamera != null
            ? levelController.targetCamera
            : Camera.main;

        Renderer[] renderers = levelController != null
            ? levelController.GetComponentsInChildren<Renderer>(false)
            : null;

        if (cam == null || renderers == null || renderers.Length == 0)
        {
            // 폴백: 화면 하단 중앙 (로딩이 끝나면 위 경로로 고정 위치가 잡힌다)
            _buttonRect.anchoredPosition = new Vector2(canvasSize.x * 0.5f, bottomMargin + buttonSize.y + 34f);
            return;
        }

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        Vector3 bottom = new Vector3(b.center.x, b.min.y, b.center.z);
        Vector3 screenPos = cam.WorldToScreenPoint(bottom);
        if (screenPos.z <= 0f) return; // 카메라 뒤 — 재시도

        Vector2 target = new Vector2(screenPos.x / sf, screenPos.y / sf - proteinGap);

        // 버튼(+아래 레벨 경로 라벨)이 화면 밖으로 나가지 않게 고정
        float halfW = buttonSize.x * 0.5f;
        target.x = Mathf.Clamp(target.x, halfW, canvasSize.x - halfW);
        target.y = Mathf.Clamp(target.y, buttonSize.y + 34f, canvasSize.y);

        _buttonRect.anchoredPosition = target;
        _positioned = true;
    }

    private void BuildUI()
    {
        // 오버레이 캔버스
        var canvasGo = new GameObject("BackButtonCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 50;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGo.AddComponent<GraphicRaycaster>();

        // 버튼 — 좌하단 앵커 + 상단 피벗(지정점 아래로 매달림), 위치는 TryPositionOnce가 1회 결정
        _buttonRoot = new GameObject("BackButton");
        _buttonRoot.transform.SetParent(canvasGo.transform, false);
        _buttonRect = _buttonRoot.AddComponent<RectTransform>();
        _buttonRect.anchorMin = _buttonRect.anchorMax = Vector2.zero;
        _buttonRect.pivot = new Vector2(0.5f, 1f);
        _buttonRect.sizeDelta = buttonSize;

        _buttonImage = _buttonRoot.AddComponent<Image>();
        _buttonImage.color = buttonColor;

        _button = _buttonRoot.AddComponent<Button>();
        _button.targetGraphic = _buttonImage;
        _button.onClick.AddListener(() => { if (levelController != null) levelController.GoBack(); });

        // 버튼 라벨
        var textGo = new GameObject("Label");
        textGo.transform.SetParent(_buttonRoot.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        var label = textGo.AddComponent<Text>();
        label.text = buttonLabel;
        label.font = labelFont;
        label.fontSize = 24;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = textColor;

        // 현재 레벨 경로 표시 — 버튼 자식으로 두어 버튼 바로 아래에 붙는다
        var levelGo = new GameObject("LevelPath");
        levelGo.transform.SetParent(_buttonRoot.transform, false);
        var levelRect = levelGo.AddComponent<RectTransform>();
        levelRect.anchorMin = levelRect.anchorMax = new Vector2(0.5f, 0f);
        levelRect.pivot = new Vector2(0.5f, 1f);
        levelRect.anchoredPosition = new Vector2(0f, -6f);
        levelRect.sizeDelta = new Vector2(500f, 28f);
        _levelText = levelGo.AddComponent<Text>();
        _levelText.font = labelFont;
        _levelText.fontSize = 18;
        _levelText.alignment = TextAnchor.MiddleCenter;
        _levelText.color = new Color(1f, 1f, 1f, 0.65f);
    }
}
