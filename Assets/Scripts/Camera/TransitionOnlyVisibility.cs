using UnityEngine;

/// <summary>
/// 지정한 오브젝트들의 렌더러를 카메라 트랜지션이 진행되는 동안에만 켠다.
///
/// DnaHelixBackdrop 같은 배경 장식은 "미시 세계로 파고드는 중"이라는 인상을 주려고 넣은
/// 연출인데, 평소에도 계속 떠 있으면 오히려 창 밖에 정체 모를 나선이 상시 떠 있는 것처럼
/// 보인다. 오브젝트 자체를 껐다 켜지 않고 렌더러만 건드리는 이유: DnaHelixBackdrop은
/// Awake에서 메시를 한 번만 짓는데, 꺼둔 채로 시작하면 Awake가 아예 안 돌아 지어지지도
/// 않는다.
/// </summary>
public class TransitionOnlyVisibility : MonoBehaviour
{
    [Tooltip("비워두면 씬에서 찾는다.")]
    public CameraTransitionDirector director;
    [Tooltip("트랜지션 중에만 보일 렌더러들의 루트. 비워두면 이 오브젝트 자신.")]
    public GameObject[] targets;

    private void Awake()
    {
        if (director == null) director = FindFirstObjectByType<CameraTransitionDirector>();
        if (targets == null || targets.Length == 0) targets = new[] { gameObject };
    }

    private void OnEnable()
    {
        if (director != null)
        {
            director.OnTransitionStarted += HandleStarted;
            director.OnTransitionCompleted += HandleCompleted;
        }

        // 평소엔 숨겨두고, 이미 트랜지션 도중에 씬에 들어왔다면 그 상태를 존중한다.
        SetVisible(director != null && director.IsTransitioning);
    }

    private void OnDisable()
    {
        if (director == null) return;

        director.OnTransitionStarted -= HandleStarted;
        director.OnTransitionCompleted -= HandleCompleted;
    }

    private void HandleStarted(QuestLevel from, QuestLevel to, LevelTransition settings) => SetVisible(true);

    private void HandleCompleted(QuestLevel level) => SetVisible(false);

    private void SetVisible(bool visible)
    {
        if (targets == null) return;

        foreach (GameObject go in targets)
        {
            if (go == null) continue;

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(includeInactive: true))
                r.enabled = visible;
        }
    }
}
