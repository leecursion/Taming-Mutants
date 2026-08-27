using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 비서가 말하는 내용을 띄우는 World Space 말풍선.
/// 타이핑 연출 -> 글자 수에 비례한 유지 시간 -> 페이드아웃 순으로 자동 진행되고,
/// 연속 호출은 큐에 쌓여 하나씩 재생된다.
///
/// AI 연동 단계에서는 AICoScientistClient.OnReplyReceived에 <see cref="Say"/>를 연결하면 된다.
/// 텍스트 컴포넌트는 QuestManagerSpatialUI와 동일하게 legacy UI.Text를 쓴다.
/// (TMP는 Essential Resources를 임포트해야 동작하므로 추가 설정 없이 바로 쓰려고 맞춘 것 —
///  나중에 임포트하면 TMP_Text로 교체해도 이 스크립트 로직은 그대로다.)
/// </summary>
public class AIAssistantSpeechBubble : MonoBehaviour
{
    [Header("참조")]
    public CanvasGroup canvasGroup;
    public Text label;
    [Tooltip("배경 패널. 숨길 때 통째로 비활성화한다.")]
    public RectTransform bubbleRect;
    [Tooltip("말하는 동안 상태를 Speaking으로 바꾼다. 비워두면 부모에서 찾는다.")]
    public AIAssistantVisual visual;
    public bool driveVisualState = true;

    [Header("강조색")]
    [Tooltip("비서의 상태 색을 따라가는 그래픽들 (외곽선, 글로우, 연결선, 헤더 등). 알파는 각자 유지된다.")]
    public Graphic[] accentGraphics;
    public bool tintAccentWithState = true;

    [Header("빌보드")]
    [Tooltip("비워두면 Camera.main. 비서가 뱅킹으로 기울어도 글자는 수평을 유지한다.")]
    public Transform lookTarget;

    [Header("선명도")]
    [Tooltip("이 캔버스의 CanvasScaler. 비워두면 같은 오브젝트에서 찾는다.")]
    public CanvasScaler canvasScaler;
    [Tooltip("기준 거리에서 캔버스 1unit이 월드에서 차지하는 거리(m). 부모(비서)를 아무리 확대해도 " +
             "이 값이 유지되도록 캔버스 스케일을 반대로 보정한다.")]
    public float metersPerCanvasUnit = 0.001f;

    [Tooltip("켜두면 카메라 거리에 비례해 말풍선을 키워, 화면에서 차지하는 크기를 항상 일정하게 유지한다. " +
             "끄면 월드 크기가 고정돼 카메라가 다가갈수록 글자가 화면에서 커지며 흐려진다.")]
    public bool constantApparentSize = true;
    [Tooltip("metersPerCanvasUnit이 그대로 적용되는 기준 카메라 거리(m).")]
    public float referenceDistance = 2f;
    [Tooltip("거리 보정에 쓸 카메라 거리의 하한/상한(m). 너무 붙거나 멀어져도 크기가 폭주하지 않게 막는다.")]
    public Vector2 distanceClamp = new Vector2(0.35f, 6f);

    [Tooltip("화면 픽셀 1개를 몇 배로 구울지. 2면 2배로 구워 확대와 안티에일리어싱에 여유를 둔다.")]
    public float supersample = 2f;
    [Tooltip("글리프 하나를 굽는 최대 픽셀. 동적 폰트 아틀라스가 넘쳐 글자가 통째로 깨지는 걸 막는 상한. " +
             "한글은 한 화면에도 고유 글리프가 수십 개라 2048 아틀라스 기준 200px 근처가 한계다.")]
    public float maxBakedFontPixels = 200f;

    [Header("한글 폰트")]
    [Tooltip("Unity 내장 LegacyRuntime.ttf에는 한글 글리프가 없다. 켜두면 실행 시 OS 폰트로 교체한다. " +
             "Quest 빌드에서는 한글 TTF를 프로젝트에 임포트해 쓰는 편이 안전하다.")]
    public bool useOsFontForKorean = true;
    [Tooltip("앞에서부터 찾아 처음 있는 것을 쓴다.")]
    public string[] osFontCandidates =
    {
        "Malgun Gothic", "맑은 고딕", "NanumGothic", "Noto Sans CJK KR", "AppleSDGothicNeo-Regular",
    };

    [Header("본문 폭")]
    [Tooltip("말풍선이 줄어들 수 있는 하한(한글 글자 수). 짧은 대사에서 패널이 헤더보다 좁아지지 않게 막는다.")]
    public int minimumCharactersPerLine = 10;

    [Tooltip("말풍선 최대 폭을 화면 폭의 이 비율로 정한다. (예: 1/3이면 화면 오른쪽 3분의 1만큼)\n" +
             "constantApparentSize 덕분에 카메라 거리와 무관하게 항상 같은 화면 비율을 차지한다.")]
    [Range(0.05f, 1f)]
    public float screenWidthFraction = 1f / 3f;

    [Tooltip("어절(띄어쓰기) 단위로 줄을 끊는다. Unity 기본 Text는 한글을 CJK로 보고 글자 단위로 " +
             "아무 데서나 끊어 '정밀검 / 사실'처럼 단어 한가운데가 갈라진다.")]
    public bool wrapAtWordBoundaries = true;

    [Tooltip("줄 간격 배수. 0 이하면 씬에 저장된 값을 그대로 쓴다.")]
    public float lineSpacing = 1.25f;

    [Header("긴 문장 나누기")]
    [Tooltip("이 글자 수를 넘는 메시지는 문장 단위로 잘라 여러 말풍선에 나눠 띄운다.\n" +
             "한 말풍선에 긴 글이 들어가면 줄이 빽빽해져 읽기 어렵다. 0 이하면 나누지 않는다.")]
    public int maxCharactersPerBubble = 60;

    [Header("음성")]
    [Tooltip("연결하면 대사를 소리로도 읽는다. 비워두면 씬에서 찾는다.")]
    public TextToSpeechBackend tts;
    [Tooltip("대사를 소리로 읽을지. 끄면 글만 띄운다.")]
    public bool speakAloud = true;
    [Tooltip("소리가 끝난 뒤 말풍선을 더 띄워두는 시간(초). 마지막 문장을 눈으로 훑을 여유다.")]
    public float voicedTailSeconds = 0.6f;
    [Tooltip("소리를 기다리는 최대 시간(초). 합성이나 재생이 멈춰도 말풍선이 갇히지 않게 하는 상한이다.")]
    public float maxVoiceSeconds = 30f;

    [Header("타이핑")]
    public bool useTypewriter = true;
    public float charactersPerSecond = 35f;

    [Header("표시 시간")]
    [Tooltip("타이핑이 끝난 뒤 유지할 기본 시간(초)")]
    public float baseHoldSeconds = 1.5f;
    [Tooltip("글자 하나당 추가로 유지할 시간(초)")]
    public float secondsPerCharacter = 0.045f;
    public float maxHoldSeconds = 12f;

    [Header("건너뛰기 버튼")]
    [Tooltip("말풍선 아래에 '건너뛰기' 버튼을 띄운다. 누르면 지금 재생 중인 대사 한 개를 " +
             "즉시 끝내고 다음 대사로 넘어간다(큐에 남은 대사는 그대로 재생된다).")]
    public bool showSkipButton = true;
    [Tooltip("직접 만든 버튼을 쓰려면 연결한다. 비워두면 실행 시 말풍선 아래에 자동으로 만든다.")]
    public Button skipButton;
    [Tooltip("말풍선 아래 가장자리와 버튼 사이 간격(캔버스 단위). 0 이하면 본문 글자 크기에서 정한다.")]
    public float skipButtonGap;
    public string skipButtonLabel = "건너뛰기 ▶";

    [Header("등장 연출")]
    public float fadeDuration = 0.25f;
    [Tooltip("등장할 때 시작 스케일. 살짝 튀어나오며 열린다.")]
    public float popFromScale = 0.88f;
    [Tooltip("사라질 때 줄어드는 스케일")]
    public float collapseToScale = 0.96f;

    /// <summary>현재 말하는 중이거나 대기 중인 메시지가 있는지.</summary>
    public bool IsBusy => _runner != null || _hasCurrent || _pending.Count > 0;

    /// <summary>재생이 멈춰 있는지. 멈춘 동안에도 큐는 유지된다.</summary>
    public bool IsPaused => _paused;

    // 밀도를 바꾸면 캔버스가 리빌드되고 글리프를 다시 굽는다.
    // 화면상 티도 안 나는 변화까지 따라가면 카메라가 움직이는 내내 매 프레임 다시 굽게 된다.
    // 로그 비율로 이 값(약 25%)을 넘을 때만 반영한다.
    private const float RebakeThreshold = 0.22f;

    private readonly Queue<SpeechCue> _pending = new Queue<SpeechCue>();
    private Coroutine _runner;
    // 재생 중인 대사. 일시정지로 끊기면 이걸 들고 있다가 다시 튼다.
    private SpeechCue _current;
    private bool _hasCurrent;
    // 같은 대사를 다시 틀어도 붙은 행동(변이 부위 반짝임 등)은 한 번만 실행한다.
    private bool _currentActionFired;
    private bool _paused;
    // 건너뛰기 버튼이 켜는 스위치. 지금 재생 중인 대사 하나에만 적용되고, 다음 대사를
    // 큐에서 꺼낼 때 다시 꺼진다.
    private bool _skipRequested;
    // 지금 읽고 있는 대사의 소리가 끝났는지. 콜백으로 켜진다.
    private bool _voiceDone = true;
    private float[] _accentAlphas;
    private Camera _camera;
    private int _largestFontSize;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponentInChildren<CanvasGroup>(includeInactive: true);
        if (visual == null) visual = GetComponentInParent<AIAssistantVisual>();
        if (lookTarget == null && Camera.main != null) lookTarget = Camera.main.transform;
        if (canvasScaler == null) canvasScaler = GetComponent<CanvasScaler>();
        if (tts == null) tts = FindFirstObjectByType<TextToSpeechBackend>(FindObjectsInactive.Include);

        // 폰트 교체와 항상-위 머티리얼 적용보다 먼저 만들어야 버튼의 글자도 함께 처리된다.
        EnsureSkipButton();

        CacheAccentAlphas();
        ApplyOsFont();
        ApplyAlwaysOnTopMaterial();
        ApplyBubbleWidth();

        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (bubbleRect != null) bubbleRect.gameObject.SetActive(false);
    }

    /// <summary>
    /// 말풍선 안의 모든 Text를 OS 폰트로 교체한다.
    /// 런타임에 만든 폰트라 씬에 저장되지 않고, 플레이 중 도메인이 리로드되면 사라지므로
    /// <see cref="LateUpdate"/>에서 없어진 걸 발견하면 다시 만든다.
    /// </summary>
    private void ApplyOsFont()
    {
        if (!useOsFontForKorean || osFontCandidates == null || osFontCandidates.Length == 0) return;

        Font font = Font.CreateDynamicFontFromOSFont(osFontCandidates, 32);
        if (font == null) return;

        foreach (var text in GetComponentsInChildren<Text>(includeInactive: true))
            text.font = font;
    }

    // --- 건너뛰기 버튼 ---

    /// <summary>
    /// 말풍선 <b>아래</b>에 붙는 건너뛰기 버튼을 준비한다.
    ///
    /// 씬(AIAssistantSetupMenu)이 만들어 둔 말풍선에는 이 버튼이 없다. 메뉴를 다시 돌려야만
    /// 생기게 하면 이미 배치된 비서에는 영영 안 붙으므로, 없으면 실행 시 직접 만든다.
    ///
    /// 위치는 <see cref="bubbleRect"/>의 자식으로 두고 앵커를 아래 가장자리 중앙에 건다.
    /// 그러면 말풍선이 대사 길이에 따라 커지고 작아져도, 비서를 따라 화면을 이동해도
    /// 버튼이 늘 말풍선 바로 아래에 붙어 다닌다 — 좌표를 따로 따라다니게 계산할 필요가 없다.
    /// 접히고 펼쳐지는 연출(알파·스케일)과 숨김(SetActive)도 말풍선과 함께 적용된다.
    /// </summary>
    private void EnsureSkipButton()
    {
        if (!showSkipButton || bubbleRect == null || label == null) return;

        if (skipButton == null) skipButton = BuildSkipButton();
        if (skipButton == null) return;

        skipButton.onClick.AddListener(SkipCurrent);

        // 이 캔버스에는 지금까지 상호작용이 없어 레이캐스터를 달지 않았다(원자 선택 클릭을
        // 가로채지 않으려고). 버튼이 생겼으니 필요하다 — 말풍선의 다른 그래픽은 모두
        // raycastTarget이 꺼져 있어서 이 버튼 말고는 아무것도 클릭을 가로채지 않는다.
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

        var canvas = GetComponent<Canvas>();
        if (canvas != null && canvas.worldCamera == null) canvas.worldCamera = ResolveCamera();

        // 말풍선 CanvasGroup은 "클릭을 받지 않는 표시물"로 꺼 둔 상태였다. 버튼이 그 안에 있으니 켠다.
        if (canvasGroup != null)
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    private Button BuildSkipButton()
    {
        float fontSize = Mathf.Max(label.fontSize, 1);
        float gap = skipButtonGap > 0f ? skipButtonGap : fontSize * 0.6f;

        var go = new GameObject("SkipButton", typeof(RectTransform));
        go.transform.SetParent(bubbleRect, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f); // 말풍선 아래 가장자리 중앙
        rect.pivot = new Vector2(0.5f, 1f);                      // 거기서 아래로 내려 단다
        rect.anchoredPosition = new Vector2(0f, -gap);
        rect.sizeDelta = new Vector2(fontSize * 5.6f, fontSize * 1.9f);

        // 말풍선 본문은 VerticalLayoutGroup이 세로로 쌓는다. 버튼은 그 흐름 밖에 있어야
        // 패널 안쪽에 한 줄로 끼어들지 않고 아래에 매달린다.
        go.AddComponent<LayoutElement>().ignoreLayout = true;

        var background = go.AddComponent<Image>();
        background.sprite = HoloSpriteFactory.Panel();
        background.type = Image.Type.Sliced;
        background.color = new Color(0.02f, 0.06f, 0.10f, 0.92f);

        Image stroke = CreateSkipLayer(rect, "Stroke", HoloSpriteFactory.Stroke(),
                                       new Color(1f, 1f, 1f, 0.7f));

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(rect, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;

        var text = textGo.AddComponent<Text>();
        text.font = label.font;
        // 본문보다 작게 둔다. 말풍선에서 가장 큰 글자가 굽는 해상도의 상한을 정하므로
        // (UpdateTextSharpness/LargestFontSize), 버튼이 그 상한을 밀어 올리면 본문이 흐려진다.
        text.fontSize = Mathf.Max(Mathf.RoundToInt(fontSize * 0.62f), 8);
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = new Color(0.86f, 0.95f, 1f);
        text.raycastTarget = false;
        text.text = skipButtonLabel;

        var button = go.AddComponent<Button>();
        button.targetGraphic = background;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        // 외곽선은 비서의 상태 색을 따라가게 해 말풍선 테두리와 같은 색으로 보이게 한다.
        AppendAccentGraphic(stroke);

        return button;
    }

    private Image CreateSkipLayer(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>상태 색을 따라갈 그래픽 목록에 하나 덧붙인다. 알파 캐시는 Awake에서 뒤이어 만들어진다.</summary>
    private void AppendAccentGraphic(Graphic graphic)
    {
        if (graphic == null) return;

        int length = accentGraphics != null ? accentGraphics.Length : 0;
        var grown = new Graphic[length + 1];
        for (int i = 0; i < length; i++) grown[i] = accentGraphics[i];
        grown[length] = graphic;
        accentGraphics = grown;
    }

    /// <summary>
    /// 지금 재생 중인 대사 하나를 즉시 끝내고 다음 대사로 넘어간다. 건너뛰기 버튼이 부른다.
    ///
    /// 큐를 비우지는 않는다 — 한 번에 여러 문장을 쌓아 두는 브리핑에서 큐째 버리면
    /// 버튼 한 번에 단계 설명이 통째로 사라진다. "이 말풍선 하나만" 넘긴다.
    /// </summary>
    public void SkipCurrent()
    {
        if (_paused || !_hasCurrent) return;

        _skipRequested = true;
        StopVoice(); // 소리가 남아 있으면 다음 대사와 겹쳐 들린다
    }

    /// <summary>
    /// 본문 글자 크기에 맞춰 말풍선 폭을 정한다.
    ///
    /// 폭을 씬에 고정 값으로 저장해 두면, 글자 크기나 UI 배율을 조금만 손대도
    /// 한 줄에 들어가는 글자 수가 통째로 달라진다. 실제로 씬에 저장돼 있던 폭 780에
    /// 글자 크기 65, 좌우 여백 114를 넣으면 한 줄에 한글 열 자밖에 들어가지 않아
    /// 두 문장짜리 대사가 일곱 줄로 쪼개졌다.
    ///
    /// 한글은 대부분 글자 하나가 글자 크기만큼의 폭을 차지하므로,
    /// "한 줄에 몇 자"를 정하면 폭은 거기서 계산된다.
    /// </summary>
    private void ApplyBubbleWidth()
    {
        if (screenWidthFraction <= 0f || bubbleRect == null || label == null) return;

        SetBubbleWidth(MaxBubbleWidth());

        if (lineSpacing > 0f) label.lineSpacing = lineSpacing;
    }

    /// <summary>좌우 여백. 레이아웃 그룹이 들고 있으므로 거기서 읽는다.</summary>
    private float HorizontalPadding()
    {
        var layout = bubbleRect != null ? bubbleRect.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
        return layout != null ? layout.padding.left + layout.padding.right : 0f;
    }

    /// <summary>말풍선이 커질 수 있는 한계. 줄바꿈은 이 폭을 기준으로 계산한다.</summary>
    private float MaxBubbleWidth() => ScreenWidthInCanvasUnits() * screenWidthFraction;

    /// <summary>
    /// 기준 거리(referenceDistance)에서 카메라 시야 폭을 캔버스 단위로 환산한 값.
    ///
    /// constantApparentSize가 켜져 있으면 캔버스 단위 하나가 화면에서 차지하는 비율은
    /// 카메라 거리와 무관하게 항상 같다(UpdateCanvasScale 참고). 그래서 이 값을 기준 거리
    /// 한 번만으로 구해도, 실제로 어느 거리에서 보든 "화면 전체 폭 = 몇 캔버스 단위"인지
    /// 정확히 맞는다. UpdateTextSharpness의 원근/직교 분기와 같은 계산을 재사용한다.
    /// </summary>
    private float ScreenWidthInCanvasUnits()
    {
        Camera cam = ResolveCamera();
        if (cam == null || metersPerCanvasUnit <= 0f) return 0f;

        float visibleWidthAtReference = cam.orthographic
            ? cam.orthographicSize * 2f * cam.aspect
            : 2f * referenceDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect;

        return visibleWidthAtReference / metersPerCanvasUnit;
    }

    /// <summary>말풍선이 줄어들 수 있는 하한. 헤더("AI CO-SCIENTIST")가 눌리지 않을 만큼은 남겨야 한다.</summary>
    private float MinBubbleWidth() => label.fontSize * minimumCharactersPerLine + HorizontalPadding();

    /// <summary>높이는 ContentSizeFitter가 글에 맞춰 정하므로 폭만 바꾼다.</summary>
    private void SetBubbleWidth(float width)
    {
        if (bubbleRect == null) return;
        bubbleRect.sizeDelta = new Vector2(width, bubbleRect.sizeDelta.y);
    }

    /// <summary>
    /// 이미 줄바꿈된 글에 맞춰 말풍선 폭을 좁힌다.
    ///
    /// 폭을 늘 최대치로 두면 짧은 대사에서 오른쪽이 텅 빈다. "좋아!" 한마디에도
    /// 스무 자짜리 패널이 뜨는 셈이다. 가장 긴 줄만큼만 남기면 대사 길이에 따라
    /// 패널이 자연스럽게 붙었다 늘어난다.
    ///
    /// 줄바꿈을 먼저 하고 폭을 나중에 정하는 순서가 중요하다. 반대로 하면 좁아진 폭에서
    /// 글이 다시 접혀 줄 수가 늘고, 그 줄에 맞춰 또 좁아지는 식으로 계속 쪼그라든다.
    /// </summary>
    private void FitBubbleWidthTo(string wrappedMessage)
    {
        if (bubbleRect == null || label == null || string.IsNullOrEmpty(wrappedMessage)) return;

        float widest = 0f;
        foreach (string line in wrappedMessage.Split('\n'))
            widest = Mathf.Max(widest, MeasureWidth(line));

        // 재는 값이 실제 렌더링보다 아주 조금 작게 나오는 경우가 있어 마지막 글자가 접힌다.
        // 한 글자 폭의 여유를 둔다.
        widest += label.fontSize * 0.25f;

        SetBubbleWidth(Mathf.Clamp(widest + HorizontalPadding(), MinBubbleWidth(), MaxBubbleWidth()));
    }

    /// <summary>본문이 실제로 쓸 수 있는 가로 폭(캔버스 단위). 좌우 여백을 뺀 값이다.</summary>
    private float AvailableTextWidth()
    {
        if (bubbleRect == null) return 0f;

        float horizontalPadding = 0f;
        var layout = bubbleRect.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (layout != null) horizontalPadding = layout.padding.left + layout.padding.right;

        return bubbleRect.rect.width - horizontalPadding;
    }

    /// <summary>
    /// 어절 단위로 줄을 끊어 <c>\n</c>을 직접 박아 넣는다.
    ///
    /// Unity의 기본 줄바꿈은 한글을 CJK로 보고 글자 사이 어디서나 끊는다. 중국어·일본어는
    /// 그게 맞지만 한국어는 띄어쓰기가 있어서 단어 한가운데가 갈라지면 눈에 띄게 어색하다.
    /// Unity 쪽 동작을 바꿀 수는 없으므로, 우리가 미리 재서 끊고 넘긴다.
    ///
    /// 한 어절이 통째로 한 줄보다 길면 그 어절만 Unity의 글자 단위 줄바꿈에 맡긴다 —
    /// 그 경우엔 어디서 끊든 단어가 갈라지므로 달리 방법이 없다.
    /// </summary>
    private string WrapAtWordBoundaries(string message)
    {
        if (!wrapAtWordBoundaries || label == null || string.IsNullOrEmpty(message)) return message;

        float available = AvailableTextWidth();
        if (available <= 0f) return message;

        var result = new System.Text.StringBuilder(message.Length + 16);
        var line = new System.Text.StringBuilder(64);

        foreach (string token in message.Split(' '))
        {
            if (token.Length == 0) continue;

            if (line.Length == 0)
            {
                line.Append(token);
                continue;
            }

            // 이 어절을 이어 붙이면 줄을 넘는지 재본다.
            string candidate = line + " " + token;
            if (MeasureWidth(candidate) <= available)
            {
                line.Clear();
                line.Append(candidate);
                continue;
            }

            if (result.Length > 0) result.Append('\n');
            result.Append(line);
            line.Clear();
            line.Append(token);
        }

        if (line.Length > 0)
        {
            if (result.Length > 0) result.Append('\n');
            result.Append(line);
        }

        return result.ToString();
    }

    /// <summary>문자열을 현재 폰트·글자 크기로 그렸을 때의 가로 폭(캔버스 단위).</summary>
    private float MeasureWidth(string text)
    {
        TextGenerationSettings settings = label.GetGenerationSettings(Vector2.zero);

        // 폭을 재는 것이 목적이므로 줄바꿈을 끄고 한 줄로 편다.
        // 켜둔 채로 재면 이미 접힌 폭이 나와 항상 한도 안으로 들어온다.
        settings.horizontalOverflow = HorizontalWrapMode.Overflow;
        settings.verticalOverflow = VerticalWrapMode.Overflow;

        return label.cachedTextGeneratorForLayout.GetPreferredWidth(text, settings) / label.pixelsPerUnit;
    }

    // 말풍선 하나로 충분 — 여러 비서 인스턴스가 생겨도 같은 셰이더 머티리얼을 공유한다
    // (Image/Text는 실제 텍스처를 CanvasRenderer가 그래픽별로 따로 바인딩하므로 안전하다).
    private static Material _alwaysOnTopMaterial;

    /// <summary>
    /// 말풍선 캔버스는 World Space라 기본적으로 씬의 3D 배경과 똑같이 뎁스 테스트를 받는다.
    /// 배경(실험실 모델의 벽/선반 등)이 카메라와 말풍선 사이에 있으면 그대로 잘려 보이는데,
    /// 배경이 어떻게 배치되든 항상 위에 그려지도록 ZTest Always 셰이더로 덮어씌운다.
    /// </summary>
    private void ApplyAlwaysOnTopMaterial()
    {
        if (_alwaysOnTopMaterial == null)
        {
            Shader shader = Shader.Find("Custom/UI_AlwaysOnTop");
            if (shader == null)
            {
                Debug.LogWarning("[AIAssistantSpeechBubble] 'Custom/UI_AlwaysOnTop' 셰이더를 찾지 못해 " +
                                 "말풍선이 기본 뎁스 테스트를 따릅니다(배경에 가려질 수 있음).");
                return;
            }
            _alwaysOnTopMaterial = new Material(shader) { name = "UI_AlwaysOnTop (AIAssistantSpeechBubble)" };
        }

        foreach (var graphic in GetComponentsInChildren<Graphic>(includeInactive: true))
            graphic.material = _alwaysOnTopMaterial;
    }

    /// <summary>
    /// 강조색을 상태 색으로 덮어쓸 때 각 요소가 원래 갖고 있던 투명도는 살려야
    /// 외곽선/글로우/구분선의 강약 차이가 유지된다.
    ///
    /// 알파만 다시 읽어 담으므로 여러 번 불러도 결과가 같다.
    /// 색을 입힐 때 알파를 그대로 되써 넣기 때문에 중간에 다시 캐시해도 값이 흔들리지 않는다.
    /// </summary>
    private void CacheAccentAlphas()
    {
        if (accentGraphics == null)
        {
            _accentAlphas = null;
            return;
        }

        _accentAlphas = new float[accentGraphics.Length];
        for (int i = 0; i < accentGraphics.Length; i++)
            _accentAlphas[i] = accentGraphics[i] != null ? accentGraphics[i].color.a : 1f;
    }

    private void Update()
    {
        if (!tintAccentWithState || visual == null || accentGraphics == null) return;

        // 플레이 중 스크립트를 고치면 도메인이 리로드되면서 직렬화되지 않는 이 캐시만
        // 날아가고 Awake는 다시 불리지 않는다. 인스펙터에서 배열 길이를 바꿔도 마찬가지다.
        // 길이가 어긋나면 그 자리에서 다시 만든다.
        if (_accentAlphas == null || _accentAlphas.Length != accentGraphics.Length)
            CacheAccentAlphas();

        Color state = visual.CurrentColor;
        for (int i = 0; i < accentGraphics.Length; i++)
        {
            if (accentGraphics[i] == null) continue;
            accentGraphics[i].color = new Color(state.r, state.g, state.b, _accentAlphas[i]);
        }
    }

    private void LateUpdate()
    {
        UpdateCanvasScale();
        UpdateTextSharpness();

        // 런타임 생성 폰트는 도메인 리로드를 넘기지 못하고 null이 된다 (글자가 통째로 사라짐).
        if (useOsFontForKorean && label != null && label.font == null) ApplyOsFont();

        if (lookTarget == null) return;

        // 캔버스의 +Z가 카메라 반대쪽을 보게 하면 UI 면이 카메라를 향한다.
        Vector3 away = transform.position - lookTarget.position;
        if (away.sqrMagnitude < 1e-6f) return;
        transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    /// <summary>
    /// 카메라에서 본 말풍선의 크기를 결정한다.
    ///
    /// 두 가지를 동시에 해야 한다.
    /// 1) 부모(비서)를 확대해도 말풍선이 같이 커지지 않도록 부모 스케일을 나눠 없앤다.
    /// 2) <see cref="constantApparentSize"/>가 켜져 있으면 카메라 거리에 비례해 키운다.
    ///
    /// 2번이 핵심이다. 월드 크기를 고정해두면 카메라가 다가올수록 글자가 화면에서 커지는데,
    /// 굽는 해상도는 그대로라 확대 보간이 걸려 흐려진다. 거리에 비례해 키우면 화면에서
    /// 차지하는 픽셀 수가 상수가 되고, 그러면 굽는 해상도도 상수로 둘 수 있어
    /// 어느 거리에서든 같은 선명도가 나온다.
    /// </summary>
    private void UpdateCanvasScale()
    {
        if (metersPerCanvasUnit <= 0f) return;

        float meters = metersPerCanvasUnit;
        if (constantApparentSize && referenceDistance > 1e-3f)
            meters *= CameraDistance() / referenceDistance;

        Transform parent = transform.parent;
        float parentScale = parent != null ? parent.lossyScale.x : 1f;
        if (parentScale <= 1e-6f) return;

        float desired = meters / parentScale;
        if (!Mathf.Approximately(transform.localScale.x, desired))
            transform.localScale = Vector3.one * desired;
    }

    /// <summary>
    /// 글자를 굽는 해상도를 화면 밀도에 맞춘다.
    ///
    /// dynamicPixelsPerUnit은 "캔버스 1unit당 굽는 픽셀 수"이고, 캔버스 1unit은 월드에서
    /// lossyScale m다. 따라서 카메라 거리에서의 화면 밀도(px/m)에 lossyScale을 곱하면
    /// "화면 픽셀과 1:1로 맞는" 값이 나온다. 여기에 supersample만큼 여유를 둔다.
    ///
    /// 예전에는 월드 스케일만 보고 정했는데, <see cref="UpdateCanvasScale"/>이 월드 스케일을
    /// 고정해버리니 결과가 항상 같은 상수였다. 화면 해상도를 정하는 건 월드 크기가 아니라
    /// 카메라 거리라서, 그 거리가 식에 없으면 특정 거리에서만 맞는다.
    /// </summary>
    private void UpdateTextSharpness()
    {
        if (canvasScaler == null) return;

        Camera cam = ResolveCamera();
        if (cam == null) return;

        float worldScale = transform.lossyScale.x;
        if (worldScale <= 0f) return;

        float distance = CameraDistance();
        float visibleHeight = cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        if (visibleHeight <= 1e-4f) return;

        float desired = cam.pixelHeight / visibleHeight * worldScale * Mathf.Max(supersample, 0.1f);

        // 굽는 크기 = fontSize x dynamicPixelsPerUnit.
        // 가장 큰 글자가 상한을 넘으면 동적 폰트 아틀라스가 넘치고, 아틀라스가 재구성되는 순간
        // 이미 그려둔 글리프의 UV가 어긋나 글자가 통째로 깨진다.
        int largest = LargestFontSize();
        if (largest > 0 && maxBakedFontPixels > 0f)
            desired = Mathf.Min(desired, maxBakedFontPixels / largest);

        desired = Mathf.Max(desired, 0.05f);

        float current = canvasScaler.dynamicPixelsPerUnit;
        if (current > 0.05f && Mathf.Abs(Mathf.Log(desired / current)) < RebakeThreshold) return;

        canvasScaler.dynamicPixelsPerUnit = desired;
    }

    /// <summary>
    /// 보정에 쓸 카메라까지의 거리. 하한/상한을 물려 크기가 폭주하지 않게 한다.
    ///
    /// 직선(유클리드) 거리가 아니라 카메라 정면 축 방향으로의 깊이를 쓴다. 원근 투영에서
    /// 화면상 크기를 결정하는 건 깊이뿐이라, 화면 구석(비서는 기본적으로 우측 상단에 뜬다 —
    /// AIAssistantFollower 참고)에 있는 물체는 직선 거리가 깊이보다 길어 "실제보다 멀리 있다"고
    /// 착각하게 된다. 그러면 이 배율을 쓰는 UpdateCanvasScale과 ScreenWidthInCanvasUnits가
    /// 필요 이상으로 크게 키워, 화면 비율 목표(screenWidthFraction)보다 커 보인다.
    /// </summary>
    private float CameraDistance()
    {
        Camera cam = ResolveCamera();
        if (cam == null) return referenceDistance;

        float depth = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);
        return Mathf.Clamp(depth, distanceClamp.x, distanceClamp.y);
    }

    /// <summary>빌보드에 쓰는 lookTarget이 곧 카메라인 경우가 대부분이라 거기서 먼저 찾는다.</summary>
    private Camera ResolveCamera()
    {
        if (_camera != null) return _camera;

        if (lookTarget != null) _camera = lookTarget.GetComponent<Camera>();
        if (_camera == null) _camera = Camera.main;
        return _camera;
    }

    /// <summary>
    /// 말풍선 안에서 가장 큰 글자 크기(캔버스 단위). 굽는 상한을 계산하는 데 쓴다.
    /// 폰트 크기는 실행 중 바뀌지 않으므로 한 번만 훑고 캐시한다.
    /// (직렬화되지 않아 도메인 리로드 후에는 0이 되고, 그때 자연히 다시 훑는다.)
    /// </summary>
    private int LargestFontSize()
    {
        if (_largestFontSize > 0) return _largestFontSize;

        foreach (var text in GetComponentsInChildren<Text>(includeInactive: true))
            if (text.fontSize > _largestFontSize) _largestFontSize = text.fontSize;

        return _largestFontSize;
    }

    // --- 외부 API ---

    /// <summary>
    /// 메시지를 큐에 넣는다. 이미 말하는 중이면 끝난 뒤에 이어서 재생된다.
    ///
    /// <paramref name="onShown"/>은 이 메시지가 화면에 뜨기 시작하는 순간 한 번 불린다.
    /// 시나리오 연출처럼 "이 대사에 맞춰 이 행동"을 붙일 때 쓴다 — 대사와 행동이 같은 큐를 타므로
    /// 별도 타이머로 맞추다 어긋나는 일이 없다. 큐가 비워지면(SayNow/Hide) 그 행동도 함께 취소된다.
    /// </summary>
    public void Say(string message, Action onShown = null)
    {
        if (string.IsNullOrEmpty(message)) return;

        // 긴 메시지는 문장 단위로 쪼개 여러 말풍선에 나눠 띄운다. 행동은 첫 조각에만 붙인다 —
        // 조각마다 실행하면 변이 부위가 세 번 반짝이는 식으로 연출이 겹친다.
        bool first = true;
        foreach (string part in SplitIntoBubbles(message))
        {
            _pending.Enqueue(new SpeechCue(part, onShown));
            onShown = null;

            // 첫 조각은 어차피 바로 합성한다. 뒤 조각들만 미리 받아두면
            // 조각 사이에 합성을 기다리는 침묵이 사라져 한 호흡으로 이어진다.
            if (!first && speakAloud && tts != null && tts.IsConfigured) tts.Prewarm(part);
            first = false;
        }

        if (_runner == null) _runner = StartCoroutine(RunQueue());
    }

    /// <summary>
    /// 한 말풍선에 담기엔 긴 글을 문장 단위로 나눈다.
    ///
    /// LLM 응답은 "2~3문장으로 짧게"라고 일러둬도 종종 그보다 길게 온다. 그걸 한 말풍선에
    /// 밀어 넣으면 줄이 빽빽하게 쌓여 읽기 어렵고, 유지 시간(maxHoldSeconds)에 걸려
    /// 다 읽기도 전에 사라진다. 문장 경계에서 끊어 여러 개로 띄우면 둘 다 해결된다.
    ///
    /// 문장 부호가 없는 긴 글(예: 목록형 응답)은 나눌 자리가 없으므로 그대로 둔다.
    /// 억지로 글자 수로 자르면 단어 한가운데가 끊긴다.
    /// </summary>
    private IEnumerable<string> SplitIntoBubbles(string message)
    {
        // 줄바꿈은 말풍선 안에서 빈 줄로 보이므로 공백으로 눕힌다.
        message = CollapseWhitespace(message);

        if (maxCharactersPerBubble <= 0 || message.Length <= maxCharactersPerBubble)
        {
            yield return message;
            yield break;
        }

        var current = new System.Text.StringBuilder();
        int start = 0;

        for (int i = 0; i < message.Length; i++)
        {
            if (!IsSentenceEnd(message, i)) continue;

            // 문장 부호 뒤 공백까지 포함해 한 문장을 떼어낸다.
            int end = i + 1;
            while (end < message.Length && message[end] == ' ') end++;

            string sentence = message.Substring(start, end - start);
            start = end;

            // 이어 붙이면 한도를 넘는다면, 지금까지 모은 것을 먼저 내보낸다.
            if (current.Length > 0 && current.Length + sentence.Length > maxCharactersPerBubble)
            {
                yield return current.ToString().TrimEnd();
                current.Clear();
            }

            current.Append(sentence);
        }

        if (start < message.Length) current.Append(message.Substring(start));
        if (current.Length > 0) yield return current.ToString().TrimEnd();
    }

    /// <summary>문장이 끝나는 자리인지. 소수점(3.5)이나 줄임표(...) 한가운데서 끊지 않는다.</summary>
    private static bool IsSentenceEnd(string message, int i)
    {
        char c = message[i];
        if (c != '.' && c != '!' && c != '?') return false;

        // 뒤에 글자가 붙어 있으면 문장 끝이 아니다 — "3.5", "kcal/mol." 뒤 공백 없는 경우 등.
        if (i + 1 < message.Length && message[i + 1] != ' ') return false;

        // 소수점: 앞뒤가 숫자면 문장 부호가 아니다.
        if (c == '.' && i > 0 && char.IsDigit(message[i - 1])
            && i + 2 < message.Length && char.IsDigit(message[i + 2])) return false;

        return true;
    }

    /// <summary>줄바꿈과 연속 공백을 공백 하나로 눕힌다.</summary>
    private static string CollapseWhitespace(string message)
    {
        var builder = new System.Text.StringBuilder(message.Length);
        bool lastWasSpace = false;

        foreach (char c in message)
        {
            bool isSpace = char.IsWhiteSpace(c);
            if (isSpace && lastWasSpace) continue;

            builder.Append(isSpace ? ' ' : c);
            lastWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }

    /// <summary>대기 중인 메시지를 버리고 즉시 이 메시지로 교체한다. (예: 오류 안내)</summary>
    public void SayNow(string message, Action onShown = null)
    {
        StopVoice();
        _skipRequested = false;
        _pending.Clear();
        if (_runner != null) StopCoroutine(_runner);
        _runner = null;
        Say(message, onShown);
    }

    public void Hide()
    {
        StopVoice();
        _skipRequested = false;
        _pending.Clear();
        _hasCurrent = false;
        if (_runner != null) StopCoroutine(_runner);
        _runner = null;
        StartCoroutine(HideRoutine());
    }

    /// <summary>
    /// 재생을 잠시 멈추고 말풍선을 접는다. 큐는 그대로 두고, 읽던 대사는 처음부터 다시 튼다.
    ///
    /// 카메라가 레벨 사이를 건너는 동안 비서는 화면에서 숨겨진다. 그때 <see cref="Hide"/>로
    /// 큐를 비워버리면 단계 브리핑이 통째로 사라지고, 그냥 두면 보이지 않는 채로 재생돼
    /// 도착했을 땐 이미 끝나 있다. 어느 쪽이든 "설명과 화면이 따로 논다".
    /// 멈춰 두었다가 도착한 뒤 이어서 말하면 둘이 맞물린다.
    /// </summary>
    public void Pause()
    {
        if (_paused) return;

        _paused = true;
        StopVoice();
        StartCoroutine(HideRoutine());
    }

    /// <summary>멈춰 둔 재생을 다시 시작한다.</summary>
    public void Resume()
    {
        if (!_paused) return;

        _paused = false;

        // 멈춰 있는 동안 새 대사가 들어왔다면 재생기가 꺼져 있을 수 있다.
        if (_runner == null && (_hasCurrent || _pending.Count > 0))
            _runner = StartCoroutine(RunQueue());
    }

    // --- 재생 ---

    private IEnumerator RunQueue()
    {
        while (_hasCurrent || _pending.Count > 0)
        {
            while (_paused) yield return null;

            if (!_hasCurrent)
            {
                _current = _pending.Dequeue();
                _hasCurrent = true;
                _currentActionFired = false;
                _skipRequested = false; // 건너뛰기는 대사 하나에만 적용된다
            }

            yield return ShowRoutine(_current);

            // 일시정지로 중간에 끊겼으면 같은 대사를 그대로 들고 있다가 다시 튼다.
            if (_paused) continue;

            _hasCurrent = false;
            _skipRequested = false;
        }

        yield return HideRoutine();
        _runner = null;
    }

    /// <summary>
    /// 말풍선 높이를 본문에 맞춘다. 반드시 두 번 돌려야 한다.
    ///
    /// 줄바꿈되는 <see cref="Text"/>의 preferredHeight는 "지금 잡혀 있는 폭"을 기준으로 계산된다.
    /// 그런데 폭 자체도 같은 레이아웃 패스에서 정해지므로, 한 번만 돌리면
    /// <b>이전 폭으로 잰 높이</b>가 남는다. 한 줄이 더 필요한데 그만큼 높이가 모자라
    /// 마지막 줄이 패널 밖으로 삐져나오거나 테두리에 닿아 보인다.
    ///
    /// 첫 패스에서 폭이 확정되고, 두 번째 패스가 그 폭으로 높이를 다시 잰다.
    /// 메시지를 띄울 때만 부르므로 매 프레임 비용이 되지는 않는다.
    /// </summary>
    private void RebuildBubbleLayout()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRect);
    }

    private IEnumerator ShowRoutine(SpeechCue cue)
    {
        string message = cue.Message;
        if (bubbleRect == null || label == null) yield break;

        bubbleRect.gameObject.SetActive(true);

        // 폭을 최대치로 되돌린 뒤 그 폭 기준으로 줄을 끊고, 끊긴 결과에 맞춰 다시 좁힌다.
        // 앞 메시지에서 좁아진 폭이 남아 있으면 이번 글이 그 폭에 맞춰 접혀 줄이 늘어난다.
        SetBubbleWidth(MaxBubbleWidth());

        // 어절 단위로 미리 끊는다. 이 뒤로는 줄바꿈이 박힌 이 문자열이 기준이 되어야 한다 —
        // 타이핑도 유지 시간도 실제로 화면에 뜨는 글을 따라가야 어긋나지 않는다.
        message = WrapAtWordBoundaries(message);

        // 가장 긴 줄만큼만 남겨 오른쪽 빈 공간을 없앤다.
        FitBubbleWidthTo(message);

        // 최종 문구로 레이아웃을 먼저 확정해 두면 타이핑 중에 말풍선 높이가 요동치지 않는다.
        label.supportRichText = true;
        label.text = message;
        RebuildBubbleLayout();

        if (driveVisualState && visual != null) visual.SetState(AIAssistantState.Speaking);

        // 상태를 Speaking으로 돌린 다음에 부른다. 시나리오 비트가 지정한 표정(Alert 등)이
        // 있다면 그쪽이 이겨야 하는데, 순서가 반대면 매번 Speaking에 덮여 사라진다.
        InvokeCue(cue);

        yield return Reveal(true);

        // 소리는 줄바꿈을 넣기 전의 원문으로 읽는다. 줄바꿈이 섞인 글을 그대로 넘기면
        // 합성기가 그 자리를 문장 끝으로 보고 어색하게 끊어 읽는다.
        bool voiced = BeginVoice(cue.Message);

        if (useTypewriter && charactersPerSecond > 0f)
        {
            yield return TypeRoutine(message);
        }
        label.text = message;

        if (voiced)
        {
            // 소리가 끝날 때까지 띄워둔다. 글자 수로 계산한 시간과 실제 낭독 길이는
            // 맞을 이유가 없어서, 그대로 두면 다 읽기 전에 말풍선이 사라지거나
            // 소리가 끝난 뒤에도 한참 남는다.
            float deadline = Time.time + Mathf.Max(maxVoiceSeconds, 1f);
            while (!_voiceDone && Time.time < deadline)
            {
                if (_paused) yield break;
                if (_skipRequested) break;
                yield return null;
            }

            yield return HoldFor(voicedTailSeconds);
            yield break;
        }

        float hold = Mathf.Min(baseHoldSeconds + message.Length * secondsPerCharacter, maxHoldSeconds);
        yield return HoldFor(hold);
    }

    /// <summary>
    /// 일시정지를 살피며 기다린다.
    ///
    /// WaitForSeconds로 통째로 기다리면 그 사이에 멈춰도 반응하지 못하고,
    /// 카메라가 다 이동한 뒤에야 뒤늦게 다음 대사로 넘어간다.
    /// </summary>
    private IEnumerator HoldFor(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            if (_paused || _skipRequested) yield break;

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>소리 내어 읽기를 시작한다. 읽을 수 없으면 false를 돌려 글자 수 기준으로 돌아간다.</summary>
    private bool BeginVoice(string spoken)
    {
        _voiceDone = false;

        if (!speakAloud || tts == null || !tts.IsConfigured || string.IsNullOrWhiteSpace(spoken))
            return false;

        tts.Speak(spoken, () => _voiceDone = true);
        return true;
    }

    private void StopVoice()
    {
        if (tts != null) tts.Stop();
        _voiceDone = true;
    }

    private IEnumerator TypeRoutine(string message)
    {
        int visible = 0;
        float carry = 0f;

        while (visible < message.Length)
        {
            if (_paused) yield break;
            // 건너뛰면 타이핑을 끝까지 돌리지 않는다. 호출한 쪽이 곧바로 전문을 한 번에 넣는다.
            if (_skipRequested) yield break;

            carry += Time.deltaTime * charactersPerSecond;
            while (carry >= 1f && visible < message.Length)
            {
                visible = NextVisibleIndex(message, visible);
                carry -= 1f;
            }

            label.text = BuildPartial(message, visible);
            yield return null;
        }
    }

    /// <summary>
    /// 아직 안 나온 부분을 지우는 대신 투명색으로 감싼다.
    /// 문자열 길이가 그대로라 줄바꿈 위치와 말풍선 높이가 타이핑 내내 고정된다.
    /// </summary>
    private static string BuildPartial(string message, int visible)
    {
        if (visible >= message.Length) return message;
        return message.Substring(0, visible) + "<color=#00000000>" + message.Substring(visible) + "</color>";
    }

    /// <summary>대사에 붙은 행동을 실행한다. 예외가 나도 대사 재생은 계속돼야 한다.</summary>
    private void InvokeCue(SpeechCue cue)
    {
        if (cue.OnShown == null || _currentActionFired) return;

        _currentActionFired = true;

        try
        {
            cue.OnShown();
        }
        catch (Exception e)
        {
            // 여기서 예외가 새어 나가면 RunQueue 코루틴이 통째로 죽어 비서가 영영 입을 다문다.
            Debug.LogException(e, this);
        }
    }

    /// <summary>다음 글자 위치. 리치 텍스트 태그는 통째로 건너뛰어 태그가 반쯤 잘려 보이지 않게 한다.</summary>
    private static int NextVisibleIndex(string message, int index)
    {
        while (index < message.Length && message[index] == '<')
        {
            int close = message.IndexOf('>', index);
            if (close < 0) break;
            index = close + 1;
        }
        return index < message.Length ? index + 1 : message.Length;
    }

    private IEnumerator HideRoutine()
    {
        yield return Reveal(false);

        if (bubbleRect != null) bubbleRect.gameObject.SetActive(false);

        // 도중에 Alert 같은 다른 상태로 바뀌었다면 덮어쓰지 않는다.
        if (driveVisualState && visual != null && visual.CurrentState == AIAssistantState.Speaking)
            visual.SetState(AIAssistantState.Idle);
    }

    /// <summary>알파와 스케일을 함께 움직여 "툭 튀어나오고 스르륵 접히는" 등장/퇴장을 만든다.</summary>
    private IEnumerator Reveal(bool show)
    {
        float alphaFrom = canvasGroup != null ? canvasGroup.alpha : 1f;
        float alphaTo = show ? 1f : 0f;
        float scaleFrom = show ? popFromScale : 1f;
        float scaleTo = show ? 1f : collapseToScale;

        if (fadeDuration <= 0f)
        {
            ApplyReveal(alphaTo, scaleTo);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / fadeDuration);

            // 알파는 선형, 스케일만 살짝 오버슈트시켜야 글자가 깜빡이지 않으면서 탄력이 생긴다.
            float alpha = Mathf.Lerp(alphaFrom, alphaTo, p);
            float scale = show
                ? Mathf.LerpUnclamped(scaleFrom, scaleTo, EaseOutBack(p))
                : Mathf.Lerp(scaleFrom, scaleTo, p);

            ApplyReveal(alpha, scale);
            yield return null;
        }

        ApplyReveal(alphaTo, scaleTo);
    }

    private void ApplyReveal(float alpha, float scale)
    {
        if (canvasGroup != null) canvasGroup.alpha = alpha;
        if (bubbleRect != null) bubbleRect.localScale = Vector3.one * scale;

        // 접히는 중(거의 안 보이는 상태)의 건너뛰기 버튼은 눌리면 안 된다 —
        // 보이지도 않는 버튼이 그 자리의 클릭을 삼키면 고장으로 보인다.
        if (skipButton != null && canvasGroup != null) canvasGroup.blocksRaycasts = alpha > 0.5f;
    }

    private static float EaseOutBack(float t)
    {
        const float overshoot = 1.4f;
        float p = t - 1f;
        return 1f + (overshoot + 1f) * p * p * p + overshoot * p * p;
    }
}

/// <summary>큐에 쌓이는 대사 한 줄과, 그 줄이 뜨는 순간 함께 일어날 행동.</summary>
public struct SpeechCue
{
    public readonly string Message;
    public readonly Action OnShown;

    public SpeechCue(string message, Action onShown)
    {
        Message = message;
        OnShown = onShown;
    }
}
