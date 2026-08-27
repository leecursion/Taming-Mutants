using UnityEngine;

/// <summary>분자 옆자리를 잡는 방식.</summary>
public enum AIAssistantAnchorPlacement
{
    /// <summary>대상 중심에서 고정 오프셋. 화면 개념이 없는 XR이나 크기가 일정한 대상에 쓴다.</summary>
    WorldOffset,

    /// <summary>대상의 화면상 경계를 재서 그 우측 상단에, 겹치지 않게 세운다.</summary>
    ScreenSpace,
}

/// <summary>
/// F-06 AI Co-Scientist의 시각적 아바타 — 사용자 시야의 오른쪽 위를 떠다니며 따라오는 비서.
///
/// 카메라의 자식으로 붙이지 않고 씬 루트에 두는 이유:
/// 자식으로 붙이면 머리 움직임에 1:1로 고정되어 XR에서 멀미를 유발하고,
/// "스스로 날아다닌다"는 느낌이 사라진다. 대신 데드존(deadZoneRadius) 밖으로
/// 벗어났을 때만 부드럽게 쫓아오는 lazy-follow 방식을 쓴다.
///
/// Desktop -> XR 전환 시에는 <see cref="followTarget"/>만 Main Camera에서
/// CenterEyeAnchor로 바꾸면 되고, 나머지 로직은 수정할 필요가 없다
/// (guide.md 1장 "컴포넌트 교체 방식" 원칙).
/// </summary>
public class AIAssistantFollower : MonoBehaviour
{
    [Header("추종 대상")]
    [Tooltip("비워두면 Camera.main 사용. XR 전환 시 CenterEyeAnchor를 지정한다.")]
    public Transform followTarget;

    [Header("정위치 (사용자 기준 오프셋, 단위 m)")]
    [Tooltip("x = 오른쪽, y = 위, z = 앞. 기본값은 시야 오른쪽 위 약 0.9m 앞.")]
    public Vector3 localOffset = new Vector3(0.45f, 0.25f, 0.9f);
    [Tooltip("켜면 좌우 회전(yaw)만 따라간다. 고개를 위아래로 숙여도 비서 높이가 흔들리지 않아 시야에서 안정적이다.")]
    public bool yawOnly = true;

    [Header("분자 구조 옆에 세우기 (선택)")]
    [Tooltip("지정하면 사용자 시야가 아니라 이 오브젝트(보통 ProteinAnchor_Main) 옆에 머문다. " +
             "비워두면 위의 사용자 기준 배치를 쓴다.")]
    public Transform anchorTarget;
    [Tooltip("ScreenSpace: 대상의 화면 경계를 재서 우측 상단에 겹치지 않게 세운다(권장). " +
             "WorldOffset: 아래 anchorOffset을 그대로 쓴다.")]
    public AIAssistantAnchorPlacement anchorPlacement = AIAssistantAnchorPlacement.ScreenSpace;
    [Tooltip("x = 사용자가 볼 때 오른쪽, y = 위, z = 대상에서 사용자 쪽으로 당기는 거리 (단위 m). " +
             "ScreenSpace 모드에서는 x/y를 무시하고 z(깊이 당김)만 쓴다.")]
    public Vector3 anchorOffset = new Vector3(0.62f, 0.35f, 1.2f);

    [Header("가림 방지 (ScreenSpace 모드)")]
    [Tooltip("대상의 화면 사각형에서 얼마나 띄울지. 화면 높이 대비 비율.")]
    public float screenMargin = 0.04f;
    [Tooltip("화면 가장자리에서 최소한 이만큼은 안쪽에 둔다. 화면 높이 대비 비율.")]
    public float screenEdgePadding = 0.025f;
    [Tooltip("대상의 경계를 다시 재는 주기(초). 원자가 새로 생기면 주기와 무관하게 즉시 다시 잰다.")]
    public float anchorBoundsRefreshInterval = 2f;

    [Header("따라오기 (lazy-follow)")]
    [Tooltip("정위치가 이 반경 밖으로 벗어났을 때만 다시 쫓아간다. 작은 머리 움직임에는 반응하지 않는다.")]
    public float deadZoneRadius = 0.15f;
    [Tooltip("따라붙는 데 걸리는 대략적인 시간(초). 클수록 느긋하게 따라온다.")]
    public float followSmoothTime = 0.35f;
    public float maxFollowSpeed = 6f;

    [Header("부유 연출")]
    [Tooltip("위아래로 까딱이는 진폭(m)")]
    public float bobAmplitude = 0.035f;
    [Tooltip("초당 왕복 횟수")]
    public float bobFrequency = 0.5f;
    public float swayAmplitude = 0.02f;
    public float swayFrequency = 0.33f;

    [Header("일시적 오버라이드 (예: p53 열안정성 카메라 클로즈업)")]
    [Tooltip("켜져 있는 동안은 anchorTarget(분자) 옆 배치 대신 사용자(카메라) 옆 배치로 전환한다. " +
             "카메라가 분자 전체가 아니라 좁은 부위로 확 당겨지면 ScreenSpace 배치가 분자의 화면 " +
             "투영 자체를 잘못 재서 깨지기 때문이다. ThermalStabilityController처럼 클로즈업 연출을 " +
             "트는 쪽이 SetCloseUpOverride()로 켜고 끈다.")]
    public bool closeUpOverrideActive;
    [Tooltip("오버라이드 중 사용할 사용자 기준 오프셋 (localOffset과 같은 축 규칙: x=오른쪽, y=위, z=앞).\n\n" +
             "z(카메라와의 거리)가 곧 비서가 화면에서 차지하는 크기다. 평소(anchorTarget 옆 " +
             "ScreenSpace 배치)에는 분자까지 거리에서 anchorOffset.z만큼 당긴 자리에 서므로 대략 " +
             "1.8m인데, 여기 z를 0.7 같은 작은 값으로 두면 그 사건에서만 비서가 2배 이상 커 보인다. " +
             "방향(x:z, y:z 비율)은 localOffset과 같게 두어 화면상 위치는 평소와 같은 자리를 유지한다.")]
    public Vector3 closeUpLocalOffset = new Vector3(0.65f, 0.36f, 1.3f);

    [Header("바라보기")]
    [Tooltip("켜면 항상 사용자를 향한다. 끄면 사용자와 같은 방향을 본다.")]
    public bool faceUser = true;
    public float turnSpeed = 6f;
    [Tooltip("이동 속도에 비례해 진행 방향으로 기울이는 각도 계수 (비행체 뱅킹 느낌)")]
    public float bankPerSpeed = 6f;
    public float maxBankAngle = 20f;

    /// <summary>데드존에 다시 안착했다고 판단하는 비율 (deadZoneRadius 대비).</summary>
    private const float SettleRatio = 0.1f;

    private Vector3 _anchorPosition;   // 부유 흔들림을 제외한 기준 위치
    private Vector3 _followVelocity;   // SmoothDamp 내부 속도 (뱅킹 계산에도 재사용)
    private bool _isChasing;
    private float _noiseSeed;

    private Transform _focusTarget;
    private float _focusUntil;

    private Camera _camera;
    private Renderer[] _selfRenderers;
    private RectTransform[] _selfRects;
    private readonly Vector3[] _rectCorners = new Vector3[4];

    // 대상(분자)의 로컬 경계 캐시. 원자가 수천 개라 매 프레임 전부 훑을 수 없다.
    private Bounds _anchorLocalBounds;
    private bool _hasAnchorBounds;
    private int _anchorChildCount = -1;
    private float _anchorBoundsRefreshAt;

    private void Awake()
    {
        if (followTarget == null && Camera.main != null) followTarget = Camera.main.transform;
        _noiseSeed = Random.value * 100f; // 비서가 여러 개여도 같은 박자로 흔들리지 않게
    }

    private void Start()
    {
        SnapToAnchor(); // 원점에서 날아오는 연출 방지
    }

    // 카메라(또는 XR 헤드 포즈)가 갱신된 뒤에 위치를 잡아야 한 프레임 밀리지 않는다.
    private void LateUpdate()
    {
        if (followTarget == null) return;

        UpdatePosition();
        UpdateRotation();
    }

    // --- 위치 ---

    private void UpdatePosition()
    {
        Vector3 desired = ComputeAnchor();

        // 데드존 밖으로 나가면 추적 시작 -> 충분히 가까워지면 다시 멈춘다(히스테리시스).
        // 임계값을 하나만 쓰면 경계에서 미세하게 떨리므로 시작/종료 기준을 분리했다.
        float distance = Vector3.Distance(_anchorPosition, desired);
        if (!_isChasing && distance > deadZoneRadius) _isChasing = true;

        if (_isChasing)
        {
            _anchorPosition = Vector3.SmoothDamp(
                _anchorPosition, desired, ref _followVelocity, followSmoothTime, maxFollowSpeed);

            if (Vector3.Distance(_anchorPosition, desired) < deadZoneRadius * SettleRatio)
            {
                _isChasing = false;
                _followVelocity = Vector3.zero;
            }
        }

        transform.position = _anchorPosition + ComputeFloatOffset();
    }

    private Vector3 ComputeAnchor()
    {
        if (closeUpOverrideActive) return ComputeUserRelativeAnchor(closeUpLocalOffset);
        if (anchorTarget != null) return ComputeAnchorBesideTarget();

        return ComputeUserRelativeAnchor(localOffset);
    }

    private Vector3 ComputeUserRelativeAnchor(Vector3 offset)
    {
        if (!yawOnly) return followTarget.TransformPoint(offset);

        Vector3 flatForward = followTarget.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
        {
            // 정수리/발밑을 정면으로 볼 때는 forward가 수직이라 쓸 수 없다. up 벡터가 수평을 가리킨다.
            flatForward = followTarget.up;
            flatForward.y = 0f;
        }
        if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;

        Quaternion basis = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        return followTarget.position + basis * offset;
    }

    /// <summary>
    /// 대상(분자) 옆자리를 잡는다. ScreenSpace 모드가 기본이고,
    /// 카메라를 못 찾거나 대상이 카메라 평면을 걸치는 등 화면 계산이 불가능하면
    /// 고정 오프셋 방식으로 물러난다.
    /// </summary>
    private Vector3 ComputeAnchorBesideTarget()
    {
        if (anchorPlacement == AIAssistantAnchorPlacement.ScreenSpace)
        {
            Camera cam = ResolveCamera();
            if (cam != null && TryComputeAnchorOnScreen(cam, out Vector3 onScreen)) return onScreen;
        }

        return ComputeAnchorBesideTargetInWorld();
    }

    /// <summary>
    /// 대상의 화면 사각형을 구하고, 그 우측 상단 바깥에 비서 어셈블리(본체 + 말풍선)를
    /// 통째로 올려놓는다.
    ///
    /// 월드 오프셋으로는 이 요구를 만족시킬 수 없다. 분자는 원자 수에 따라 크기가 제각각이고
    /// 실행 중에도 확대·회전하므로 "몇 미터 옆"이라는 상수는 어떤 값을 골라도 어떤 크기에서는
    /// 겹친다. 가림 여부는 결국 화면에서 결정되니 화면에서 직접 푼다.
    ///
    /// 비서를 원하는 화면 위치에 놓는 건 ViewportToWorldPoint로 한 번에 역산된다.
    /// 깊이를 먼저 정하고(대상까지의 거리에서 anchorOffset.z만큼 당김) 그 평면 위에서 위치를 잡는다.
    /// </summary>
    private bool TryComputeAnchorOnScreen(Camera cam, out Vector3 position)
    {
        position = default;

        if (!TryGetAnchorWorldBounds(out Bounds targetBounds)) return false;
        if (!TryProjectToViewport(cam, targetBounds, out Rect targetRect)) return false;

        // 비서가 설 깊이. 대상보다 사용자 쪽으로 당겨야 분자에 파묻히지 않는다.
        float depth = Vector3.Distance(cam.transform.position, targetBounds.center) - anchorOffset.z;
        depth = Mathf.Max(depth, cam.nearClipPlane + 0.05f);

        // 그 깊이에서 뷰포트 1.0이 덮는 월드 크기. 어셈블리 여유(m)를 뷰포트 단위로 바꾸는 데 쓴다.
        float viewHeight = cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float viewWidth = viewHeight * cam.aspect;
        if (viewHeight <= 1e-4f || viewWidth <= 1e-4f) return false;

        MeasureAssemblyExtents(cam, out float left, out float right, out float bottom, out float top);

        // 루트 기준 여유를 뷰포트 비율로. left/bottom은 음수라 빼면 그만큼 밀린다.
        float vLeft = left / viewWidth;
        float vRight = right / viewWidth;
        float vBottom = bottom / viewHeight;
        float vTop = top / viewHeight;

        // 비서는 여기서 계산한 정위치에 정확히 서 있지 않다. 데드존 안에서는 쫓아가지 않고,
        // 그 위에 부유 흔들림까지 얹힌다. 그 최대 이탈량만큼 여백을 미리 벌어두지 않으면
        // 정위치가 아무리 정확해도 실제로는 대상에 걸친다.
        float slack = (deadZoneRadius + bobAmplitude + swayAmplitude) / viewHeight;
        float marginY = screenMargin + slack;
        // 가로 여백을 세로와 같은 비율로 두면 화면비 때문에 더 넓어 보인다. 화면 높이 기준으로 맞춘다.
        float marginX = marginY / Mathf.Max(cam.aspect, 1e-3f);

        float padX = screenEdgePadding / Mathf.Max(cam.aspect, 1e-3f);
        float maxVx = 1f - padX - vRight;

        // 가로만 확실히 비켜도 겹침은 이미 불가능하다. 가로·세로를 둘 다 밀어내면
        // 어셈블리가 대각선으로 멀어져 화면을 크게 낭비하고 모서리에 자주 부딪힌다.
        // 그래서 가로는 "대상의 오른쪽 끝 바깥"으로 강제하고,
        // 세로는 어셈블리 윗변을 대상 윗변에 맞춰 우측 "상단"으로 읽히게만 한다.
        float vx = targetRect.xMax + marginX - vLeft;
        float vy = targetRect.yMax - vTop;

        if (vx > maxVx)
        {
            // 오른쪽에 그만한 자리가 없다. 가로로는 겹칠 수밖에 없으니
            // 이번엔 세로로 완전히 넘겨서 가림을 피한다.
            vx = maxVx;
            vy = targetRect.yMax + marginY - vBottom;
        }

        // 그래도 안 들어가면(대상이 화면을 꽉 채운 경우) 가림 방지보다 "보이는 것"이 우선이다.
        vx = Mathf.Clamp(vx, padX - vLeft, maxVx);
        vy = Mathf.Clamp(vy, screenEdgePadding - vBottom, 1f - screenEdgePadding - vTop);

        position = cam.ViewportToWorldPoint(new Vector3(vx, vy, depth));
        return true;
    }

    /// <summary>
    /// 고정 오프셋 방식. 기준축을 "사용자 -> 대상" 방향에서 만들기 때문에
    /// 사용자가 테이블 반대편으로 돌아가도 비서는 늘 사용자 쪽 면에 서고,
    /// 분자 뒤로 숨어 가려지는 일이 없다.
    /// </summary>
    private Vector3 ComputeAnchorBesideTargetInWorld()
    {
        Vector3 target = anchorTarget.position;

        Vector3 towardUser = followTarget.position - target;
        towardUser.y = 0f;
        if (towardUser.sqrMagnitude < 1e-4f)
        {
            // 사용자가 대상 바로 위/아래에 있어 수평 방향이 없을 때
            towardUser = -followTarget.forward;
            towardUser.y = 0f;
        }
        if (towardUser.sqrMagnitude < 1e-4f) towardUser = Vector3.back;
        towardUser.Normalize();

        Vector3 viewForward = -towardUser;                                  // 사용자가 대상을 보는 방향
        Vector3 right = Vector3.Cross(Vector3.up, viewForward).normalized;  // 그 방향 기준 오른쪽

        return target
             + right * anchorOffset.x
             + Vector3.up * anchorOffset.y
             + towardUser * anchorOffset.z;
    }

    // --- 경계 측정 ---

    private Camera ResolveCamera()
    {
        if (_camera != null) return _camera;

        if (followTarget != null) _camera = followTarget.GetComponent<Camera>();
        if (_camera == null) _camera = Camera.main;
        return _camera;
    }

    /// <summary>
    /// 대상의 월드 경계. 로컬 공간에서 한 번만 재고, 매 프레임에는 그 상자를 월드로 옮기기만 한다.
    /// 원자가 수천 개라 매 프레임 렌더러를 전부 훑으면 비싸지만, 로컬 경계는 원자가 새로
    /// 생기기 전까지 변하지 않는다. 회전과 확대는 상자를 옮기는 것만으로 그대로 반영된다.
    /// </summary>
    private bool TryGetAnchorWorldBounds(out Bounds world)
    {
        world = default;
        if (anchorTarget == null) return false;

        // 원자는 로딩이 끝난 뒤 한꺼번에 생기므로 자식 수 변화가 가장 확실한 신호다.
        bool childrenChanged = anchorTarget.childCount != _anchorChildCount;
        if (!_hasAnchorBounds || childrenChanged || Time.time >= _anchorBoundsRefreshAt)
            RefreshAnchorLocalBounds();

        if (!_hasAnchorBounds) return false;

        world = TransformBounds(anchorTarget.localToWorldMatrix, _anchorLocalBounds);
        return true;
    }

    private void RefreshAnchorLocalBounds()
    {
        _anchorChildCount = anchorTarget.childCount;
        _anchorBoundsRefreshAt = Time.time + Mathf.Max(anchorBoundsRefreshInterval, 0.1f);
        _hasAnchorBounds = false;

        Matrix4x4 toLocal = anchorTarget.worldToLocalMatrix;
        foreach (Renderer r in anchorTarget.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled) continue;

            Bounds local = TransformBounds(toLocal, r.bounds);
            if (!_hasAnchorBounds)
            {
                _anchorLocalBounds = local;
                _hasAnchorBounds = true;
            }
            else
            {
                _anchorLocalBounds.Encapsulate(local);
            }
        }
    }

    /// <summary>
    /// 비서 어셈블리가 루트 위치를 기준으로 카메라의 좌우/상하 축으로 얼마나 삐져나오는지(m).
    /// 말풍선은 CanvasRenderer라 Renderer로 잡히지 않으므로 RectTransform 코너를 따로 센다.
    ///
    /// 말풍선이 꺼져 있어도 자리를 미리 비워둔다. 그래야 말하기 시작하는 순간
    /// 비서가 옆으로 밀려나지 않는다.
    /// </summary>
    private void MeasureAssemblyExtents(Camera cam, out float left, out float right, out float bottom, out float top)
    {
        left = right = bottom = top = 0f;

        Vector3 origin = transform.position;
        Vector3 camRight = cam.transform.right;
        Vector3 camUp = cam.transform.up;

        if (_selfRenderers == null) _selfRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in _selfRenderers)
        {
            // 파티클(주변 입자)은 경계가 프레임마다 출렁여서 넣으면 정위치가 떨린다.
            if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;

            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
                Accumulate(BoundsCorner(b, i) - origin, camRight, camUp, ref left, ref right, ref bottom, ref top);
        }

        if (_selfRects == null) _selfRects = GetComponentsInChildren<RectTransform>(true);
        foreach (RectTransform rect in _selfRects)
        {
            if (rect == null) continue;

            rect.GetWorldCorners(_rectCorners);
            for (int i = 0; i < 4; i++)
                Accumulate(_rectCorners[i] - origin, camRight, camUp, ref left, ref right, ref bottom, ref top);
        }
    }

    private static void Accumulate(Vector3 offset, Vector3 camRight, Vector3 camUp,
                                   ref float left, ref float right, ref float bottom, ref float top)
    {
        float x = Vector3.Dot(offset, camRight);
        float y = Vector3.Dot(offset, camUp);

        if (x < left) left = x;
        if (x > right) right = x;
        if (y < bottom) bottom = y;
        if (y > top) top = y;
    }

    /// <summary>
    /// 월드 AABB를 뷰포트 사각형으로. 코너 하나라도 카메라 뒤에 있으면 투영이 뒤집혀
    /// 엉뚱한 사각형이 나오므로, 그때는 실패로 처리하고 고정 오프셋 방식에 맡긴다.
    /// (사용자가 분자 안으로 들어간 상황이라 애초에 "옆에 세우기"가 성립하지 않는다.)
    /// </summary>
    private static bool TryProjectToViewport(Camera cam, Bounds bounds, out Rect rect)
    {
        rect = default;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            Vector3 v = cam.WorldToViewportPoint(BoundsCorner(bounds, i));
            if (v.z <= 0f) return false;

            if (v.x < minX) minX = v.x;
            if (v.x > maxX) maxX = v.x;
            if (v.y < minY) minY = v.y;
            if (v.y > maxY) maxY = v.y;
        }

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private static Vector3 BoundsCorner(Bounds b, int index)
    {
        Vector3 e = b.extents;
        return b.center + new Vector3(
            (index & 1) == 0 ? -e.x : e.x,
            (index & 2) == 0 ? -e.y : e.y,
            (index & 4) == 0 ? -e.z : e.z);
    }

    /// <summary>AABB를 행렬로 옮긴 뒤 다시 축정렬 상자로 감싼다. 회전이 들어가면 상자가 조금 커진다.</summary>
    private static Bounds TransformBounds(Matrix4x4 m, Bounds b)
    {
        Vector3 center = m.MultiplyPoint3x4(b.center);
        Vector3 e = b.extents;

        Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
        Vector3 ay = m.MultiplyVector(new Vector3(0f, e.y, 0f));
        Vector3 az = m.MultiplyVector(new Vector3(0f, 0f, e.z));

        Vector3 extents = new Vector3(
            Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
            Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
            Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));

        return new Bounds(center, extents * 2f);
    }

    // 정위치 주변을 천천히 맴도는 부유감. 위치 추적과 분리해 두어야
    // 데드존 판정이 흔들림 때문에 계속 참이 되는 일이 없다.
    private Vector3 ComputeFloatOffset()
    {
        float t = Time.time + _noiseSeed;
        float bob = Mathf.Sin(t * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        float sway = Mathf.Sin(t * swayFrequency * Mathf.PI * 2f + 1.3f) * swayAmplitude;
        return Vector3.up * bob + followTarget.right * sway;
    }

    // --- 회전 ---

    private void UpdateRotation()
    {
        Vector3 lookPoint = ResolveLookPoint();
        Vector3 dir = lookPoint - transform.position;
        if (dir.sqrMagnitude < 1e-6f) return;

        Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);

        // 옆으로 이동하는 속도만큼 롤을 줘서 날아다니는 느낌을 낸다.
        float lateral = Vector3.Dot(_followVelocity, transform.right);
        float bank = Mathf.Clamp(-lateral * bankPerSpeed, -maxBankAngle, maxBankAngle);
        look *= Quaternion.Euler(0f, 0f, bank);

        // 프레임레이트에 무관한 감속 보간
        float k = 1f - Mathf.Exp(-turnSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, k);
    }

    private Vector3 ResolveLookPoint()
    {
        if (_focusTarget != null)
        {
            if (Time.time < _focusUntil) return _focusTarget.position;
            _focusTarget = null;
        }

        return faceUser
            ? followTarget.position
            : transform.position + followTarget.forward;
    }

    // --- 외부 제어 API ---

    /// <summary>정위치로 즉시 이동. 씬 전환 직후나 텔레포트 후에 호출한다.</summary>
    public void SnapToAnchor()
    {
        if (followTarget == null) return;

        _anchorPosition = ComputeAnchor();
        _followVelocity = Vector3.zero;
        _isChasing = false;
        transform.position = _anchorPosition;
    }

    /// <summary>정위치 오프셋 변경. 예: 대화 중에는 시야 중앙 쪽으로 당겨오기.</summary>
    public void SetLocalOffset(Vector3 offset)
    {
        localOffset = offset;
        _isChasing = true;
    }

    /// <summary>지정한 대상을 잠시 바라본다. 예: 변이 부위를 설명하며 그쪽을 쳐다보기.</summary>
    public void FocusOn(Transform pointOfInterest, float duration = 3f)
    {
        _focusTarget = pointOfInterest;
        _focusUntil = Time.time + duration;
    }

    /// <summary>
    /// ThermalStabilityController처럼 카메라를 분자의 좁은 부위로 클로즈업시키는 연출을 트는 쪽이
    /// 연출 시작/종료에 맞춰 호출한다. true면 anchorTarget 옆 배치를 잠시 멈추고 사용자 옆으로
    /// 옮기며, false면 원래 배치로 되돌린다. 실제 이동은 기존 lazy-follow(SmoothDamp)를 그대로
    /// 타므로 순간이동 없이 부드럽게 전환된다.
    /// </summary>
    public void SetCloseUpOverride(bool active)
    {
        closeUpOverrideActive = active;
    }

    private void OnDrawGizmosSelected()
    {
        Transform target = followTarget != null ? followTarget
            : (Camera.main != null ? Camera.main.transform : null);
        if (target == null) return;

        Transform prev = followTarget;
        followTarget = target;
        Vector3 anchor = ComputeAnchor();
        followTarget = prev;

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
        Gizmos.DrawWireSphere(anchor, deadZoneRadius);
        Gizmos.DrawLine(target.position, anchor);
    }
}
