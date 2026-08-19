using System;
using System.Text;
using UnityEngine;

/// <summary>
/// 비서가 말을 걸 LLM 백엔드의 공통 계약.
///
/// 구현체가 둘 있고, 상황에 따라 컴포넌트만 갈아끼운다
/// (guide.md 1장 "컴포넌트 교체 방식" 원칙).
///
///   <see cref="SolarChatClient"/>       : Upstage Solar를 직접 호출. 개발·시연용으로 빠르다.
///   <see cref="AICoScientistClient"/>   : 자체 백엔드 프록시를 호출. 배포용.
///
/// <see cref="AIAssistantBrain"/>은 이 타입만 알고 있어서, 어느 쪽을 쓰든 코드를 고칠 필요가 없다.
/// </summary>
public abstract class AIChatBackend : MonoBehaviour
{
    /// <summary>어떤 요청이든 응답이 오면 발생한다. 요청별 콜백과 별개로 쓰는 관찰용 훅.</summary>
    public event Action<string> OnReplyReceived;

    /// <summary>요청이 실패했을 때. 비서는 이걸 받아 Alert 상태로 바꾼다.</summary>
    public event Action<string> OnError;

    /// <summary>
    /// 실제로 호출할 수 있는 상태인지 (키나 엔드포인트가 채워져 있는지).
    ///
    /// 이 값이 있어야 비서가 "네트워크를 시도했다가 실패해서 멈추는" 대신
    /// 처음부터 대본 대사로 진행할 수 있다. 백엔드 없이도 게임이 끝까지 돌아가야 한다.
    /// </summary>
    public abstract bool IsConfigured { get; }

    /// <summary>보낸 요청 중 아직 응답이 오지 않은 개수.</summary>
    public int PendingRequests { get; protected set; }

    /// <summary>
    /// 질문을 보낸다. 성공하면 onReply, 실패하면 onFailed가 불린다.
    /// 콜백을 요청마다 따로 받는 이유는 비서가 실패에 다르게 반응해야 하기 때문이다
    /// (브리핑 실패는 조용히 넘기고, 사용자 질문 실패는 사과 문구를 띄운다).
    /// </summary>
    public abstract void Ask(string userMessage, AIRequestContext context,
                             Action<string> onReply = null, Action<string> onFailed = null);

    // C# 이벤트는 선언한 클래스 밖에서 Invoke할 수 없어 파생 클래스용 통로를 둔다.
    protected void RaiseReply(string reply) => OnReplyReceived?.Invoke(reply);

    protected void RaiseError(string reason) => OnError?.Invoke(reason);

    /// <summary>설정이 안 된 상태에서 호출됐을 때 공통 처리. 구현체에서 Ask 앞부분에 쓴다.</summary>
    protected bool RejectIfUnusable(string userMessage, Action<string> onFailed)
    {
        if (!IsConfigured)
        {
            const string reason = "LLM 백엔드가 설정되지 않았습니다.";
            RaiseError(reason);
            onFailed?.Invoke(reason);
            return true;
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            onFailed?.Invoke("보낼 질문이 비어 있습니다.");
            return true;
        }

        return false;
    }
}

/// <summary>
/// 요청에 함께 보내는 상황 정보. 모델이 "지금 사용자가 어디에서 뭘 보고 있는지" 모르면
/// 매번 원론적인 답만 돌려주므로, 퀘스트·단계·선택 대상을 묶어서 보낸다.
/// </summary>
[Serializable]
public class AIRequestContext
{
    public string questId;
    public string questHeader;    // "퀘스트: KRAS G12C (KRAS G12C) / 질환: ..."
    public string stage;          // 단계 enum 이름
    public string stageTitle;
    public string stageObjective;
    public string stageKnowledge; // QuestStageBriefing.llmContext
    public string selection;      // 지금 선택한 잔기/원자/후보물질

    public string Compose()
    {
        var builder = new StringBuilder();

        Append(builder, questHeader);
        Append(builder, string.IsNullOrEmpty(stageTitle) ? stage : $"현재 단계: {stageTitle}");
        Append(builder, string.IsNullOrEmpty(stageObjective) ? null : $"단계 목표: {stageObjective}");
        Append(builder, stageKnowledge);
        Append(builder, string.IsNullOrEmpty(selection) ? null : $"사용자가 선택한 대상: {selection}");

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        if (builder.Length > 0) builder.Append('\n');
        builder.Append(line);
    }
}
