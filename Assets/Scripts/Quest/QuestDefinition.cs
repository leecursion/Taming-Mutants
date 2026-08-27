using System;
using UnityEngine;

/// <summary>
/// 퀘스트 하나의 정의. "어떤 유전자의 어떤 변이를, 어떤 순서로 풀어내는가"를 데이터로 적어둔다.
///
/// 코드가 아니라 에셋으로 두는 이유: 퀘스트는 앞으로 계속 늘어나는데(KRAS G12C, EGFR L858R, ...)
/// 늘어날 때마다 스크립트를 고치면 게임 흐름 코드와 콘텐츠가 뒤엉킨다.
/// <see cref="QuestSession"/>과 <see cref="AIAssistantBrain"/>은 이 에셋만 읽으므로,
/// 새 퀘스트를 추가할 때 코드는 한 줄도 건드리지 않는다.
///
/// 만들기: 프로젝트 창 우클릭 > Create > Taming Mutants > Quest Definition
/// (Tools > Taming Mutants > 인트로 + 퀘스트 카탈로그 생성 을 쓰면 기본 2종이 자동 생성된다.)
/// </summary>
[CreateAssetMenu(fileName = "Quest_", menuName = "Taming Mutants/Quest Definition")]
public class QuestDefinition : ScriptableObject
{
    [Header("식별")]
    [Tooltip("저장 데이터와 LLM 컨텍스트에 쓰는 고유 id. 영문 소문자 + 언더스코어 권장.")]
    public string questId = "kras_g12c";

    [Header("카드에 표시할 정보")]
    public string title = "KRAS G12C";
    public string subtitle = "비소세포폐암 (NSCLC)";
    [Tooltip("유전자 이름. 비서 대사와 LLM 컨텍스트에 들어간다.")]
    public string gene = "KRAS";
    [Tooltip("변이 표기. 예: G12C, L858R")]
    public string mutation = "G12C";
    [TextArea(2, 5)]
    public string summary = "12번 코돈의 글라이신이 시스테인으로 바뀌면서 KRAS가 항상 켜진 상태로 고정됩니다. " +
                            "새로 생긴 시스테인의 황 원자를 표적으로 삼아 공유결합 억제제를 설계하세요.";
    [Range(1, 5)] public int difficulty = 3;
    [Tooltip("카드 강조색. 비서 상태색과 별개로 퀘스트마다 다른 색을 준다.")]
    public Color accent = new Color(0.35f, 0.85f, 1f);

    [Header("구조 데이터")]
    [Tooltip("StreamingAssets 기준 상대 경로. ProteinLoader.streamingAssetsRelativePath에 그대로 들어간다.")]
    public string structureStreamingPath = "structures/P01116.json";
    [Tooltip("변이 잔기 번호. MutationHighlighter가 강조할 위치.")]
    public int[] mutationResidueIds = { 12 };
    [Tooltip("변이 자리의 별명. 예: '고장 난 스위치 자리'. 비서가 번호 대신 이 이름으로 부르고, " +
             "화면의 번호표에도 함께 적힌다 — 중학생에게 '12번'은 기억에 남지 않는다. " +
             "비워두면 예전처럼 번호로만 부른다.")]
    public string mutationSiteAlias;
    [Tooltip("표적 포켓 이름. 비서 대사와 LLM 컨텍스트에 쓰인다.")]
    public string targetPocketLabel = "Switch-II Pocket";

    [Header("도입 시나리오 (퀘스트를 시작할 때 비서가 연기하는 사건 브리핑)")]
    [Tooltip("비워두면 비서가 제목과 요약만 읽고 바로 1단계로 넘어간다.")]
    public QuestScenario scenario = new QuestScenario();

    [Header("단계별 진행 (PDF의 Level 1~5에 대응)")]
    public QuestStageBriefing[] stages = Array.Empty<QuestStageBriefing>();

    [Header("후보물질 (Step 3&4 도킹)")]
    public CandidateCompound[] candidates = Array.Empty<CandidateCompound>();

    /// <summary>해당 단계의 브리핑. 없으면 null.</summary>
    public QuestStageBriefing FindStage(QuestManagerSpatialUI.QuestStage stage)
    {
        if (stages == null) return null;

        foreach (QuestStageBriefing briefing in stages)
            if (briefing != null && briefing.stage == stage) return briefing;

        return null;
    }

    /// <summary>정답으로 지정된 후보물질. 없으면 null.</summary>
    public CandidateCompound FindCorrectCandidate()
    {
        if (candidates == null) return null;

        foreach (CandidateCompound candidate in candidates)
            if (candidate != null && candidate.isCorrect) return candidate;

        return null;
    }

    /// <summary>LLM에 넘길 퀘스트 배경. 매 요청마다 붙여 보낸다.</summary>
    public string BuildContextHeader()
    {
        return $"퀘스트: {title} ({gene} {mutation}) / 질환: {subtitle} / 표적: {targetPocketLabel}";
    }

    /// <summary>
    /// LLM에 넘길 사건 설정. 이게 없으면 모델은 교과서식 설명만 돌려주고,
    /// 플레이어가 듣고 있는 "사건을 조사하는 이야기"와 말투도 내용도 따로 논다.
    /// </summary>
    public string BuildScenarioContext()
    {
        return scenario != null ? scenario.BuildContext() : null;
    }
}

/// <summary>
/// 한 단계에서 "무엇을 해야 하는가"와 "비서가 무슨 말을 하는가".
///
/// 비서 대사를 데이터로 적어두는 게 핵심이다. LLM 응답만으로 진행하면 백엔드가 없거나
/// 네트워크가 끊긴 순간 게임이 멈춘다. 여기 적힌 대사는 항상 동작하는 바닥선이고,
/// LLM은 그 위에 얹는 심화 설명 역할을 맡는다.
/// </summary>
[Serializable]
public class QuestStageBriefing
{
    public QuestManagerSpatialUI.QuestStage stage;

    [Tooltip("퀘스트 패널에 표시할 단계 이름")]
    public string title;
    [TextArea(1, 3)]
    [Tooltip("이 단계의 목표 한 줄")]
    public string objective;

    [TextArea(2, 5)]
    [Tooltip("단계에 들어설 때 비서가 순서대로 말하는 대사")]
    public string[] assistantLines = Array.Empty<string>();

    [TextArea(2, 5)]
    [Tooltip("사용자가 힌트를 요청했을 때 순서대로 꺼내는 대사 (LLM이 없을 때의 바닥선)")]
    public string[] hints = Array.Empty<string>();

    [TextArea(2, 6)]
    [Tooltip("이 단계에서 LLM에 함께 넘길 배경 지식. 모델이 엉뚱한 단계를 설명하지 않게 잡아준다.")]
    public string llmContext;
}

/// <summary>도킹 단계에서 고를 수 있는 후보물질 카드 (PDF 3장 표).</summary>
[Serializable]
public class CandidateCompound
{
    public string displayName = "Compound A";
    public string subtitle = "AMG 510 / Sotorasib 유사체";

    [TextArea(1, 4)]
    [Tooltip("분자 특징. 카드에 그대로 표시한다.")]
    public string features;

    [Tooltip("정답 후보물질인지")]
    public bool isCorrect;

    [Tooltip("결합 친화도 (kcal/mol). 음수일수록 강하게 결합한다.")]
    public float affinityKcalPerMol = -8.3f;

    [TextArea(1, 4)]
    [Tooltip("도킹 시도 결과로 비서가 말할 문장")]
    public string resultMessage;
}
