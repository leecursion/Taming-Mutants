using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 사건 4(CFTR F508del) 전용 HUD. 화면 좌상단에 고정된 홀로그램 패널로
/// Surface CFTR(세포막 도달량)·Channel activity(채널 활성)·ER stress 경고, 그리고
/// 설계서의 "HUD:" 문구를 그대로 띄우는 메시지 줄을 보여준다.
///
/// ThermalStabilityHUD(p53)와 같은 자리 — 코드로 전부 조립하는 같은 기법을 그대로 따른다.
/// </summary>
public class CftrHUD : MonoBehaviour
{
    [Header("표시")]
    public Vector2 cornerMargin = new Vector2(28f, 28f);
    public float panelWidth = 380f;

    [Header("홀로그램 톤")]
    public Color accentColor = new Color(0.3f, 0.9f, 0.65f);
    public Color panelColor = new Color(0.02f, 0.06f, 0.10f, 0.94f);
    public Color textColor = new Color(0.85f, 0.95f, 1f);
    public Color warningColor = new Color(1f, 0.35f, 0.2f);
    public Color goodColor = new Color(0.35f, 1f, 0.55f);
    public Color badColor = new Color(1f, 0.55f, 0.2f);

    private Text _surfaceLabel;
    private Image _surfaceFill;
    private Text _channelLabel;
    private Image _channelFill;
    private GameObject _warningRow;
    private Text _warningText;
    private Text _messageText;

    private void Awake()
    {
        BuildUI();
        SetSurfaceCftr(0f, "매우 적음");
        SetChannelActivity(0f, "측정 전");
        HideWarning();
        ShowMessage(string.Empty);

        // 사건 4를 시작하기 전까지는 보이면 안 된다 — CftrRescueController가
        // 아미노산 레벨(Level 2) 진입 시 명시적으로 다시 켠다.
        gameObject.SetActive(false);
    }

    // --- 외부 API ---

    public void SetSurfaceCftr(float value01, string label)
    {
        value01 = Mathf.Clamp01(value01);
        if (_surfaceFill != null)
        {
            _surfaceFill.fillAmount = value01;
            _surfaceFill.color = Color.Lerp(badColor, goodColor, value01);
        }
        if (_surfaceLabel != null) _surfaceLabel.text = $"세포 표면의 CFTR 양: {label}";
    }

    public void SetChannelActivity(float value01, string label)
    {
        value01 = Mathf.Clamp01(value01);
        if (_channelFill != null)
        {
            _channelFill.fillAmount = value01;
            _channelFill.color = Color.Lerp(badColor, goodColor, value01);
        }
        if (_channelLabel != null) _channelLabel.text = $"채널이 열리는 정도: {label}";
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

    // --- 조립 (ThermalStabilityHUD.BuildUI와 같은 기법) ---

    private void BuildUI()
    {
        var canvasGo = new GameObject("CftrHudCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var rootGo = new GameObject("CftrHud", typeof(RectTransform));
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
        title.text = "CFTR 상태 화면";

        _surfaceLabel = CreateText(rootGo.transform, "SurfaceLabel", 18, FontStyle.Normal, textColor);
        _surfaceFill = CreateBar(rootGo.transform, "SurfaceBar");

        _channelLabel = CreateText(rootGo.transform, "ChannelLabel", 18, FontStyle.Normal, textColor);
        _channelFill = CreateBar(rootGo.transform, "ChannelBar");

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
