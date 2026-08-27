using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 마이크 버튼과 음성 질문의 흐름을 잇는다.
///
///   버튼 누름 → <see cref="SpeechToTextBackend"/> 녹음 → 다시 누름 → 변환 →
///   <see cref="AIAssistantBrain.AskAssistant"/>로 질문 → 비서가 답한다.
///
/// 버튼 UI도 여기서 만든다. 비서 말풍선과 같은 방식(World Space 캔버스 + 빌보드)이라
/// 씬에 미리 배치해 둘 필요가 없고, 백엔드가 없으면 아예 띄우지 않는다 —
/// 눌러도 아무 일 없는 버튼을 보여주는 것보다 없는 편이 낫다.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class VoiceInputController : MonoBehaviour
{
    [Header("연결 (비워두면 씬에서 찾는다)")]
    public AIAssistantBrain assistant;
    public SpeechToTextBackend speechToText;

    [Header("배치")]
    [Tooltip("이 대상을 따라다닌다. 비워두면 비서를 따라간다.")]
    public Transform followTarget;
    [Tooltip("따라갈 대상 기준 오프셋(m). 기본값은 비서 아래쪽이다.")]
    public Vector3 localOffset = new Vector3(0f, -0.22f, 0f);
    [Tooltip("비워두면 Camera.main을 바라본다.")]
    public Transform lookTarget;
    [Tooltip("캔버스 1unit이 월드에서 차지하는 거리(m).")]
    public float metersPerCanvasUnit = 0.001f;

    [Header("모양")]
    public float buttonSize = 96f;
    public Color idleColor = new Color(0.12f, 0.18f, 0.24f, 0.92f);
    public Color listeningColor = new Color(0.95f, 0.25f, 0.22f, 0.95f);
    public Color busyColor = new Color(0.55f, 0.45f, 0.95f, 0.92f);
    public Color iconColor = new Color(0.92f, 0.97f, 1f);

    [Header("입력 레벨 표시")]
    [Tooltip("마이크 입력을 몇 배로 부풀려 보여줄지. 말소리는 보통 0.05~0.3 사이라 그대로 그리면 막대가 거의 안 움직인다.")]
    public float levelGain = 4f;
    [Tooltip("이 시간(초) 동안 소리가 안 들어오면 마이크를 확인하라고 알린다.")]
    public float silentWarningSeconds = 1.5f;
    public Color quietColor = new Color(0.85f, 0.35f, 0.3f, 0.95f);
    public Color activeColor = new Color(0.35f, 0.95f, 0.5f, 0.95f);

    [Header("동작")]
    [Tooltip("비서가 말하는 동안에는 녹음을 시작하지 못하게 한다. 스피커 소리가 마이크로 들어가는 걸 막는다.")]
    public bool blockWhileAssistantSpeaks = true;
    [Tooltip("녹음을 시작하면 비서의 말을 즉시 멈춘다. 끼어들어 질문할 수 있게 하려면 켜세요.")]
    public bool interruptAssistantOnRecord;
    [Tooltip("비서가 말하는 동안에는 버튼을 통째로 감춘다. 말풍선이 버튼 자리를 덮어 " +
             "가려진 버튼만 남기 때문이다. 끼어들기를 켜면 이 설정과 무관하게 계속 보인다.")]
    public bool hideWhileAssistantSpeaks = true;

    /// <summary>지금 녹음 중인지.</summary>
    public bool IsRecording => speechToText != null && speechToText.IsListening;

    private Canvas _canvas;
    private GraphicRaycaster _raycaster;
    private Button _button;
    private Image _background;
    private Image _icon;
    private Text _hint;
    private Image _levelTrack;
    private Image _levelFill;
    private RectTransform _levelFillRect;
    // 녹음 내내 소리가 안 들어왔는지 판단할 시각. 잠깐의 침묵으로 경고하지 않기 위해 시간을 둔다.
    private float _silentSince;

    private void Awake()
    {
        if (assistant == null) assistant = FindFirstObjectByType<AIAssistantBrain>(FindObjectsInactive.Include);
        if (speechToText == null) speechToText = FindFirstObjectByType<SpeechToTextBackend>(FindObjectsInactive.Include);
        if (lookTarget == null && Camera.main != null) lookTarget = Camera.main.transform;
        if (followTarget == null && assistant != null) followTarget = assistant.transform;

        BuildUi();
    }

    private void OnEnable()
    {
        if (speechToText != null)
        {
            speechToText.OnTranscribed += HandleTranscribed;
            speechToText.OnError += HandleError;
        }
    }

    private void OnDisable()
    {
        if (speechToText != null)
        {
            speechToText.OnTranscribed -= HandleTranscribed;
            speechToText.OnError -= HandleError;
        }
    }

    private void LateUpdate()
    {
        // 백엔드가 없거나 마이크가 없으면 버튼을 숨긴다. 매 프레임 보는 이유는
        // 마이크가 실행 중에 연결·해제될 수 있어서다.
        bool usable = speechToText != null && speechToText.IsConfigured;
        SetUiVisible(usable && !HiddenByAssistantSpeech());

        if (!usable) return;

        // 말풍선에 가려 안 보이는 동안에도 따라다니기와 상태 갱신은 계속한다.
        // 여기서 멈추면 비서가 말을 마쳤을 때 버튼이 지난 자리에 한 프레임 나타났다가 따라온다.
        FollowAndFaceCamera();
        RefreshVisual();
    }

    // --- 배치 ---

    private void FollowAndFaceCamera()
    {
        if (followTarget != null)
            transform.position = followTarget.TransformPoint(localOffset);

        Transform look = lookTarget != null ? lookTarget : (Camera.main != null ? Camera.main.transform : null);
        if (look == null) return;

        // 캔버스의 +Z가 카메라 반대쪽을 보게 하면 UI 면이 카메라를 향한다.
        Vector3 away = transform.position - look.position;
        if (away.sqrMagnitude < 1e-6f) return;

        transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);

        // 부모(비서)가 확대돼 있어도 버튼 크기는 유지한다.
        Transform parent = transform.parent;
        float parentScale = parent != null ? parent.lossyScale.x : 1f;
        if (parentScale > 1e-6f) transform.localScale = Vector3.one * (metersPerCanvasUnit / parentScale);
    }

    /// <summary>
    /// 비서가 말하는 동안 버튼을 감출지.
    ///
    /// 말풍선은 비서를 기준으로 펼쳐지면서 버튼 자리(<see cref="localOffset"/>)를 덮는다.
    /// 그대로 두면 말풍선 뒤에 가린 버튼만 남아 보이지도 눌리지도 않으므로, 아예 감춘다.
    ///
    /// 두 경우는 예외로 둔다.
    /// 끼어들기(<see cref="interruptAssistantOnRecord"/>)를 켰다면 이 버튼이 비서의 말을 끊고
    /// 질문하는 유일한 수단이라, 감추면 그 기능 자체가 사라진다.
    /// 녹음 중이라면 지금 눌러야 녹음을 멈출 수 있으므로 역시 감추지 않는다.
    /// </summary>
    private bool HiddenByAssistantSpeech()
    {
        if (!hideWhileAssistantSpeaks || interruptAssistantOnRecord) return false;
        if (IsRecording) return false;
        if (assistant == null) return false;

        // 멈춰 있는 동안(카메라가 레벨 사이를 건너는 중)에는 말풍선이 접혀 있어 가릴 것이 없다.
        // AIAssistantBrain.PushInputLock이 "정말 말하는 중"을 가리는 방식과 같은 조건이다.
        return assistant.IsBusy && !(assistant.bubble != null && assistant.bubble.IsPaused);
    }

    /// <summary>
    /// 버튼과 안내 문구, 입력 막대를 한꺼번에 보이거나 감춘다.
    ///
    /// 루트 GameObject를 끄면 LateUpdate가 같이 멎어 다시 켤 방법이 없어지므로
    /// 캔버스와 레이캐스터만 끈다. 그리기와 클릭 판정이 함께 멎는다.
    /// 버튼 하나만 꺼서는 부족하다 — 안내 문구와 입력 막대는 버튼이 아니라
    /// 캔버스에 직접 붙어 있어서 그대로 말풍선 위에 남는다.
    /// </summary>
    private void SetUiVisible(bool visible)
    {
        if (_canvas != null && _canvas.enabled != visible) _canvas.enabled = visible;
        if (_raycaster != null && _raycaster.enabled != visible) _raycaster.enabled = visible;
    }

    // --- 상태 표시 ---

    private void RefreshVisual()
    {
        if (_background == null) return;

        if (IsRecording)
        {
            // 맥동이 아니라 실제 입력 크기로 밝기를 움직인다. 규칙적으로 깜빡이기만 하면
            // 마이크가 죽어 있어도 똑같이 보여서 "듣고 있다"는 착각을 준다.
            float level = speechToText.InputLevel;
            _background.color = Color.Lerp(listeningColor * 0.75f, listeningColor * 1.35f,
                                           Mathf.Clamp01(level * levelGain));

            SetHint(BuildRecordingHint(level));
        }
        else if (speechToText.IsTranscribing)
        {
            _background.color = busyColor;
            SetHint("알아듣는 중…");
        }
        else if (IsBlocked())
        {
            _background.color = idleColor * 0.55f;
            SetHint("비서가 말하는 중");
        }
        else
        {
            _background.color = idleColor;
            SetHint("눌러서 질문하기");
        }

        if (_button != null) _button.interactable = IsRecording || !IsBlocked();

        UpdateLevelMeter();
    }

    /// <summary>입력 크기를 막대로 그린다. 녹음 중이 아닐 때는 숨긴다.</summary>
    private void UpdateLevelMeter()
    {
        if (_levelTrack == null) return;

        if (_levelTrack.gameObject.activeSelf != IsRecording)
            _levelTrack.gameObject.SetActive(IsRecording);

        if (!IsRecording || _levelFillRect == null) return;

        float filled = Mathf.Clamp01(speechToText.InputLevel * levelGain);
        _levelFillRect.anchorMax = new Vector2(filled, 1f);

        // 소리가 들어오면 초록, 거의 없으면 붉게 — 색만으로도 상태를 알 수 있게 한다.
        _levelFill.color = Color.Lerp(quietColor, activeColor, Mathf.Clamp01(filled * 2.5f));
    }

    /// <summary>녹음 중 안내 문구. 한동안 조용하면 마이크 문제를 짚어준다.</summary>
    private string BuildRecordingHint(float level)
    {
        bool audible = level * levelGain > 0.08f;
        if (audible) _silentSince = Time.time;

        if (!audible && Time.time - _silentSince > silentWarningSeconds)
        {
            string device = speechToText.ActiveDeviceName;
            return string.IsNullOrEmpty(device)
                ? "소리가 안 들어와요 — 마이크를 확인하세요"
                : $"소리가 안 들어와요 — {device}";
        }

        return "듣는 중… 다시 누르면 전송";
    }

    private bool IsBlocked()
    {
        if (speechToText.IsTranscribing) return true;
        if (!blockWhileAssistantSpeaks || interruptAssistantOnRecord) return false;

        return assistant != null && assistant.IsBusy;
    }

    private void SetHint(string text)
    {
        if (_hint != null && _hint.text != text) _hint.text = text;
    }

    // --- 흐름 ---

    private void HandleClick()
    {
        if (speechToText == null || !speechToText.IsConfigured) return;

        if (IsRecording)
        {
            speechToText.StopListening();
            return;
        }

        if (IsBlocked()) return;

        if (interruptAssistantOnRecord && assistant != null && assistant.bubble != null)
            assistant.bubble.Hide();

        _silentSince = Time.time;
        speechToText.StartListening();
    }

    private void HandleTranscribed(string text)
    {
        if (assistant == null)
        {
            Debug.LogWarning($"[VoiceInputController] 비서를 찾지 못해 질문을 전달하지 못했습니다: {text}", this);
            return;
        }

        // 알아들은 말을 먼저 되읊어 준다. 잘못 들었을 때 사용자가 바로 알아채고 다시 물을 수 있다.
        assistant.SpeakNow($"\"{text}\" 라고 물어봤구나. 잠깐만!");
        assistant.AskAssistant(text);
    }

    private void HandleError(string reason)
    {
        Debug.LogWarning($"[VoiceInputController] 음성 인식 실패: {reason}", this);

        if (assistant != null)
            assistant.SpeakNow("어? 잘 못 들었어. 조금 더 또렷하게 다시 말해줄래?");
    }

    // --- UI 만들기 ---

    private void BuildUi()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;

        _raycaster = GetComponent<GraphicRaycaster>();
        if (_raycaster == null) _raycaster = gameObject.AddComponent<GraphicRaycaster>();

        var canvasRect = (RectTransform)transform;
        canvasRect.sizeDelta = new Vector2(buttonSize * 3f, buttonSize * 2f);
        transform.localScale = Vector3.one * metersPerCanvasUnit;

        // --- 버튼 ---
        var buttonGo = new GameObject("MicButton", typeof(RectTransform));
        buttonGo.transform.SetParent(transform, false);

        var buttonRect = (RectTransform)buttonGo.transform;
        buttonRect.sizeDelta = new Vector2(buttonSize, buttonSize);
        buttonRect.anchoredPosition = new Vector2(0f, buttonSize * 0.35f);

        _background = buttonGo.AddComponent<Image>();
        _background.color = idleColor;
        // 원형 버튼. Circle()은 9-slice 테두리가 없어 Simple로 둬야 모양이 유지된다.
        _background.sprite = HoloSpriteFactory.Circle();
        _background.type = Image.Type.Simple;

        _button = buttonGo.AddComponent<Button>();
        _button.targetGraphic = _background;
        _button.onClick.AddListener(HandleClick);

        // --- 마이크 아이콘 ---
        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(buttonGo.transform, false);

        var iconRect = (RectTransform)iconGo.transform;
        iconRect.sizeDelta = new Vector2(buttonSize * 0.34f, buttonSize * 0.5f);

        _icon = iconGo.AddComponent<Image>();
        _icon.color = iconColor;
        // 마이크 머리 모양 — 원을 세로로 늘려 캡슐처럼 보이게 한다.
        _icon.sprite = HoloSpriteFactory.Circle();
        _icon.type = Image.Type.Simple;
        _icon.raycastTarget = false;

        // --- 안내 문구 ---
        var hintGo = new GameObject("Hint", typeof(RectTransform));
        hintGo.transform.SetParent(transform, false);

        var hintRect = (RectTransform)hintGo.transform;
        hintRect.sizeDelta = new Vector2(buttonSize * 3f, buttonSize * 0.5f);
        hintRect.anchoredPosition = new Vector2(0f, -buttonSize * 0.45f);

        _hint = hintGo.AddComponent<Text>();
        _hint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _hint.fontSize = 22;
        _hint.alignment = TextAnchor.MiddleCenter;
        _hint.color = new Color(0.85f, 0.93f, 1f, 0.85f);
        _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
        _hint.raycastTarget = false;
        _hint.text = "눌러서 질문하기";

        BuildLevelMeter();
        ApplyKoreanFont();

        WarnIfNoEventSystem();
    }

    /// <summary>
    /// 입력 크기를 보여주는 막대. 버튼 바로 아래에 붙인다.
    ///
    /// 버튼 색만으로도 크기를 알 수 있지만, 막대가 있어야 "조금 들어오는지 전혀 안 들어오는지"가
    /// 구분된다. 마이크가 잡히긴 했는데 볼륨이 0에 가까운 경우가 실제로 가장 헷갈린다.
    /// </summary>
    private void BuildLevelMeter()
    {
        var trackGo = new GameObject("LevelTrack", typeof(RectTransform));
        trackGo.transform.SetParent(transform, false);

        var trackRect = (RectTransform)trackGo.transform;
        trackRect.sizeDelta = new Vector2(buttonSize * 1.6f, buttonSize * 0.1f);
        trackRect.anchoredPosition = new Vector2(0f, -buttonSize * 0.2f);

        _levelTrack = trackGo.AddComponent<Image>();
        _levelTrack.color = new Color(0f, 0f, 0f, 0.45f);
        _levelTrack.raycastTarget = false;

        var fillGo = new GameObject("LevelFill", typeof(RectTransform));
        fillGo.transform.SetParent(trackGo.transform, false);

        // 앵커를 늘려 채우는 방식이라 폭 계산 없이 anchorMax.x만 움직이면 된다.
        _levelFillRect = (RectTransform)fillGo.transform;
        _levelFillRect.anchorMin = new Vector2(0f, 0f);
        _levelFillRect.anchorMax = new Vector2(0f, 1f);
        _levelFillRect.offsetMin = Vector2.zero;
        _levelFillRect.offsetMax = Vector2.zero;
        _levelFillRect.pivot = new Vector2(0f, 0.5f);

        _levelFill = fillGo.AddComponent<Image>();
        _levelFill.color = quietColor;
        _levelFill.raycastTarget = false;

        trackGo.SetActive(false);
    }

    /// <summary>내장 LegacyRuntime.ttf에는 한글 글리프가 없다. 말풍선과 같은 방식으로 OS 폰트를 쓴다.</summary>
    private void ApplyKoreanFont()
    {
        Font font = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Noto Sans CJK KR" }, 24);
        if (font == null) return;

        foreach (Text text in GetComponentsInChildren<Text>(true)) text.font = font;
    }

    private void WarnIfNoEventSystem()
    {
        if (EventSystem.current != null) return;

        Debug.LogWarning("[VoiceInputController] 씬에 EventSystem이 없어 마이크 버튼을 누를 수 없습니다. " +
                         "GameObject > UI > Event System 으로 추가하세요.", this);
    }
}
