using UnityEngine;

/// <summary>
/// 비서의 얼굴 연출 — 눈 깜빡임, 시선 추적, 상태별 눈 모양.
///
/// 역할 분담: 눈의 "색/발광"은 <see cref="AIAssistantVisual"/>이 MaterialPropertyBlock으로
/// 처리하고, 이 스크립트는 "모양"(스케일/위치/기울기)만 건드린다.
/// 둘이 같은 오브젝트를 만져도 충돌하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class AIAssistantFace : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("상태별 표정을 위해 참조. 비워두면 같은 오브젝트에서 찾는다.")]
    public AIAssistantVisual visual;
    [Tooltip("시선이 향할 대상. 비워두면 Camera.main")]
    public Transform lookTarget;
    [Tooltip("눈 트랜스폼. 부모(Face)의 스케일이 1이어야 계산이 어긋나지 않는다.")]
    public Transform eyeLeft;
    public Transform eyeRight;

    [Header("깜빡임")]
    [Tooltip("깜빡임 간격의 최소/최대(초). 매번 무작위로 뽑아 기계적으로 보이지 않게 한다.")]
    public Vector2 blinkIntervalRange = new Vector2(2.5f, 6f);
    public float blinkDuration = 0.12f;

    [Header("시선 이동")]
    [Tooltip("눈이 좌우/상하로 움직이는 최대 거리(m)")]
    public float eyeTravel = 0.006f;
    public float eyeFollowSpeed = 10f;
    [Tooltip("고개를 돌린 정도를 눈 이동으로 환산하는 감도")]
    public float gazeSensitivity = 2.5f;
    [Tooltip("몸이 항상 사용자를 향하므로 시선만으로는 눈이 거의 안 움직인다. 미세한 흔들림을 더해 살아 있게 만든다.")]
    [Range(0f, 1f)] public float idleGazeDrift = 0.3f;

    [Header("표정 반응 속도")]
    public float shapeSpeed = 8f;

    // 눈의 원래 위치/크기. 매 프레임 이 값을 기준으로 다시 계산하므로,
    // 플레이 중 스크립트를 고쳐 도메인이 리로드될 때 날아가면 눈이 원점으로 끌려가고
    // 스케일이 0이 되어 사라진다. [SerializeField]로 두면 리로드를 넘어 살아남는다.
    [SerializeField, HideInInspector] private Vector3 _eyeLeftBasePos, _eyeRightBasePos;
    [SerializeField, HideInInspector] private Vector3 _eyeLeftBaseScale, _eyeRightBaseScale;

    private float _blinkTimer;
    private bool _isBlinking;
    private float _blinkElapsed;

    private float _openAmount = 1f;   // 상태별 눈 크기 배율 (실눈/부릅뜸)
    private float _tiltAngle;         // 안쪽으로 기울여 화난 표정을 만드는 각도
    private Vector2 _gaze;            // -1..1 로 정규화된 시선 오프셋

    private void Awake()
    {
        if (visual == null) visual = GetComponent<AIAssistantVisual>();
        if (lookTarget == null && Camera.main != null) lookTarget = Camera.main.transform;

        if (eyeLeft != null)
        {
            _eyeLeftBasePos = eyeLeft.localPosition;
            _eyeLeftBaseScale = eyeLeft.localScale;
        }
        if (eyeRight != null)
        {
            _eyeRightBasePos = eyeRight.localPosition;
            _eyeRightBaseScale = eyeRight.localScale;
        }

        ScheduleNextBlink();
    }

    private void Update()
    {
        float k = 1f - Mathf.Exp(-shapeSpeed * Time.deltaTime);

        GetStateShape(out float targetOpen, out float targetTilt);
        _openAmount = Mathf.Lerp(_openAmount, targetOpen, k);
        _tiltAngle = Mathf.Lerp(_tiltAngle, targetTilt, k);

        UpdateBlink();
        UpdateGaze();
        ApplyToEyes();
    }

    private void GetStateShape(out float open, out float tilt)
    {
        AIAssistantState state = visual != null ? visual.CurrentState : AIAssistantState.Idle;
        switch (state)
        {
            case AIAssistantState.Listening: open = 1.15f; tilt = 0f; break;
            case AIAssistantState.Thinking:  open = 0.55f; tilt = 0f; break;   // 실눈 뜨고 고민
            case AIAssistantState.Speaking:  open = 1.05f; tilt = 0f; break;
            case AIAssistantState.Alert:     open = 1.25f; tilt = 16f; break;  // 부릅뜬 채 안쪽으로 기울임
            default:                         open = 1f;    tilt = 0f; break;
        }
    }

    private void UpdateBlink()
    {
        if (_isBlinking)
        {
            _blinkElapsed += Time.deltaTime;
            if (_blinkElapsed >= blinkDuration)
            {
                _isBlinking = false;
                ScheduleNextBlink();
            }
            return;
        }

        _blinkTimer -= Time.deltaTime;
        if (_blinkTimer <= 0f)
        {
            _isBlinking = true;
            _blinkElapsed = 0f;
        }
    }

    private void ScheduleNextBlink()
    {
        _blinkTimer = Random.Range(blinkIntervalRange.x, blinkIntervalRange.y);
    }

    /// <summary>1 = 완전히 뜬 상태, 0 = 완전히 감은 상태. 감았다 뜨는 동작을 사인 한 주기로 만든다.</summary>
    private float CurrentOpenness()
    {
        if (!_isBlinking || blinkDuration <= 0f) return 1f;
        float p = Mathf.Clamp01(_blinkElapsed / blinkDuration);
        return 1f - Mathf.Sin(p * Mathf.PI);
    }

    private void UpdateGaze()
    {
        Vector2 desired = Vector2.zero;

        AIAssistantState state = visual != null ? visual.CurrentState : AIAssistantState.Idle;
        if (state == AIAssistantState.Thinking)
        {
            // 고민 중에는 사용자를 보지 않고 허공을 천천히 훑는다.
            float t = Time.time;
            desired = new Vector2(Mathf.Sin(t * 0.9f) * 0.8f, 0.5f + Mathf.Sin(t * 0.5f) * 0.3f);
        }
        else if (lookTarget != null)
        {
            // 대상 방향을 얼굴 로컬 좌표로 옮겨서, 정면에서 벗어난 만큼만 눈을 굴린다.
            Vector3 local = transform.InverseTransformPoint(lookTarget.position);
            if (local.z > 0.01f)
            {
                desired = new Vector2(local.x / local.z, local.y / local.z) * gazeSensitivity;
            }
            desired += IdleDrift();
        }

        desired = Vector2.ClampMagnitude(desired, 1f);
        float k = 1f - Mathf.Exp(-eyeFollowSpeed * Time.deltaTime);
        _gaze = Vector2.Lerp(_gaze, desired, k);
    }

    // 주기가 서로 안 맞는 사인 두 개를 겹쳐서 규칙성이 눈에 띄지 않는 미세한 흔들림을 만든다.
    private Vector2 IdleDrift()
    {
        if (idleGazeDrift <= 0f) return Vector2.zero;

        float t = Time.time;
        float x = Mathf.Sin(t * 0.37f) * 0.6f + Mathf.Sin(t * 1.13f) * 0.4f;
        float y = Mathf.Sin(t * 0.29f + 2.1f) * 0.5f + Mathf.Sin(t * 0.83f) * 0.3f;
        return new Vector2(x, y) * idleGazeDrift;
    }

    private void ApplyToEyes()
    {
        float openness = CurrentOpenness() * _openAmount;
        Vector3 offset = new Vector3(_gaze.x, _gaze.y, 0f) * eyeTravel;

        ApplyEye(eyeLeft, _eyeLeftBasePos, _eyeLeftBaseScale, offset, openness, _tiltAngle);
        ApplyEye(eyeRight, _eyeRightBasePos, _eyeRightBaseScale, offset, openness, -_tiltAngle);
    }

    private static void ApplyEye(Transform eye, Vector3 basePos, Vector3 baseScale,
                                 Vector3 offset, float openness, float tilt)
    {
        if (eye == null) return;

        eye.localPosition = basePos + offset;
        // 세로만 눌러서 감기게 한다. 0까지 내리면 메시가 완전히 뒤집혀 보여서 하한을 둔다.
        eye.localScale = new Vector3(baseScale.x, baseScale.y * Mathf.Max(openness, 0.05f), baseScale.z);
        eye.localRotation = Quaternion.Euler(0f, 0f, tilt);
    }
}
