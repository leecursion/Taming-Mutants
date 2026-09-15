using UnityEngine;

/// <summary>
/// 비서 주변 궤도 링을 계속 회전시킨다. 상태가 Thinking이면 빨라져서
/// "연산 중"이라는 게 색뿐 아니라 움직임으로도 읽힌다.
/// </summary>
public class AIAssistantSpin : MonoBehaviour
{
    [Header("회전")]
    public Vector3 localAxis = Vector3.up;
    public float degreesPerSecond = 40f;

    [Header("상태 연동 (선택)")]
    [Tooltip("비워두면 부모에서 찾는다. 못 찾으면 항상 기본 속도로 돈다.")]
    public AIAssistantVisual visual;
    public float idleMultiplier = 1f;
    public float listeningMultiplier = 1.6f;
    public float thinkingMultiplier = 4f;
    public float speakingMultiplier = 2f;
    public float alertMultiplier = 3f;
    [Tooltip("속도가 목표치로 바뀌는 속도")]
    public float responseSpeed = 3f;

    private float _multiplier = 1f;

    private void Awake()
    {
        if (visual == null) visual = GetComponentInParent<AIAssistantVisual>();
        _multiplier = TargetMultiplier();
    }

    private void Update()
    {
        float k = 1f - Mathf.Exp(-responseSpeed * Time.deltaTime);
        _multiplier = Mathf.Lerp(_multiplier, TargetMultiplier(), k);

        if (localAxis.sqrMagnitude < 1e-6f) return;
        transform.Rotate(localAxis.normalized, degreesPerSecond * _multiplier * Time.deltaTime, Space.Self);
    }

    private float TargetMultiplier()
    {
        if (visual == null) return idleMultiplier;

        switch (visual.CurrentState)
        {
            case AIAssistantState.Listening: return listeningMultiplier;
            case AIAssistantState.Thinking:  return thinkingMultiplier;
            case AIAssistantState.Speaking:  return speakingMultiplier;
            case AIAssistantState.Alert:     return alertMultiplier;
            default:                         return idleMultiplier;
        }
    }
}
