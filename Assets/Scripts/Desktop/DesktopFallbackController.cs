using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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

    private Vector2 _lastMousePos;
    private bool _isDragging;

    private void Awake()
    {
        // target이 비어있거나 씬 인스턴스가 아닌 프리팹 "애셋"의 Transform이 연결된 경우
        // (씬에 보이는 단백질은 회전하지 않고 애셋 원본 값만 오염됨)
        // 씬 안에 로드된 ProteinLoader의 Transform으로 자동 교체한다.
        if (target == null || !target.gameObject.scene.IsValid())
        {
            var loader = FindFirstObjectByType<ProteinLoader>();
            if (loader != null)
            {
                target = loader.transform;
                Debug.LogWarning("[DesktopFallbackController] target이 씬 오브젝트가 아니어서 ProteinLoader 인스턴스로 자동 재연결했습니다.");
            }
        }
    }

    private void Update()
    {
        if (target == null || Mouse.current == null) return;

        HandleRotate();
        HandleScrollZoom();
    }

    // 마우스 오른쪽 버튼(기본값)을 누른 채 드래그 -> 대상 회전 (HandGestureController의 한 손 회전과 동일한 역할)
    private void HandleRotate()
    {
        ButtonControl button = GetRotateButton();
        if (button == null) return;

        if (button.wasPressedThisFrame)
        {
            _isDragging = true;
            _lastMousePos = Mouse.current.position.ReadValue();
        }
        else if (button.wasReleasedThisFrame)
        {
            _isDragging = false;
        }

        if (!_isDragging) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector2 delta = mousePos - _lastMousePos;
        float yaw = delta.x * rotationSpeed * Time.deltaTime * 0.01f;
        float pitch = -delta.y * rotationSpeed * Time.deltaTime * 0.01f;

        target.Rotate(Vector3.up, yaw, Space.World);
        target.Rotate(Vector3.right, pitch, Space.World);

        _lastMousePos = mousePos;
    }

    // rotateMouseButton(0=왼쪽, 1=오른쪽, 2=휠클릭)을 New Input System 버튼으로 매핑
    private ButtonControl GetRotateButton()
    {
        switch (rotateMouseButton)
        {
            case 0: return Mouse.current.leftButton;
            case 2: return Mouse.current.middleButton;
            default: return Mouse.current.rightButton;
        }
    }

    // 마우스 휠 -> 확대/축소 (HandGestureController의 양손 핀치 스케일과 동일한 역할)
    private void HandleScrollZoom()
    {
        // Windows에서 한 노치(클릭) = 120 단위이므로 legacy Input.GetAxis와 비슷한 크기로 정규화
        float scroll = Mouse.current.scroll.ReadValue().y / 120f;
        if (Mathf.Approximately(scroll, 0f)) return;

        float currentScale = target.localScale.x;
        float newScale = Mathf.Clamp(currentScale + scroll * scrollZoomSpeed, minScale, maxScale);
        target.localScale = new Vector3(newScale, newScale, newScale);
    }
}
