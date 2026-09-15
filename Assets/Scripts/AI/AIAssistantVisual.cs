using System;
using UnityEngine;

/// <summary>비서의 현재 상태. 나중에 AICoScientistClient의 요청/응답 흐름과 1:1로 연결한다.</summary>
public enum AIAssistantState
{
    Idle,      // 대기 — 느리고 옅은 맥동
    Listening, // 사용자 입력 대기
    Thinking,  // 백엔드 요청 중 — 빠른 맥동
    Speaking,  // 응답 출력 중
    Alert      // 오류 / 주의 환기
}

/// <summary>
/// 비서 오브젝트의 상태를 색과 맥동(발광)으로 표현한다.
/// PLDDTColorizer / PulseHighlight와 동일하게 머티리얼을 복제하지 않고
/// MaterialPropertyBlock으로 색을 덮어써서 인스턴스가 늘어나지 않게 한다.
///
/// URP Lit(_BaseColor)과 Custom/Hologram(_HologramColor)을 섞어 쓸 수 있도록
/// 색상 프로퍼티 이름을 배열로 받는다. 셰이더에 없는 이름은 무시되므로 둘 다 적어두면 된다.
/// </summary>
public class AIAssistantVisual : MonoBehaviour
{
    [Serializable]
    public class StateStyle
    {
        public AIAssistantState state;
        public Color color = new Color(0.2f, 0.8f, 1f);
        [Tooltip("초당 맥동 횟수")] public float pulseSpeed = 0.6f;
        [Tooltip("맥동으로 어두워지는 정도 (0 = 맥동 없음)")]
        [Range(0f, 1f)] public float pulseDepth = 0.25f;
        public float emissionIntensity = 1.2f;
    }

    [Header("대상 렌더러 (비워두면 자식 전체를 자동 수집)")]
    public Renderer[] targetRenderers;

    [Header("셰이더 프로퍼티 이름")]
    [Tooltip("URP Lit은 _BaseColor, Custom/Hologram은 _HologramColor. 없는 이름은 무시된다.")]
    public string[] colorProperties = { "_BaseColor", "_HologramColor" };
    [Tooltip("비워두면 발광 색을 쓰지 않는다.")]
    public string emissionProperty = "_EmissionColor";

    [Header("보조 조명 (선택)")]
    public Light glowLight;
    public float lightIntensityScale = 1.5f;

    [Header("상태별 스타일")]
    public StateStyle[] styles =
    {
        new StateStyle { state = AIAssistantState.Idle,      color = new Color(0.20f, 0.80f, 1.00f), pulseSpeed = 0.5f, pulseDepth = 0.20f, emissionIntensity = 1.0f },
        new StateStyle { state = AIAssistantState.Listening, color = new Color(0.35f, 1.00f, 0.75f), pulseSpeed = 1.0f, pulseDepth = 0.30f, emissionIntensity = 1.4f },
        new StateStyle { state = AIAssistantState.Thinking,  color = new Color(0.75f, 0.55f, 1.00f), pulseSpeed = 2.2f, pulseDepth = 0.45f, emissionIntensity = 1.8f },
        new StateStyle { state = AIAssistantState.Speaking,  color = new Color(1.00f, 0.85f, 0.35f), pulseSpeed = 1.4f, pulseDepth = 0.30f, emissionIntensity = 1.6f },
        new StateStyle { state = AIAssistantState.Alert,     color = new Color(1.00f, 0.30f, 0.25f), pulseSpeed = 3.0f, pulseDepth = 0.55f, emissionIntensity = 2.2f },
    };

    [Header("상태 전환")]
    [Tooltip("상태가 바뀔 때 색이 넘어가는 속도")]
    public float transitionSpeed = 4f;

    public AIAssistantState CurrentState { get; private set; } = AIAssistantState.Idle;

    /// <summary>맥동을 뺀 현재 상태 색. 말풍선 강조색 등 다른 UI를 같은 색으로 맞출 때 쓴다.</summary>
    public Color CurrentColor => _currentColor;

    public event Action<AIAssistantState> OnStateChanged;

    private MaterialPropertyBlock _mpb;
    private StateStyle _targetStyle;
    private Color _currentColor;
    private float _currentPulseSpeed;
    private float _currentPulseDepth;
    private float _currentEmission;
    private float _phase; // 맥동 위상. 속도가 바뀔 때 색이 튀지 않도록 시간이 아니라 위상을 누적한다.

    private void Awake()
    {
        InitializeRuntimeState();
    }

    private void InitializeRuntimeState()
    {
        _mpb = new MaterialPropertyBlock();

        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>(includeInactive: true);

        _targetStyle = FindStyle(CurrentState);
        _currentColor = _targetStyle.color;
        _currentPulseSpeed = _targetStyle.pulseSpeed;
        _currentPulseDepth = _targetStyle.pulseDepth;
        _currentEmission = _targetStyle.emissionIntensity;
    }

    private void Update()
    {
        // 플레이 중 스크립트를 고치면 도메인이 리로드되면서 직렬화되지 않는 이 필드들만
        // 날아가고 Awake는 다시 불리지 않는다. 그대로 두면 매 프레임 예외가 터진다.
        if (_mpb == null || _targetStyle == null) InitializeRuntimeState();

        float k = 1f - Mathf.Exp(-transitionSpeed * Time.deltaTime);
        _currentColor = Color.Lerp(_currentColor, _targetStyle.color, k);
        _currentPulseSpeed = Mathf.Lerp(_currentPulseSpeed, _targetStyle.pulseSpeed, k);
        _currentPulseDepth = Mathf.Lerp(_currentPulseDepth, _targetStyle.pulseDepth, k);
        _currentEmission = Mathf.Lerp(_currentEmission, _targetStyle.emissionIntensity, k);

        _phase += Time.deltaTime * _currentPulseSpeed * Mathf.PI * 2f;
        float pulse = 1f - _currentPulseDepth * (1f - (Mathf.Sin(_phase) + 1f) * 0.5f);

        Apply(_currentColor * pulse);
    }

    private void Apply(Color color)
    {
        foreach (var renderer in targetRenderers)
        {
            if (renderer == null) continue;

            renderer.GetPropertyBlock(_mpb);
            foreach (var property in colorProperties)
            {
                if (string.IsNullOrEmpty(property)) continue;
                _mpb.SetColor(property, color);
            }
            if (!string.IsNullOrEmpty(emissionProperty))
                _mpb.SetColor(emissionProperty, color * _currentEmission);

            renderer.SetPropertyBlock(_mpb);
        }

        if (glowLight != null)
        {
            glowLight.color = color;
            glowLight.intensity = color.maxColorComponent * _currentEmission * lightIntensityScale;
        }
    }

    /// <summary>
    /// 상태 전환. AI 연동 단계에서는 요청 시작 시 Thinking, 응답 수신 시 Speaking,
    /// 오류 시 Alert로 호출하면 된다.
    /// </summary>
    public void SetState(AIAssistantState state)
    {
        if (state == CurrentState) return;

        CurrentState = state;
        _targetStyle = FindStyle(state);
        OnStateChanged?.Invoke(state);
    }

    private StateStyle FindStyle(AIAssistantState state)
    {
        if (styles != null)
        {
            foreach (var style in styles)
                if (style != null && style.state == state) return style;
        }
        return new StateStyle { state = state }; // 인스펙터에서 스타일을 지웠을 때의 안전한 기본값
    }
}
