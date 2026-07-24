using UnityEngine;

/// <summary>
/// F-02.2 핸드 제스처 제어의 PC 개발용 대체 컨트롤러.
/// Meta 실기기가 없을 때 HandGestureController 대신 이 컴포넌트를 붙여
/// 마우스 드래그로 회전, 마우스 휠로 확대/축소를 테스트할 수 있다.
///
/// 나중에 실기기가 생기면 이 컴포넌트를 비활성화하고
/// HandGestureController를 대신 활성화하기만 하면 된다 (target 필드는 동일).
/// </summary>
public class DesktopFallbackController : MonoBehaviour
{
    [Header("대상")]
    public Transform target; // 회전/스케일 대상 (DNA, 단백질 루트) - HandGestureController와 동일 필드

    [Header("회전 설정")]
    // 1 = 오른쪽 버튼. MouseWorldSelector가 왼쪽 클릭(0)으로 원자를 선택하므로
    // 회전은 오른쪽 버튼으로 분리해 "드래그 시작 = 의도치 않은 선택"을 방지한다.
    public int rotateMouseButton = 1;
    public float rotationSpeed = 150f;

    [Header("스케일 설정")]
    public float minScale = 0.3f;
    public float maxScale = 3f;
    public float scrollZoomSpeed = 0.3f;

    private Vector3 _lastMousePos;
    private bool _isDragging;

    private void Update()
    {
        if (target == null) return;

        HandleRotate();
        HandleScrollZoom();
    }

    // 마우스 오른쪽 버튼(기본값)을 누른 채 드래그 -> 대상 회전 (HandGestureController의 한 손 회전과 동일한 역할)
    private void HandleRotate()
    {
        if (Input.GetMouseButtonDown(rotateMouseButton))
        {
            _isDragging = true;
            _lastMousePos = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(rotateMouseButton))
        {
            _isDragging = false;
        }

        if (!_isDragging) return;

        Vector3 delta = Input.mousePosition - _lastMousePos;
        float yaw = delta.x * rotationSpeed * Time.deltaTime * 0.01f;
        float pitch = -delta.y * rotationSpeed * Time.deltaTime * 0.01f;

        target.Rotate(Vector3.up, yaw, Space.World);
        target.Rotate(Vector3.right, pitch, Space.World);

        _lastMousePos = Input.mousePosition;
    }

    // 마우스 휠 -> 확대/축소 (HandGestureController의 양손 핀치 스케일과 동일한 역할)
    private void HandleScrollZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(scroll, 0f)) return;

        float currentScale = target.localScale.x;
        float newScale = Mathf.Clamp(currentScale + scroll * scrollZoomSpeed, minScale, maxScale);
        target.localScale = new Vector3(newScale, newScale, newScale);
    }
}
