using UnityEngine;
using UnityEngine.UI;

/// <summary>질문을 기억할 부담을 덜고, 음성 오인식을 확인한 뒤 답변을 보내는 작은 연구 노트.</summary>
public sealed class OralCheckPanel : MonoBehaviour
{
    private AIAssistantBrain _brain;
    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _panel;
    private Text _case, _step, _question, _feedback, _evidence, _hint, _sendLabel, _skipLabel;
    private InputField _input;
    private Button _send, _retry, _skip;
    private int _revision = -1;
    private int _run = -1;
    private float _completedAt = -1f;
    private CameraTransitionDirector _director;

    public static OralCheckPanel Ensure(AIAssistantBrain brain)
    {
        foreach (var panel in FindObjectsByType<OralCheckPanel>(FindObjectsSortMode.None))
            if (panel._brain == brain) return panel;
        var go = new GameObject("OralCheckPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                                typeof(GraphicRaycaster));
        var created = go.AddComponent<OralCheckPanel>();
        created._brain = brain;
        created.Build();
        return created;
    }

    private static readonly Color Ink = new Color(0.86f, 0.95f, 0.98f);
    private static readonly Color Muted = new Color(0.57f, 0.72f, 0.78f);
    private static readonly Color Accent = new Color(0.27f, 0.91f, 0.77f);

    private void Build()
    {
        _director = FindFirstObjectByType<CameraTransitionDirector>();
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 180;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _group = gameObject.AddComponent<CanvasGroup>();

        _panel = Rect("Research note", transform, 24, 120, 460, 760);
        var background = _panel.gameObject.AddComponent<Image>();
        background.sprite = HoloSpriteFactory.Panel();
        background.type = Image.Type.Sliced;
        background.color = new Color(0.025f, 0.055f, 0.075f, 0.98f);
        var shadow = _panel.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
        shadow.effectDistance = new Vector2(0, -6);

        Label("RESEARCH NOTE  /  연구 노트", 28, 24, 404, 24, 15, Accent);
        _case = Label("", 28, 56, 404, 30, 22, Ink);
        _step = Label("", 28, 98, 404, 25, 15, Accent);
        var rule = Rect("Rule", _panel, 28, 139, 404, 2).gameObject.AddComponent<Image>();
        rule.color = new Color(0.2f, 0.47f, 0.48f, 0.6f);
        _question = Label("", 28, 156, 404, 120, 22, Ink);
        _feedback = Label("", 28, 286, 404, 108, 17, Muted);
        _feedback.resizeTextForBestFit = true;
        _feedback.resizeTextMinSize = 14;
        _feedback.resizeTextMaxSize = 17;
        _question.resizeTextForBestFit = true;
        _question.resizeTextMinSize = 18;
        _question.resizeTextMaxSize = 22;
        _evidence = Label("", 28, 405, 404, 70, 14, Accent);

        var inputRect = Rect("Answer", _panel, 28, 486, 404, 142);
        var inputBg = inputRect.gameObject.AddComponent<Image>();
        inputBg.color = new Color(0.065f, 0.11f, 0.14f);
        _input = inputRect.gameObject.AddComponent<InputField>();
        _input.targetGraphic = inputBg;
        _input.lineType = InputField.LineType.MultiLineNewline;
        _input.characterLimit = OralAnswerDraft.MaxLength;
        var text = TextAt("Answer text", inputRect, 14, 10, 376, 122, 18, Ink);
        var placeholder = TextAt("Placeholder", inputRect, 14, 10, 376, 122, 18, Muted);
        placeholder.text = "마이크로 말하거나 여기에 적어 주세요.\n예: 자리가 이렇게 바뀌어서, 이 약은…";
        _input.textComponent = text;
        _input.placeholder = placeholder;
        _input.onValueChanged.AddListener(value => {
            if (_brain != null && _brain.IsAwaitingOralAnswer) _brain.EditOralAnswer(value);
        });

        _hint = Label("", 28, 639, 404, 24, 14, Muted);
        _send = ButtonAt("이 설명 보내기", 28, 675, 250, Accent, out _sendLabel);
        _send.onClick.AddListener(() => { if (_brain != null) _brain.SubmitOralAnswer(_input.text); });
        _retry = ButtonAt("다시 입력", 290, 675, 142, new Color(0.13f, 0.22f, 0.27f), out _);
        _retry.onClick.AddListener(() => { if (_brain != null) _brain.RetryOralRecording(); });
        _skip = ButtonAt("지금은 넘어가기", 130, 721, 200, new Color(0, 0, 0, 0), out _skipLabel);
        _skipLabel.color = Muted;
        _skip.onClick.AddListener(() => {
            if (_brain == null) return;
            if (_brain.OralPhase == OralCheckPhase.Completed) _brain.DismissOralResult();
            else _brain.SkipOralCheck();
        });
        _input.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    private void LateUpdate()
    {
        if (_brain == null) { Destroy(gameObject); return; }
        if (_run != _brain.OralRunId)
        {
            _run = _brain.OralRunId;
            _revision = -1;
            _completedAt = -1f;
        }
        var phase = _brain.OralPhase;
        bool visible = phase != OralCheckPhase.Hidden && phase != OralCheckPhase.Explaining &&
            (_director == null || !_director.IsTransitioning);
        _group.alpha = visible ? 1f : 0f;
        _group.interactable = visible;
        _group.blocksRaycasts = visible;
        if (!visible) return;

        // XR에서는 화면 오버레이 대신 카메라 앞 공간에 같은 노트를 배치한다.
        bool xr = UnityEngine.XR.XRSettings.enabled;
        _canvas.renderMode = xr ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
        if (xr && Camera.main != null)
        {
            var camera = Camera.main;
            _canvas.worldCamera = camera;
            var root = (RectTransform)transform;
            root.sizeDelta = new Vector2(1920, 1080);
            root.position = camera.transform.TransformPoint(new Vector3(-0.38f, 0f, 1.2f));
            root.rotation = camera.transform.rotation;
            root.localScale = Vector3.one * 0.00065f;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.anchoredPosition = Vector2.zero;
        }

        if (!xr)
        {
            transform.localScale = Vector3.one;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0, 1);
            _panel.anchoredPosition = new Vector2(24, -120);
        }

        _case.text = _brain.OralQuestTitle;
        _question.text = _brain.OralQuestion;
        _feedback.text = _brain.OralFeedback;
        _evidence.text = string.IsNullOrWhiteSpace(_brain.OralEvidence) ? "" :
            "내 설명에서 찾은 근거\n“" + _brain.OralEvidence + "”";
        _step.text = phase == OralCheckPhase.Reviewing ? "02  함께 다시 보기" :
            phase == OralCheckPhase.Completed ? "03  설명 정리" :
            phase == OralCheckPhase.Grading ? "02  이유 확인 중" :
            _brain.OralAttempt > 1 ? "02  내 설명 보완하기" : "01  내 말로 설명하기";

        bool answering = _brain.IsAwaitingOralAnswer;
        if (_revision != _brain.OralDraftRevision)
        {
            _input.SetTextWithoutNotify(answering ? _brain.OralDraftText : _brain.OralSubmittedAnswer);
            _revision = _brain.OralDraftRevision;
        }
        _input.interactable = answering && !_brain.OralIsRecording && !_brain.OralIsTranscribing;
        _send.gameObject.SetActive(answering);
        _retry.gameObject.SetActive(answering);
        _send.interactable = answering && !string.IsNullOrWhiteSpace(_input.text) &&
            !_brain.OralIsRecording && !_brain.OralIsTranscribing;
        _retry.interactable = answering && !_brain.OralIsTranscribing;
        _skipLabel.text = phase == OralCheckPhase.Completed ? "노트 닫기" : "지금은 넘어가기";
        _skip.interactable = phase != OralCheckPhase.Completed || !_brain.IsOralCheckActive;
        _hint.text = _brain.OralIsRecording ? "듣고 있어요 · 기존 마이크 버튼을 다시 누르면 확인" :
            _brain.OralIsTranscribing ? "말을 글로 옮기고 있어요." :
            answering ? (_brain.OralSecondsRemaining < 15f
                ? "잠시 후 넘어가요 · 입력하면 확인 시간이 늘어나요."
                : "내용을 확인한 뒤 보내세요 · " + _input.text.Length + " / 1000") :
            phase == OralCheckPhase.Grading ? "확인이 늦어져도 언제든 넘어갈 수 있어요." : OutcomeLabel(_brain.OralOutcome);
        if (phase == OralCheckPhase.Completed && !_brain.IsOralCheckActive)
        {
            if (_completedAt < 0f) _completedAt = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _completedAt > 20f) _brain.DismissOralResult();
        }
    }

    private static string OutcomeLabel(OralCheckOutcome outcome)
    {
        switch (outcome)
        {
            case OralCheckOutcome.Understood: return "내 설명으로 이해를 확인했어요.";
            case OralCheckOutcome.Improved: return "복습한 내용을 연결해서 설명했어요.";
            case OralCheckOutcome.Explained: return "함께 핵심을 정리했어요.";
            case OralCheckOutcome.Unavailable: return "이번 답변은 확인하지 못했어요 · 진행은 계속할 수 있어요.";
            case OralCheckOutcome.Skipped: return "이번 설명은 건너뛰었어요.";
            default: return "정답을 외우기보다, 이유를 연결해 보세요.";
        }
    }

    private Button ButtonAt(string label, float x, float y, float width, Color color, out Text text)
    {
        var rect = Rect(label, _panel, x, y, width, 36);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = HoloSpriteFactory.Panel();
        image.type = Image.Type.Sliced;
        image.color = color;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        text = TextAt("Label", rect, 0, 0, width, 36, 17, color == Accent ? new Color(0.02f, 0.10f, 0.12f) : Ink);
        text.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private Text Label(string value, float x, float y, float w, float h, int size, Color color)
    {
        var label = TextAt("Label", _panel, x, y, w, h, size, color);
        label.text = value;
        return label;
    }

    private static Text TextAt(string name, Transform parent, float x, float y, float w, float h, int size, Color color)
    {
        var text = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Text>();
        text.font = HoloFont.Resolve() ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.color = color;
        text.supportRichText = false; // 학생의 답변을 마크업으로 해석하지 않는다.
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }
}
