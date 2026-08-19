using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F-04.1 시선 추적 기반 활성 부위 탐색, F-02.4 상황 맥락 브리핑의 PC 개발용 대체.
/// Meta 실기기(Eye Tracking)가 없을 때, 마우스 포인터 위치에서
/// 카메라 방향으로 레이캐스트를 쏴서 같은 원자/결합 포켓을 선택한다.
///
/// 나중에 실기기가 생기면 이 스크립트를 비활성화하고
/// Gaze Interactor(OVRPlugin eyeGaze 또는 Meta XR Interaction SDK의
/// GazeInteractor) 기반 셀렉터로 교체하되, 아래와 동일하게
/// MutationHighlighter.SelectResidue() / AtomInfo를 호출하도록 맞추면 된다.
/// </summary>
public class MouseWorldSelector : MonoBehaviour
{
    [Header("참조")]
    public Camera targetCamera;              // 비워두면 Camera.main 사용
    public MutationHighlighter mutationHighlighter;

    [Header("설정")]
    public LayerMask selectableLayers = ~0;  // 원자 오브젝트가 속한 레이어
    public float maxRayDistance = 50f;

    [Header("UI (선택된 원자 정보 표시용)")]
    public bool logToConsole = true;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TrySelectAtObjectUnderMouse();
        }
    }

    private void TrySelectAtObjectUnderMouse()
    {
        if (targetCamera == null || Mouse.current == null) return;

        Ray ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, selectableLayers)) return;

        AtomInfo atomInfo = hit.collider.GetComponent<AtomInfo>();
        if (atomInfo == null) return;

        if (logToConsole)
        {
            Debug.Log($"[MouseWorldSelector] 선택됨: {atomInfo.GetDisplayLabel()}");
        }

        if (mutationHighlighter != null)
        {
            mutationHighlighter.SelectResidue(atomInfo.ResidueId);
        }
    }
}
