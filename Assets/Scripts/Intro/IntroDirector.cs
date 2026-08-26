using System.Collections;
using UnityEngine;

/// <summary>
/// 게임을 켰을 때의 인트로 진행자.
///
///   페이드 인 → 비서 등장 → 인사 → 퀘스트 보드 펼치기 → 선택 대기
///   → 비서 확인 → 보드 접기 → 비서를 분자 옆자리로 이동 → QuestSession 시작
///
/// DOTween을 쓰지 않고 코루틴으로 짠 이유: 이 프로젝트의
/// <c>SceneTransitionManager.cs</c>는 DOTween 의존 때문에 파일 전체가 주석 처리된 상태다.
/// 인트로가 같은 이유로 막히면 안 되므로 외부 패키지 없이 돌아가게 했다.
///
/// PDF 설계서의 Level 0~5 카메라 트랜지션(인체 → 세포 → DNA → 단백질 → 원자 → HUD)은
/// 여기가 아니라 퀘스트 진행 쪽 몫이다. 이 스크립트는 "퀘스트를 고르기까지"만 책임진다.
/// </summary>
public class IntroDirector : MonoBehaviour
{
    [Header("연결")]
    public AIAssistantBrain assistant;
    public QuestSelectionBoard board;
    public QuestSession session;
    [Tooltip("구조 최상위(Ribbon)에서 '이전'을 한 번 더 누르면(OnExitRequested) 현재 사건을 접고 " +
             "퀘스트 선택으로 돌아온다. 비워두면 씬에서 자동 탐색")]
    public StructureLevelController levelController;
    [Tooltip("비워두면 Camera.main")]
    public Camera targetCamera;

    [Header("퀘스트 시작 후 비서가 머물 대상")]
    [Tooltip("보통 ProteinAnchor_Main. 지정하면 퀘스트 시작과 함께 비서가 분자 옆으로 옮겨간다.")]
    public Transform questAnchor;

    [Header("인트로 중 비서 위치")]
    [Tooltip("켜면 아래 뷰포트 좌표로 배치한다. 보드 크기가 화면 비율을 따르므로 " +
             "비서도 같은 기준으로 잡아야 화면비가 달라져도 둘이 겹치지 않는다.")]
    public bool placeAssistantByViewport = true;
    [Tooltip("화면 좌표 (0,0)=좌하단, (1,1)=우상단. 보드 오른쪽 바깥이 기본값이다.")]
    public Vector2 assistantViewportPosition = new Vector2(0.88f, 0.76f);
    [Tooltip("위 옵션을 끌 때 쓰는 고정 오프셋 (사용자 시야 기준, m). x=오른쪽 y=위 z=앞")]
    public Vector3 assistantIntroOffset = new Vector3(0.95f, 0.36f, 1.6f);

    [Header("보드 배치")]
    [Tooltip("카메라 앞 거리(m). 보드 크기는 화면 비율로 정해지므로 이 값이 크기를 바꾸지는 않는다.")]
    public float boardDistance = 1.6f;
    [Tooltip("눈높이 기준 위아래 오프셋(m). 음수면 아래.")]
    public float boardHeightOffset = 0f;
    [Tooltip("좌우 오프셋(m). 비서가 오른쪽에 서므로 보드는 살짝 왼쪽으로 민다.")]
    public float boardLateralOffset = -0.22f;

    [Header("무대 감추기")]
    [Tooltip("퀘스트를 고르기 전에는 분자와 테이블을 숨긴다. " +
             "끄면 인트로 배경에 단백질 구조가 그대로 보인다.")]
    public bool hideStageUntilQuestStarts = true;

    [Header("타이밍 (초)")]
    public float startDelay = 0.6f;
    [Tooltip("인사가 끝난 뒤 보드를 펼치기까지 기다리는 시간")]
    public float beatBeforeBoard = 0.35f;
    [Tooltip("비서의 인사가 끝나기를 기다리는 최대 시간. 넘으면 그냥 진행한다.")]
    public float maxWaitForGreeting = 12f;
    [Tooltip("고른 단백질 설명(대본 + LLM 응답)이 끝나기를 기다리는 최대 시간. 넘으면 그냥 진행한다.")]
    public float maxWaitForTargetIntro = 20f;
    [Tooltip("선택 확인 대사 뒤 퀘스트를 시작하기까지의 간격")]
    public float beatBeforeQuest = 1.2f;

    [Header("화면 페이드 (선택)")]
    [Tooltip("검은 화면에서 밝아지는 연출용. 비워두면 페이드를 건너뛴다.")]
    public CanvasGroup fadeOverlay;
    public float fadeDuration = 0.8f;

    [Header("자동 실행")]
    public bool playOnStart = true;

    public bool IsRunning { get; private set; }

    /// <summary>사용자가 고른 퀘스트. 아직 고르지 않았으면 null.</summary>
    private QuestDefinition _chosen;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        // 비서는 Play 버튼을 누르기 전(에디터 편집 모드)에는 보이지 않도록 씬에 비활성 상태로
        // 저장돼 있다. 비활성 오브젝트는 FindFirstObjectByType 기본 옵션에서 제외되므로 찾을 때도
        // 비활성까지 뒤진다.
        if (assistant == null) assistant = FindFirstObjectByType<AIAssistantBrain>(FindObjectsInactive.Include);
        if (board == null) board = FindFirstObjectByType<QuestSelectionBoard>();
        if (session == null) session = FindFirstObjectByType<QuestSession>();
        if (levelController == null) levelController = FindFirstObjectByType<StructureLevelController>(FindObjectsInactive.Include);

        // 게임이 실제로 시작(Play)된 시점이므로 비서를 켠다.
        if (assistant != null) assistant.gameObject.SetActive(true);

        // Start가 아니라 Awake에서 숨긴다. 모든 오브젝트의 Awake는 어떤 Start보다 먼저 돌기 때문에,
        // 여기서 꺼두면 ProteinLoader.Start가 아예 불리지 않아 인트로 동안 로딩이 시작되지 않는다.
        // Start에서 껐다면 실행 순서에 따라 이미 로딩이 시작된 뒤일 수 있다.
        if (hideStageUntilQuestStarts && playOnStart) HideStage();
    }

    private void OnEnable()
    {
        if (board != null) board.OnQuestSelected += HandleQuestSelected;
        if (levelController != null) levelController.OnExitRequested += HandleExitRequested;
    }

    private void OnDisable()
    {
        if (board != null) board.OnQuestSelected -= HandleQuestSelected;
        if (levelController != null) levelController.OnExitRequested -= HandleExitRequested;
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    /// <summary>인트로를 처음부터 재생한다.</summary>
    public void Play()
    {
        if (IsRunning) return;

        StopAllCoroutines();
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        IsRunning = true;
        _chosen = null;

        if (hideStageUntilQuestStarts) HideStage();
        PlaceAssistantForIntro();

        yield return FadeOverlay(1f, 0f);

        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        // 비서 인사 — 게임을 처음 켰을 때만 한다. 사건을 바꾸러 되돌아왔을 때는
        // ReturnToQuestSelectionRoutine이 대신 짧은 한마디만 하고 바로 보드로 넘어간다.
        if (assistant != null)
        {
            assistant.SpeakGreeting();
            yield return WaitForAssistant();
        }

        yield return SelectAndStartQuestRoutine();
    }

    /// <summary>
    /// 진행 중이던 구조에서 최상위(Ribbon)까지 나온 뒤 '이전'을 한 번 더 누르면
    /// (StructureLevelController.OnExitRequested) 여기로 들어온다. 지금 사건을 접고
    /// 퀘스트 선택 보드를 다시 펼친다 — 인사말은 다시 하지 않는다.
    /// </summary>
    public void ReturnToQuestSelection()
    {
        if (IsRunning) return; // 이미 인트로/선택 진행 중이면 중복 시작하지 않는다

        StopAllCoroutines();
        StartCoroutine(ReturnToQuestSelectionRoutine());
    }

    private void HandleExitRequested() => ReturnToQuestSelection();

    private IEnumerator ReturnToQuestSelectionRoutine()
    {
        IsRunning = true;
        _chosen = null;

        HideStage();
        PlaceAssistantForIntro();

        // 방금 나온 사건에서 아직 답이 오지 않은 질문이 있었다면 여기서 끊는다. 끊지 않으면
        // 뒤늦게 도착한 답이 "다른 사건을 골라볼까?" 뒤에 붙어 이전 사건 얘기를 계속하게 된다.
        if (assistant != null)
        {
            assistant.ResetConversation();
            assistant.SpeakNow("다른 사건을 골라볼까?");
        }

        yield return SelectAndStartQuestRoutine();
    }

    /// <summary>퀘스트 보드를 펼치고 선택을 기다린 뒤, 고른 퀘스트를 시작한다.
    /// 처음 플레이할 때(PlayRoutine)와 사건을 바꾸러 돌아왔을 때(ReturnToQuestSelectionRoutine)
    /// 둘 다 이 지점부터는 같은 절차라 공용으로 뺐다.</summary>
    private IEnumerator SelectAndStartQuestRoutine()
    {
        if (beatBeforeBoard > 0f) yield return new WaitForSeconds(beatBeforeBoard);

        // 퀘스트 보드 펼치기
        if (board != null)
        {
            PlaceBoard();
            board.Show();
        }
        else
        {
            Debug.LogError("[IntroDirector] QuestSelectionBoard가 없어 퀘스트를 고를 수 없습니다.", this);
            IsRunning = false;
            yield break;
        }

        // 선택 대기 — 타임아웃 없이 기다린다. 인트로는 사용자가 고를 때까지가 끝이다.
        while (_chosen == null) yield return null;

        // 확인하고 보드를 접는다. 고른 단백질이 어떤 아이인지는 비서가 설명한다 —
        // 대본 한마디로 즉시 반응하고, 백엔드가 있으면 LLM 설명을 이어 붙인다.
        board.Hide();
        if (assistant != null)
        {
            assistant.IntroduceQuestTarget(_chosen);

            // 설명이 끝나기를 기다렸다가 퀘스트를 시작한다. 기다리지 않으면 뒤늦게 도착한
            // LLM 설명이 이미 시작된 도입 시나리오 대사 사이에 끼어든다.
            yield return WaitForAssistantIdle(maxWaitForTargetIntro);
        }

        if (beatBeforeQuest > 0f) yield return new WaitForSeconds(beatBeforeQuest);

        // 비서를 분자 옆자리로 넘기고 퀘스트를 시작한다.
        // 무대를 다시 켜는 건 QuestSession.StartQuest가 맡는다 —
        // 켜는 순서가 구조 로딩보다 앞서야 해서 그쪽에 두는 편이 안전하다.
        MoveAssistantToQuestAnchor();

        if (session != null) session.StartQuest(_chosen);
        else Debug.LogError("[IntroDirector] QuestSession이 없어 퀘스트를 시작하지 못했습니다.", this);

        IsRunning = false;
    }

    /// <summary>분자와 테이블을 숨긴다. 퀘스트를 고르기 전에는 볼 이유가 없다.</summary>
    private void HideStage()
    {
        if (session != null) session.SetStageVisible(false);
    }

    private void HandleQuestSelected(QuestDefinition quest)
    {
        if (quest == null || _chosen != null) return;

        _chosen = quest;
    }

    // --- 배치 ---

    /// <summary>
    /// 인트로 동안에는 비서가 분자가 아니라 사용자 시야를 따라다니게 한다.
    /// anchorTarget이 남아 있으면 아직 아무것도 고르지 않았는데 분자 옆에 가 있게 된다.
    /// </summary>
    private void PlaceAssistantForIntro()
    {
        AIAssistantFollower follower = ResolveFollower();
        if (follower == null) return;

        follower.anchorTarget = null;
        follower.localOffset = ResolveAssistantOffset(follower);
        follower.SnapToAnchor(); // 원점에서 날아오는 연출 방지
    }

    /// <summary>
    /// 뷰포트 좌표를 <see cref="AIAssistantFollower.localOffset"/>이 쓰는 오프셋으로 환산한다.
    ///
    /// 팔로워는 yawOnly 기준축(수평 정면 + 월드 up)에서 오프셋을 더하므로,
    /// 카메라가 만든 월드 위치를 그 기준축으로 되돌려야 같은 자리에 선다.
    /// 미터로 직접 적으면 화면비가 바뀔 때마다 비서가 보드를 파고들거나 화면 밖으로 나간다.
    /// </summary>
    private Vector3 ResolveAssistantOffset(AIAssistantFollower follower)
    {
        if (!placeAssistantByViewport || targetCamera == null) return assistantIntroOffset;

        Vector3 world = targetCamera.ViewportToWorldPoint(new Vector3(
            assistantViewportPosition.x, assistantViewportPosition.y, boardDistance));

        Transform cam = targetCamera.transform;
        Vector3 delta = world - cam.position;

        Vector3 flatForward = cam.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f) return assistantIntroOffset;
        flatForward.Normalize();

        // yawOnly가 꺼져 있으면 팔로워가 카메라 로컬 공간을 그대로 쓰므로 기준축도 그쪽에 맞춘다.
        Quaternion basis = follower.yawOnly
            ? Quaternion.LookRotation(flatForward, Vector3.up)
            : cam.rotation;

        return Quaternion.Inverse(basis) * delta;
    }

    private void MoveAssistantToQuestAnchor()
    {
        if (questAnchor == null) return;

        AIAssistantFollower follower = ResolveFollower();
        if (follower == null) return;

        // 스냅하지 않는다. lazy-follow가 부드럽게 날아가는 편이 전환 연출로도 자연스럽다.
        follower.anchorTarget = questAnchor;
    }

    private AIAssistantFollower ResolveFollower()
    {
        if (assistant != null && assistant.follower != null) return assistant.follower;
        return FindFirstObjectByType<AIAssistantFollower>();
    }

    /// <summary>보드를 카메라 앞 고정 거리에 한 번만 놓는다. 이후 방향만 빌보드로 따라간다.</summary>
    private void PlaceBoard()
    {
        if (board == null || targetCamera == null) return;

        Transform cam = targetCamera.transform;

        // 고개를 위아래로 숙인 상태에서 시작해도 보드가 바닥이나 천장에 박히지 않도록
        // 수평 방향만 뽑아 쓴다.
        Vector3 forward = cam.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        board.transform.position = cam.position
                                 + forward * boardDistance
                                 + Vector3.up * boardHeightOffset
                                 + right * boardLateralOffset;
    }

    // --- 대기 / 페이드 ---

    /// <summary>
    /// 비서가 말을 마칠 때까지 기다린다. 상한을 두는 이유는 말풍선이 어떤 이유로든
    /// 바쁜 상태에서 빠져나오지 못하면 인트로가 영영 멈추기 때문이다.
    /// </summary>
    private IEnumerator WaitForAssistant()
    {
        float deadline = Time.time + Mathf.Max(maxWaitForGreeting, 0.1f);

        // 말풍선은 Say() 직후 한 프레임 뒤에야 IsBusy가 켜지므로 한 프레임 흘려보낸다.
        yield return null;

        while (assistant != null && assistant.IsBusy && Time.time < deadline)
            yield return null;
    }

    /// <summary>
    /// 비서가 말을 마치고, 보낸 요청의 답까지 다 받을 때까지 기다린다.
    ///
    /// <see cref="AIAssistantBrain.IsBusy"/>만 보면 LLM 응답을 기다리는 몇 초 동안
    /// 말풍선 큐가 비어 "끝났다"고 판단해 버린다. 상한을 두는 이유는 인사 대기와 같다 —
    /// 응답이 영영 오지 않아도 퀘스트는 시작돼야 한다.
    /// </summary>
    private IEnumerator WaitForAssistantIdle(float maxSeconds)
    {
        float deadline = Time.time + Mathf.Max(maxSeconds, 0.1f);

        yield return null;

        while (assistant != null && assistant.IsBusyOrWaiting && Time.time < deadline)
            yield return null;
    }

    private IEnumerator FadeOverlay(float from, float to)
    {
        if (fadeOverlay == null) yield break;

        fadeOverlay.alpha = from;
        fadeOverlay.blocksRaycasts = to > 0.5f;

        if (fadeDuration <= 0f)
        {
            fadeOverlay.alpha = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            fadeOverlay.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        fadeOverlay.alpha = to;
        fadeOverlay.blocksRaycasts = to > 0.5f;
    }
}
