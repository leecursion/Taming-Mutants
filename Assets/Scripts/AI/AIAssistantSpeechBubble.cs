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

    [Header("타이핑")]
    public bool useTypewriter = true;
    public float charactersPerSecond = 35f;

    [Header("표시 시간")]
    [Tooltip("타이핑이 끝난 뒤 유지할 기본 시간(초)")]
    public float baseHoldSeconds = 1.5f;
    [Tooltip("글자 하나당 추가로 유지할 시간(초)")]
    public float secondsPerCharacter = 0.045f;
    public float maxHoldSeconds = 12f;

    [Header("등장 연출")]
    public float fadeDuration = 0.25f;
    [Tooltip("등장할 때 시작 스케일. 살짝 튀어나오며 열린다.")]
    public float popFromScale = 0.88f;
    [Tooltip("사라질 때 줄어드는 스케일")]
    public float collapseToScale = 0.96f;

    /// <summary>현재 말하는 중이거나 대기 중인 메시지가 있는지.</summary>
    public bool IsBusy => _runner != null || _pending.Count > 0;

    // 밀도를 바꾸면 캔버스가 리빌드되고 글리프를 다시 굽는다.
    // 화면상 티도 안 나는 변화까지 따라가면 카메라가 움직이는 내내 매 프레임 다시 굽게 된다.
    // 로그 비율로 이 값(약 25%)을 넘을 때만 반영한다.
    private const float RebakeThreshold = 0.22f;

    private readonly Queue<string> _pending = new Queue<string>();
    private Coroutine _runner;
    private float[] _accentAlphas;
    private Camera _camera;
    private int _largestFontSize;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponentInChildren<CanvasGroup>(includeInactive: true);
        if (visual == null) visual = GetComponentInParent<AIAssistantVisual>();
        if (lookTarget == null && Camera.main != null) lookTarget = Camera.main.transform;
        if (canvasScaler == null) canvasScaler = GetComponent<CanvasScaler>();

        CacheAccentAlphas();
        ApplyOsFont();
        ApplyAlwaysOnTopMaterial();

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

    /// <summary>보정에 쓸 카메라까지의 거리. 하한/상한을 물려 크기가 폭주하지 않게 한다.</summary>
    private float CameraDistance()
    {
        Camera cam = ResolveCamera();
        if (cam == null) return referenceDistance;

        float distance = Vector3.Distance(cam.transform.position, transform.position);
        return Mathf.Clamp(distance, distanceClamp.x, distanceClamp.y);
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

    /// <summary>메시지를 큐에 넣는다. 이미 말하는 중이면 끝난 뒤에 이어서 재생된다.</summary>
    public void Say(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        _pending.Enqueue(message);
        if (_runner == null) _runner = StartCoroutine(RunQueue());
    }

    /// <summary>대기 중인 메시지를 버리고 즉시 이 메시지로 교체한다. (예: 오류 안내)</summary>
    public void SayNow(string message)
    {
        _pending.Clear();
        if (_runner != null) StopCoroutine(_runner);
        _runner = null;
        Say(message);
    }

    public void Hide()
    {
        _pending.Clear();
        if (_runner != null) StopCoroutine(_runner);
        _runner = null;
        StartCoroutine(HideRoutine());
    }

    // --- 재생 ---

    private IEnumerator RunQueue()
    {
        while (_pending.Count > 0)
        {
            yield return ShowRoutine(_pending.Dequeue());
        }

        yield return HideRoutine();
        _runner = null;
    }

    private IEnumerator ShowRoutine(string message)
    {
        if (bubbleRect == null || label == null) yield break;

        bubbleRect.gameObject.SetActive(true);

        // 최종 문구로 레이아웃을 먼저 확정해 두면 타이핑 중에 말풍선 높이가 요동치지 않는다.
        label.supportRichText = true;
        label.text = message;
        LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRect);

        if (driveVisualState && visual != null) visual.SetState(AIAssistantState.Speaking);

        yield return Reveal(true);

        if (useTypewriter && charactersPerSecond > 0f)
        {
            yield return TypeRoutine(message);
        }
        label.text = message;

        float hold = Mathf.Min(baseHoldSeconds + message.Length * secondsPerCharacter, maxHoldSeconds);
        yield return new WaitForSeconds(hold);
    }

    private IEnumerator TypeRoutine(string message)
    {
        int visible = 0;
        float carry = 0f;

        while (visible < message.Length)
        {
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
    }

    private static float EaseOutBack(float t)
    {
        const float overshoot = 1.4f;
        float p = t - 1f;
        return 1f + (overshoot + 1f) * p * p * p + overshoot * p * p;
    }
}
