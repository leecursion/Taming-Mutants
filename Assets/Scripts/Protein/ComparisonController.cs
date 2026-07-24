using UnityEngine;

/// <summary>
/// F-03.4 동기화 비교 관찰
/// Wild Type(정상)과 Mutant(변이) 단백질을 나란히 배치하고,
/// 마스터 오브젝트(사용자가 실제로 손으로 조작하는 쪽)의 회전/스케일 변화를
/// 슬레이브 오브젝트에 그대로 복제하여 항상 같은 각도로 보이게 한다.
/// </summary>
public class ComparisonController : MonoBehaviour
{
    [Header("동기화 대상")]
    [Tooltip("사용자가 직접 잡고 조작하는 쪽 (보통 Wild Type)")]
    public Transform master;
    [Tooltip("마스터를 따라가는 쪽 (보통 Mutant)")]
    public Transform slave;

    [Header("옵션")]
    [Tooltip("위치는 각자 고정하고 회전/스케일만 동기화할지 여부")]
    public bool syncRotation = true;
    public bool syncScale = true;
    public bool syncPosition = false;

    private Vector3 _slaveInitialLocalPos;

    private void Start()
    {
        if (slave != null) _slaveInitialLocalPos = slave.localPosition;
    }

    private void LateUpdate()
    {
        if (master == null || slave == null) return;

        if (syncRotation)
            slave.rotation = master.rotation;

        if (syncScale)
            slave.localScale = master.localScale;

        if (syncPosition)
            slave.position = master.position;
        else
            slave.localPosition = _slaveInitialLocalPos; // 원래 나란히 배치된 상대 위치 유지
    }
}
