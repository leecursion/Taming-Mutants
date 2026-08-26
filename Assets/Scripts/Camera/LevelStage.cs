using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 설계서(KRAS G12C MR Quest.pdf)의 시점 레벨.
/// Level 0(인체)에서 Level 5(연구실 HUD)까지 축척이 단계적으로 바뀐다.
/// </summary>
public enum QuestLevel
{
    Level0_Body = 0,       // 실물 크기 홀로그램 인체, 폐 종양 점멸
    Level1_DNA = 1,        // DNA 이중나선, 12번 코돈
    Level2_Protein = 2,    // 접힘이 끝난 단백질 리본 + WT/변이형 비교
    Level3_Pocket = 3,     // Switch-II Pocket 내부, Cys12 황 원자
    Level4_Docking = 4,    // 후보물질 도킹, 공유결합 섬광
    Level5_Dashboard = 5,  // 연구실 벽면 커브드 HUD
}

/// <summary>
/// 레벨 하나의 무대. "카메라가 어디에 서고, 그때 무엇이 보이는가"를 들고 있다.
///
/// <b>레벨마다 무대를 따로 두는 이유</b>는 정밀도 때문이다.
/// 설계서는 인체(약 1m)에서 원자(약 10^-10 m)까지 연속으로 줌인한다고 적혀 있는데,
/// 이걸 카메라 한 대의 실제 이동으로 표현하면 좌표가 10자릿수 넘게 벌어진다.
/// float32의 유효자릿수는 약 7자리라서, 그 범위에 들어가는 순간 위치가 계단처럼 튀고
/// 회전도 떨린다(Z-fighting, jitter).
///
/// 그래서 각 레벨을 <b>보기 편한 크기로 따로 만들어 서로 떨어진 자리에 놓고</b>,
/// 카메라가 그 사이를 날아가게 한다. 축척 변화는 실제 거리가 아니라 연출
/// (파티클 워프, 페이드, FOV 변화)이 만든다 — 관객이 느끼는 결과는 같으면서
/// 좌표는 항상 다루기 좋은 범위에 머문다.
/// </summary>
public class LevelStage : MonoBehaviour
{
    /// <summary>레벨 내용을 감추는 방식.</summary>
    public enum HideMode
    {
        /// <summary>오브젝트째 끈다. 가장 가볍지만 그 안의 코루틴도 함께 멈춘다.</summary>
        Deactivate,

        /// <summary>렌더러만 끈다. 뒤에서 로딩이 계속 돌아야 하는 무대에 쓴다.</summary>
        DisableRenderers,
    }

    [Header("정체")]
    public QuestLevel level = QuestLevel.Level0_Body;

    [Header("카메라가 설 자리")]
    [Tooltip("이 레벨에 도착했을 때 카메라의 위치와 회전. 비워두면 이 오브젝트 자신을 쓴다.")]
    public Transform cameraAnchor;
    [Tooltip("도착 시 시야각. 0이면 카메라의 현재 값을 유지한다.")]
    public float fieldOfView = 60f;

    [Header("이 레벨에서 보이는 것")]
    [Tooltip("레벨에 들어설 때 켜고 나갈 때 끄는 루트. 비워두면 아무것도 건드리지 않는다.")]
    public GameObject contentRoot;
    [Tooltip("ProteinLoader처럼 뒤에서 코루틴이 돌아야 하는 무대는 DisableRenderers를 쓴다. " +
             "오브젝트를 꺼버리면 로딩이 중간에 멈춘다.")]
    public HideMode hideMode = HideMode.Deactivate;

    [Header("훅")]
    [Tooltip("도착 직후. 파티클 재생, 히트맵 켜기 등 레벨별 연출을 여기에 건다.")]
    public UnityEvent onEnter;
    [Tooltip("이 레벨을 떠날 때")]
    public UnityEvent onExit;

    public bool IsActive { get; private set; }

    /// <summary>카메라가 도착해야 할 포즈. cameraAnchor가 없으면 자기 자신.</summary>
    public Transform Anchor => cameraAnchor != null ? cameraAnchor : transform;

    private Renderer[] _cachedRenderers;
    private int _lastChildCount = -1;

    /// <summary>
    /// 무대를 켜고 끈다. <paramref name="invokeEvents"/>를 끄면 훅 없이 상태만 맞춘다
    /// (시작할 때 모든 레벨을 한 번에 정리하는 용도).
    /// </summary>
    public void SetActive(bool active, bool invokeEvents = true)
    {
        bool changed = IsActive != active;
        IsActive = active;

        ApplyVisibility(active);

        if (!changed || !invokeEvents) return;

        if (active) onEnter?.Invoke();
        else onExit?.Invoke();
    }

    private void ApplyVisibility(bool visible)
    {
        if (contentRoot == null) return;

        if (hideMode == HideMode.Deactivate)
        {
            if (contentRoot.activeSelf != visible) contentRoot.SetActive(visible);
            return;
        }

        // 원자 수천 개를 매 호출마다 훑지 않도록 캐시한다.
        // 캐시를 버리는 판단은 LateUpdate 한 곳에서만 한다.
        if (_cachedRenderers == null)
            _cachedRenderers = contentRoot.GetComponentsInChildren<Renderer>(includeInactive: true);

        foreach (Renderer r in _cachedRenderers)
            if (r != null) r.enabled = visible;
    }

    private void LateUpdate()
    {
        if (contentRoot == null || hideMode != HideMode.DisableRenderers) return;

        // 로딩이 끝나 원자가 새로 생기면, 숨긴 상태였더라도 새 렌더러는 켜진 채로 태어난다.
        // 자식 수가 달라진 프레임에 한 번만 다시 적용해 새 오브젝트도 같은 상태로 맞춘다.
        int count = contentRoot.transform.childCount;
        if (count == _lastChildCount) return;

        _lastChildCount = count;
        _cachedRenderers = null;
        ApplyVisibility(IsActive);
    }
}
