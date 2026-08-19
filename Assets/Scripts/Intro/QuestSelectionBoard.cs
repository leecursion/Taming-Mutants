using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 인트로에서 비서가 펼쳐 보이는 퀘스트 선택 보드.
///
/// 카드를 프리팹으로 두지 않고 런타임에 조립하는 이유: 카드 수는
/// <see cref="QuestCatalog"/>에 몇 개가 들어 있느냐로 정해진다. 프리팹으로 만들면
/// 퀘스트를 추가할 때마다 씬을 열어 카드를 복제하고 참조를 다시 이어야 한다.
///
/// 크기는 <b>화면을 채우는 비율</b>로 정한다. World Space 캔버스에 고정 스케일을 주면
/// 화면 해상도·FOV·거리가 조금만 달라져도 글자가 읽을 수 없이 작아진다.
/// 카드가 몇 장이든 뷰포트 안에 꽉 차게 맞추는 편이 안전하다.
///
/// 텍스트는 프로젝트의 다른 UI와 같이 legacy UI.Text를 쓴다
/// (TMP는 Essential Resources 임포트가 선행돼야 해서 추가 설정 없이 바로 돌지 않는다).
/// </summary>
public class QuestSelectionBoard : MonoBehaviour
{
    [Header("데이터")]
    public QuestCatalog catalog;

    [Header("배치")]
    [Tooltip("비워두면 Camera.main. 보드가 항상 이쪽을 향한다.")]
    public Transform lookTarget;

    [Header("크기 — 화면을 채우는 비율")]
    [Tooltip("끄면 아래 metersPerCanvasUnit을 그대로 쓴다. (거리에 따라 크기가 변한다)")]
    public bool fitToViewport = true;
    [Tooltip("보드 폭이 화면 가로에서 차지할 최대 비율")]
    [Range(0.2f, 0.95f)] public float viewportWidthFraction = 0.68f;
    [Tooltip("보드 높이가 화면 세로에서 차지할 최대 비율. 카드가 많아지면 이쪽이 먼저 걸린다.")]
    [Range(0.2f, 0.95f)] public float viewportHeightFraction = 0.72f;
    [Tooltip("fitToViewport를 끌 때 쓰는 고정 스케일 (캔버스 1unit이 차지하는 m)")]
    public float metersPerCanvasUnit = 0.0022f;

    [Header("레이아웃 (캔버스 단위)")]
    [Tooltip("보드 기준 폭. 실제 월드 크기는 위 비율이 정하므로 여기는 '가로세로 비율'만 결정한다.")]
    public float boardWidth = 900f;
    public float cardHeight = 240f;
    public float cardSpacing = 20f;

    [Header("선명도")]
    [Tooltip("화면 픽셀 1개를 몇 배로 구울지")]
    public float supersample = 2f;
    [Tooltip("글리프 하나를 굽는 최대 픽셀. 동적 폰트 아틀라스가 넘쳐 글자가 깨지는 걸 막는 상한.")]
    public float maxBakedFontPixels = 200f;

    [Header("연출")]
    public float fadeDuration = 0.35f;
    [Tooltip("카드가 하나씩 나타나는 간격(초)")]
    public float cardStagger = 0.09f;

    [Header("색")]
    public Color panelColor = new Color(0.02f, 0.06f, 0.10f, 0.94f);
    public Color bodyColor = new Color(0.86f, 0.95f, 1f);

    /// <summary>카드를 눌렀을 때. <see cref="IntroDirector"/>가 구독한다.</summary>
    public event Action<QuestDefinition> OnQuestSelected;

    public bool IsVisible { get; private set; }

    private Canvas _canvas;
    private CanvasScaler _scaler;
    private CanvasGroup _group;
    private RectTransform _content;
    private readonly List<CanvasGroup> _cardGroups = new List<CanvasGroup>();
    private Coroutine _revealRoutine;
    private Camera _camera;
    private int _largestFontSize;
    private bool _built;

    private void Awake()
    {
        if (lookTarget == null && Camera.main != null) lookTarget = Camera.main.transform;
        Build();
        SetVisibleImmediate(false);
    }

    private void LateUpdate()
    {
        UpdateScale();
        UpdateTextSharpness();
        FaceCamera();
    }

    // --- 크기와 방향 ---

    /// <summary>
    /// 내용 전체가 뷰포트 안에 들어오도록 캔버스 스케일을 정한다.
    ///
    /// 가로 비율과 세로 비율을 각각 계산해 <b>작은 쪽</b>을 쓴다. 한쪽만 보면
    /// 카드가 늘어났을 때 세로로 화면을 뚫고 나가거나, 카드가 하나뿐일 때
    /// 가로로 지나치게 커진다.
    /// </summary>
    private void UpdateScale()
    {
        if (!fitToViewport)
        {
            transform.localScale = Vector3.one * metersPerCanvasUnit;
            return;
        }

        Camera cam = ResolveCamera();
        if (cam == null || _content == null) return;

        float contentHeight = _content.rect.height;
        if (contentHeight <= 1f || boardWidth <= 1f) return;

        float distance = Vector3.Distance(cam.transform.position, transform.position);
        float viewHeight = cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * Mathf.Max(distance, 0.01f) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float viewWidth = viewHeight * cam.aspect;
        if (viewHeight <= 1e-4f) return;

        float byWidth = viewWidth * viewportWidthFraction / boardWidth;
        float byHeight = viewHeight * viewportHeightFraction / contentHeight;
        float meters = Mathf.Min(byWidth, byHeight);

        if (!Mathf.Approximately(transform.localScale.x, meters))
            transform.localScale = Vector3.one * meters;
    }

    /// <summary>
    /// 글자를 굽는 해상도를 화면 밀도에 맞춘다.
    /// dynamicPixelsPerUnit은 "캔버스 1unit당 굽는 픽셀"이고 캔버스 1unit은 월드에서
    /// lossyScale m이므로, 화면 밀도(px/m)에 lossyScale을 곱하면 화면 픽셀과 1:1이 된다.
    /// </summary>
    private void UpdateTextSharpness()
    {
        if (_scaler == null) return;

        Camera cam = ResolveCamera();
        if (cam == null) return;

        float worldScale = transform.lossyScale.x;
        if (worldScale <= 0f) return;

        float distance = Vector3.Distance(cam.transform.position, transform.position);
        float viewHeight = cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * Mathf.Max(distance, 0.01f) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        if (viewHeight <= 1e-4f) return;

        float desired = cam.pixelHeight / viewHeight * worldScale * Mathf.Max(supersample, 0.1f);

        // 굽는 크기 = fontSize x dynamicPixelsPerUnit. 상한을 넘기면 동적 폰트 아틀라스가
        // 넘치면서 이미 그려둔 글리프의 UV가 어긋나 글자가 통째로 깨진다.
        int largest = LargestFontSize();
        if (largest > 0 && maxBakedFontPixels > 0f)
            desired = Mathf.Min(desired, maxBakedFontPixels / largest);

        desired = Mathf.Max(desired, 0.05f);

        // 밀도를 바꿀 때마다 캔버스가 리빌드되고 글리프를 다시 굽는다.
        // 눈에 띄지 않는 변화까지 따라가면 카메라가 움직이는 내내 다시 굽게 되므로
        // 로그 비율로 약 25% 이상 벌어졌을 때만 반영한다.
        float current = _scaler.dynamicPixelsPerUnit;
        if (current > 0.05f && Mathf.Abs(Mathf.Log(desired / current)) < 0.22f) return;

        _scaler.dynamicPixelsPerUnit = desired;
    }

    private void FaceCamera()
    {
        if (lookTarget == null) return;

        // 캔버스의 +Z가 카메라 반대쪽을 보게 하면 UI 면이 카메라를 향한다.
        Vector3 away = transform.position - lookTarget.position;
        if (away.sqrMagnitude < 1e-6f) return;

        transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    /// <summary>
    /// World Space 캔버스는 worldCamera가 비어 있으면 GraphicRaycaster가 아무 일도 하지 않는다.
    /// 오류도 경고도 없이 클릭만 먹지 않아 원인을 찾기 어려우므로, 띄우기 직전에 확인한다.
    /// (Awake 시점에 Camera.main이 아직 없을 수도 있어 여기서 한 번 더 잡는다.)
    /// </summary>
    private void EnsureRaycastCamera()
    {
        if (_canvas == null || _canvas.worldCamera != null) return;

        _canvas.worldCamera = ResolveCamera();

        if (_canvas.worldCamera == null)
            Debug.LogWarning("[QuestSelectionBoard] 클릭 판정에 쓸 카메라를 찾지 못했습니다. " +
                             "카메라에 MainCamera 태그가 있는지 확인하거나 lookTarget을 지정하세요.", this);
    }

    private Camera ResolveCamera()
    {
        if (_camera != null) return _camera;

        if (lookTarget != null) _camera = lookTarget.GetComponent<Camera>();
        if (_camera == null) _camera = Camera.main;
        return _camera;
    }

    private int LargestFontSize()
    {
        if (_largestFontSize > 0) return _largestFontSize;

        foreach (Text text in GetComponentsInChildren<Text>(includeInactive: true))
            if (text.fontSize > _largestFontSize) _largestFontSize = text.fontSize;

        return _largestFontSize;
    }

    // --- 표시 제어 ---

    public void Show()
    {
        if (!_built) Build();

        EnsureRaycastCamera();

        IsVisible = true;
        gameObject.SetActive(true);

        if (_revealRoutine != null) StopCoroutine(_revealRoutine);
        _revealRoutine = StartCoroutine(RevealRoutine());
    }

    public void Hide()
    {
        IsVisible = false;

        if (!gameObject.activeInHierarchy)
        {
            SetVisibleImmediate(false);
            return;
        }

        if (_revealRoutine != null) StopCoroutine(_revealRoutine);
        _revealRoutine = StartCoroutine(FadeOutRoutine());
    }

    private void SetVisibleImmediate(bool visible)
    {
        IsVisible = visible;
        if (_group == null) return;

        _group.alpha = visible ? 1f : 0f;
        _group.blocksRaycasts = visible;
    }

    private IEnumerator RevealRoutine()
    {
        _group.blocksRaycasts = true;

        // 보드 전체를 먼저 띄우고, 카드는 하나씩 뒤따라 나타나게 한다.
        foreach (CanvasGroup card in _cardGroups) card.alpha = 0f;

        yield return FadeGroup(_group, 1f);

        foreach (CanvasGroup card in _cardGroups)
        {
            StartCoroutine(FadeGroup(card, 1f));
            if (cardStagger > 0f) yield return new WaitForSeconds(cardStagger);
        }

        _revealRoutine = null;
    }

    private IEnumerator FadeOutRoutine()
    {
        _group.blocksRaycasts = false;
        yield return FadeGroup(_group, 0f);

        gameObject.SetActive(false);
        _revealRoutine = null;
    }

    private IEnumerator FadeGroup(CanvasGroup group, float target)
    {
        if (group == null) yield break;

        if (fadeDuration <= 0f)
        {
            group.alpha = target;
            yield break;
        }

        float from = group.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            group.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        group.alpha = target;
    }

    // --- 조립 ---

    private void Build()
    {
        if (_built) return;
        _built = true;

        _canvas = gameObject.GetComponent<Canvas>();
        if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        // World Space 캔버스에서 마우스 클릭을 받으려면 어떤 카메라로 레이를 쏠지 알려줘야 한다.
        _canvas.worldCamera = ResolveCamera();

        var canvasRect = _canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = Vector2.zero; // 앵커를 원점 한 점으로 모아 pivot만으로 배치한다
        canvasRect.localScale = Vector3.one * metersPerCanvasUnit;

        _scaler = gameObject.GetComponent<CanvasScaler>();
        if (_scaler == null) _scaler = gameObject.AddComponent<CanvasScaler>();
        _scaler.dynamicPixelsPerUnit = 1f; // 실행 중 UpdateTextSharpness가 다시 정한다

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        _group = gameObject.GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

        BuildContent();

        HoloFont.Apply(gameObject);

        // 스케일 계산이 내용 높이를 읽어야 하므로 레이아웃을 먼저 확정해둔다.
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);

        WarnIfNoEventSystem();
    }

    /// <summary>
    /// 헤더와 카드를 한 덩어리로 묶는다. 따로 두면 전체 높이를 알 수 없어
    /// "화면에 맞추기"를 계산할 수 없다.
    /// </summary>
    private void BuildContent()
    {
        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(transform, false);

        _content = (RectTransform)contentGo.transform;
        _content.anchorMin = _content.anchorMax = new Vector2(0.5f, 0.5f);
        _content.pivot = new Vector2(0.5f, 0.5f);   // 보드 원점을 내용의 한가운데로
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = new Vector2(boardWidth, 100f); // 높이는 아래 fitter가 덮어쓴다

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.spacing = cardSpacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // 폭은 고정, 높이만 내용에 맞춘다
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader(contentGo.transform);
        BuildCards(contentGo.transform);
    }

    private void BuildHeader(Transform parent)
    {
        Text title = CreateText(parent, "Header", 44, FontStyle.Bold, new Color(1f, 1f, 1f, 0.92f));
        title.text = "돌연변이 길들이기 — 사례 선택";
        title.alignment = TextAnchor.MiddleLeft;

        var element = title.gameObject.AddComponent<LayoutElement>();
        element.minHeight = 62f;
        element.preferredHeight = 62f;
    }

    private void BuildCards(Transform parent)
    {
        _cardGroups.Clear();

        if (catalog == null || catalog.Count == 0)
        {
            Debug.LogWarning("[QuestSelectionBoard] 카탈로그가 비어 있습니다. " +
                             "Tools > Taming Mutants > 인트로 + 퀘스트 카탈로그 생성 을 실행하세요.", this);
            return;
        }

        for (int i = 0; i < catalog.Count; i++)
        {
            QuestDefinition quest = catalog.Get(i);
            if (quest != null) BuildCard(parent, quest);
        }
    }

    private void BuildCard(Transform parent, QuestDefinition quest)
    {
        var cardGo = new GameObject($"Card_{quest.questId}", typeof(RectTransform));
        cardGo.transform.SetParent(parent, false);

        var element = cardGo.AddComponent<LayoutElement>();
        element.minHeight = cardHeight;
        element.preferredHeight = cardHeight;

        var group = cardGo.AddComponent<CanvasGroup>();
        _cardGroups.Add(group);

        // 배경 3겹: 글로우 -> 패널 -> 강조 외곽선.
        CreateLayer(cardGo.transform, "Glow", HoloSpriteFactory.Glow(),
            new Color(quest.accent.r, quest.accent.g, quest.accent.b, 0.20f), 26f);
        // 패널이 카드 전체에 깔려 있고 이것만 클릭을 받는다. 어디를 눌러도 카드가 선택된다.
        Image panel = CreateLayer(cardGo.transform, "Panel", HoloSpriteFactory.Panel(), panelColor, 0f,
            raycastTarget: true);
        CreateLayer(cardGo.transform, "Stroke", HoloSpriteFactory.Stroke(),
            new Color(quest.accent.r, quest.accent.g, quest.accent.b, 0.8f), 0f);

        BuildCardText(cardGo.transform, quest);
        BuildDifficulty(cardGo.transform, quest);

        // 버튼은 카드 전체에 깔린 배경 패널을 대상으로 삼아 어디를 눌러도 잡히게 한다.
        var button = cardGo.AddComponent<Button>();
        button.targetGraphic = panel;
        button.transition = Selectable.Transition.ColorTint;

        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        // 루프 변수를 그대로 캡처하면 마지막 퀘스트만 잡히므로 지역 변수에 담아 넘긴다.
        QuestDefinition captured = quest;
        button.onClick.AddListener(() => HandleCardClicked(captured));
    }

    private void BuildCardText(Transform parent, QuestDefinition quest)
    {
        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(parent, false);

        var rect = (RectTransform)textGo.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(30f, 22f);
        rect.offsetMax = new Vector2(-130f, -20f); // 오른쪽은 난이도 표시 자리로 비워둔다

        var layout = textGo.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        Text title = CreateText(textGo.transform, "Title", 42, FontStyle.Bold, Color.white);
        title.text = $"{quest.gene} {quest.mutation}";

        Text subtitle = CreateText(textGo.transform, "Subtitle", 28, FontStyle.Normal,
            new Color(quest.accent.r, quest.accent.g, quest.accent.b, 0.95f));
        subtitle.text = quest.subtitle;

        Text summary = CreateText(textGo.transform, "Summary", 25, FontStyle.Normal,
            new Color(bodyColor.r, bodyColor.g, bodyColor.b, 0.82f));
        summary.text = quest.summary;
        summary.lineSpacing = 1.25f;
    }

    private void BuildDifficulty(Transform parent, QuestDefinition quest)
    {
        var rowGo = new GameObject("Difficulty", typeof(RectTransform));
        rowGo.transform.SetParent(parent, false);

        var rect = (RectTransform)rowGo.transform;
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-30f, 0f);
        rect.sizeDelta = new Vector2(96f, 24f);

        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 7f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        for (int i = 0; i < 5; i++)
        {
            var dotGo = new GameObject($"Dot{i}", typeof(RectTransform));
            dotGo.transform.SetParent(rowGo.transform, false);

            var dot = dotGo.AddComponent<Image>();
            dot.sprite = HoloSpriteFactory.Circle();
            dot.raycastTarget = false;
            // 채워진 칸은 강조색, 나머지는 흐리게 남겨 난이도를 눈으로 세게 한다.
            dot.color = i < quest.difficulty ? quest.accent : new Color(1f, 1f, 1f, 0.18f);

            var element = dotGo.AddComponent<LayoutElement>();
            element.preferredWidth = 14f;
            element.preferredHeight = 14f;
        }
    }

    private void HandleCardClicked(QuestDefinition quest)
    {
        if (!IsVisible) return; // 페이드아웃 중 클릭이 한 번 더 들어오는 걸 막는다

        OnQuestSelected?.Invoke(quest);
    }

    // --- 작은 조립 헬퍼 ---

    /// <param name="raycastTarget">
    /// 클릭을 받을 그래픽인지. Button은 자기 GameObject에 붙어 있어도 직접 레이캐스트를 받지 않는다 —
    /// GraphicRaycaster가 raycastTarget인 Graphic을 먼저 찾고, 거기서 부모로 올라가며 핸들러를 찾는다.
    /// 카드 안의 모든 그래픽을 false로 두면 클릭이 그대로 통과해 버튼이 영영 눌리지 않는다.
    /// </param>
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
        text.font = HoloFont.Resolve();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        return text;
    }

    /// <summary>
    /// EventSystem이 없으면 Button이 클릭을 전혀 받지 못한다.
    /// 씬을 새로 만들었을 때 가장 자주 걸리는 함정이라 명시적으로 알린다.
    /// </summary>
    private void WarnIfNoEventSystem()
    {
        if (EventSystem.current != null) return;

        Debug.LogWarning("[QuestSelectionBoard] 씬에 EventSystem이 없어 카드를 클릭할 수 없습니다. " +
                         "Hierarchy 우클릭 > UI > Event System 을 추가하세요.", this);
    }
}
