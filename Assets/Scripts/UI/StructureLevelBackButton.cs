using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 리본 → Helix → 아미노산으로 내려간 표시 레벨을 버튼으로 되돌린다.
/// (StructureLevelController.GoBack()의 UI 버전 — Esc 키와 동일 동작)
/// Canvas/Button을 런타임에 직접 생성하므로 씬에는 빈 GameObject에 이 스크립트만 붙이면 된다.
///
/// 배치: 화면 우하단 고정. 구조를 가리지 않도록 작게 만들고,
/// 퀘스트 보드와 같은 홀로그램 톤(HoloSpriteFactory 패널/외곽선/글로우 + HoloFont)으로 그린다.
/// 내용: "◀ 이전" 버튼 하나와 그 아래 현재 단계 이름만.
/// 구조가 로드되어 리본이 보일 때 함께 나타나고, 리본(최상위)에서는 흐리게 비활성된다.
/// </summary>
public class StructureLevelBackButton : MonoBehaviour
{
    [Header("참조")]
    public StructureLevelController levelController;

    [Header("표시")]
    public string buttonLabel = "◀ 이전";
    public Vector2 buttonSize = new Vector2(132f, 46f);
    [Tooltip("화면 우하단 모서리에서의 여백 (캔버스 기준 px)")]
    public Vector2 cornerMargin = new Vector2(28f, 24f);

    [Header("홀로그램 톤 (퀘스트 보드와 동일 계열)")]
    [Tooltip("외곽선/글로우 강조색")]
    public Color accentColor = new Color(0.25f, 0.75f, 1f);
    [Tooltip("패널 배경색")]
    public Color panelColor = new Color(0.02f, 0.06f, 0.10f, 0.94f);
    public Color textColor = new Color(0.75f, 0.95f, 1f);

    private Button _button;
    private CanvasGroup _buttonGroup; // 리본(되돌아갈 곳 없음)에서 흐리게 만들 때 사용
    private Text _levelText;
    private Canvas _canvas;

    private ProteinLoader _proteinLoader;

    private void Awake()
    {
        // 참조 누락으로 버튼이 아예 동작하지 않는 실수를 방지 — 씬에서 자동 탐색
        // (인트로 동안 무대가 꺼져 있으므로 비활성 오브젝트까지 뒤진다)
        if (levelController == null)
        {
            levelController = FindFirstObjectByType<StructureLevelController>(FindObjectsInactive.Include);
            if (levelController == null)
                Debug.LogWarning("[StructureLevelBackButton] StructureLevelController를 찾지 못했습니다. " +
                                 "ProteinAnchor_Main에 컴포넌트가 붙어 있는지 확인하세요.");
        }
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

    // 인트로(퀘스트 선택) 동안에는 구조가 없으므로 버튼도 숨겨져 있다가,
    // 리본 구조가 처음 보이는 이 시점에 함께 나타난다.
    private void HandleProteinLoaded(ProteinLoader.ProteinData data)
    {
        if (_canvas != null) _canvas.gameObject.SetActive(true);
    }

    private void HandleLevelChanged(StructureLevelController.ViewLevel level)
    {
        // 버튼은 항상 표시하되, 최상위(리본)에서는 흐리게 — 존재만 알 수 있게 한다
        bool canGoBack = level != StructureLevelController.ViewLevel.Ribbon;
        if (_button != null) _button.interactable = canGoBack;
        if (_buttonGroup != null) _buttonGroup.alpha = canGoBack ? 1f : 0.35f;

        if (_levelText != null)
        {
            switch (level)
            {
                case StructureLevelController.ViewLevel.Ribbon: _levelText.text = "Ribbon"; break;
                case StructureLevelController.ViewLevel.Helix: _levelText.text = "Helix"; break;
                case StructureLevelController.ViewLevel.AminoAcid: _levelText.text = "Amino Acid"; break;
            }
        }
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

        const float levelTextHeight = 24f;
        const float levelTextGap = 4f;

        // 루트: 화면 우하단 모서리 고정 (버튼 + 아래 단계 라벨을 한 덩어리로)
        var rootGo = new GameObject("BackControl");
        rootGo.transform.SetParent(canvasGo.transform, false);
        var rootRect = rootGo.AddComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(1f, 0f); // 우하단
        rootRect.pivot = new Vector2(1f, 0f);
        rootRect.anchoredPosition = new Vector2(-cornerMargin.x, cornerMargin.y);
        rootRect.sizeDelta = new Vector2(buttonSize.x, buttonSize.y + levelTextGap + levelTextHeight);

        // 버튼 (루트 상단)
        var buttonGo = new GameObject("BackButton");
        buttonGo.transform.SetParent(rootGo.transform, false);
        var buttonRect = buttonGo.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 1f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(0.5f, 1f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = new Vector2(0f, buttonSize.y);

        _buttonGroup = buttonGo.AddComponent<CanvasGroup>();

        // 홀로그램 3겹: 글로우 → 패널 → 외곽선 (퀘스트 보드 카드와 동일한 구성)
        CreateLayer(buttonGo.transform, "Glow", HoloSpriteFactory.Glow(),
            new Color(accentColor.r, accentColor.g, accentColor.b, 0.18f), 16f);
        Image panel = CreateLayer(buttonGo.transform, "Panel", HoloSpriteFactory.Panel(), panelColor, 0f,
            raycastTarget: true);
        CreateLayer(buttonGo.transform, "Stroke", HoloSpriteFactory.Stroke(),
            new Color(accentColor.r, accentColor.g, accentColor.b, 0.8f), 0f);

        _button = buttonGo.AddComponent<Button>();
        _button.targetGraphic = panel;
        _button.transition = Selectable.Transition.ColorTint;
        var colors = _button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = Color.white; // 비활성 표현은 CanvasGroup 알파가 담당
        colors.fadeDuration = 0.12f;
        _button.colors = colors;
        _button.onClick.AddListener(() => { if (levelController != null) levelController.GoBack(); });

        // 버튼 라벨
        Text label = CreateText(buttonGo.transform, "Label", 22, FontStyle.Bold, textColor);
        label.text = buttonLabel;
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

        // 현재 단계 라벨 — 버튼 아래
        Text levelText = CreateText(rootGo.transform, "LevelLabel", 16, FontStyle.Normal,
            new Color(textColor.r, textColor.g, textColor.b, 0.7f));
        _levelText = levelText;
        var levelRect = (RectTransform)levelText.transform;
        levelRect.anchorMin = new Vector2(0f, 0f);
        levelRect.anchorMax = new Vector2(1f, 0f);
        levelRect.pivot = new Vector2(0.5f, 0f);
        levelRect.anchoredPosition = Vector2.zero;
        levelRect.sizeDelta = new Vector2(0f, levelTextHeight);

        // 플레이 직후(인트로/퀘스트 선택 중)에는 보이지 않게 — 구조 로드 시점에 켠다
        canvasGo.SetActive(false);
    }

    private static Image CreateLayer(Transform parent, string name, Sprite sprite, Color color, float expand,
                                     bool raycastTarget = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-expand, -expand);
        rect.offsetMax = new Vector2(expand, expand);

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
    }

    private static Text CreateText(Transform parent, string name, int fontSize, FontStyle style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var text = go.AddComponent<Text>();
        text.font = HoloFont.Resolve(); // 한글("이전", 단계 이름) 표시를 위해 공용 폰트 사용
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
