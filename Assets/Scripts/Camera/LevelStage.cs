using System.Collections.Generic;
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

    /// <summary>
    /// DisableRenderers 모드의 캐시/가시성 상태. contentRoot 기준으로 공유한다.
    ///
    /// Level2~4처럼 여러 LevelStage가 같은 contentRoot(ProteinAnchor_Main)를 가리킬 때,
    /// 각자 자기 IsActive만 보고 렌더러를 켜고 끄면 문제가 생긴다: 구조가 새로 로드되어
    /// 자식 수가 바뀌면 셋 다 같은 프레임에 LateUpdate가 돌면서 각자 캐시를 다시 만드는데,
    /// 실행 순서가 보장되지 않아 "지금 활성인 레벨"이 방금 켠 렌더러를 "비활성인 형제 레벨"이
    /// 뒤이어 도로 꺼버릴 수 있다 — Ribbon/Helix/원자가 로딩 직후 안 보이는 원인이 이것이다.
    /// contentRoot당 하나의 상태를 공유해, "이 contentRoot를 쓰는 레벨이 하나라도 활성인가"로
    /// 판단하면 형제끼리 서로 덮어쓰는 일이 없다.
    /// </summary>
    private class SharedContentState
    {
        public readonly HashSet<LevelStage> activeStages = new HashSet<LevelStage>();
        public Renderer[] cachedRenderers;
        public int lastChildCount = -1;
    }

    private static readonly Dictionary<GameObject, SharedContentState> _sharedStates =
        new Dictionary<GameObject, SharedContentState>();

    // "Enter Play Mode Options"로 도메인 리로드를 끈 경우에도 이전 플레이의 캐시가
    // 다음 플레이로 새지 않도록 플레이 시작마다 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSharedStates() => _sharedStates.Clear();

    /// <summary>
    /// 무대를 켜고 끈다. <paramref name="invokeEvents"/>를 끄면 훅 없이 상태만 맞춘다
    /// (시작할 때 모든 레벨을 한 번에 정리하는 용도).
    /// </summary>
    public void SetActive(bool active, bool invokeEvents = true)
    {
        bool changed = IsActive != active;
        IsActive = active;

        ApplyVisibility();

        if (!changed || !invokeEvents) return;

        if (active) onEnter?.Invoke();
        else onExit?.Invoke();
    }

    private SharedContentState GetSharedState()
    {
        if (!_sharedStates.TryGetValue(contentRoot, out SharedContentState state))
        {
            state = new SharedContentState();
            _sharedStates[contentRoot] = state;
        }

        return state;
    }

    /// <summary>
    /// contentRoot 아래에서 렌더러 구성이 바뀌었을 때(원자 재로딩, 리본/Helix 재생성 등)
    /// 직접 불러서 캐시를 버린다.
    ///
    /// LateUpdate의 직계 자식 수 감시만으로는 놓치는 경우가 있다 — 리본/Helix 세그먼트는
    /// contentRoot의 손자로 붙고, Destroy()는 프레임 끝에야 실제로 반영되는 등 타이밍이
    /// 얽혀 있다. 구조를 실제로 다시 만드는 쪽(StructureLevelController, ProteinLoader)이
    /// "지금 바뀌었다"고 직접 알려주면 그런 타이밍 문제와 무관하게 다음 갱신에서 확실히
    /// 다시 잡힌다.
    /// </summary>
    public static void InvalidateSharedContent(GameObject contentRoot)
    {
        if (contentRoot == null) return;
        if (_sharedStates.TryGetValue(contentRoot, out SharedContentState state))
            state.cachedRenderers = null;
    }

    private void ApplyVisibility()
    {
        if (contentRoot == null) return;

        if (hideMode == HideMode.Deactivate)
        {
            if (contentRoot.activeSelf != IsActive) contentRoot.SetActive(IsActive);
            return;
        }

        SharedContentState state = GetSharedState();
        if (IsActive) state.activeStages.Add(this);
        else state.activeStages.Remove(this);

        // 같은 contentRoot를 쓰는 레벨이 하나라도 활성이면 보인다.
        bool visible = state.activeStages.Count > 0;

        // 원자 수천 개를 매 호출마다 훑지 않도록 캐시한다.
        // 캐시를 버리는 판단은 LateUpdate 한 곳에서만 한다.
        if (state.cachedRenderers == null)
            state.cachedRenderers = contentRoot.GetComponentsInChildren<Renderer>(includeInactive: true);

        foreach (Renderer r in state.cachedRenderers)
            if (r != null) r.enabled = visible;
    }

    private void LateUpdate()
    {
        if (contentRoot == null || hideMode != HideMode.DisableRenderers) return;

        SharedContentState state = GetSharedState();

        // 로딩이 끝나 원자가 새로 생기면, 숨긴 상태였더라도 새 렌더러는 켜진 채로 태어난다.
        // 자식 수가 달라진 프레임에 한 번만 다시 적용해 새 오브젝트도 같은 상태로 맞춘다.
        // contentRoot를 공유하는 레벨이 여럿이면 이 검사도 상태를 공유하므로, 한 프레임에
        // 여러 LevelStage의 LateUpdate가 돌아도 실제 재적용은 한 번만 일어난다.
        //
        // 캐시가 이미 비어 있으면(InvalidateSharedContent로 방금 버려졌으면) 자식 수가
        // 그대로여도 다시 채워 넣는다 — 손자로 붙는 리본/Helix 세그먼트는 자식 수 변화만으론
        // 못 잡을 수 있어서, 호출 쪽이 직접 무효화한 신호를 여기서 놓치면 안 된다.
        int count = contentRoot.transform.childCount;
        if (count == state.lastChildCount && state.cachedRenderers != null) return;

        state.lastChildCount = count;
        state.cachedRenderers = null;
        ApplyVisibility();
    }
}
