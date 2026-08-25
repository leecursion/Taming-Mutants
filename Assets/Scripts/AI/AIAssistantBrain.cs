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

    [Header("실패 시 문구")]
    [TextArea(2, 4)]
    public string offlineNotice = "지금은 외부 지식 연결이 잠깐 꺼져 있어. 내가 아는 만큼 최대한 쉽게 설명해줄게!";
    [TextArea(2, 4)]
    public string requestFailedNotice = "어? 연결이 잠깐 끊겼나 봐. 조금 있다가 다시 물어봐 줄래?";

    /// <summary>백엔드에 실제로 물어볼 수 있는 상태인지.</summary>
    public bool CanUseLlm => client != null && client.IsConfigured;

    /// <summary>지금 말하고 있거나 대기 중인 문장이 있는지.</summary>
    public bool IsBusy => bubble != null && bubble.IsBusy;

    // 힌트는 부르는 대로 하나씩 넘어간다. 단계가 바뀌면 처음으로 되돌린다.
    private int _hintIndex;
    private bool _offlineNoticeShown;

    private void Awake()
    {
        if (visual == null) visual = GetComponentInChildren<AIAssistantVisual>(true);
        if (bubble == null) bubble = GetComponentInChildren<AIAssistantSpeechBubble>(true);
        if (follower == null) follower = GetComponentInChildren<AIAssistantFollower>(true);

        if (session == null) session = FindFirstObjectByType<QuestSession>();
        if (client == null) client = FindFirstObjectByType<AIChatBackend>();
        if (mutationHighlighter == null) mutationHighlighter = FindFirstObjectByType<MutationHighlighter>();

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
            SpeakOfflineFallback();
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

    // --- 퀘스트 이벤트 ---

    private void HandleQuestStarted(QuestDefinition quest)
    {
        _hintIndex = 0;
        if (quest == null) return;

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
        var context = new AIRequestContext { selection = selection };

        if (session == null) return context;

        QuestDefinition quest = session.CurrentQuest;
        if (quest != null)
        {
            context.questId = quest.questId;
            context.questHeader = quest.BuildContextHeader();
        }

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

    private void SetState(AIAssistantState state)
    {
        if (visual != null) visual.SetState(state);
    }
}
