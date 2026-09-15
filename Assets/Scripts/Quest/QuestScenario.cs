using System;
using System.Text;
using UnityEngine;

/// <summary>
/// 시나리오 대사 한 줄에 붙는 비서의 행동. "말만 하는" 브리핑과 "연기하는" 도입부를 가르는 부분이다.
///
/// 대사와 따로 타이머를 돌리지 않고 <see cref="AIAssistantSpeechBubble.Say"/>의 콜백에 실어 보낸다.
/// 대사 길이는 글자 수에 따라 달라지므로 초 단위로 맞춰 두면 문장을 한 글자만 고쳐도 어긋난다.
/// </summary>
public enum ScenarioAction
{
    /// <summary>표정(상태색)만 바꾸고 아무 행동도 하지 않는다.</summary>
    None,

    /// <summary>사용자 쪽을 돌아본다. 사건을 브리핑하며 눈을 맞추는 대사에 쓴다.</summary>
    LookAtUser,

    /// <summary>분자 쪽을 돌아본다. "저기를 봐" 하는 대사에 쓴다.</summary>
    LookAtMolecule,

    /// <summary>변이 부위를 반짝인다. "바로 여기가 문제야" 하는 대사에 쓴다.
    /// <see cref="ScenarioBeat.focusResidueId"/>를 지정하면 그 잔기 하나만 짚는다.</summary>
    FlashMutationSite,

    /// <summary>
    /// 이 대사와 함께 LLM에게 심화 설명을 요청한다.
    /// 응답은 도착하는 대로 말풍선 큐 <b>맨 뒤</b>에 붙으므로, 마지막 비트에 두는 편이 자연스럽다.
    /// </summary>
    AskLlm,
}

/// <summary>
/// 퀘스트를 시작할 때 비서가 연기하는 도입부 — "사건 파일"을 펼치는 장면.
///
/// <see cref="QuestStageBriefing"/>이 "이 단계에서 뭘 해야 하는가"라면 여기는 그 앞의 "왜 하는가"다.
/// 중학생이 단백질 이름부터 듣고 흥미를 느끼기는 어려우니, 먼저 가상의 사건과 의뢰인을 세워
/// 플레이어를 그 사건의 조사관 자리에 앉힌다.
///
/// 대사와 마찬가지로 데이터로 둔다. 퀘스트가 늘어날 때 <see cref="AIAssistantBrain"/>은 그대로 두고
/// 에셋만 채우면 된다. 비워 두면 비서는 예전처럼 제목과 요약만 읽고 넘어간다 —
/// 시나리오가 없다고 퀘스트가 막히지는 않는다.
/// </summary>
[Serializable]
public class QuestScenario
{
    [Header("사건 파일")]
    [Tooltip("사건 번호. 비서의 첫 대사에 들어간다. 예: CASE-01")]
    public string caseCode = "CASE-01";
    [Tooltip("사건이 벌어진 곳. 예: 시립병원 3층 검사실")]
    public string place;
    [Tooltip("의뢰인. 예: 소아과 윤정 선생님")]
    public string client;

    [TextArea(2, 6)]
    [Tooltip("사건 개요. 비서가 그대로 읽지는 않고, LLM에 배경으로 넘겨 답이 시나리오를 벗어나지 않게 잡아준다.")]
    public string premise;

    [Header("도입 연출")]
    [Tooltip("퀘스트가 시작되면 이 순서대로 말하고 행동한다.")]
    public ScenarioBeat[] beats = Array.Empty<ScenarioBeat>();

    /// <summary>연기할 내용이 실제로 들어 있는지.</summary>
    public bool HasBeats
    {
        get
        {
            if (beats == null) return false;

            foreach (ScenarioBeat beat in beats)
                if (beat != null && !string.IsNullOrWhiteSpace(beat.line)) return true;

            return false;
        }
    }

    /// <summary>사건 파일을 펼치는 첫 대사. 사건 번호·장소·의뢰인이 없으면 null.</summary>
    public string BuildHeadline(QuestDefinition quest)
    {
        var parts = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(caseCode)) parts.Append($"사건 파일 {caseCode.Trim()}");
        if (quest != null && !string.IsNullOrWhiteSpace(quest.subtitle))
        {
            if (parts.Length > 0) parts.Append(" — ");
            parts.Append(quest.subtitle.Trim());
        }

        if (parts.Length == 0) return null;

        parts.Append('.');
        if (!string.IsNullOrWhiteSpace(place)) parts.Append($" 현장은 {place.Trim()}입니다.");
        if (!string.IsNullOrWhiteSpace(client)) parts.Append($" 의뢰인은 {client.Trim()}이세요.");

        return parts.ToString();
    }

    /// <summary>LLM에 함께 넘길 시나리오 배경. 모델이 사건 설정 밖으로 나가지 않게 잡아준다.</summary>
    public string BuildContext()
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(caseCode)) builder.Append($"사건 번호: {caseCode.Trim()}\n");
        if (!string.IsNullOrWhiteSpace(place)) builder.Append($"사건 현장: {place.Trim()}\n");
        if (!string.IsNullOrWhiteSpace(client)) builder.Append($"의뢰인: {client.Trim()}\n");
        if (!string.IsNullOrWhiteSpace(premise)) builder.Append($"사건 개요: {premise.Trim()}");

        return builder.ToString().TrimEnd();
    }
}

/// <summary>도입부의 대사 한 줄과, 그 줄에 맞춰 비서가 하는 행동.</summary>
[Serializable]
public class ScenarioBeat
{
    [TextArea(2, 4)]
    [Tooltip("비서가 말할 대사")]
    public string line;

    [Tooltip("이 대사를 말하는 동안의 표정(상태색). 놀란 대목은 Alert, 곱씹는 대목은 Thinking.")]
    public AIAssistantState mood = AIAssistantState.Speaking;

    [Tooltip("이 대사에 맞춰 할 행동")]
    public ScenarioAction action = ScenarioAction.None;

    [Tooltip("action이 FlashMutationSite일 때 짚을 잔기 번호. 0이면 등록된 변이 부위를 전부 반짝인다. " +
             "'858번 자리를 보세요'처럼 번호를 말하는 대사라면 반드시 그 번호를 적는다 — " +
             "전부 반짝이면 정작 어느 것이 858인지 알 수 없다.")]
    public int focusResidueId;

    [TextArea(1, 4)]
    [Tooltip("action이 AskLlm일 때 모델에게 던질 질문. 비워두면 기본 질문을 쓴다.")]
    public string llmPrompt;
}
