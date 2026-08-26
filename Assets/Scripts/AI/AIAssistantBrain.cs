using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F-06 AI Co-Scientist의 "역할" 계층 — 비서가 언제 무엇을 말할지 정한다.
///
/// 아래 세 조각을 묶는 자리다. 각자는 서로를 모른다.
///   <see cref="AIAssistantVisual"/>        : 상태를 색과 맥동으로 표현 (어떻게 보이는가)
///   <see cref="AIAssistantSpeechBubble"/>  : 문장을 말풍선으로 출력 (어떻게 말하는가)
///   <see cref="AICoScientistClient"/>      : 백엔드와 통신 (무엇을 물어보는가)
///   <see cref="QuestSession"/>             : 진행 상태 (지금 어디인가)
///
/// 설계에서 가장 중요한 부분은 <b>대본과 LLM의 이중화</b>다.
/// 단계 브리핑·힌트는 <see cref="QuestStageBriefing"/>에 적힌 대본으로 항상 먼저 말하고,
/// LLM은 그 위에 얹는 심화 설명으로만 쓴다. 백엔드가 없거나(엔드포인트가 placeholder),
/// 네트워크가 끊기거나, 응답이 늦어도 퀘스트 진행은 한 번도 막히지 않는다.
/// LLM 응답만으로 진행하게 만들면 백엔드가 준비되기 전까지 게임을 켤 수조차 없다.
/// </summary>
public class AIAssistantBrain : MonoBehaviour
{
    [Header("비서 구성 요소 (비워두면 자신과 자식에서 찾는다)")]
    public AIAssistantVisual visual;
    public AIAssistantSpeechBubble bubble;
    public AIAssistantFollower follower;

    [Header("연결")]
    [Tooltip("LLM 백엔드. SolarChatClient(직접 호출) 또는 AICoScientistClient(자체 프록시) 중 하나. " +
             "비워두거나 설정이 안 돼 있으면 대본만으로 진행한다.")]
    public AIChatBackend client;
    [Tooltip("진행 상태. 비워두면 씬에서 찾는다.")]
    public QuestSession session;
    [Tooltip("변이 부위 선택을 설명하려면 연결한다. 비워두면 씬에서 찾는다.")]
    public MutationHighlighter mutationHighlighter;
    [Tooltip("후보물질 도킹 결과를 설명하려면 연결한다. 비워두면 씬에서 찾는다.")]
    public DockingQuestController dockingQuest;
    [Tooltip("구조 단계(리본 → 나선 → 아미노산) 이동을 해설하려면 연결한다. 비워두면 씬에서 찾는다.")]
    public StructureLevelController levelController;
    [Tooltip("말하는 동안 후보물질 선택을 막으려면 연결한다. 비워두면 씬에서 찾는다.")]
    public CompoundSelectionPanel compoundPanel;

    [Header("인트로 대사")]
    [TextArea(2, 4)]
    public string[] greetingLines =
    {
        "안녕! 오늘부터 이 과학실에서 함께 일할 AI 도우미야.",
        "그런데 방금 센서에 이상한 신호가 잡혔어. 우리 몸 세포 안에서 뭔가 평소와 다르게 움직이고 있대.",
        "어떤 사건부터 조사해 볼까?",
    };

    [Header("자동 브리핑")]
    [Tooltip("단계에 들어설 때 대본 대사를 자동으로 읽어준다.")]
    public bool announceStages = true;
    [Tooltip("대본을 읽은 뒤 LLM에게 심화 설명을 한 번 더 요청한다. 백엔드가 있을 때만 동작한다.")]
    public bool elaborateWithLlm = true;

    [Header("도입 시나리오")]
    [Tooltip("퀘스트를 시작할 때 QuestDefinition.scenario의 사건 브리핑을 연기한다. " +
             "끄거나 시나리오가 비어 있으면 제목과 요약만 읽는다.")]
    public bool playQuestScenario = true;

    [Header("단백질을 고른 직후")]
    [Tooltip("보드에서 사건(단백질)을 고르면 그 단백질이 무슨 일을 하는지 LLM에게 설명을 받아 덧붙인다.")]
    public bool introduceTargetWithLlm = true;
    [TextArea(2, 5)]
    [Tooltip("위 요청에 쓰는 질문. {gene} {mutation} {title} 자리는 고른 퀘스트 값으로 채워진다.")]
    public string targetIntroPrompt =
        "플레이어가 '{title}' 사건을 골랐어요. {gene} 단백질이 우리 몸에서 원래 무슨 일을 하는지, " +
        "그리고 {mutation} 변이가 생기면 무엇이 달라지는지 일상적인 비유를 들어 설명해주세요. " +
        "아직 단계별 공략이나 정답 물질은 알려주지 마세요.";

    [Header("후보물질 도킹 결과")]
    [Tooltip("후보물질을 시도하면 결과 문구를 말한 뒤 LLM에게 이유 설명을 받아 덧붙인다.")]
    public bool explainDockingWithLlm = true;
    [TextArea(2, 5)]
    [Tooltip("성공했을 때 쓰는 질문. {compound} {quest} 자리는 실제 값으로 채워진다.")]
    public string dockingSuccessPrompt =
        "플레이어가 '{compound}'로 도킹에 성공했어요. 이 물질이 왜 통했는지, " +
        "그리고 이게 환자에게 어떤 의미인지 칭찬을 섞어 설명해주세요.";
    [TextArea(2, 5)]
    [Tooltip("실패했을 때 쓰는 질문. 정답을 흘리지 않도록 한 걸음만 이끄는 문장을 권장한다.")]
    public string dockingFailurePrompt =
        "플레이어가 '{compound}'를 시도했는데 실패했어요. 왜 이런 결과가 나왔는지 " +
        "'현재 상황'에 적힌 판정 내용을 근거로 설명하고, 다음에 무엇을 눈여겨보면 좋을지 " +
        "한 걸음만 이끌어 주세요. 정답 물질은 알려주지 마세요.";

    [Header("구조 단계 해설")]
    [Tooltip("사용자가 구조를 클릭해 파고들 때마다 지금 무엇을 보고 있는지 말해준다.")]
    public bool narrateStructureLevels = true;
    [Tooltip("대본을 말한 뒤 LLM에게 이 단계에 대한 심화 설명을 받아 덧붙인다.")]
    public bool elaborateStructureLevelsWithLlm = true;
    [TextArea(2, 4)]
    [Tooltip("리본 단계 — 단백질 전체 모양")]
    public string ribbonLine = "지금 보이는 게 단백질 전체 모양이야. 리본처럼 생긴 줄기를 따라가다 " +
                               "궁금한 곳을 눌러보면 그 부분만 확대해서 볼 수 있어.";
    [TextArea(2, 4)]
    [Tooltip("나선 단계 — 선택한 구간")]
    public string helixLine = "좋아, 네가 고른 구간만 크게 띄웠어. 이렇게 돌돌 말린 모양을 나선이라고 불러. " +
                              "한 번 더 누르면 이 안의 원자까지 들어갈 수 있어.";
    [TextArea(2, 4)]
    [Tooltip("아미노산 단계 — 원자 하나하나")]
    public string aminoAcidLine = "여기가 가장 안쪽이야. 이제 공 하나하나가 원자야. " +
                                  "색깔이 다른 건 서로 다른 원소라는 뜻이지.";

    [Header("말하는 동안 입력 잠금")]
    [Tooltip("설명이 끝나기 전에 구조나 후보물질을 클릭해 넘어가지 못하게 막는다.")]
    public bool blockInteractionWhileSpeaking = true;
    [Tooltip("LLM 응답을 기다리는 동안에도 잠글지. 켜면 설명이 완전히 끝날 때까지 막지만, " +
             "응답이 느리면 그만큼 기다려야 한다.")]
    public bool blockWhileWaitingForLlm;

    [Header("실패 시 문구")]
    [TextArea(2, 4)]
    public string offlineNotice = "지금은 외부 지식 연결이 잠깐 꺼져 있어. 내가 아는 만큼 최대한 쉽게 설명해줄게!";
    [TextArea(2, 4)]
    public string requestFailedNotice = "어? 연결이 잠깐 끊겼나 봐. 조금 있다가 다시 물어봐 줄래?";

    /// <summary>백엔드에 실제로 물어볼 수 있는 상태인지.</summary>
    public bool CanUseLlm => client != null && client.IsConfigured;

    /// <summary>지금 말하고 있거나 대기 중인 문장이 있는지.</summary>
    public bool IsBusy => bubble != null && bubble.IsBusy;

    /// <summary>
    /// 말하는 중이거나, 아직 답이 오지 않은 요청이 남아 있는지.
    ///
    /// <see cref="IsBusy"/>만 보고 기다리면 LLM 응답을 기다리는 동안 큐가 잠깐 비어
    /// "말이 끝났다"고 오판한다. 그 틈에 다음 연출이 시작되면 뒤늦게 도착한 설명이
    /// 엉뚱한 장면 위에 얹힌다.
    /// </summary>
    public bool IsBusyOrWaiting => IsBusy || (client != null && client.PendingRequests > 0);

    // 힌트는 부르는 대로 하나씩 넘어간다. 단계가 바뀌면 처음으로 되돌린다.
    private int _hintIndex;
    // 도킹 시도 일련번호. 늦게 도착한 이전 시도의 LLM 답을 걸러내는 데 쓴다.
    private int _dockingAttempt;
    // 마지막으로 해설한 구조 단계. 같은 단계를 다시 읊지 않기 위해 기억한다.
    private StructureLevelController.ViewLevel? _narratedLevel;
    private bool _offlineNoticeShown;

    private void Awake()
    {
        if (visual == null) visual = GetComponentInChildren<AIAssistantVisual>(true);
        if (bubble == null) bubble = GetComponentInChildren<AIAssistantSpeechBubble>(true);
        if (follower == null) follower = GetComponentInChildren<AIAssistantFollower>(true);

        if (session == null) session = FindFirstObjectByType<QuestSession>();
        if (client == null) client = FindFirstObjectByType<AIChatBackend>();
        if (mutationHighlighter == null) mutationHighlighter = FindFirstObjectByType<MutationHighlighter>();
        // 도킹 컨트롤러는 4단계 전까지 꺼져 있을 수 있어 비활성까지 뒤진다.
        if (dockingQuest == null) dockingQuest = FindFirstObjectByType<DockingQuestController>(FindObjectsInactive.Include);
        if (levelController == null) levelController = FindFirstObjectByType<StructureLevelController>(FindObjectsInactive.Include);
        if (compoundPanel == null) compoundPanel = FindFirstObjectByType<CompoundSelectionPanel>(FindObjectsInactive.Include);

        if (bubble == null)
            Debug.LogError("[AIAssistantBrain] 말풍선을 찾지 못했습니다. " +
                           "Tools > Taming Mutants > AI 비서 생성 으로 비서를 만들어 주세요.", this);
    }

    private void OnEnable()
    {
        if (session != null)
        {
            session.OnQuestStarted += HandleQuestStarted;
            session.OnStageEntered += HandleStageEntered;
            session.OnQuestCompleted += HandleQuestCompleted;
        }

        if (client != null) client.OnError += HandleClientError;
        if (mutationHighlighter != null) mutationHighlighter.OnMutationSelected += HandleMutationSelected;
        if (dockingQuest != null) dockingQuest.OnDockingFinished += HandleDockingFinished;
        if (levelController != null) levelController.OnLevelChanged += HandleStructureLevelChanged;
    }

    private void OnDisable()
    {
        if (session != null)
        {
            session.OnQuestStarted -= HandleQuestStarted;
            session.OnStageEntered -= HandleStageEntered;
            session.OnQuestCompleted -= HandleQuestCompleted;
        }

        if (client != null) client.OnError -= HandleClientError;
        if (mutationHighlighter != null) mutationHighlighter.OnMutationSelected -= HandleMutationSelected;
        if (dockingQuest != null) dockingQuest.OnDockingFinished -= HandleDockingFinished;
        if (levelController != null) levelController.OnLevelChanged -= HandleStructureLevelChanged;
    }

    private void Update()
    {
        PushInputLock();
    }

    private void OnDestroy()
    {
        // 비서가 사라지면서 잠금이 켜진 채로 남으면 구조를 영영 클릭할 수 없게 된다.
        ReleaseInputLock();
    }

    /// <summary>
    /// 말하는 동안 구조·후보물질 클릭을 막는다.
    ///
    /// 이벤트로 켜고 끄지 않고 매 프레임 현재 상태를 밀어 넣는 이유: 말풍선은 큐·일시정지·
    /// 즉시교체(SayNow)로 상태가 여러 갈래로 바뀐다. "말하기 시작/끝" 두 지점을 잡아 토글하면
    /// 어느 한 경로에서 짝이 어긋나는 순간 잠금이 영구히 남는다. 상태를 그대로 반영하면
    /// 어긋날 짝이 없다.
    /// </summary>
    private void PushInputLock()
    {
        if (!blockInteractionWhileSpeaking)
        {
            ReleaseInputLock();
            return;
        }

        // 멈춰 있는 동안(카메라 이동 중)은 잠그지 않는다.
        //
        // 이때 비서는 화면에서 숨겨져 있고 입력은 CameraTransitionDirector가 이미 막고 있다.
        // 반면 여기서 잠그면 위험하다 — CameraTransitionDirector.StopRunning()은 전환 코루틴을
        // OnTransitionCompleted 없이 끊을 수 있어서, 그 경로로 들어가면 Resume()이 영영 오지 않고
        // 말풍선이 멈춘 채로 남는다. 그 상태를 잠금으로 옮기면 구조를 영영 클릭할 수 없게 된다.
        bool speaking = IsBusy && !(bubble != null && bubble.IsPaused);
        bool locked = blockWhileWaitingForLlm ? speaking || (client != null && client.PendingRequests > 0) : speaking;

        if (levelController != null) levelController.InputLocked = locked;
        if (compoundPanel != null) compoundPanel.SpeechLocked = locked;
    }

    private void ReleaseInputLock()
    {
        if (levelController != null) levelController.InputLocked = false;
        if (compoundPanel != null) compoundPanel.SpeechLocked = false;
    }

    // --- 말하기 ---

    /// <summary>한 문장 말한다. 이미 말하는 중이면 큐에 쌓인다.</summary>
    public void Speak(string line)
    {
        if (bubble == null || string.IsNullOrWhiteSpace(line)) return;
        bubble.Say(line);
    }

    /// <summary>여러 문장을 순서대로 말한다.</summary>
    public void SpeakSequence(IEnumerable<string> lines)
    {
        if (lines == null) return;

        foreach (string line in lines) Speak(line);
    }

    /// <summary>대기 중인 문장을 버리고 즉시 이 문장으로 교체한다. (오류 안내 등)</summary>
    public void SpeakNow(string line)
    {
        if (bubble == null || string.IsNullOrWhiteSpace(line)) return;
        bubble.SayNow(line);
    }

    /// <summary>대사가 화면에 뜨는 순간 <paramref name="onShown"/>을 함께 실행한다.</summary>
    private void Speak(string line, System.Action onShown)
    {
        if (bubble == null || string.IsNullOrWhiteSpace(line)) return;
        bubble.Say(line, onShown);
    }

    /// <summary>인트로 인사. <see cref="IntroDirector"/>가 부른다.</summary>
    public void SpeakGreeting()
    {
        SetState(AIAssistantState.Idle);
        SpeakSequence(greetingLines);
    }

    // --- 외부에서 들어오는 요청 ---

    /// <summary>
    /// 사용자의 자유 질문. 백엔드가 있으면 LLM에, 없으면 안내 문구로 답한다.
    /// </summary>
    public void AskAssistant(string question, string selection = null)
    {
        if (string.IsNullOrWhiteSpace(question)) return;

        if (!CanUseLlm)
        {
            // 사용자가 직접 물은 것이므로 매번 답한다. SpeakOfflineFallback은 평생 한 번만
            // 말하도록 돼 있어서(단계마다 반복하면 잔소리라서) 여기 쓰면 두 번째 질문부터는
            // 아무 반응이 없다 — 음성으로 물었는데 비서가 침묵하면 고장으로 보인다.
            SpeakNow(offlineNotice);
            return;
        }

        SetState(AIAssistantState.Thinking);
        client.Ask(question, BuildContext(selection),
            onReply: Speak,
            onFailed: _ => SpeakNow(requestFailedNotice));
    }

    /// <summary>
    /// 힌트 요청. 대본 힌트를 먼저 소진하고, 다 떨어지면 LLM에게 넘긴다.
    /// 순서를 반대로 하면 백엔드가 없을 때 힌트 기능 자체가 죽는다.
    /// </summary>
    public void RequestHint()
    {
        QuestStageBriefing briefing = session != null ? session.CurrentBriefing : null;

        if (briefing != null && briefing.hints != null && _hintIndex < briefing.hints.Length)
        {
            Speak(briefing.hints[_hintIndex]);
            _hintIndex++;
            return;
        }

        if (!CanUseLlm)
        {
            Speak("여기까지가 내가 줄 수 있는 힌트야! 구조를 손으로 돌려가며 다른 각도에서 한번 살펴봐.");
            return;
        }

        AskAssistant("지금 단계에서 막혔어요. 중학생도 이해할 수 있게 쉬운 말로, 정답을 바로 알려주지 말고 한 단계만 더 힌트를 주세요.");
    }

    /// <summary>
    /// 사용자가 잔기나 원자를 선택했을 때의 설명.
    /// MouseWorldSelector / MutationHighlighter.OnMutationSelected에 연결한다.
    /// </summary>
    public void ExplainSelection(string label, string description = null)
    {
        if (string.IsNullOrWhiteSpace(label)) return;

        string spoken = string.IsNullOrWhiteSpace(description) ? label : $"{label} — {description}";

        // 같은 부위를 다시 선택해도 즉시 반응해야 한다. Speak(큐 적재)로 두면
        // 진행 중인 브리핑/LLM 응답 뒤로 밀려 "다시 선택이 안 되는" 것처럼 보인다.
        if (!CanUseLlm)
        {
            SpeakNow(spoken);
            return;
        }

        SpeakNow(spoken);
        SetState(AIAssistantState.Thinking);
        client.Ask($"{label}에 대해 중학생도 이해할 수 있게 쉬운 말로 두세 문장으로 설명해줘.", BuildContext(label),
            onReply: Speak,
            onFailed: _ => { /* 이미 이름은 말했으니 실패는 조용히 넘긴다 */ });
    }

    /// <summary>
    /// 도킹 시도 결과를 알린다. 결과 문구는 퀘스트 데이터(PDF의 후보물질 표)에 적혀 있다.
    /// </summary>
    public void ReportDockingResult(CandidateCompound candidate)
    {
        if (candidate == null) return;

        SetState(candidate.isCorrect ? AIAssistantState.Speaking : AIAssistantState.Alert);

        string message = string.IsNullOrWhiteSpace(candidate.resultMessage)
            ? $"{candidate.displayName}: 친화도 {candidate.affinityKcalPerMol:0.0} kcal/mol"
            : candidate.resultMessage;

        SpeakNow(message);
    }

    /// <summary>
    /// 후보물질 도킹 결과를 설명한다. <see cref="DockingQuestController.OnDockingFinished"/>가 부른다.
    ///
    /// 대본(퀘스트 JSON의 result_message)을 먼저 즉시 말하고, 그 위에 LLM 설명을 얹는다.
    /// 순서를 반대로 하면 물질을 골라놓고 몇 초 동안 아무 말도 안 들린다 —
    /// 화면에는 이미 결과 연출이 끝나 있는데 비서만 침묵하는 상태가 된다.
    /// </summary>
    public void ReportDockingResult(DockingResult result)
    {
        if (result.Compound == null) return;

        // 시도를 세어 둔다. 실패 뒤에는 패널이 다시 열려 바로 다음 물질을 고를 수 있는데,
        // 그때 이전 시도의 LLM 답이 뒤늦게 도착하면 방금 시도한 물질의 설명인 척 끼어든다.
        int attempt = ++_dockingAttempt;

        SetState(result.IsSuccess ? AIAssistantState.Speaking : AIAssistantState.Alert);

        string spoken = string.IsNullOrWhiteSpace(result.Message)
            ? BuildFallbackDockingLine(result)
            : result.Message;
        SpeakNow(spoken);

        if (!explainDockingWithLlm || !CanUseLlm)
        {
            if (!CanUseLlm) SpeakOfflineFallback();
            return;
        }

        SetState(AIAssistantState.Thinking);
        client.Ask(FormatDockingPrompt(result), BuildDockingContext(result),
            onReply: reply =>
            {
                // 그 사이 다른 물질을 시도했다면 이 답은 이미 지난 이야기다.
                if (attempt != _dockingAttempt) return;
                Speak(reply);
            },
            onFailed: _ => { /* 결과 문구는 이미 말했으므로 실패해도 진행에 지장이 없다 */ });
    }

    /// <summary>결과 문구가 비어 있는 후보물질을 위한 바닥선. 최소한 판정은 알려준다.</summary>
    private string BuildFallbackDockingLine(DockingResult result)
    {
        string name = string.IsNullOrWhiteSpace(result.Compound.display_name)
            ? "이 물질" : result.Compound.display_name;

        if (result.IsOrderError) return $"{name}은 방향은 맞는데, 아직 순서가 일러.";

        return result.IsSuccess
            ? $"{name}, 딱 맞았어! 포켓에 제대로 붙었어."
            : $"{name}은 이번엔 잘 안 붙었네. 다시 해보자.";
    }

    /// <summary>질문 틀의 자리표시자를 채운다.</summary>
    private string FormatDockingPrompt(DockingResult result)
    {
        string template = result.IsSuccess ? dockingSuccessPrompt : dockingFailurePrompt;
        if (string.IsNullOrWhiteSpace(template))
        {
            template = result.IsSuccess
                ? "'{compound}'가 왜 통했는지 중학생 눈높이로 설명해주세요."
                : "'{compound}'가 왜 실패했는지 중학생 눈높이로 설명하고, 정답은 알려주지 마세요.";
        }

        string questTitle = session != null && session.CurrentQuest != null ? session.CurrentQuest.title : "";

        return template
            .Replace("{compound}", result.Compound.display_name)
            .Replace("{quest}", questTitle);
    }

    /// <summary>
    /// 도킹 판정을 모델이 읽을 수 있는 문장으로 만들어 컨텍스트에 싣는다.
    ///
    /// outcome 이름(StericClash 등)만 던지면 모델이 그 단어에서 이유를 지어낸다.
    /// 무슨 일이 벌어졌는지는 우리가 이미 아는 사실이므로 넘겨주고, 모델에게는
    /// 그걸 쉬운 말로 풀어주는 일만 맡긴다.
    /// </summary>
    private AIRequestContext BuildDockingContext(DockingResult result)
    {
        CompoundData compound = result.Compound;

        var selection = new System.Text.StringBuilder();
        selection.Append($"후보물질 '{compound.display_name}'");
        if (!string.IsNullOrWhiteSpace(compound.subtitle)) selection.Append($" ({compound.subtitle})");
        selection.Append($" / 판정: {DescribeOutcome(result)}");

        // 진입조차 못 한 경우의 친화도 값은 의미가 없다 — 패널도 "측정 불가"로 표시한다.
        if (!result.IsOrderError && result.Outcome != DockingOutcome.StericClash)
            selection.Append($" / 결합 친화도 {compound.affinity:0.0} kcal/mol (음수일수록 강하게 결합)");

        AIRequestContext context = BuildContext(selection.ToString());
        return context;
    }

    /// <summary>판정별로 "실제로 무슨 일이 일어났는가"를 한 줄로 적는다.</summary>
    private static string DescribeOutcome(DockingResult result)
    {
        if (result.IsOrderError)
            return "순서 오류 — 결합 자리는 맞지만, 먼저 성공해야 할 다른 물질이 아직 남아 있어 물러났다. 오답이 아니다.";

        switch (result.Outcome)
        {
            case DockingOutcome.Success:
                return "성공 — 포켓에 들어가 표적 원자와 결합을 만들고 고정됐다.";
            case DockingOutcome.NoWarhead:
                return "실패 — 포켓 안에는 들어갔지만 붙잡아 둘 반응기가 없어 고정되지 못하고 다시 빠져나왔다.";
            case DockingOutcome.StericClash:
                return "실패 — 분자가 너무 커서 포켓 입구에서 걸렸다. 안으로 들어가지도 못했다.";
            case DockingOutcome.OffTarget:
                return "실패 — 결합 부위가 서로 맞지 않아 접근 도중 밀려났다. 애초에 이 자리를 노리는 물질이 아니다.";
            case DockingOutcome.FragmentHit:
                return "부분 성공 — 포켓에 들어가 안정화 효과가 잠깐 나타났지만, 붙어 있는 힘이 약해 곧 이탈했다.";
            case DockingOutcome.WrongStrategy:
                return "실패 — 이 포켓과 상관없는 전략이라 결합 자체가 일어나지 않았다. 노리는 곳이 다르다.";
            case DockingOutcome.NoStabilization:
                return "실패 — 표적 원자 근처까지는 닿았지만 붙잡아 주는 상호작용이 만들어지지 않아 흔들림이 그대로다.";
            case DockingOutcome.NonSelective:
                return "실패 — 이 포켓뿐 아니라 주변 여러 자리에도 들러붙었다. 독성 위험이 있다.";
            default:
                return "실패 — 결합에 성공하지 못했다.";
        }
    }

    private void HandleDockingFinished(DockingResult result) => ReportDockingResult(result);

    // --- 퀘스트 이벤트 ---

    private void HandleQuestStarted(QuestDefinition quest)
    {
        _hintIndex = 0;
        // 사건이 바뀌면 구조도 처음부터 다시 본다. 기억을 비우지 않으면 새 단백질의
        // 리본 단계에 들어가도 "이미 해설한 단계"로 보고 아무 말도 하지 않는다.
        _narratedLevel = null;
        if (quest == null) return;

        QuestScenario scenario = quest.scenario;
        if (playQuestScenario && scenario != null && scenario.HasBeats)
        {
            PlayScenario(quest, scenario);
            return;
        }

        // 시나리오가 없는 퀘스트도 있어야 한다. 데이터가 덜 채워졌다고 도입부가 통째로 비면
        // 플레이어는 아무 설명 없이 분자 앞에 놓인다.
        Speak($"좋아, '{quest.title}' 사건 조사를 시작하자!");
        if (!string.IsNullOrWhiteSpace(quest.summary)) Speak(quest.summary);
    }

    private void HandleStageEntered(QuestStageBriefing briefing)
    {
        _hintIndex = 0;
        if (!announceStages || briefing == null) return;

        SpeakSequence(briefing.assistantLines);

        if (!elaborateWithLlm) return;

        if (!CanUseLlm)
        {
            SpeakOfflineFallback();
            return;
        }

        SetState(AIAssistantState.Thinking);
        client.Ask(
            $"'{briefing.title}' 단계를 시작했어요. 중학생도 이해할 수 있게 쉬운 말로, 지금 무엇에 집중해야 하는지 두세 문장으로 짚어주세요.",
            BuildContext(),
            onReply: Speak,
            onFailed: _ => { /* 대본 브리핑은 이미 말했으므로 실패해도 진행에 지장이 없다 */ });
    }

    private void HandleQuestCompleted(QuestDefinition quest)
    {
        SetState(AIAssistantState.Speaking);
        Speak(quest != null
            ? $"'{quest.title}' 사건 해결 완료! 정말 잘했어."
            : "사건 해결 완료! 정말 잘했어.");
    }

    private void HandleClientError(string reason)
    {
        SetState(AIAssistantState.Alert);
        Debug.LogWarning($"[AIAssistantBrain] 백엔드 요청이 실패해 대본으로 대체합니다 — {reason}", this);
    }

    /// <summary>
    /// 사용자가 변이 잔기를 클릭했을 때. MutationHighlighter가 이 이벤트를 쏜다.
    /// 비서가 "설명해주는" 역할이 실제로 붙는 지점이다.
    /// </summary>
    private void HandleMutationSelected(MutationHighlighter.MutationSite site)
    {
        if (site == null) return;

        // 잔기를 잠깐 쳐다보게 하면 어디를 말하는 중인지 시선으로 드러난다.
        if (follower != null && mutationHighlighter != null)
            follower.FocusOn(mutationHighlighter.transform);

        ExplainSelection($"{site.residueId}번 잔기", site.description);
    }

    // --- 구조 단계 해설 ---

    /// <summary>
    /// 사용자가 구조를 클릭해 리본 → 나선 → 아미노산으로 파고들 때마다 지금 무엇을 보고 있는지 말한다.
    ///
    /// 클릭에 대한 반응이므로 <see cref="SpeakNow"/>로 즉시 끼어든다. 큐에 쌓으면 앞선 브리핑이
    /// 끝나기를 기다리는 동안 화면은 이미 다음 단계로 넘어가 있어, 정작 도착했을 땐
    /// 한 단계 전 이야기를 하고 있게 된다 — 사용자가 어긋난다고 느끼는 지점이 여기다.
    /// </summary>
    private void HandleStructureLevelChanged(StructureLevelController.ViewLevel level)
    {
        if (!narrateStructureLevels) return;

        // SetLevel은 같은 단계로 다시 들어와도 이벤트를 쏜다(원자 필터를 다시 적용할 때 등).
        // 그때마다 같은 해설을 반복하면 잔소리가 된다.
        if (_narratedLevel.HasValue && _narratedLevel.Value == level) return;
        _narratedLevel = level;

        string line = ResolveLevelLine(level);
        if (string.IsNullOrWhiteSpace(line)) return;

        SetState(AIAssistantState.Speaking);
        SpeakNow(line);

        if (!elaborateStructureLevelsWithLlm || !CanUseLlm) return;

        SetState(AIAssistantState.Thinking);
        client.Ask(BuildLevelQuestion(level), BuildContext(DescribeLevel(level)),
            onReply: reply =>
            {
                // 그 사이 또 파고들었다면 이 답은 이미 지난 단계 이야기다.
                if (_narratedLevel.HasValue && _narratedLevel.Value == level) Speak(reply);
            },
            onFailed: _ => { /* 대본은 이미 말했으므로 조용히 넘긴다 */ });
    }

    private string ResolveLevelLine(StructureLevelController.ViewLevel level)
    {
        switch (level)
        {
            case StructureLevelController.ViewLevel.Ribbon: return ribbonLine;
            case StructureLevelController.ViewLevel.Helix: return helixLine;
            case StructureLevelController.ViewLevel.AminoAcid: return aminoAcidLine;
            default: return null;
        }
    }

    /// <summary>모델에게 "지금 화면에 무엇이 떠 있는지"를 알려주는 한 줄.</summary>
    private static string DescribeLevel(StructureLevelController.ViewLevel level)
    {
        switch (level)
        {
            case StructureLevelController.ViewLevel.Ribbon:
                return "화면에 단백질 전체가 리본(뼈대) 모양으로 떠 있다.";
            case StructureLevelController.ViewLevel.Helix:
                return "사용자가 리본의 한 구간을 골라, 그 구간만 확대해 나선 모양으로 보고 있다.";
            case StructureLevelController.ViewLevel.AminoAcid:
                return "가장 안쪽 단계다. 선택한 구간의 원자 하나하나가 공으로 보인다.";
            default:
                return null;
        }
    }

    private string BuildLevelQuestion(StructureLevelController.ViewLevel level)
    {
        return "플레이어가 방금 이 단계로 들어왔어요. '현재 상황'에 적힌 화면 상태를 근거로, " +
               "지금 보이는 것이 무엇이고 어디를 눈여겨보면 좋을지 한두 문장으로 짚어주세요. " +
               "다음 단계 이야기나 정답은 아직 하지 마세요.";
    }

    // --- 도입 시나리오 ---

    /// <summary>
    /// 사건 파일을 펼치듯 도입부를 연기한다.
    ///
    /// 대사는 전부 한 번에 말풍선 큐에 넣는다. 코루틴으로 한 줄씩 기다리게 짜면
    /// 그 사이에 <see cref="HandleStageEntered"/>가 1단계 브리핑을 큐에 밀어 넣어
    /// 시나리오 중간에 단계 안내가 끼어든다 — QuestSession은 StartQuest 안에서
    /// OnQuestStarted 직후 바로 1단계로 들어가기 때문이다.
    /// 지금 방식은 순서가 큐 하나로 결정되므로 그런 뒤섞임이 없다.
    /// </summary>
    private void PlayScenario(QuestDefinition quest, QuestScenario scenario)
    {
        string headline = scenario.BuildHeadline(quest);
        if (!string.IsNullOrWhiteSpace(headline))
            Speak(headline, () => RunBeatAction(quest, AIAssistantState.Alert, ScenarioAction.LookAtUser, null));

        foreach (ScenarioBeat beat in scenario.beats)
        {
            if (beat == null || string.IsNullOrWhiteSpace(beat.line)) continue;

            // 지역 변수로 받아 캡처한다. 루프 변수를 그대로 캡처하면 모든 대사가
            // 마지막 비트의 행동을 실행하게 된다.
            ScenarioBeat current = beat;
            Speak(current.line, () => RunBeatAction(quest, current.mood, current.action, current.llmPrompt));
        }
    }

    /// <summary>대사에 붙은 행동 하나를 실행한다.</summary>
    private void RunBeatAction(QuestDefinition quest, AIAssistantState mood, ScenarioAction action, string llmPrompt)
    {
        SetState(mood);

        switch (action)
        {
            case ScenarioAction.LookAtUser:
                if (follower != null && follower.followTarget != null)
                    follower.FocusOn(follower.followTarget);
                break;

            case ScenarioAction.LookAtMolecule:
                if (follower != null && follower.anchorTarget != null)
                    follower.FocusOn(follower.anchorTarget);
                break;

            case ScenarioAction.FlashMutationSite:
                // SelectResidue가 아니라 FlashResidue를 쓴다. 선택 이벤트를 쏘면
                // 그걸 받은 ExplainSelection이 SayNow로 큐를 비워 시나리오가 끊긴다.
                if (mutationHighlighter != null) mutationHighlighter.FlashAllSites();
                if (follower != null && follower.anchorTarget != null)
                    follower.FocusOn(follower.anchorTarget);
                break;

            case ScenarioAction.AskLlm:
                AskScenarioElaboration(quest, llmPrompt);
                break;
        }
    }

    /// <summary>
    /// 시나리오 비트가 요청한 심화 설명. 응답은 큐 맨 뒤에 붙으므로
    /// 도입부의 마지막 한마디처럼 들린다.
    /// </summary>
    private void AskScenarioElaboration(QuestDefinition quest, string llmPrompt)
    {
        if (!CanUseLlm)
        {
            SpeakOfflineFallback();
            return;
        }

        string question = string.IsNullOrWhiteSpace(llmPrompt)
            ? "방금 브리핑한 사건이 왜 문제인지, 일상적인 비유를 들어 중학생 눈높이로 두세 문장 덧붙여 주세요."
            : llmPrompt;

        SetState(AIAssistantState.Thinking);
        client.Ask(question, BuildQuestContext(quest),
            onReply: Speak,
            onFailed: _ => { /* 대본 도입부는 이미 다 말했으므로 실패해도 진행에 지장이 없다 */ });
    }

    // --- 단백질(사건)을 고른 직후 ---

    /// <summary>
    /// 사용자가 보드에서 조사할 단백질을 골랐을 때. <see cref="IntroDirector"/>가 부른다.
    ///
    /// 대본 한마디로 즉시 반응해 클릭이 먹었다는 걸 알리고, 그 위에 LLM 설명을 얹는다.
    /// 순서가 중요하다 — LLM을 먼저 기다리면 카드를 눌러도 몇 초 동안 아무 반응이 없다.
    /// </summary>
    public void IntroduceQuestTarget(QuestDefinition quest)
    {
        if (quest == null) return;

        SetState(AIAssistantState.Speaking);
        SpeakNow($"'{quest.title}' 사건이구나. 좋아, {quest.gene} 단백질을 직접 보러 가자!");

        if (!introduceTargetWithLlm) return;

        if (!CanUseLlm)
        {
            // 백엔드가 없어도 설명 없이 넘어가지는 않는다. 카드에 적힌 요약을 대신 읽어준다.
            if (!string.IsNullOrWhiteSpace(quest.summary)) Speak(quest.summary);
            SpeakOfflineFallback();
            return;
        }

        SetState(AIAssistantState.Thinking);
        client.Ask(FormatTargetPrompt(quest), BuildQuestContext(quest, quest.gene),
            onReply: Speak,
            onFailed: _ =>
            {
                // 이름은 이미 말했으니 사과 문구 대신 대본 요약으로 조용히 메운다.
                if (!string.IsNullOrWhiteSpace(quest.summary)) Speak(quest.summary);
            });
    }

    /// <summary>질문 틀의 자리표시자를 고른 퀘스트 값으로 채운다.</summary>
    private string FormatTargetPrompt(QuestDefinition quest)
    {
        string template = string.IsNullOrWhiteSpace(targetIntroPrompt)
            ? "'{title}' 사건의 {gene} 단백질과 {mutation} 변이를 중학생 눈높이로 설명해주세요."
            : targetIntroPrompt;

        return template
            .Replace("{title}", quest.title)
            .Replace("{gene}", quest.gene)
            .Replace("{mutation}", quest.mutation);
    }

    // --- 보조 ---

    /// <summary>
    /// 오프라인 안내는 한 번만 한다. 단계마다 반복하면 잔소리가 된다.
    /// </summary>
    private void SpeakOfflineFallback()
    {
        if (_offlineNoticeShown) return;

        _offlineNoticeShown = true;
        Speak(offlineNotice);
    }

    private AIRequestContext BuildContext(string selection = null)
    {
        if (session == null) return new AIRequestContext { selection = selection };

        AIRequestContext context = BuildQuestContext(session.CurrentQuest, selection);

        context.stage = session.CurrentStage.ToString();

        QuestStageBriefing briefing = session.CurrentBriefing;
        if (briefing != null)
        {
            context.stageTitle = briefing.title;
            context.stageObjective = briefing.objective;
            context.stageKnowledge = briefing.llmContext;
        }

        return context;
    }

    /// <summary>
    /// 퀘스트 하나만 가지고 만드는 컨텍스트.
    ///
    /// 보드에서 카드를 고른 시점에는 <see cref="QuestSession"/>이 아직 그 퀘스트를 시작하지
    /// 않아 CurrentQuest가 비어 있다. 세션을 거쳐 컨텍스트를 만들면 그 순간의 요청만
    /// 배경 없이 나가고, 모델은 어떤 단백질을 묻는지도 모른 채 답하게 된다.
    /// </summary>
    private AIRequestContext BuildQuestContext(QuestDefinition quest, string selection = null)
    {
        var context = new AIRequestContext { selection = selection };
        if (quest == null) return context;

        context.questId = quest.questId;
        context.questHeader = quest.BuildContextHeader();
        context.scenario = quest.BuildScenarioContext();

        return context;
    }

    private void SetState(AIAssistantState state)
    {
        if (visual != null) visual.SetState(state);
    }
}
