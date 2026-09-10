using System;
using System.Collections;
using UnityEngine;

public partial class AIAssistantBrain
{
    [Header("사건 마무리 확인")]
    public bool useOralCheck = true;
    public OralCheckClient oralCheckClient;
    [Min(0)] public int maxOralRetries = 1;
    [Tooltip("마이크가 없는 PC에서도 확인 패널에 직접 입력할 수 있다. 기본값은 기존 음성 전용 동작을 유지한다.")]
    public bool allowTypedOralAnswersWithoutMicrophone;
    public string oralIntroLine = "마지막으로 연구원님의 생각을 들려주세요.";
    public string oralListenPrompt = "마이크로 말한 뒤, 알아들은 내용이 맞는지 확인해 주세요. 글로 고쳐도 괜찮아요.";
    public string oralPassLine = "왜 통했는지 자기 말로 잘 설명하셨어요.";
    public string oralSkipLine = "지금은 설명을 정리하고 다음으로 넘어갈게요.";
    public bool IsAwaitingOralAnswer => _oralDraft.IsOpen;
    public bool IsOralCheckActive => _oralQuest != null;
    public int ConversationGeneration => _conversationGeneration;
    public int OralRunId { get; private set; }
    public OralCheckPhase OralPhase { get; private set; }
    public OralCheckOutcome OralOutcome { get; private set; }
    public string OralQuestion { get; private set; } = "";
    public string OralFeedback { get; private set; } = "";
    public string OralEvidence { get; private set; } = "";
    public string OralSubmittedAnswer { get; private set; } = "";
    public string OralQuestTitle { get; private set; } = "";
    public string OralDraftText => _oralDraft.Text;
    public int OralDraftRevision => _oralDraft.Revision;
    public int OralAttempt => _oralRetries + 1;
    public float OralSecondsRemaining => Mathf.Max(0f, _oralDraft.Deadline - Time.realtimeSinceStartup);
    public bool OralIsRecording => _oralSpeech != null && _oralSpeech.IsListening;
    public bool OralIsTranscribing => _oralSpeech != null && _oralSpeech.IsTranscribing;

    private readonly OralAnswerDraft _oralDraft = new OralAnswerDraft();
    private QuestDefinition _oralQuest;
    private DockingQuestCatalog _oralCatalog;
    private SpeechToTextBackend _oralSpeech;
    private Coroutine _oralRoutine;
    private int _oralRetries;
    private bool _oralStageJump, _oralSpeechSettled, _oralSkip;
    private QuestManagerSpatialUI.QuestStage _oralReturnStage;

    private void TryStartOralCheck(DockingResult result)
    {
        // 교정제 하나의 성공은 CFTR 단계 완료가 아니다.
        if (!useOralCheck || IsOralCheckActive || !result.IsSuccess ||
            result.Compound == null || !result.Compound.completes_stage || session == null) return;
        var quest = session.CurrentQuest;
        var check = quest != null ? quest.oralCheck : null;
        if (check == null || !check.enabled || string.IsNullOrWhiteSpace(check.question) ||
            string.IsNullOrWhiteSpace(check.criteria)) return;
        if (oralCheckClient == null) oralCheckClient = OralCheckClient.Ensure();
        var voice = FindFirstObjectByType<VoiceInputController>();
        _oralSpeech = voice != null && voice.assistant == this ? voice.speechToText : null;
        bool canSpeak = _oralSpeech != null && _oralSpeech.isActiveAndEnabled && _oralSpeech.IsConfigured;
        if (oralCheckClient == null || !oralCheckClient.IsConfigured ||
            (!canSpeak && !allowTypedOralAnswersWithoutMicrophone)) return;

        OralRunId++;
        _oralQuest = quest;
        _oralReturnStage = session.questPanel != null ? session.questPanel.currentStage : session.CurrentStage;
        _oralRetries = 0;
        _oralSkip = false;
        OralOutcome = OralCheckOutcome.None;
        OralPhase = OralCheckPhase.Explaining;
        OralQuestTitle = quest.title;
        OralQuestion = check.question;
        OralFeedback = "도킹 결과를 함께 살펴보고 있어요.";
        OralEvidence = "";
        OralSubmittedAnswer = "";
        OralCheckPanel.Ensure(this);
        _oralCatalog = FindFirstObjectByType<DockingQuestCatalog>();
        // 다른 이벤트 구독자가 이미 예약한 전환까지 보류한다.
        if (_oralCatalog != null) _oralCatalog.SuspendAutoAdvance(this);
        _oralRoutine = StartCoroutine(OralCheckRoutine(_conversationGeneration, OralRunId));
    }

    private bool OralCurrent(int generation, int run) =>
        generation == _conversationGeneration && run == OralRunId && isActiveAndEnabled &&
        _oralQuest != null && session != null && session.CurrentQuest == _oralQuest;

    private IEnumerator WaitForOralSpeech(int generation, int run)
    {
        _oralSpeechSettled = false;
        yield return null;
        float deadline = Time.realtimeSinceStartup + 40f;
        int streak = 0;
        var p53 = FindFirstObjectByType<P53QuestDirector>();
        var cftr = FindFirstObjectByType<CftrFinaleController>();
        var cameraDirector = FindFirstObjectByType<CameraTransitionDirector>();
        while (OralCurrent(generation, run) && !_oralSkip && Time.realtimeSinceStartup < deadline)
        {
            bool busy = IsBusyOrWaiting || (bubble != null && bubble.IsPaused) ||
                (cameraDirector != null && cameraDirector.IsTransitioning) ||
                (p53 != null && p53.IsFinalePlaying) || (cftr != null && cftr.IsFinalePlaying);
            streak = busy ? 0 : streak + 1;
            if (streak >= 6) { _oralSpeechSettled = true; yield break; }
            yield return null;
        }
    }

    private IEnumerator OralCheckRoutine(int generation, int run)
    {
        try
        {
            var check = _oralQuest.oralCheck;
            string previousAnswer = "";
            yield return WaitForOralSpeech(generation, run);
            if (!OralCurrent(generation, run)) yield break;
            if (_oralSpeechSettled && !_oralSkip)
            {
                Speak(oralIntroLine);
                Speak(OralQuestion);
                Speak(oralListenPrompt);
            }
            while (OralCurrent(generation, run) && !_oralSkip && _oralSpeechSettled)
            {
                yield return WaitForOralSpeech(generation, run);
                if (!OralCurrent(generation, run)) yield break;
                if (!_oralSpeechSettled || _oralSkip) break;
                OralPhase = OralCheckPhase.Answering;
                OralFeedback = _oralRetries == 0
                    ? "관찰한 변화와 약의 모양을 연결해 보세요. 어려운 용어는 필요 없어요."
                    : "방금 확인한 부분을 앞선 설명에 덧붙여 보세요.";
                _oralDraft.Begin(Time.realtimeSinceStartup);
                SetState(AIAssistantState.Idle);
                while (OralCurrent(generation, run) && !_oralSkip && _oralDraft.IsOpen)
                {
                    // 녹음·변환 중에는 여유를 주지만 한 답변의 총 대기는 3분을 넘기지 않는다.
                    if (OralIsRecording || OralIsTranscribing) _oralDraft.Touch(Time.realtimeSinceStartup);
                    if (_oralDraft.Expired(Time.realtimeSinceStartup)) { _oralSkip = true; break; }
                    yield return null;
                }
                if (!OralCurrent(generation, run)) yield break;
                if (_oralSkip) break;

                OralPhase = OralCheckPhase.Grading;
                OralFeedback = "설명에 담긴 이유를 살펴보고 있어요.";
                string answer = _oralDraft.Text;
                OralSubmittedAnswer = answer;
                OralGrade grade = null;
                bool done = false;
                if (oralCheckClient != null && oralCheckClient.IsConfigured)
                {
                    oralCheckClient.Grade(_oralQuest.questId, check.criteria, answer, check.concepts,
                        result => { if (OralCurrent(generation, run) && !_oralSkip) { grade = result; done = true; } },
                        error => { if (OralCurrent(generation, run)) done = true; }, previousAnswer);
                }
                else done = true;
                float deadline = Time.realtimeSinceStartup + 65f;
                while (OralCurrent(generation, run) && !_oralSkip && !done &&
                       Time.realtimeSinceStartup < deadline) yield return null;
                if (!OralCurrent(generation, run)) yield break;
                if (_oralSkip) break;
                if (!OralGradeProtocol.HasValidEvidence(grade, answer, previousAnswer))
                {
                    CompleteOral(OralCheckOutcome.Unavailable,
                        "지금은 답변을 확인하지 못했어요. 설명을 함께 정리하고 계속할게요.");
                    Speak(check.modelAnswer);
                    break;
                }

                // 서버와 클라이언트 모두 원문에 있는 인용인지 확인한다.
                OralEvidence = !string.IsNullOrWhiteSpace(grade.evidence) && (answer + "\n" + previousAnswer).Contains(grade.evidence)
                    ? grade.evidence : "";
                if (grade.understood)
                {
                    CompleteOral(_oralRetries == 0 ? OralCheckOutcome.Understood : OralCheckOutcome.Improved,
                        string.IsNullOrWhiteSpace(grade.followUp) ? oralPassLine : grade.followUp);
                    break;
                }
                if (_oralRetries >= Mathf.Clamp(maxOralRetries, 0, 3))
                {
                    CompleteOral(OralCheckOutcome.Explained,
                        "함께 핵심을 정리해 볼게요. 이 설명을 가지고 다음 사건에 도전해 봐요.");
                    Speak(check.modelAnswer);
                    OralFeedback = string.IsNullOrWhiteSpace(check.modelAnswer) ? oralSkipLine : check.modelAnswer;
                    break;
                }

                previousAnswer = string.IsNullOrEmpty(previousAnswer) ? answer : previousAnswer + "\n" + answer;
                if (previousAnswer.Length > 2000) previousAnswer = previousAnswer.Substring(previousAnswer.Length - 2000);
                _oralRetries++;
                OralPhase = OralCheckPhase.Reviewing;
                OralFeedback = string.IsNullOrWhiteSpace(grade.followUp)
                    ? "빠진 연결고리를 한 번 더 살펴볼까요?" : grade.followUp;
                Speak(OralFeedback);
                OralConcept concept = null;
                if (check.concepts != null)
                    foreach (var entry in check.concepts)
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.key) &&
                            entry.key == grade.missingConcept) { concept = entry; break; }
                if (concept != null && session.CurrentQuest.FindStage(concept.reviewStage) != null)
                {
                    Speak(concept.reviewHint);
                    yield return WaitForOralSpeech(generation, run);
                    if (!OralCurrent(generation, run)) yield break;
                    if (_oralSkip || !_oralSpeechSettled) break;
                    JumpForOral(concept.reviewStage);
                    // 복습 단계 설명과 화면 이동을 모두 마친 뒤 짧은 질문을 붙인다.
                    yield return WaitForOralSpeech(generation, run);
                    if (!OralCurrent(generation, run)) yield break;
                    if (_oralSkip || !_oralSpeechSettled) break;
                }
                OralQuestion = concept != null && !string.IsNullOrWhiteSpace(concept.retryQuestion)
                    ? concept.retryQuestion : check.question;
                Speak(OralQuestion);
                Speak(oralListenPrompt);
            }

            if (!OralCurrent(generation, run)) yield break;
            if (OralOutcome == OralCheckOutcome.None)
                CompleteOral(_oralSkip ? OralCheckOutcome.Skipped : OralCheckOutcome.Unavailable, oralSkipLine);
            _oralDraft.Close();
            if (_oralSpeech != null) _oralSpeech.Cancel();
            if (oralCheckClient != null) oralCheckClient.Cancel();
            _oralSkip = false;
            yield return WaitForOralSpeech(generation, run);
            if (!OralCurrent(generation, run)) yield break;
            // 성공 상태는 이미 확보됐다. 복습 때문에 재도킹을 강요하지 않는다.
            if (session.CurrentStage != _oralReturnStage) JumpForOral(_oralReturnStage);
        }
        finally
        {
            if (run == OralRunId) ReleaseOralCheck();
        }
    }

    private void JumpForOral(QuestManagerSpatialUI.QuestStage stage)
    {
        _oralStageJump = true;
        try { session.JumpToStage(stage); }
        finally { _oralStageJump = false; }
    }

    private void CompleteOral(OralCheckOutcome outcome, string feedback)
    {
        OralOutcome = outcome;
        OralFeedback = feedback;
        OralPhase = OralCheckPhase.Completed;
        Speak(feedback);
    }

    public void EditOralAnswer(string answer) => _oralDraft.Edit(answer, Time.realtimeSinceStartup);

    public void OfferOralAnswer(string answer)
    {
        if (!IsAwaitingOralAnswer) return;
        EditOralAnswer(answer);
        OralFeedback = "이렇게 알아들었어요. 내용이 맞으면 보내고, 다르면 고치거나 다시 말해 주세요.";
        // 소리로도 오인식을 알아챌 수 있게 앞부분을 되읊고 전체 문장은 노트에 남긴다.
        string echo = _oralDraft.Text.Length > 120 ? _oralDraft.Text.Substring(0, 120) + "…" : _oralDraft.Text;
        SpeakNow($"\"{echo}\"라고 들었어요. 노트에서 확인한 뒤 보내 주세요.");
    }

    public void SubmitOralAnswer(string answer)
    {
        if (!IsAwaitingOralAnswer) return;
        EditOralAnswer(answer);
        if (!_oralDraft.Confirm(Time.realtimeSinceStartup)) return;
        OralSubmittedAnswer = _oralDraft.Text;
        if (_oralSpeech != null) _oralSpeech.Cancel();
        OralPhase = OralCheckPhase.Grading;
        SetState(AIAssistantState.Thinking);
    }

    public void RetryOralRecording()
    {
        if (!IsAwaitingOralAnswer) return;
        if (_oralSpeech != null) _oralSpeech.Cancel();
        EditOralAnswer("");
        OralFeedback = "괜찮아요. 마이크를 누르고 다시 말해 주세요.";
    }

    public void SkipOralCheck()
    {
        if (!IsOralCheckActive || OralPhase == OralCheckPhase.Completed) return;
        _oralSkip = true;
        _oralDraft.Close();
        if (_oralSpeech != null) _oralSpeech.Cancel();
        if (oralCheckClient != null) oralCheckClient.Cancel();
        if (bubble != null) bubble.Hide();
    }

    public void DismissOralResult()
    {
        if (!IsOralCheckActive) { OralPhase = OralCheckPhase.Hidden; OralEvidence = ""; }
    }

    private void HandleOralBackRequested()
    {
        if (IsOralCheckActive) ResetConversation();
        else DismissOralResult();
    }

    private void CancelOralCheck()
    {
        OralRunId++; // 같은 사건을 다시 시작해도 이전 답변이 새 퀴즈에 섞이지 않는다.
        if (_oralRoutine != null) StopCoroutine(_oralRoutine);
        ReleaseOralCheck();
        OralPhase = OralCheckPhase.Hidden;
    }

    private void ReleaseOralCheck()
    {
        _oralDraft.Close();
        if (_oralQuest != null)
        {
            if (_oralSpeech != null) _oralSpeech.Cancel();
            if (oralCheckClient != null) oralCheckClient.Cancel();
        }
        _oralQuest = null;
        _oralRoutine = null;
        if (_oralCatalog != null) _oralCatalog.ResumeAutoAdvance(this);
        _oralCatalog = null;
    }
}
