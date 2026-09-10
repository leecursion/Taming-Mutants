using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

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

    // 사실 앵커링용 기록처. 클릭마다 FindFirstObjectByType이 돌지 않게 Awake에서 한 번만 잡는다.
    private StructureFactProvider _factProvider;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        // 하이라이터는 씬에 저장돼 있지 않다 — 참조가 비어 있으면 변이 잔기를 클릭해도
        // 아무 반응이 없다. QuestSession/AIAssistantBrain과 같은 EnsureFor로 확보한다.
        if (mutationHighlighter == null)
            mutationHighlighter = MutationHighlighter.EnsureFor(
                FindFirstObjectByType<ProteinLoader>(FindObjectsInactive.Include));

        _factProvider = StructureFactProvider.Ensure();
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

        // UI 위를 클릭한 것이라면 뒤에 있는 구조까지 함께 고르지 않는다.
        //
        // 구술 확인 패널의 입력창을 클릭할 때 그 뒤에 변이 잔기가 있으면 선택 이벤트가
        // 함께 발생해, 답을 쓰는 도중 비서가 그 잔기 설명을 시작한다. 버튼을 누를 때마다
        // 같은 일이 일어나므로 퀴즈 밖에서도 마찬가지다.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Ray ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, selectableLayers)) return;

        AtomInfo atomInfo = hit.collider.GetComponent<AtomInfo>();
        if (atomInfo == null) return;

        if (logToConsole)
        {
            Debug.Log($"[MouseWorldSelector] 선택됨: {atomInfo.GetDisplayLabel()}");
        }

        // 사실 앵커링용 — 지금 무엇을 보고 있는지 LLM 컨텍스트에 넣기 위해 기록한다.
        //
        // 아래 SelectResidue보다 먼저, 그리고 그것과 무관하게 부른다. SelectResidue는
        // mutationSites에 등록된 변이 잔기가 아니면 조용히 반환하므로, 그 뒤에 기록하면
        // 일반 잔기를 클릭했을 때 아무것도 남지 않는다. 사실 앵커링은 변이 잔기가 아닌
        // 곳도 다뤄야 한다 — 학습자는 아무 원자나 찍고 "이건 뭐예요?"라고 묻는다.
        if (_factProvider != null) _factProvider.RecordSelection(atomInfo);

        if (mutationHighlighter != null)
        {
            mutationHighlighter.SelectResidue(atomInfo.ResidueId);
        }
    }
}
