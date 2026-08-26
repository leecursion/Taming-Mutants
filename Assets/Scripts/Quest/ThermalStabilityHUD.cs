using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// p53 Y220C 열안정성 퀘스트 전용 HUD. 화면 좌상단에 고정된 홀로그램 패널로
/// 온도·안정성(Stability)·흔들림(Wobble)·p53 총량·DNA 결합능·독성/선택성 경고,
/// 그리고 설계서에 적힌 "HUD:" 문구를 그대로 띄우는 메시지 줄을 보여준다.
///
/// 다른 퀘스트의 진행률 패널(QuestManagerSpatialUI)과는 별개다 — 이 지표들은
/// 도킹 성공/실패가 아니라 "지금 단백질이 얼마나 안정적인가"를 보여주는,
/// 이 퀘스트에만 있는 관측 장비 화면이라는 컨셉이라 분리했다.
/// </summary>
public class ThermalStabilityHUD : MonoBehaviour
{
    [Header("표시")]
    public Vector2 cornerMargin = new Vector2(28f, 28f);
    public float panelWidth = 380f;

    [Header("홀로그램 톤")]
    public Color accentColor = new Color(0.35f, 0.85f, 1f);
    public Color panelColor = new Color(0.02f, 0.06f, 0.10f, 0.94f);
    public Color textColor = new Color(0.85f, 0.95f, 1f);
    public Color warningColor = new Color(1f, 0.35f, 0.2f);
    public Color goodColor = new Color(0.35f, 1f, 0.55f);
    public Color badColor = new Color(1f, 0.55f, 0.2f);

    private Canvas _canvas;
    private Text _tempText;
    private Image _stabilityFill;
    private Text _stabilityLabel;
    private Image _wobbleFill;
    private Text _wobbleLabel;
    private Image _p53Fill;
    private Image _dnaDot;
    private Text _dnaLabel;
    private GameObject _warningRow;
    private Text _warningText;
    private Text _messageText;

    private void Awake()
    {
        BuildUI();
        SetStability(0f, "LOW");
        SetWobble(0f, "—");
        SetP53Quantity(0f);
        SetDnaBindingCompetent(false);
        HideWarning();
        ShowMessage(string.Empty);

        // 사건 5(p53)를 시작하기 전까지는 보이면 안 된다 — ThermalStabilityController가
        // EnterThermalStage()에서 명시적으로 다시 켠다.
        gameObject.SetActive(false);
    }

    // --- 외부 API ---

    public void SetTemperature(float celsius)
    {
        if (_tempText != null) _tempText.text = $"Temperature: {celsius:0}°C";
    }

    public void SetStability(float value01, string label)
    {
        value01 = Mathf.Clamp01(value01);
        if (_stabilityFill != null)
        {
            _stabilityFill.fillAmount = value01;
            _stabilityFill.color = Color.Lerp(badColor, goodColor, value01);
        }
        if (_stabilityLabel != null) _stabilityLabel.text = $"Stability: {label}";
    }

    public void SetWobble(float value01, string label)
    {
        value01 = Mathf.Clamp01(value01);
        if (_wobbleFill != null)
        {
            _wobbleFill.fillAmount = value01;
            _wobbleFill.color = Color.Lerp(goodColor, badColor, value01); // 높을수록(많이 흔들릴수록) 나쁜색
        }
        if (_wobbleLabel != null) _wobbleLabel.text = $"Wobble: {label}";
    }

    public void SetP53Quantity(float value01)
    {
        if (_p53Fill != null) _p53Fill.fillAmount = Mathf.Clamp01(value01);
    }

    public void SetDnaBindingCompetent(bool competent)
    {
        if (_dnaDot != null) _dnaDot.color = competent ? goodColor : new Color(1f, 1f, 1f, 0.25f);
        if (_dnaLabel != null) _dnaLabel.text = competent ? "DNA-binding: competent" : "DNA-binding: none";
    }

    public void ShowWarning(string text)
    {
        if (_warningRow != null) _warningRow.SetActive(true);
        if (_warningText != null) _warningText.text = text;
    }

    public void HideWarning()
    {
        if (_warningRow != null) _warningRow.SetActive(false);
    }

    /// <summary>설계서의 "HUD: ..." 문구를 그대로 띄운다. 빈 문자열이면 줄을 비운다.</summary>
    public void ShowMessage(string text)
    {
        if (_messageText != null) _messageText.text = text;
    }

    // --- 조립 ---

    private void BuildUI()
    {
        var canvasGo = new GameObject("ThermalHudCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 40;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var rootGo = new GameObject("ThermalHud", typeof(RectTransform));
        rootGo.transform.SetParent(canvasGo.transform, false);
        var rootRect = (RectTransform)rootGo.transform;
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 1f); // 좌상단
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = new Vector2(cornerMargin.x, -cornerMargin.y);
        rootRect.sizeDelta = new Vector2(panelWidth, 10f); // 높이는 fitter가 정한다

        CreateLayer(rootGo.transform, "Glow", HoloSpriteFactory.Glow(),
            new Color(accentColor.r, accentColor.g, accentColor.b, 0.16f), 18f);
        CreateLayer(rootGo.transform, "Panel", HoloSpriteFactory.Panel(), panelColor, 0f);
        CreateLayer(rootGo.transform, "Stroke", HoloSpriteFactory.Stroke(),
            new Color(accentColor.r, accentColor.g, accentColor.b, 0.7f), 0f);

        var layout = rootGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 16);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = rootGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Text title = CreateText(rootGo.transform, "Title", 22, FontStyle.Bold,
            new Color(accentColor.r, accentColor.g, accentColor.b, 0.95f));
        title.text = "THERMAL STABILITY MONITOR";

        _tempText = CreateText(rootGo.transform, "Temp", 20, FontStyle.Normal, textColor);

        _stabilityLabel = CreateText(rootGo.transform, "StabilityLabel", 18, FontStyle.Normal, textColor);
        _stabilityFill = CreateBar(rootGo.transform, "StabilityBar");

        _wobbleLabel = CreateText(rootGo.transform, "WobbleLabel", 18, FontStyle.Normal, textColor);
        _wobbleFill = CreateBar(rootGo.transform, "WobbleBar");

        var p53Row = new GameObject("P53Row", typeof(RectTransform));
        p53Row.transform.SetParent(rootGo.transform, false);
        var p53Layout = p53Row.AddComponent<HorizontalLayoutGroup>();
        p53Layout.spacing = 10f;
        p53Layout.childAlignment = TextAnchor.MiddleLeft;
        p53Layout.childControlWidth = true;
        p53Layout.childControlHeight = true;
        Text p53Label = CreateText(p53Row.transform, "P53Label", 18, FontStyle.Normal, textColor);
        p53Label.text = "p53 quantity";
        var p53LabelElement = p53Label.gameObject.AddComponent<LayoutElement>();
        p53LabelElement.preferredWidth = 150f;
        _p53Fill = CreateBar(p53Row.transform, "P53Bar");

        var dnaRow = new GameObject("DnaRow", typeof(RectTransform));
        dnaRow.transform.SetParent(rootGo.transform, false);
        var dnaLayout = dnaRow.AddComponent<HorizontalLayoutGroup>();
        dnaLayout.spacing = 8f;
        dnaLayout.childAlignment = TextAnchor.MiddleLeft;
        dnaLayout.childControlWidth = false;
        dnaLayout.childControlHeight = true;
        var dnaDotGo = new GameObject("Dot", typeof(RectTransform));
        dnaDotGo.transform.SetParent(dnaRow.transform, false);
        _dnaDot = dnaDotGo.AddComponent<Image>();
        _dnaDot.sprite = HoloSpriteFactory.Circle();
        var dnaDotElement = dnaDotGo.AddComponent<LayoutElement>();
        dnaDotElement.preferredWidth = dnaDotElement.preferredHeight = 14f;
        _dnaLabel = CreateText(dnaRow.transform, "DnaLabel", 18, FontStyle.Normal, textColor);

        _warningRow = new GameObject("Warning", typeof(RectTransform));
        _warningRow.transform.SetParent(rootGo.transform, false);
        _warningText = CreateText(_warningRow.transform, "WarningText", 17, FontStyle.Bold, warningColor);
        var warnRect = (RectTransform)_warningText.transform;
        warnRect.anchorMin = Vector2.zero; warnRect.anchorMax = Vector2.one;
        warnRect.offsetMin = warnRect.offsetMax = Vector2.zero;
        _warningText.horizontalOverflow = HorizontalWrapMode.Wrap;

        var divider = new GameObject("Divider", typeof(RectTransform));
        divider.transform.SetParent(rootGo.transform, false);
        var dividerImg = divider.AddComponent<Image>();
        dividerImg.color = new Color(1f, 1f, 1f, 0.2f);
        var dividerElement = divider.AddComponent<LayoutElement>();
        dividerElement.minHeight = dividerElement.preferredHeight = 1f;

        _messageText = CreateText(rootGo.transform, "Message", 18, FontStyle.Italic,
            new Color(textColor.r, textColor.g, textColor.b, 0.9f));
        _messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
        var msgElement = _messageText.gameObject.AddComponent<LayoutElement>();
        msgElement.preferredWidth = panelWidth - 40f;
    }

    private Image CreateBar(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var track = new GameObject("Track", typeof(RectTransform));
        track.transform.SetParent(go.transform, false);
        var trackImg = track.AddComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.12f);
        var trackRect = (RectTransform)track.transform;
        trackRect.anchorMin = Vector2.zero; trackRect.anchorMax = Vector2.one;
        trackRect.offsetMin = trackRect.offsetMax = Vector2.zero;

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(go.transform, false);
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.color = goodColor;
        var fillRect = (RectTransform)fillGo.transform;
        fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;

        var element = go.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = 12f;
        element.flexibleWidth = 1f;

        return fillImg;
    }

    private static Image CreateLayer(Transform parent, string name, Sprite sprite, Color color, float expand)
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
        image.raycastTarget = false;
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
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        var element = go.AddComponent<LayoutElement>();
        element.preferredHeight = fontSize + 6f;

        return text;
    }
}
