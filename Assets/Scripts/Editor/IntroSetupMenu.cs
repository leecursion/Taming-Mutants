#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

using Stage = QuestManagerSpatialUI.QuestStage;

/// <summary>
/// 메뉴 [Tools/Taming Mutants/인트로 + 퀘스트 카탈로그 생성]으로
/// 퀘스트 에셋과 인트로 진행 오브젝트를 한 번에 만든다.
///
/// KRAS G12C 퀘스트의 단계 대사와 후보물질 4종은 설계서(KRAS G12C MR Quest.pdf)의
/// Step 0~5 및 후보물질 표를 그대로 옮겨 적은 것이다.
///
/// 여러 번 실행해도 안전하다. 이미 있는 에셋과 오브젝트는 다시 만들지 않고 참조만 다시 잇는다.
/// </summary>
public static class IntroSetupMenu
{
    private const string QuestFolder = "Assets/Quests";
    private const string CatalogPath = QuestFolder + "/QuestCatalog.asset";

    [MenuItem("Tools/Taming Mutants/인트로 + 퀘스트 카탈로그 생성")]
    public static void Setup()
    {
        // Play 모드 중에는 EditorSceneManager.MarkSceneDirty가 예외를 던진다 — 그 전에 만든
        // 씬 오브젝트는 만들어지긴 하지만 Play를 멈추는 순간 사라져 씬에 남지 않는다.
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("인트로 구성 실패",
                "Play 모드 중에는 실행할 수 없습니다.\n\n" +
                "Play를 멈춘 뒤(편집 모드) 다시 실행하세요 — Play 중에 만든 오브젝트는 " +
                "Play를 멈추는 순간 사라져 씬에 남지 않습니다.", "확인");
            return;
        }

        QuestCatalog catalog = BuildCatalog();
        BuildSceneObjects(catalog);

        AssetDatabase.SaveAssets();
        Debug.Log("[IntroSetup] 인트로 구성을 완료했습니다. Play를 눌러 확인하세요.");
    }

    // --- 퀘스트 에셋 ---

    private static QuestCatalog BuildCatalog()
    {
        EnsureFolder(QuestFolder);

        QuestDefinition kras = LoadOrCreate<QuestDefinition>(QuestFolder + "/Quest_KRAS_G12C.asset");
        FillKrasG12C(kras);
        EditorUtility.SetDirty(kras);

        QuestDefinition egfr = LoadOrCreate<QuestDefinition>(QuestFolder + "/Quest_EGFR_L858R.asset");
        FillEgfrL858R(egfr);
        EditorUtility.SetDirty(egfr);

        QuestDefinition abl1 = LoadOrCreate<QuestDefinition>(QuestFolder + "/Quest_ABL1_T315I.asset");
        FillAbl1T315I(abl1);
        EditorUtility.SetDirty(abl1);

        QuestDefinition cftr = LoadOrCreate<QuestDefinition>(QuestFolder + "/Quest_CFTR_F508del.asset");
        FillCftrF508del(cftr);
        EditorUtility.SetDirty(cftr);

        QuestDefinition p53 = LoadOrCreate<QuestDefinition>(QuestFolder + "/Quest_P53_Y220C.asset");
        FillP53Y220C(p53);
        EditorUtility.SetDirty(p53);

        QuestCatalog catalog = LoadOrCreate<QuestCatalog>(CatalogPath);
        catalog.quests = new[] { kras, egfr, abl1, cftr, p53 };
        EditorUtility.SetDirty(catalog);

        return catalog;
    }

    /// <summary>
    /// 사건 1: 멈추지 않는 세포 성장 (KRAS G12C).
    /// 중학생 눈높이 스토리 — 학교 과학실의 신입 연구원이 "세포 속 이상 신호"를 조사한다.
    /// 실제 과학 용어(KRAS, Switch-II 등)는 남겨 두되, 처음 등장할 때 항상 쉬운 말로 먼저 풀어준다.
    /// </summary>
    private static void FillKrasG12C(QuestDefinition quest)
    {
        quest.questId = "kras_g12c";
        quest.title = "KRAS G12C";
        quest.subtitle = "사건 1: 멈추지 않는 세포 성장";
        quest.gene = "KRAS";
        quest.mutation = "G12C";
        quest.difficulty = 3;
        quest.accent = new Color(0.35f, 0.85f, 1f);
        quest.summary = "우리 몸 세포에서 이상한 신호가 잡혔어요. 세포의 '성장 스위치' 역할을 하는 KRAS라는 " +
                        "단백질이 고장 나서, 세포가 멈추지 않고 계속 자라고 있대요. 신입 연구원인 당신이 " +
                        "이 사건을 함께 해결해 주세요!";

        quest.structureStreamingPath = "structures/P01116.json";
        quest.mutationResidueIds = new[] { 12 };
        quest.targetPocketLabel = "스위치를 끄는 열쇠 구멍 (Switch-II Pocket)";
        quest.mutationSiteAlias = "고장 난 스위치";

        quest.stages = new[]
        {
            new QuestStageBriefing
            {
                stage = Stage.Quest1_DiseaseAnalysis,
                title = "1화 · 사건 현장",
                objective = "세포 속으로 들어가서, 이상 신호가 시작된 KRAS 단백질의 12번 자리를 찾아봐요.",
                assistantLines = new[]
                {
                    "여기 보이는 세포들에서 이상한 신호가 나오고 있어요. 안으로 들어가서 살펴봐요.",
                    "세포 속으로 더 들어가면, 우리 몸의 설계도인 DNA가 나선 모양으로 보일 거예요.",
                    "설계도 글자가 딱 하나 바뀌었어요! 그래서 KRAS라는 단백질 모양이 살짝 달라졌어요.",
                },
                hints = new[]
                {
                    "혼자 떨고 있는 자리가 12번 자리예요. 나선을 따라 천천히 위로 살펴보세요.",
                    "바뀐 글자는 딱 하나뿐이에요 — 아주 작은 차이가 큰 문제를 만들었어요.",
                },
                llmContext = "KRAS는 '이제 자라도 돼!'라는 신호를 보냈다가 다시 꺼지는 스위치 역할 단백질이다. " +
                             "12번 자리의 아주 작은 부분이 다른 모양으로 바뀌면 스위치가 꺼지지 않고 계속 켜진 " +
                             "상태로 남는다. 그러면 세포가 멈추지 않고 계속 자라난다. 중학생에게는 '고장 나서 " +
                             "계속 눌려있는 스위치' 비유로 설명하면 이해하기 쉽다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest2_ProteinStructure,
                title = "2화 · 단백질 탐정",
                objective = "접힌 단백질의 전체 모양을 살펴보고, 12번 자리가 어디쯤인지 찾아봐요.",
                assistantLines = new[]
                {
                    "바뀐 설계도대로 단백질이 새로 만들어져요. 사슬이 접히면서 입체 모양이 되는 걸 지켜봐요.",
                    "다 접히면 색이 칠해져요. 이 색은 줄기가 어떤 모양으로 접혔는지 알려줘요 — 자주색은 나선, 노란색은 납작한 가닥이에요.",
                    "특히 이 근처가 많이 흔들리네요. 여기가 우리가 눈여겨봐야 할 자리예요.",
                },
                hints = new[]
                {
                    "혼자 지직거리는 자리가 12번이에요. 번호표를 따라가 보세요.",
                    "안쪽 원자까지 들어가면 색의 뜻이 바뀌어요 — 거기서는 AI가 얼마나 확신하는지를 보여줘요.",
                },
                llmContext = "AlphaFold라는 AI가 단백질의 3D 모양을 예측했고, 색은 pLDDT라는 예측 신뢰도 점수를 " +
                             "보여준다(파랑=자신 있음, 빨강·주황=자신 없음). Switch-I, Switch-II라 부르는 부분은 " +
                             "실제로 잘 움직이는 부분이라 신뢰도가 낮게 나오는데, 이 '잘 움직임'이 나중에 약물이 " +
                             "들어갈 틈을 만든다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest3_TargetDiscovery,
                title = "3화 · 숨은 열쇠 구멍",
                objective = "단백질 안쪽에 숨어있는 작은 '열쇠 구멍'과, 새로 생긴 특별한 원자를 찾아 표시해 봐요.",
                assistantLines = new[]
                {
                    "단백질 표면을 지나 안쪽 빈 공간으로 들어가 볼게요. 이 자리가 바로 열쇠 구멍이에요.",
                    "12번 자리에 새로 생긴 노란 원자가 보이시죠? 저게 우리가 노리는 특별한 자리예요.",
                    "이 원자는 다른 것과 아주 잘 달라붙어요. 여기에 딱 맞는 열쇠를 꽂으면 두 번 다시 안 풀리는 결합을 만들 수 있어요.",
                },
                hints = new[]
                {
                    "이 열쇠 구멍은 정상 단백질에는 거의 없어요. 모양이 바뀐 단백질에서만 새로 열려요.",
                    "12번 자리를 찾아보세요. 노란빛 원자가 열쇠 구멍 안쪽을 향하고 있어요.",
                },
                llmContext = "이 자리는 KRAS G12C에서만 열리는 특별한 포켓(Switch-II Pocket)이다. 12번 자리에 " +
                             "새로 생긴 시스테인이라는 아미노산의 황 원자가 반응성이 높아, 여기에 딱 맞는 분자를 " +
                             "걸면 되돌릴 수 없이(공유결합) 붙잡을 수 있다. 정상 단백질에는 이 황 원자가 없어 " +
                             "같은 방법을 쓸 수 없다 — 그래서 이 전략은 이 돌연변이에만 통한다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest4_CandidateEvaluation,
                title = "4화 · 딱 맞는 열쇠 고르기",
                objective = "후보물질 4종 중, 열쇠 구멍에 맞고 특별한 원자와도 단단히 붙는 진짜 열쇠를 찾아봐요.",
                assistantLines = new[]
                {
                    "후보물질 네 개를 준비했어요. 하나씩 열쇠 구멍에 가져다 대 봐요.",
                    "구멍에 들어가는 것만으로는 부족해요. 그 원자를 실제로 붙잡는 '팔'이 달려 있어야 해요.",
                },
                hints = new[]
                {
                    "모양만 맞는 것과 진짜로 붙잡는 것은 달라요. 잘 살펴보세요.",
                    "달라붙는 특수한 '팔(반응기)'이 있는지 확인해 보세요.",
                    "숫자가 마이너스(-)로 클수록 더 세게 붙는다는 뜻이에요. 플러스(+)면 오히려 밀려나요.",
                },
                llmContext = "정답 후보는 표적 원자와 비가역적(한번 붙으면 안 떨어지는) 공유결합을 형성하는 " +
                             "반응기(warhead)를 갖고 있다. 모양은 비슷해도 이 반응기가 없으면 결합이 오래가지 " +
                             "못하고 다시 빠져나온다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest5_Verification,
                title = "5화 · 결과 확인",
                objective = "세포가 진짜로 좋아졌는지, 다른 정상 세포는 안전한지 화면으로 확인하고 사건을 마무리해요.",
                assistantLines = new[]
                {
                    "원자 속 세계에서 나와서 다시 연구실로 돌아왔어요.",
                    "화면에 결과가 떴어요! 이상 신호를 내던 세포는 줄어들고, 정상 세포는 그대로 잘 지내고 있어요.",
                    "정상 단백질은 건드리지 않았으니 부작용도 적을 거예요.",
                },
                hints = new[]
                {
                    "혹시 결과 화면에서 '내성'이라는 말이 보이면, 세포가 또 다른 방법으로 저항할 수도 있다는 뜻이에요.",
                },
                llmContext = "G12C에만 딱 맞는 억제제는 정상 KRAS를 건드리지 않아 부작용이 적다. 다만 실제 " +
                             "치료에서는 세포가 또 다른 변화(2차 변이)를 일으켜 약이 안 듣게 되는 '내성'이 " +
                             "생기기도 해서, 계속 관찰과 다음 대책이 필요하다.",
            },
        };

        quest.candidates = new[]
        {
            new CandidateCompound
            {
                displayName = "후보물질 A",
                subtitle = "열쇠 구멍에 딱 맞는 진짜 열쇠",
                features = "구멍 모양에도 맞고, 딱 붙잡는 특수한 팔도 있음",
                isCorrect = true,
                affinityKcalPerMol = -8.3f,
                resultMessage = "성공! 특별한 원자와 단단히 결합해서 스위치를 완전히 껐어요. " +
                                "이제 세포가 더 이상 이상하게 자라지 않을 거예요.",
            },
            new CandidateCompound
            {
                displayName = "후보물질 B",
                subtitle = "모양은 같지만 팔이 없는 가짜 열쇠",
                features = "겉보기엔 비슷하지만 딱 붙잡는 팔이 없음",
                isCorrect = false,
                affinityKcalPerMol = 3.5f,
                resultMessage = "구멍에는 들어갔지만 고정되지 않고 다시 튕겨 나왔어요. 붙잡는 팔이 없나 봐요!",
            },
            new CandidateCompound
            {
                displayName = "후보물질 C",
                subtitle = "너무 큰 열쇠",
                features = "분자가 너무 크고 복잡한 모양",
                isCorrect = false,
                affinityKcalPerMol = 0f,
                resultMessage = "열쇠 구멍 입구에서부터 걸렸어요. 구멍보다 열쇠가 너무 커요!",
            },
            new CandidateCompound
            {
                displayName = "후보물질 D",
                subtitle = "완전히 다른 모양의 열쇠",
                features = "전혀 다른 모양의 분자",
                isCorrect = false,
                affinityKcalPerMol = 1.2f,
                resultMessage = "가까이 가자마자 밀려났어요. 이 열쇠 구멍이랑은 모양 자체가 안 맞아요!",
            },
        };
    }

    /// <summary>
    /// 사건 2: 멈추지 않는 신호 안테나 (EGFR L858R).
    /// 저항성(T790M)까지 포함해, "1차 해결 -> 되돌아옴 -> 다시 해결"이라는 반전이 있는 사건으로 구성했다.
    /// </summary>
    private static void FillEgfrL858R(QuestDefinition quest)
    {
        quest.questId = "egfr_l858r";
        quest.title = "EGFR L858R";
        quest.subtitle = "사건 2: 멈추지 않는 신호 안테나";
        quest.gene = "EGFR";
        quest.mutation = "L858R";
        quest.difficulty = 2;
        quest.accent = new Color(1f, 0.72f, 0.3f);
        quest.summary = "이번엔 세포 표면의 '신호 안테나' EGFR이 말썽이에요. 신호가 오지도 않았는데 계속 " +
                        "'자라라!'는 명령을 보내고 있대요. 게다가 처음 쓴 약을 피해가는 교묘한 방법까지 " +
                        "찾아냈다니, 함께 끝까지 조사해 봐요!";

        quest.structureStreamingPath = "structures/P00533.json";
        quest.mutationResidueIds = new[] { 858, 790 };
        quest.targetPocketLabel = "신호 물질이 들어가는 자리 (ATP 결합 포켓)";
        quest.mutationSiteAlias = "굳어버린 스위치";

        quest.stages = new[]
        {
            new QuestStageBriefing
            {
                stage = Stage.Quest1_DiseaseAnalysis,
                title = "1화 · 사건 현장",
                objective = "세포 표면의 EGFR 안테나에서 이상해진 858번 자리를 찾아봐요.",
                assistantLines = new[]
                {
                    "EGFR은 세포 표면에 있는 신호 안테나예요. 원래는 신호가 와야만 켜지는데, 지금은 아니에요.",
                    "858번 자리를 살펴봐요. 여기 모양이 조금 달라졌어요.",
                    "작은 부분 하나가 바뀌면서 안테나가 신호도 없이 계속 켜진 채로 고정돼 버렸어요.",
                },
                hints = new[] { "858번 자리를 클릭해 보세요. 빨갛게 지직거리고 있어요." },
                llmContext = "EGFR은 세포 표면의 신호 수신 안테나 역할을 하는 단백질이다. 정상일 때는 신호 " +
                             "물질(리간드)이 와야 켜지지만, 활성 부위 858번 자리가 바뀌면 신호 없이도 계속 켜진 " +
                             "상태로 고정된다. 그러면 세포가 멈추지 않고 계속 자라라는 신호를 받는다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest2_ProteinStructure,
                title = "2화 · 단백질 탐정",
                objective = "접힌 단백질의 전체 모양을 살펴보고, 858번 자리가 어디쯤인지 찾아봐요.",
                assistantLines = new[]
                {
                    "여기 색은 줄기가 접힌 모양을 뜻해요 — 자주색은 나선, 노란색은 납작한 가닥이에요. 안쪽 원자까지 들어가면 그때는 AI의 확신 정도를 색으로 보여줘요.",
                },
                hints = new[] { "리본 모양에서 원자 단위까지 들어가면 하나하나 자세히 볼 수 있어요." },
                llmContext = "색은 AI 예측 신뢰도(pLDDT) 점수를 나타낸다. 90 이상은 매우 확실함, 70~90은 " +
                             "믿을 만함, 50~70은 낮음, 50 미만은 모양이 아직 정해지지 않았을 가능성이 크다는 뜻이다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest3_TargetDiscovery,
                title = "3화 · 숨은 열쇠 구멍",
                objective = "안테나 안쪽, 원래 신호를 만드는 연료가 들어가는 자리를 찾아 표시해요.",
                assistantLines = new[]
                {
                    "원래 이 자리엔 세포를 움직이게 하는 연료 같은 물질이 들어가요. 그 자리를 찾으면 그게 표적이에요.",
                },
                hints = new[] { "790번 자리 근처를 살펴봐요. 나중에 안테나가 '방어'에 쓰는 자리이기도 해요." },
                llmContext = "이 자리는 원래 ATP라는 세포의 에너지 물질이 들어가는 자리다. 여기를 막으면 " +
                             "안테나가 신호를 못 보낸다. 다만 790번 근처 모양이 나중에 바뀌면(저항), 처음 만든 " +
                             "약이 잘 안 듣게 될 수 있다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest4_CandidateEvaluation,
                title = "4화 · 다시 찾는 열쇠",
                objective = "저항하는 세포까지 막을 수 있는 진짜 열쇠를 찾아봐요.",
                assistantLines = new[] { "처음 만든 열쇠는 세포가 방법을 바꾸는 바람에 더 안 듣게 됐어요. 새로운 열쇠를 찾아봐요." },
                hints = new[] { "797번 자리에 단단히 달라붙는 열쇠를 찾아봐요." },
                llmContext = "세포가 790번 자리 모양을 바꾸면(저항) 예전 열쇠가 안 맞게 된다. 새로운 열쇠는 " +
                             "797번 자리에 단단히(공유결합) 달라붙어 저항을 우회한다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest5_Verification,
                title = "5화 · 결과 확인",
                objective = "효과와 안전성을 확인하고 사건을 마무리해요.",
                assistantLines = new[] { "정상 안테나까지 막으면 피부 같은 곳에서 부작용이 생길 수 있어요. 결과를 확인해 봐요." },
                hints = new[] { "변형된 것만 골라서 막을수록 정상 세포는 더 안전해요." },
                llmContext = "정상 EGFR까지 막으면 피부 발진 같은 부작용이 생길 수 있어, 변이된 것만 골라 " +
                             "막는 선택성이 중요하다.",
            },
        };

        quest.candidates = System.Array.Empty<CandidateCompound>();
    }

    /// <summary>
    /// 사건 3: 되돌아온 신호 (ABL1 T315I).
    /// 18개월 전 Imatinib으로 해결했던 사건 1(KRAS와는 별개로, ABL1/BCR-ABL 만성골수성백혈병
    /// 시나리오)이 gatekeeper 돌연변이(T315I)로 재발한다는 설정 — "1차 해결 -> 내성 재발 ->
    /// 구조를 우회하는 새 억제제로 재해결"이라는 반전 구조는 EGFR 사건과 같은 얼개를 따른다.
    /// 후보물질 5종(Imatinib/Nilotinib/Dasatinib/Ponatinib/Asciminib 유사체)은
    /// StreamingAssets/compounds/abl1_t315i/*.json 의 실제 도킹 정의와 1:1로 대응한다.
    /// </summary>
    private static void FillAbl1T315I(QuestDefinition quest)
    {
        quest.questId = "abl1_t315i";
        quest.title = "ABL1 T315I";
        quest.subtitle = "사건 3: 되돌아온 신호";
        quest.gene = "ABL1";
        quest.mutation = "T315I";
        quest.difficulty = 4;
        quest.accent = new Color(0.78f, 0.4f, 0.95f);
        quest.summary = "18개월 전, 이 환자의 백혈병 세포는 Imatinib이라는 약으로 잠잠해졌어요. 그런데 얼마 " +
                        "전부터 신호가 다시 강해지고 있대요. 범인은 ABL1이라는 단백질의 315번 자리에 새로 생긴 " +
                        "변화 — 문지기가 바뀌어서 예전 약이 더 이상 안 듣는대요. 신입 연구원인 당신이 이번엔 그 " +
                        "문지기를 피해가는 새로운 열쇠를 찾아 주세요!";

        quest.structureStreamingPath = "structures/P00519.json";
        quest.mutationResidueIds = new[] { 315 };
        quest.targetPocketLabel = "문지기가 바뀐 ATP 결합 포켓 (Gatekeeper T315I)";
        quest.mutationSiteAlias = "문지기 자리";

        quest.stages = new[]
        {
            new QuestStageBriefing
            {
                stage = Stage.Quest1_DiseaseAnalysis,
                title = "1화 · 첫 번째 승리",
                objective = "예전에 이 사건을 해결했던 방법과, 표적이 된 ABL1 단백질의 315번 자리를 되짚어 봐요.",
                assistantLines = new[]
                {
                    "다시 만나서 반가워요! 이번엔 예전에 우리가 해결했던 사건이 돌아왔어요.",
                    "만성골수성백혈병이라는 병은 BCR-ABL이라는 융합 단백질이 계속 '자라라!' 신호를 보내서 생겨요. 그때 Imatinib이라는 열쇠로 이 신호를 멈췄었죠.",
                    "그 열쇠는 ABL1 단백질의 315번 자리 근처, ATP가 들어가는 좁은 문 앞에서 딱 맞물렸었어요.",
                },
                hints = new[] { "315번 자리를 기억해 두세요. 이번 사건의 진짜 무대예요." },
                llmContext = "BCR-ABL은 정상 ABL1 유전자가 BCR 유전자와 융합되며 항상 켜진 상태로 고정된 " +
                             "키나아제다. Imatinib 같은 ATP 경쟁적 억제제는 ATP가 들어가는 자리(포켓)를 막아 " +
                             "신호를 끈다. 이 포켓 입구에는 '문지기(gatekeeper)'라 부르는 아미노산이 있는데, " +
                             "정상일 때는 Thr(트레오닌)315번이다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest2_ProteinStructure,
                title = "2화 · 18개월 후, 돌아온 신호",
                objective = "커진 315번 자리 곁사슬을 찾아, 무엇이 달라졌는지 확인해 봐요.",
                assistantLines = new[]
                {
                    "⏳ 18개월 후... 화면이 잠깐 어두워졌다가, 다시 같은 자리를 비춰 드릴게요.",
                    "315번 자리를 다시 봐 주세요. 작았던 Thr(트레오닌)가 훨씬 커다란 Ile(아이소류신)로 바뀌었어요.",
                    "저 빨갛게 빛나는 곳이 바로 그 자리예요. 곁사슬이 커지면서 예전 열쇠가 들어가던 틈을 막아 버렸어요.",
                },
                hints = new[] { "315번 자리를 클릭해 보세요. 빨갛게 지직거리며 커진 게 보일 거예요." },
                llmContext = "T315I는 ABL1의 문지기 잔기 Thr315이 Ile(아이소류신)로 바뀌는 돌연변이다. " +
                             "아이소류신은 곁사슬이 더 크고 소수성이라, 기존 ATP 경쟁 억제제(예: Imatinib)가 " +
                             "자리 잡던 공간을 물리적으로 막아버린다(steric hindrance). 이 때문에 한 번 들었던 " +
                             "약이 더 이상 듣지 않게 된다 — 이것이 임상에서 가장 흔한 내성 메커니즘 중 하나다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest3_TargetDiscovery,
                title = "3화 · 문지기가 된 아미노산",
                objective = "커진 문지기를 피해서 접근해야 하는 ATP 포켓의 새로운 모양을 살펴봐요.",
                assistantLines = new[]
                {
                    "이 포켓은 예전과 똑같은 자리이지만, 315번 문지기 때문에 입구 모양이 완전히 달라졌어요.",
                    "이제 필요한 건 이 문지기를 정면으로 밀어붙이는 열쇠가 아니라, 살짝 돌아가는 열쇠예요.",
                },
                hints = new[] { "포켓 안쪽 다른 잔기들(248, 271, 317, 318번 등)은 그대로예요 — 오직 315번만 달라졌어요." },
                llmContext = "ATP 포켓의 나머지 결합 잔기(hinge, DFG motif 등)는 그대로 유지되므로, 새로운 " +
                             "억제제는 이 잔기들과의 상호작용은 지키면서 315번 근처만 피해가도록 설계돼야 " +
                             "한다. 이것이 Ponatinib 같은 3세대 억제제가 채택한 전략이다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest4_CandidateEvaluation,
                title = "4화 · 다섯 개의 열쇠",
                objective = "후보물질 5종 중, 커진 문지기를 피해 포켓에 안착하는 진짜 열쇠를 찾아봐요.",
                assistantLines = new[]
                {
                    "후보물질을 다섯 개 준비했어요. Imatinib, Nilotinib, Dasatinib은 전에도 쓰였던 익숙한 열쇠들이고, 나머지 둘은 이번 사건을 위해 새로 나온 후보예요.",
                    "하나씩 포켓에 가져다 대 봐요. 커진 315번 자리를 피해가는 모양인지 잘 살펴보세요.",
                },
                hints = new[]
                {
                    "결합력(친화도) 숫자가 마이너스로 클수록 더 세게 붙는다는 뜻이에요.",
                    "곧게 뻗은 좁은 연결고리를 가진 열쇠가 좁아진 틈을 통과하기 유리해요.",
                },
                llmContext = "Imatinib-like/Nilotinib-like는 T315I 부근에서 steric clash로 막히고, " +
                             "Dasatinib-like는 다른 결합 모드를 쓰지만 여전히 안정적으로 고정되지 못한다. " +
                             "Ponatinib-like만이 직선형 ethynyl(삼중결합) 링커로 Ile315의 커진 곁사슬을 " +
                             "비켜가며 포켓 깊숙이 도킹한다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest5_Verification,
                title = "5화 · 구조가 만든 승리",
                objective = "실패한 시도들과 성공한 결합을 비교하고, 이번 사건에서 배운 교훈을 정리해요.",
                assistantLines = new[]
                {
                    "돌아보면 Imatinib, Nilotinib, Dasatinib 모두 315번 자리에서 막혔었죠.",
                    "하지만 Ponatinib은 곧게 뻗은 연결고리 하나로 그 벽을 피해갔어요. 키나아제 활동이 완전히 멈췄어요!",
                    "약물의 아주 작은 구조적 변화가 내성을 이겨내느냐 마느냐를 가르는 거예요. (A small structural change in a drug can determine whether resistance is overcome.)",
                },
                hints = new[]
                {
                    "궁금하면 물어봐 주세요 — Asciminib이라는 약은 아예 다른 자리(미리스토일 포켓)를 노려서 이 문지기를 신경 쓸 필요가 없어요.",
                },
                llmContext = "이 퀘스트의 핵심 교훈은 '작은 구조 변화가 내성 극복을 가른다'는 것이다. " +
                             "Ponatinib은 ethynyl linker로 gatekeeper 우회에 성공한 대표 사례다. 보너스로, " +
                             "Asciminib은 ATP 포켓이 아닌 myristoyl 부위에 결합하는 알로스테릭 억제제로, " +
                             "T315I의 영향을 아예 받지 않는 또 다른 전략을 보여준다(STAMP 억제제).",
            },
        };

        quest.candidates = new[]
        {
            new CandidateCompound
            {
                displayName = "Imatinib-like",
                subtitle = "1세대 ATP 경쟁 저해제",
                features = "Thr315 인식과 ATP pocket의 기존 형상에 의존하는 1세대 저해제",
                isCorrect = false,
                affinityKcalPerMol = 2.4f,
                resultMessage = "T315I 자리에서 막혔어요! 커진 Ile315 곁사슬이 기존 결합 자리를 가로막고 " +
                                "있어요. 예전 상호작용은 끊어지고 튕겨 나왔어요.",
            },
            new CandidateCompound
            {
                displayName = "Nilotinib-like",
                subtitle = "2세대 고친화도 저해제",
                features = "Imatinib보다 결합력을 높였지만 T315I엔 여전히 취약함",
                isCorrect = false,
                affinityKcalPerMol = 1.6f,
                resultMessage = "포켓 안쪽까지는 들어왔지만, 문지기 자리 바로 옆에서 충돌했어요. 결합력이 " +
                                "높아도 이 장벽은 넘지 못하네요.",
            },
            new CandidateCompound
            {
                displayName = "Dasatinib-like",
                subtitle = "2세대 이중결합모드 저해제",
                features = "다른 결합 모드를 쓰지만 T315I에는 충분한 활성을 보이지 못함",
                isCorrect = false,
                affinityKcalPerMol = 0.6f,
                resultMessage = "포켓에 들어가긴 했지만 안정적으로 자리 잡지 못하고 다시 튕겨 나왔어요. " +
                                "결합 방식은 달라도 결국 같은 저항 장벽에 막히네요.",
            },
            new CandidateCompound
            {
                displayName = "Ponatinib-like",
                subtitle = "직선형 에티닐 링커 저해제",
                features = "직선형 ethynyl(삼중결합) 링커로 T315I gatekeeper 주변을 회피하도록 설계됨",
                isCorrect = true,
                affinityKcalPerMol = -9.4f,
                resultMessage = "성공! 곧게 뻗은 에티닐 연결고리가 커진 Ile315을 피해 포켓 깊숙이 정확히 " +
                                "안착했어요. 키나아제 활동이 멈췄습니다!",
            },
            new CandidateCompound
            {
                displayName = "Asciminib-like",
                subtitle = "미리스토일 부위 결합 (보너스)",
                features = "ATP pocket을 직접 경쟁하지 않고 myristoyl 부위라는 다른 결합 부위를 이용함",
                isCorrect = false,
                affinityKcalPerMol = 0f,
                resultMessage = "이 화합물은 ATP 포켓과 자리를 다투지 않아요 — 반대쪽 끝의 '미리스토일 " +
                                "자리'라는 완전히 다른 곳에 붙는 방식이라, 여기서는 가까이 가자마자 밀려났어요.",
            },
        };
    }

    /// <summary>
    /// 사건 4: 닫혀버린 문 (CFTR F508del).
    /// 앞선 세 사건과 달리 정답이 하나가 아니라 "corrector로 세포막 도달 -> potentiator로
    /// channel 개방" 두 단계를 순서대로 완료해야 하는 시나리오다. DockingQuestController가
    /// CompoundData.requires_prior_success_id/completes_stage를 봐서 순서를 강제한다 —
    /// potentiator(ivacaftor_like)를 먼저 고르면 오답이 아니라 "순서 오류" 안내로 corrector
    /// 단계로 되돌린다. 구조는 AlphaFold 예측이 아니라 실제 cryo-EM 구조(RCSB 8EJ1)를 쓴다 —
    /// F508del은 결실(deletion)이라 508번 자리가 구조에 아예 존재하지 않기 때문에,
    /// AlphaFold(WT 서열 예측)로는 이 결실 자체를 표현할 수 없다.
    /// </summary>
    private static void FillCftrF508del(QuestDefinition quest)
    {
        quest.questId = "cftr_f508del";
        quest.title = "CFTR F508del";
        quest.subtitle = "사건 4: 닫혀버린 문";
        quest.gene = "CFTR";
        quest.mutation = "F508del";
        quest.difficulty = 4;
        quest.accent = new Color(0.3f, 0.9f, 0.65f);
        quest.summary = "이번엔 폐 이야기예요. 낭포성 섬유증을 앓는 환자의 세포막에는 염소 이온이 드나드는 " +
                        "문(CFTR 채널)이 있어야 하는데, 508번 자리의 아미노산이 통째로 빠지면서 그 문이 " +
                        "세포막까지 가지도 못하고 있대요. 신입 연구원인 당신이 이번엔 두 가지 약을 순서대로 " +
                        "조합해서 문을 다시 열어 주세요!";

        quest.structureStreamingPath = "structures/8EJ1.json";
        quest.mutationResidueIds = new[] { 507, 509 }; // 508 자체는 결실이라 구조에 없음 — 양옆 잔기로 표시
        quest.targetPocketLabel = "F508 결실 주변, NBD1 접힘 자리";
        quest.mutationSiteAlias = "빠진 글자 자리";

        quest.stages = new[]
        {
            new QuestStageBriefing
            {
                stage = Stage.Quest1_DiseaseAnalysis,
                title = "1화 · 숨쉬기 힘든 이유",
                objective = "낭포성 섬유증이 왜 생기는지, CFTR 단백질의 508번 자리에 무슨 일이 일어났는지 알아봐요.",
                assistantLines = new[]
                {
                    "이번 사건은 폐와 관련이 있어요. 낭포성 섬유증이라는 병에 걸린 환자의 세포를 살펴볼 거예요.",
                    "CFTR이라는 단백질은 원래 세포막에서 염소 이온(Cl⁻)이 드나드는 문 역할을 해요. 그런데 이 환자는 그 문이 아예 세포막까지 가지도 못했대요.",
                    "설계도를 보니 508번 자리의 아미노산 하나가 통째로 빠져 있어요. 이게 F508del이라는 돌연변이예요.",
                },
                hints = new[] { "508번 자리를 찾아보세요 — 그런데 그 자리는 통째로 비어있을 거예요. 그게 바로 단서예요." },
                llmContext = "CFTR은 상피세포 막에서 Cl- 이온을 내보내는 통로 단백질이다. F508del은 508번 " +
                             "페닐알라닌이 통째로 없어지는 결실(deletion) 돌연변이로, 단백질이 제대로 접히지 " +
                             "못해(misfolding) 세포 안(소포체)에서 분해되고, 세포막까지 도달하지 못한다. 그 " +
                             "결과 Cl- 이동과 물 이동이 막혀 점액이 끈적해진다(낭포성 섬유증).",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest2_ProteinStructure,
                title = "2화 · 불안정한 구조",
                objective = "접히지 못하고 불안정하게 흔들리는 CFTR의 507/509번 자리 주변을 살펴봐요.",
                assistantLines = new[]
                {
                    "이 구조를 보세요 — 508번 자리가 있어야 할 곳이 비어 있어서 그 주변(507, 509번)이 계속 흔들리고 있어요.",
                    "이렇게 불안정한 모양으로는 세포가 '불량품'이라고 판단해서 밖으로 내보내지 않고 분해해 버려요.",
                },
                hints = new[] { "507번과 509번 자리를 클릭해 보세요. 빨갛게 지직거리며 흔들리고 있어요." },
                llmContext = "F508del CFTR은 NBD1 도메인의 folding이 불안정해지고, NBD1과 막관통 도메인(TMD) " +
                             "사이의 조립도 약해진다. 세포는 품질관리 시스템(ERAD)을 통해 이 불안정한 단백질을 " +
                             "인식해 분해해 버리므로, 애초에 세포막에 도달하는 양 자체가 크게 줄어든다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest3_TargetDiscovery,
                title = "3화 · 두 가지 문제, 두 가지 해결책",
                objective = "이 CFTR에게 필요한 두 가지 도움 — '접히도록 돕는 것'과 '열리도록 돕는 것'을 구분해 봐요.",
                assistantLines = new[]
                {
                    "이 사건은 문제가 두 개예요. 첫째, 접힘이 불안정해서 세포막까지 못 간다는 것. 둘째, 설령 막에 도달해도 문이 잘 안 열린다는 것.",
                    "그래서 약도 두 종류가 필요해요 — 접히게 도와주는 'corrector'와, 문을 열어 주는 'potentiator'.",
                },
                hints = new[] { "순서가 중요해요 — 문을 열어 줄 약도, 문 자체가 세포막에 없으면 소용없겠죠?" },
                llmContext = "Corrector(예: Lumacaftor, Tezacaftor, Elexacaftor)는 CFTR의 folding/조립을 도와 " +
                             "세포막 도달량(surface CFTR)을 늘린다. Potentiator(예: Ivacaftor)는 이미 막에 " +
                             "도달한 CFTR의 channel gating(열리는 정도)을 늘린다. 두 기전은 독립적이라, " +
                             "potentiator만으로는 애초에 막에 CFTR이 거의 없어 효과가 매우 제한적이다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest4_CandidateEvaluation,
                title = "4화 · 다섯 개의 약, 두 번의 선택",
                objective = "후보물질 5종 중, 먼저 CFTR을 세포막으로 보내줄 corrector를 찾고, 그다음 문을 " +
                            "열어줄 potentiator를 찾아봐요.",
                assistantLines = new[]
                {
                    "후보물질을 다섯 개 준비했어요. 이번엔 정답이 두 개예요 — 순서대로 골라야 해요.",
                    "Corrector부터 골라서 CFTR을 세포막까지 보내고, 그다음 potentiator로 문을 열어 주세요.",
                    "게임에서 이 순서로 배우는 건 '작용 원리를 이해하기 위한 순서'예요 — 실제 병원에서 약을 먹는 순서(복약 순서)와는 다를 수 있어요. (Mechanistic learning sequence ≠ clinical dosing sequence)",
                },
                hints = new[]
                {
                    "Potentiator를 먼저 써보면, '아직은 이르다'는 안내가 나올 거예요 — 그럼 corrector부터 다시 시작해 보세요.",
                    "Lumacaftor 같은 초기 세대 corrector는 일부만 회복시켜 줘요. 최종 완료엔 더 강력한 조합이 필요해요.",
                },
                llmContext = "정답은 corrector_pair(Elexacaftor+Tezacaftor 유사) -> ivacaftor_like(potentiator) " +
                             "순서로 두 화합물을 모두 성공시켜야 완료된다. Ivacaftor-like를 먼저 고르면 " +
                             "오답이 아니라 '순서 오류' 안내가 나오고, corrector 단계로 돌아가라는 힌트를 " +
                             "준다. Lumacaftor-like는 부분 회복만 보여주고 최종 완료 조건은 아니다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest5_Verification,
                title = "5화 · 다시 열린 문",
                objective = "채널이 열리고 Cl⁻ 이온이 흐르기 시작한 결과를 확인하고 사건을 마무리해요.",
                assistantLines = new[]
                {
                    "corrector와 potentiator가 함께 작용하니 CFTR이 드디어 세포막에서 문을 열었어요!",
                    "Cl⁻ 이온이 흐르기 시작하면서 기도 표면의 액체층(airway surface liquid)이 늘고, 끈적했던 점액도 묽어지고 있어요.",
                    "섬모(cilia)도 다시 움직이기 시작했어요 — 이제 점액을 밖으로 밀어낼 수 있어요!",
                },
                hints = new[]
                {
                    "약이 하나가 아니라 두 개가 함께 필요했다는 걸 기억해 주세요 — 접히는 문제와 열리는 문제는 서로 다른 문제였어요.",
                },
                llmContext = "Corrector+potentiator 병용(예: Trikafta = Elexacaftor/Tezacaftor/Ivacaftor)은 " +
                             "surface CFTR을 늘리고 channel gating도 함께 개선해, 상피세포의 Cl-/수분 이동을 " +
                             "회복시킨다. 그 결과 airway surface liquid가 늘고 점액 점도가 낮아지며 섬모 " +
                             "운동(mucociliary clearance)이 회복된다.",
            },
        };

        quest.candidates = new[]
        {
            new CandidateCompound
            {
                displayName = "Corrector Pair — Elexacaftor + Tezacaftor-like",
                subtitle = "상보적 Corrector 조합",
                features = "상보적인 corrector 작용으로 F508del CFTR의 folding/domain assembly와 " +
                           "세포막 도달량을 개선",
                isCorrect = true,
                affinityKcalPerMol = -7.8f,
                resultMessage = "Snap! 상보적인 corrector 작용으로 CFTR의 접힘과 도메인 조립이 좋아지고, " +
                                "세포막까지 도달하는 양이 늘었어요. Surface CFTR ↑",
            },
            new CandidateCompound
            {
                displayName = "Ivacaftor-like",
                subtitle = "Potentiator",
                features = "세포막에 도달한 CFTR의 channel opening을 증가시킴",
                isCorrect = true,
                affinityKcalPerMol = -8.5f,
                resultMessage = "성공! 막에 자리 잡은 CFTR의 문(gate)이 열리면서 Cl⁻ 이온이 흐르기 " +
                                "시작했어요. Channel activity ↑",
            },
            new CandidateCompound
            {
                displayName = "Lumacaftor-like",
                subtitle = "초기 세대 Corrector",
                features = "F508del CFTR을 rescue하는 corrector 계열이지만 최신 조합 대비 제한적인 회복만 보여줌",
                isCorrect = false,
                affinityKcalPerMol = -3.0f,
                resultMessage = "구조 흔들림이 조금 줄었어요 — Surface CFTR: LOW → PARTIAL. 다음 단계엔 " +
                                "더 효과적인 corrector 조합이 필요해요.",
            },
            new CandidateCompound
            {
                displayName = "Proteasome Inhibitor-like",
                subtitle = "경로 차단",
                features = "misfolded protein의 분해 자체를 막으려는 접근",
                isCorrect = false,
                affinityKcalPerMol = 1.5f,
                resultMessage = "분해 신호는 줄었지만 불안정한 CFTR 상태는 그대로예요. ER stress 경고가 " +
                                "떴어요. Blocking disposal does not correct folding.",
            },
            new CandidateCompound
            {
                displayName = "KRAS G12C Inhibitor-like",
                subtitle = "표적 불일치",
                features = "사건 1(KRAS)에서 사용한 mutant KRAS 표적 저해제 계열",
                isCorrect = false,
                affinityKcalPerMol = 4.0f,
                resultMessage = "CFTR 근처에서는 결합 자리가 전혀 맞지 않아요. Right drug class, wrong molecular target.",
            },
        };
    }

    /// <summary>
    /// 사건 5: 뜨거워지면 무너지는 방패 (p53 Y220C).
    /// 구조는 2026년에 막 공개된 실제 결정구조(RCSB 9S9O — Mavridi et al., Cell Death Dis 2026,
    /// "Targeting the p53 cancer mutants Y220C ... with the small-molecule stabilizer rezatapopt")를
    /// 직접 파싱해 썼다. 정답 후보물질(p53_stabilizer.json)도 이 구조에서 실제로 결합해 있던
    /// Rezatapopt 리간드 좌표를 그대로 쓰고, 부분 정답(p53_fragment.json)은 2VUK의 PhiKan083
    /// 좌표를 쓴다 — 둘 다 가상의 화합물이 아니라 실재하는 분자다.
    /// </summary>
    private static void FillP53Y220C(QuestDefinition quest)
    {
        quest.questId = "p53_y220c";
        quest.title = "p53 Y220C";
        quest.subtitle = "사건 5: 뜨거워지면 무너지는 방패";
        quest.gene = "TP53";
        quest.mutation = "Y220C";
        quest.difficulty = 5;
        quest.accent = new Color(1f, 0.45f, 0.3f);
        quest.summary = "p53은 손상된 세포가 계속 자라지 못하게 막는, 우리 몸의 '방패' 같은 단백질이에요. " +
                        "그런데 220번 자리가 바뀐 세포에서는 이 방패가 체온 정도의 열에도 흔들리다가 " +
                        "제 역할을 못 하게 된대요. 신입 연구원인 당신이 이 흔들리는 방패를 다시 " +
                        "단단하게 붙잡아 줄 분자를 찾아 주세요!";

        quest.structureStreamingPath = "structures/P53_Y220C_9S9O.json";
        quest.mutationResidueIds = new[] { 220 };
        quest.targetPocketLabel = "220번 자리 옆에 새로 생긴 틈 (Y220C pocket)";
        quest.mutationSiteAlias = "무너진 틈";

        quest.stages = new[]
        {
            new QuestStageBriefing
            {
                stage = Stage.Quest1_DiseaseAnalysis,
                title = "1화 · 흔들리는 방패",
                objective = "p53이라는 방패 단백질에서, 220번 자리가 바뀌며 생긴 문제를 확인해요.",
                assistantLines = new[]
                {
                    "p53은 손상된 세포가 더 퍼지지 않게 막는 방패 단백질이에요. '유전체의 수호자'라고도 불려요.",
                    "그런데 220번 자리의 아미노산 하나가 바뀌면서, 방패의 접힘이 헐거워졌어요.",
                    "헐거워진 접힘은 온도에 특히 약해요. 몸속 온도(37°C) 정도에서도 흔들릴 수 있어요.",
                },
                hints = new[]
                {
                    "220번 자리를 찾아보세요. 다른 곳보다 유독 헐거워 보일 거예요.",
                    "정상 p53과 비교하면 이 자리 하나만 다르다는 걸 알 수 있어요.",
                },
                llmContext = "p53(TP53 유전자)은 DNA 손상을 감지해 세포주기를 멈추거나 세포를 없애는 " +
                             "종양억제단백질이다. Y220C는 접힘 안정성을 낮추는 '불안정화 변이'로, " +
                             "코돈을 직접 손상시키는 KRAS/EGFR류 변이와 달리 단백질이 정상적으로 " +
                             "번역되고도 온도에 취약해져 풀려버리는 것이 문제다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest2_ProteinStructure,
                title = "2화 · 온도를 올려보기",
                objective = "구조 안쪽 원자까지 들어가서, 온도를 올리면 이 방패가 얼마나 흔들리는지 관찰해요.",
                assistantLines = new[]
                {
                    "이 방패를 직접 데워 볼 거예요. 온도 조절기는 지금 보이는 리본이나 그다음 나선 단계에는 없어요.",
                    "구조를 눌러 원자 단계까지 들어가야 화면 아래에 조절기가 나와요. 거기서부터 온도를 올려 볼 수 있어요.",
                    "미리 말해 두면, 체온인 37도에서도 이미 꽤 불안정해요. 완전히 풀리진 않지만 원래보다 훨씬 약하다는 게 이 사건의 핵심이에요.",
                    "얼마나 약한지는 직접 온도를 올려 보면 바로 보일 거예요. 들어가 볼까요?",
                },
                hints = new[]
                {
                    "리본 → 나선 → 원자 순서로 두 번 누르면 화면 아래에 온도 조절기가 나타나요. 앞의 두 단계에는 조절기가 없어요.",
                    "조절기가 나오면 37도 근처에서 멈춰 화면에 뜨는 안내문을 읽어 보세요.",
                },
                llmContext = "실제 논문 데이터에 기반한 연출이다 — Y220C는 야생형 p53보다 열 안정성이 " +
                             "낮아 생리 온도에서도 unfolding 경향이 두드러진다. 다만 '완전히 풀린다'가 " +
                             "아니라 '정상보다 훨씬 불안정하다'가 정확한 표현이라 화면에서도 그렇게 " +
                             "표현한다. 온도-wobble-투명도-응집 입자는 실제 분자동역학이 아니라 이 정도 " +
                             "불안정성을 보여주기 위한 시각적 장치다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest3_TargetDiscovery,
                title = "3화 · 새로 생긴 틈",
                objective = "220번 자리 바뀌면서 새로 생긴 작은 틈(포켓)을 찾아 표시해요.",
                assistantLines = new[]
                {
                    "방패 표면 안쪽으로 들어가 볼게요. 여기, 원래는 없던 작은 틈이 보이시죠?",
                    "이 틈은 방패가 헐거워지면서 생긴 자리예요. 여기에 뭔가를 끼워 넣으면 다시 조여 줄 수 있어요.",
                },
                hints = new[]
                {
                    "220번 자리 근처를 살펴보세요. 다른 곳보다 빈 공간이 넓어요.",
                    "이 틈은 정상 p53에는 없어요 — 이 변이에서만 생기는 자리예요.",
                },
                llmContext = "이 포켓은 Y220C 변이로 소수성 코어에 생기는 표면 결함(surface crevice)이다. " +
                             "PhiKan083(2008, 2VUK) 같은 초기 fragment부터 최근 rezatapopt(2026, 9S9O)까지, " +
                             "이 틈에 딱 맞는 분자를 채워 넣어 접힘을 다시 조여주는 'mutant-selective " +
                             "stabilizer' 전략이 실제로 연구되고 있다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest4_CandidateEvaluation,
                title = "4화 · 방패를 다시 조이기",
                objective = "후보물질 5종 중, 이 틈에 맞고 방패를 실제로 단단하게 만들어 주는 것을 찾아봐요.",
                assistantLines = new[]
                {
                    "후보물질을 다섯 개 준비했어요. 하나씩 틈에 가져다 대 봐요.",
                    "틈에 들어가는 것과, 실제로 흔들림을 잡아 주는 건 다른 문제예요. 결과 화면(HUD)을 잘 보세요.",
                },
                hints = new[]
                {
                    "작게 들어맞는 것과 딱 맞게 최적화된 것은 안정화 효과가 달라요.",
                    "이 틈이 아니라 완전히 다른 곳(다른 단백질)을 노리는 후보도 섞여 있어요.",
                    "틈에 닿기만 하고 실제로 붙잡지 못하는 후보도 있어요 — wobble이 그대로인지 확인해 보세요.",
                },
                llmContext = "다섯 후보는 실제 신약개발 단계를 압축해 보여준다: PhiKan083-like(초기 fragment, " +
                             "부분 정답)와 rezatapopt-like(최적화된 stabilizer, 정답)는 실존 분자 좌표를 " +
                             "그대로 쓴다. 나머지 셋(Nutlin-like MDM2 억제제, 비특이적 Cys 반응기, " +
                             "비선택적 결합체)은 '왜 틀렸는지'가 서로 다른 가상의 오답이다 — 같은 도킹 " +
                             "판정 로직에 서로 다른 결과 문구만 연결했다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest5_Verification,
                title = "5화 · 37°C에서 다시 확인하기",
                objective = "체온(37°C)에서 안정화 전/후를 비교하고, 방패가 다시 제 역할(DNA 결합)을 하는지 확인해요.",
                assistantLines = new[]
                {
                    "안정화제를 붙인 채로 다시 37°C로 맞춰 볼게요. Before/After를 비교해 봐요.",
                    "Wobble이 크게 줄고 Stability가 올라갔어요! 이제 방패가 원래 하던 일을 다시 할 수 있어요.",
                    "방패 네 개가 모여 DNA를 붙잡는 장면을 보여 드릴게요 — 이게 p53이 원래 하는 일이에요.",
                },
                hints = new[]
                {
                    "Before는 안정화 전, After는 안정화 후 같은 온도에서의 비교예요.",
                    "DNA에 붙는 장면에서 안정화제 분자 자체가 등장하지 않는 건, 그 분자가 방패를 고쳐 준 것이지 방패 네 개가 뭉치는 과정 자체를 만든 게 아니기 때문이에요.",
                },
                llmContext = "DBD(DNA-binding domain)가 안정화되면 그 결과로 정상적인 사량체(tetramer) " +
                             "형성과 DNA 결합 능력이 회복된다는 것이 핵심이다. Rezatapopt 같은 stabilizer는 " +
                             "DBD 접힘을 도와줄 뿐 사량체화 자체를 새로 만들어내는 게 아니므로, 연출에서도 " +
                             "안정화제 분자는 DNA 결합 장면에 등장시키지 않는다.",
            },
        };

        quest.candidates = new[]
        {
            new CandidateCompound
            {
                displayName = "PhiKan083-like Weak Fragment",
                subtitle = "부분 정답 — 초기 Fragment Hit (2VUK 실제 좌표)",
                features = "Y220C mutation-induced pocket에 들어가지만 안정화 효과가 제한적인 초기 fragment",
                isCorrect = false,
                affinityKcalPerMol = -3.1f,
                resultMessage = "Useful fragment hit, but stabilization is insufficient.",
            },
            new CandidateCompound
            {
                displayName = "Rezatapopt-like Stabilizer",
                subtitle = "정답 — Optimized Stabilizer (9S9O 실제 좌표)",
                features = "Y220C pocket에 최적화된 mutant-selective stabilizer",
                isCorrect = true,
                affinityKcalPerMol = -9.2f,
                resultMessage = "Mutant-selective stabilization.",
            },
            new CandidateCompound
            {
                displayName = "Nutlin-like MDM2 Inhibitor",
                subtitle = "오답 1 — 다른 p53 전략",
                features = "p53-MDM2 상호작용을 차단해 p53 분해를 억제하려는 전략. Y220C의 구조적 " +
                           "불안정성 자체는 교정하지 않음",
                isCorrect = false,
                affinityKcalPerMol = 2.5f,
                resultMessage = "More mutant p53 is not the same as functional p53.",
            },
            new CandidateCompound
            {
                displayName = "Generic Cys-reactive Warhead",
                subtitle = "오답 2 — Covalency만 의존",
                features = "Cys220과 반응할 가능성만 노리는 가상의 electrophilic compound. 포켓 적합성과 " +
                           "안정화 상호작용은 부족",
                isCorrect = false,
                affinityKcalPerMol = 0.8f,
                resultMessage = "Covalency alone does not guarantee conformational rescue.",
            },
            new CandidateCompound
            {
                displayName = "Non-selective Surface Binder",
                subtitle = "오답 3 — 선택성 결여",
                features = "변이 포켓에 특이적이지 않고 여러 단백질 표면에 비선택적으로 결합하는 가상 후보",
                isCorrect = false,
                affinityKcalPerMol = 1.5f,
                resultMessage = "A stabilizer must also be selective for the intended target state.",
            },
        };
    }

    // --- 씬 오브젝트 ---

    private static void BuildSceneObjects(QuestCatalog catalog)
    {
        EnsureEventSystem();

        GameObject flowGo = FindOrCreate("GameFlow");
        QuestSession session = GetOrAdd<QuestSession>(flowGo);
        IntroDirector director = GetOrAdd<IntroDirector>(flowGo);
        AIChatBackend client = EnsureChatBackend(flowGo);

        // 씬에 이미 있는 것들을 찾아 잇는다.
        var proteinLoader = Object.FindFirstObjectByType<ProteinLoader>();
        var highlighter = Object.FindFirstObjectByType<MutationHighlighter>();
        var questPanel = Object.FindFirstObjectByType<QuestManagerSpatialUI>();
        // AI 비서는 Play 전에는 비활성 상태로 씬에 있으므로 비활성까지 뒤져야 찾는다.
        var follower = Object.FindFirstObjectByType<AIAssistantFollower>(FindObjectsInactive.Include);

        session.proteinLoader = proteinLoader;
        session.mutationHighlighter = highlighter;
        session.questPanel = questPanel;

        GameObject boardGo = FindOrCreate("QuestBoard");
        QuestSelectionBoard board = GetOrAdd<QuestSelectionBoard>(boardGo);
        board.catalog = catalog;
        if (Camera.main != null) board.lookTarget = Camera.main.transform;

        director.session = session;
        director.board = board;
        director.questAnchor = proteinLoader != null ? proteinLoader.transform : null;
        if (Camera.main != null) director.targetCamera = Camera.main;

        // 비서에 두뇌를 붙인다. 비서 자체는 [AI 비서 생성] 메뉴로 미리 만들어 둬야 한다.
        if (follower != null)
        {
            AIAssistantBrain brain = GetOrAdd<AIAssistantBrain>(follower.gameObject);
            brain.client = client;
            brain.session = session;
            brain.follower = follower;
            brain.mutationHighlighter = highlighter;
            brain.visual = follower.GetComponentInChildren<AIAssistantVisual>(true);
            brain.bubble = follower.GetComponentInChildren<AIAssistantSpeechBubble>(true);
            director.assistant = brain;

            EditorUtility.SetDirty(brain);
        }
        else
        {
            Debug.LogWarning("[IntroSetup] 씬에서 AI 비서를 찾지 못했습니다. " +
                             "Tools > Taming Mutants > AI 비서 생성 을 먼저 실행한 뒤 다시 실행하세요.");
        }

        EditorUtility.SetDirty(session);
        EditorUtility.SetDirty(director);
        EditorUtility.SetDirty(board);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Selection.activeGameObject = flowGo;
    }

    /// <summary>
    /// EventSystem이 없으면 UI 클릭이 전혀 동작하지 않는다.
    ///
    /// 이 프로젝트는 Active Input Handling이 Input System 패키지 전용(activeInputHandler: 1)이라
    /// 구형 StandaloneInputModule은 UnityEngine.Input을 읽다가 예외를 던지고 죽는다.
    /// 반드시 InputSystemUIInputModule을 붙여야 한다.
    /// </summary>
    /// <summary>
    /// LLM 백엔드를 확보한다. 씬에 이미 어떤 구현체가 있으면 그대로 두고,
    /// 없을 때만 SolarChatClient를 새로 붙인다.
    /// (개발 중에는 Solar 직접 호출, 배포 때는 AICoScientistClient 프록시로 교체하는 전제)
    /// </summary>
    private static AIChatBackend EnsureChatBackend(GameObject flowGo)
    {
        AIChatBackend existing = Object.FindFirstObjectByType<AIChatBackend>();
        if (existing != null)
        {
            Debug.Log($"[IntroSetup] 기존 LLM 백엔드 '{existing.GetType().Name}'를 그대로 사용합니다.");
            return existing;
        }

        SolarChatClient solar = Undo.AddComponent<SolarChatClient>(flowGo);
        Debug.Log("[IntroSetup] SolarChatClient를 추가했습니다. " +
                  "GameFlow 인스펙터에서 API 키를 넣거나 UPSTAGE_API_KEY 환경변수를 설정하세요.");
        return solar;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem",
            typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
    }

    // --- 작은 헬퍼 ---

    private static GameObject FindOrCreate(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null) return existing;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        return go;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }

    private static T LoadOrCreate<T>(string assetPath) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset != null) return asset;

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, assetPath);
        return asset;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        var parts = new List<string>(folder.Split('/'));
        string current = parts[0]; // "Assets"

        for (int i = 1; i < parts.Count; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
