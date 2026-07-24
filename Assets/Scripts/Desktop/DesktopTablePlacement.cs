using System;
using UnityEngine;

/// <summary>
/// F-01.1 공간 앵커링의 PC 개발용 대체.
/// 실기기의 ExperimentTableAnchor(Spatial Anchor 저장/로드)와 달리,
/// 실제 물리 공간이 없는 Desktop 환경에서는 인스펙터에서 지정한
/// 고정 좌표(placedPosition/placedRotation)에 테이블을 배치하는 것으로 충분하다.
///
/// 나중에 실기기가 생기면 이 컴포넌트를 비활성화하고
/// ExperimentTableAnchor를 대신 활성화하기만 하면 된다
/// (OnPlacementConfirmed 이벤트를 구독하는 쪽 코드는 수정할 필요 없음).
/// </summary>
public class DesktopTablePlacement : MonoBehaviour
{
    [Header("고정 배치 좌표 (실 공간이 없으므로 임의 지정)")]
    public Vector3 placedPosition = new Vector3(0f, 0.8f, 1.2f); // 카메라 기준 약 1.2m 앞, 책상 높이
    public Vector3 placedEulerRotation = Vector3.zero;

    public event Action OnPlacementConfirmed;

    private void Start()
    {
        // Desktop 모드에서는 별도의 "배치 확정" 상호작용 없이 시작 시 바로 고정 위치에 놓는다.
        PlaceAndConfirm();
    }

    public void PlaceAndConfirm()
    {
        transform.position = placedPosition;
        transform.eulerAngles = placedEulerRotation;
        OnPlacementConfirmed?.Invoke();
        Debug.Log("[DesktopTablePlacement] 테이블을 고정 좌표에 배치했습니다 (PC 모드).");
    }
}
