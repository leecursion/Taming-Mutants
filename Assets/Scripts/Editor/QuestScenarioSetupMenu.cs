#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 메뉴 [Tools/Taming Mutants/퀘스트 도입 시나리오 채우기]로
/// 다섯 사건의 도입부(가상 시나리오 + 비서 연기)를 퀘스트 에셋에 써넣는다.
///
/// <see cref="IntroSetupMenu"/>와 나눠 둔 이유: 저쪽은 단계 대사와 후보물질 같은
/// "퀘스트 뼈대"를 만들고, 여기는 그 위에 얹는 이야기만 손본다. 대사 톤을 다듬는 일은
/// 뼈대보다 훨씬 자주 일어나는데, 한 파일에 두면 문장 하나 고치려고 퀘스트 데이터 전체를
/// 다시 써야 한다.
///
/// 사건·환자·의뢰인은 모두 가상이다. 실제 환자 사례가 아니라, 중학생 플레이어가
/// "왜 이 단백질을 조사하는가"를 먼저 납득하도록 세운 무대다.
///
/// 여러 번 실행해도 안전하다. 시나리오 항목만 덮어쓰고 나머지 퀘스트 데이터는 건드리지 않는다.
/// </summary>
public static class QuestScenarioSetupMenu
{
    private const string QuestFolder = "Assets/Quests";

    [MenuItem("Tools/Taming Mutants/퀘스트 도입 시나리오 채우기")]
    public static void Setup()
    {
        int filled = 0;

        filled += Apply("Quest_KRAS_G12C.asset", BuildKrasScenario());
        filled += Apply("Quest_EGFR_L858R.asset", BuildEgfrScenario());
        filled += Apply("Quest_ABL1_T315I.asset", BuildAbl1Scenario());
        filled += Apply("Quest_CFTR_F508del.asset", BuildCftrScenario());
        filled += Apply("Quest_P53_Y220C.asset", BuildP53Scenario());

        AssetDatabase.SaveAssets();
        Debug.Log($"[QuestScenarioSetup] 퀘스트 {filled}개에 도입 시나리오를 채웠습니다.");
    }

    /// <summary>퀘스트 에셋 하나에 시나리오를 써넣는다. 에셋이 없으면 건너뛴다.</summary>
    private static int Apply(string assetName, QuestScenario scenario)
    {
        string path = QuestFolder + "/" + assetName;
        var quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);

        if (quest == null)
        {
            Debug.LogWarning($"[QuestScenarioSetup] {path} 를 찾지 못해 건너뜁니다. " +
                             "먼저 [Tools/Taming Mutants/인트로 + 퀘스트 카탈로그 생성]을 실행하세요.");
            return 0;
        }

        quest.scenario = scenario;
        EditorUtility.SetDirty(quest);
        return 1;
    }

    // --- 사건 1: KRAS G12C ---

    private static QuestScenario BuildKrasScenario()
    {
        return new QuestScenario
        {
            caseCode = "CASE-01",
            place = "시립병원 3층 정밀검사실",
            client = "호흡기내과 한지우 선생님",
            premise = "가상의 사건이다. 한 환자의 폐에서 세포 덩어리가 멈추지 않고 커지고 있다. " +
                      "우리 몸은 분명히 '이제 그만 자라'라는 신호를 보내는데 그 세포들만 전혀 듣지 않는다. " +
                      "병원에서 꺼내 온 세포를 연구실로 가져왔고, 신입 연구원인 플레이어가 " +
                      "AI 도우미와 함께 세포 속으로 들어가 원인을 찾는다.",
            beats = new[]
            {
                new ScenarioBeat
                {
                    line = "방금 병원에서 급한 연락이 왔어요. 한 환자의 폐에서 세포 덩어리가 계속 커지고 있대요.",
                    mood = AIAssistantState.Alert,
                    action = ScenarioAction.LookAtUser,
                },
                new ScenarioBeat
                {
                    line = "이상한 건, 우리 몸이 분명 '이제 그만 자라'라고 신호를 보냈는데 그 세포들만 전혀 안 듣는다는 거예요.",
                    mood = AIAssistantState.Speaking,
                },
                new ScenarioBeat
                {
                    line = "그래서 세포를 여기로 가져왔어요. 저기 떠 있는 게 그 세포 속에서 꺼낸 KRAS 단백질이에요.",
                    mood = AIAssistantState.Thinking,
                    action = ScenarioAction.LookAtMolecule,
                },
                new ScenarioBeat
                {
                    line = "지금 혼자 떨고 있는 저 자리 보이시죠? 12번 자리예요. 설계도에서 딱 한 글자가 바뀐 곳이에요.",
                    mood = AIAssistantState.Alert,
                    action = ScenarioAction.FlashMutationSite,
                    focusResidueId = 12,
                },
                new ScenarioBeat
                {
                    line = "여기서부터가 우리 일이에요. 글자 하나 때문에 왜 이런 일이 생겼는지 같이 밝혀 봐요!",
                    mood = AIAssistantState.Speaking,
                    action = ScenarioAction.AskLlm,
                    llmPrompt = "플레이어에게 방금 사건 현장을 브리핑했어요. KRAS가 세포에서 어떤 스위치 역할을 하는지, " +
                                "12번 자리가 바뀌면 왜 그 스위치가 꺼지지 않게 되는지 일상적인 비유로 설명해주세요. " +
                                "해결 방법이나 약 이야기는 아직 하지 마세요.",
                },
            },
        };
    }

    // --- 사건 2: EGFR L858R ---

    private static QuestScenario BuildEgfrScenario()
    {
        return new QuestScenario
        {
            caseCode = "CASE-02",
            place = "시립병원 흉부영상 판독실",
            client = "흉부외과 도현 선생님",
            premise = "가상의 사건이다. 담배를 한 번도 피운 적 없는 환자의 폐에서 종양이 발견됐다. " +
                      "세포 표면에는 바깥 신호를 받는 안테나 같은 단백질 EGFR이 있는데, " +
                      "이 환자의 세포는 바깥에서 아무 신호가 오지 않는데도 안테나가 계속 " +
                      "'신호 왔다!'고 안쪽에 외치고 있다.",
            beats = new[]
            {
                new ScenarioBeat
                {
                    line = "이번 환자는 담배를 한 번도 피운 적이 없대요. 그런데도 폐에 종양이 생겼어요.",
                    mood = AIAssistantState.Alert,
                    action = ScenarioAction.LookAtUser,
                },
                new ScenarioBeat
                {
                    line = "세포 표면에는 바깥 소식을 받는 안테나가 달려 있어요. EGFR이라고 불러요.",
                    mood = AIAssistantState.Speaking,
                },
                new ScenarioBeat
                {
                    line = "그런데 이 세포의 안테나는 밖에서 아무 신호가 안 왔는데도 계속 '신호 왔다!'고 소리치고 있어요.",
                    mood = AIAssistantState.Thinking,
                    action = ScenarioAction.LookAtMolecule,
                },
                new ScenarioBeat
                {
                    line = "지직거리는 858번 자리를 보세요. 여기 글자가 바뀌면서 안테나가 눌린 채로 굳어 버렸어요.",
                    mood = AIAssistantState.Alert,
                    action = ScenarioAction.FlashMutationSite,
                    focusResidueId = 858,
                },
                new ScenarioBeat
                {
                    line = "고장 난 안테나를 어떻게 조용히 시킬 수 있을까요? 그게 이번 사건의 숙제예요.",
                    mood = AIAssistantState.Speaking,
                    action = ScenarioAction.AskLlm,
                    llmPrompt = "EGFR이 세포 표면에서 어떤 안테나 역할을 하는지, L858R 변이가 생기면 왜 " +
                                "신호가 계속 켜진 채로 남는지 일상적인 비유로 설명해주세요. " +
                                "약 이름이나 정답은 아직 말하지 마세요.",
                },
            },
        };
    }

    // --- 사건 3: ABL1 T315I ---

    private static QuestScenario BuildAbl1Scenario()
    {
        return new QuestScenario
        {
            caseCode = "CASE-03",
            place = "혈액종양내과 외래 진료실",
            client = "혈액내과 서윤 선생님",
            premise = "가상의 사건이다. 3년 동안 잘 듣던 약이 어느 날부터 갑자기 듣지 않게 된 환자가 있다. " +
                      "약은 ABL1이라는 단백질의 열쇠 구멍에 들어가 작동을 막는 방식인데, " +
                      "그 구멍 앞을 지키는 '문지기' 자리가 더 큰 모양으로 바뀌면서 약이 들어가지 못하게 됐다.",
            beats = new[]
            {
                new ScenarioBeat
                {
                    line = "이번엔 좀 억울한 사건이에요. 3년 동안 잘 듣던 약이 어느 날 갑자기 안 듣게 됐대요.",
                    mood = AIAssistantState.Alert,
                    action = ScenarioAction.LookAtUser,
                },
                new ScenarioBeat
                {
                    line = "그 약은 ABL1이라는 단백질의 열쇠 구멍에 쏙 들어가서 작동을 막아 주는 약이었어요.",
                    mood = AIAssistantState.Speaking,
                },
                new ScenarioBeat
                {
                    line = "그런데 구멍 앞을 지키는 문지기 자리가 더 커다란 모양으로 바뀌어 버렸어요. 315번 자리예요.",
                    mood = AIAssistantState.Thinking,
                    action = ScenarioAction.FlashMutationSite,
                    focusResidueId = 315,
                },
                new ScenarioBeat
                {
                    line = "문이 막힌 게 아니라 문 앞이 좁아진 거예요. 열쇠는 멀쩡한데 이제 들어갈 수가 없어요.",
                    mood = AIAssistantState.Speaking,
                    action = ScenarioAction.LookAtMolecule,
                },
                new ScenarioBeat
                {
                    line = "그럼 우리는 어떻게 해야 할까요? 같이 방법을 찾아봐요.",
                    mood = AIAssistantState.Speaking,
                    action = ScenarioAction.AskLlm,
                    llmPrompt = "ABL1의 315번 자리가 왜 '문지기'라고 불리는지, 이 자리가 더 큰 모양으로 바뀌면 " +
                                "왜 원래 쓰던 약이 들어가지 못하게 되는지 일상적인 비유로 설명해주세요. " +
                                "다음에 쓸 약이나 정답은 아직 말하지 마세요.",
                },
            },
        };
    }

    // --- 사건 4: CFTR F508del ---

    private static QuestScenario BuildCftrScenario()
    {
        return new QuestScenario
        {
            caseCode = "CASE-04",
            place = "소아과 병동 3호실",
            client = "소아과 민서 선생님",
            premise = "가상의 사건이다. 어린 환자가 끈끈한 가래 때문에 계속 기침을 한다. " +
                      "세포 표면에는 물을 내보내는 문 역할의 CFTR 단백질이 있어야 하는데, " +
                      "설계도에서 글자 하나가 통째로 사라지는 바람에 문이 제 모양으로 접히지 못하고 " +
                      "표면에 도착하기도 전에 폐기 처리된다.",
            beats = new[]
            {
                new ScenarioBeat
                {
                    line = "이번 환자는 우리보다 어린 친구예요. 끈끈한 가래 때문에 계속 기침을 한대요.",
                    mood = AIAssistantState.Alert,
                    action = ScenarioAction.LookAtUser,
                },
                new ScenarioBeat
                {
                    line = "우리 세포 표면에는 물을 내보내는 문이 있어요. 그 문 덕분에 가래가 묽게 유지되거든요.",
                    mood = AIAssistantState.Speaking,
                },
                new ScenarioBeat
                {
                    line = "그런데 이 친구의 세포에선 문이 만들어지긴 하는데, 표면까지 가지도 못하고 버려지고 있어요.",
                    mood = AIAssistantState.Thinking,
                    action = ScenarioAction.LookAtMolecule,
                },
                new ScenarioBeat
                {
                    // 이 대사만은 두 자리를 함께 부르므로 focusResidueId를 비워 둔다(=0).
                    // 507과 509를 같이 짚어야 "그 사이가 빈자리"라는 말이 성립한다.
                    line = "설계도에서 508번 글자가 통째로 빠졌거든요. 지직거리는 507번과 509번 사이, 저기가 빈자리예요.",
                    mood = AIAssistantState.Alert,
                    action = ScenarioAction.FlashMutationSite,
                },
                new ScenarioBeat
                {
                    line = "고장 난 문을 고쳐서 제자리로 보내 주는 게 이번 사건의 목표예요.",
                    mood = AIAssistantState.Speaking,
                    action = ScenarioAction.AskLlm,
                    llmPrompt = "CFTR이 세포에서 어떤 문 역할을 하는지, 508번 글자가 사라지면 왜 그 문이 " +
                                "제 모양으로 접히지 못하고 버려지는지 일상적인 비유로 설명해주세요. " +
                                "치료제 이름이나 정답은 아직 말하지 마세요.",
                },
            },
        };
    }

    // --- 사건 5: p53 Y220C ---

    private static QuestScenario BuildP53Scenario()
    {
        return new QuestScenario
        {
            caseCode = "CASE-05",
            place = "병리과 유전자 분석실",
            client = "병리과 하람 선생님",
            premise = "가상의 사건이다. 세포에는 설계도가 망가졌는지 검사하고 문제가 있으면 " +
                      "세포를 멈춰 세우는 p53이라는 감시 단백질이 있다. " +
                      "이 환자의 p53은 220번 자리가 바뀌면서 옆구리에 틈이 생겼고, " +
                      "체온 정도의 온도에서도 모양이 스르르 풀려 감시 역할을 못 한다.",
            beats = new[]
            {
                new ScenarioBeat
                {
                    line = "마지막 사건이에요. 이번 상대는 우리 몸을 지키던 경비원이 쓰러진 경우예요.",
                    mood = AIAssistantState.Alert,
                    action = ScenarioAction.LookAtUser,
                },
                new ScenarioBeat
                {
                    line = "p53은 설계도가 망가지지 않았는지 검사하고, 문제가 있으면 세포를 멈춰 세우는 감시자예요.",
                    mood = AIAssistantState.Speaking,
                },
                new ScenarioBeat
                {
                    line = "그런데 이 환자의 p53은 우리 체온 정도만 돼도 모양이 스르르 풀려 버려요.",
                    mood = AIAssistantState.Thinking,
                    action = ScenarioAction.LookAtMolecule,
                },
                new ScenarioBeat
                {
                    line = "불안하게 떨고 있는 220번 자리를 보세요. 여기가 바뀌면서 옆구리에 틈이 생겼어요. 그 틈 때문에 무너지는 거예요.",
                    mood = AIAssistantState.Alert,
                    action = ScenarioAction.FlashMutationSite,
                    focusResidueId = 220,
                },
                new ScenarioBeat
                {
                    line = "재미있는 건, 그 틈이 약점이자 기회라는 거예요. 왜 그런지 같이 알아봐요.",
                    mood = AIAssistantState.Speaking,
                    action = ScenarioAction.AskLlm,
                    llmPrompt = "p53이 세포에서 어떤 감시자 역할을 하는지, Y220C 변이로 생긴 틈 때문에 왜 " +
                                "단백질이 열에 약해지는지 일상적인 비유로 설명해주세요. " +
                                "그 틈을 이용하는 구체적인 방법이나 정답은 아직 말하지 마세요.",
                },
            },
        };
    }
}
#endif
