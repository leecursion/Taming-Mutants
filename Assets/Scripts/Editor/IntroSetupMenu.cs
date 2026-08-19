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

        QuestCatalog catalog = LoadOrCreate<QuestCatalog>(CatalogPath);
        catalog.quests = new[] { kras, egfr };
        EditorUtility.SetDirty(catalog);

        return catalog;
    }

    /// <summary>설계서 PDF의 KRAS G12C 퀘스트.</summary>
    private static void FillKrasG12C(QuestDefinition quest)
    {
        quest.questId = "kras_g12c";
        quest.title = "KRAS G12C";
        quest.subtitle = "비소세포폐암 (NSCLC)";
        quest.gene = "KRAS";
        quest.mutation = "G12C";
        quest.difficulty = 3;
        quest.accent = new Color(0.35f, 0.85f, 1f);
        quest.summary = "12번 코돈의 글라이신이 시스테인으로 바뀌면서 KRAS가 항상 켜진 상태로 굳어집니다. " +
                        "새로 생긴 시스테인의 황 원자를 표적으로 삼아 공유결합 억제제를 설계하세요.";

        // 구조 데이터는 아직 없다. AlphaFold에서 P01116(KRAS)을 받아 파싱해 넣어야 한다.
        quest.structureStreamingPath = "structures/P01116.json";
        quest.mutationResidueIds = new[] { 12 };
        quest.targetPocketLabel = "Switch-II Pocket";

        quest.stages = new[]
        {
            new QuestStageBriefing
            {
                stage = Stage.Quest1_DiseaseAnalysis,
                title = "질병 원인 분석",
                objective = "폐 종양 조직에서 세포핵까지 내려가 KRAS 12번 코돈을 찾으세요.",
                assistantLines = new[]
                {
                    "폐 영역에 붉게 점멸하는 종양 조직이 보이시죠? 저기서부터 내려갑니다.",
                    "세포막을 통과해 핵 안으로 들어가면 DNA 이중나선이 눈앞에 펼쳐질 거예요.",
                    "12번 코돈의 GGT를 TGT로 바꾸면 글라이신(Gly12)이 시스테인(Cys12)으로 바뀝니다.",
                },
                hints = new[]
                {
                    "점멸하는 위치가 12번 코돈입니다. 나선을 따라 위로 훑어보세요.",
                    "바꿀 염기는 첫 번째 G 하나뿐이에요. GGT에서 G를 T로.",
                },
                llmContext = "KRAS는 GTP/GDP 결합 상태로 세포 증식 신호를 켜고 끄는 스위치 단백질이다. " +
                             "12번 코돈의 글라이신은 GTP 가수분해에 필요한 좁은 공간을 만든다. " +
                             "여기가 부피 큰 잔기로 치환되면 GAP이 접근하지 못해 GTP 결합 상태로 고정되고, " +
                             "신호가 계속 켜진 채로 남아 종양이 생긴다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest2_ProteinStructure,
                title = "단백질 구조 분석",
                objective = "접힘이 끝난 변이 단백질을 야생형과 비교하고 B-factor 히트맵을 확인하세요.",
                assistantLines = new[]
                {
                    "바뀐 코돈이 리보솜에서 그대로 번역됩니다. 아미노산 사슬이 접히는 걸 지켜보세요.",
                    "접힘이 끝나면 B-factor 히트맵이 켜집니다. 붉게 요동치는 곳이 유연한 루프예요.",
                    "가장 크게 흔들리는 Switch-II 영역이 우리가 노릴 자리입니다.",
                },
                hints = new[]
                {
                    "야생형과 변이형을 나란히 두고 12번 위치만 비교해 보세요.",
                    "pLDDT가 70 아래인 구간은 예측 신뢰도가 낮으니 해석에 주의하세요.",
                },
                llmContext = "AlphaFold 예측 구조에서 B-factor 자리에는 pLDDT(예측 신뢰도)가 들어간다. " +
                             "Switch-I(30~40번)과 Switch-II(60~76번) 루프는 실제로 유연해 신뢰도가 낮게 나오며, " +
                             "이 유연성이 약물이 들어갈 포켓을 여닫는다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest3_TargetDiscovery,
                title = "치료 표적 발굴",
                objective = "Switch-II Pocket과 Cys12 잔기를 찾아 고정(Pin)하세요.",
                assistantLines = new[]
                {
                    "리본 표면을 뚫고 포켓 안으로 들어갑니다. 주변 잔기는 반투명하게 바뀔 거예요.",
                    "12번 시스테인의 황 원자가 보이면 그게 표적입니다. 선택해서 고정하세요.",
                    "황 원자는 반응성이 높아요. 여기에 걸 수 있으면 되돌릴 수 없는 결합이 됩니다.",
                },
                hints = new[]
                {
                    "Switch-II Pocket은 야생형에는 거의 없는 자리예요. 변이형에서만 열립니다.",
                    "잔기 번호 12를 찾으세요. 노란빛 황 원자가 포켓 안쪽을 향하고 있습니다.",
                },
                llmContext = "Switch-II Pocket(S-IIP)은 KRAS G12C의 GDP 결합 상태에서 열리는 알로스테릭 포켓이다. " +
                             "Cys12의 티올기(-SH)가 이 포켓 가장자리에 노출되어 공유결합 표적이 된다. " +
                             "야생형 KRAS에는 시스테인이 없어 같은 전략을 쓸 수 없고, 이것이 G12C 선택적 억제제의 근거다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest4_CandidateEvaluation,
                title = "후보물질 탐색·평가",
                objective = "후보물질 4종 중 Cys12와 공유결합을 형성할 수 있는 것을 고르세요.",
                assistantLines = new[]
                {
                    "후보물질 네 개를 준비했어요. 하나씩 포켓에 끌어다 놓아 보세요.",
                    "포켓에 들어가는 것만으로는 부족합니다. 황 원자와 결합을 만들 반응기가 있어야 해요.",
                },
                hints = new[]
                {
                    "포켓 모양에 맞는 것과, 실제로 결합을 만드는 것은 다른 문제예요.",
                    "Acrylamide 같은 반응기(Warhead)가 있는지 확인하세요.",
                    "친화도 값이 음수일수록 강하게 결합합니다. 양수면 오히려 밀어냅니다.",
                },
                llmContext = "공유결합 억제제는 표적 잔기와 비가역적 결합을 형성한다. " +
                             "Sotorasib(AMG 510)은 아크릴아마이드 warhead가 Cys12의 티올과 마이클 첨가 반응을 일으켜 " +
                             "KRAS를 GDP 결합(OFF) 상태로 잠근다. warhead가 없으면 포켓에 맞아도 붙잡히지 않는다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest5_Verification,
                title = "치료 효과 검증",
                objective = "HUD 대시보드에서 효능·독성·내성 지표를 확인하고 임상 승인 단계로 넘어가세요.",
                assistantLines = new[]
                {
                    "원자 공간에서 빠져나와 연구실로 돌아갑니다.",
                    "벽면 HUD에 종양 감소율, 정상세포 생존율, 내성 예측이 정렬됩니다.",
                    "야생형 KRAS는 건드리지 않았으니 정상세포 독성이 낮게 나올 거예요.",
                },
                hints = new[]
                {
                    "내성 예측이 높다면 결합 부위 주변에 2차 변이가 생길 여지가 있다는 뜻입니다.",
                },
                llmContext = "G12C 선택적 억제제는 야생형 KRAS를 건드리지 않아 정상 조직 독성이 낮다. " +
                             "다만 임상에서는 Y96D 같은 2차 변이나 우회 경로 활성화로 내성이 생길 수 있어 " +
                             "병용요법이 논의된다.",
            },
        };

        // 설계서 3장의 후보물질 표.
        quest.candidates = new[]
        {
            new CandidateCompound
            {
                displayName = "Compound A",
                subtitle = "AMG 510 / Sotorasib 유사체",
                features = "Switch-II Pocket 최적 피팅 구조 · Acrylamide Warhead 보유",
                isCorrect = true,
                affinityKcalPerMol = -8.3f,
                resultMessage = "결합했습니다! Cys12의 황 원자와 공유결합이 형성됐어요. " +
                                "친화도 -8.3 kcal/mol, KRAS가 OFF 상태로 잠겼습니다.",
            },
            new CandidateCompound
            {
                displayName = "Compound B",
                subtitle = "Non-covalent Analog",
                features = "Compound A와 외형은 비슷하지만 공유결합 반응기(Warhead)가 없음",
                isCorrect = false,
                affinityKcalPerMol = 3.5f,
                resultMessage = "포켓에는 들어갔는데 고정되지 않고 튕겨 나왔어요. " +
                                "Cys12와 공유결합을 형성할 Warhead가 없습니다!",
            },
            new CandidateCompound
            {
                displayName = "Compound C",
                subtitle = "Bulky Macrocycle",
                features = "분자 크기가 매우 크고 복잡한 형태",
                isCorrect = false,
                affinityKcalPerMol = 0f,
                resultMessage = "포켓 입구에서 걸렸습니다. 포켓의 3D 공간 대비 분자 구조가 너무 큽니다!",
            },
            new CandidateCompound
            {
                displayName = "Compound D",
                subtitle = "EGFR Inhibitor 타깃",
                features = "전혀 다른 3D 바인딩 셰이프",
                isCorrect = false,
                affinityKcalPerMol = 1.2f,
                resultMessage = "접근하자마자 밀려났어요. KRAS G12C 결합 부위와 구조적으로 상충합니다!",
            },
        };
    }

    /// <summary>지금 프로젝트에 실제 구조 데이터(P00533.json)가 있는 EGFR 사례.</summary>
    private static void FillEgfrL858R(QuestDefinition quest)
    {
        quest.questId = "egfr_l858r";
        quest.title = "EGFR L858R";
        quest.subtitle = "폐선암 (Lung adenocarcinoma)";
        quest.gene = "EGFR";
        quest.mutation = "L858R";
        quest.difficulty = 2;
        quest.accent = new Color(1f, 0.72f, 0.3f);
        quest.summary = "키나아제 도메인 858번 류신이 아르기닌으로 바뀌어 수용체가 리간드 없이도 활성화됩니다. " +
                        "T790M 저항성 변이까지 함께 살펴보세요.";

        quest.structureStreamingPath = "structures/P00533.json";
        quest.mutationResidueIds = new[] { 858, 790 };
        quest.targetPocketLabel = "ATP-binding pocket";

        quest.stages = new[]
        {
            new QuestStageBriefing
            {
                stage = Stage.Quest1_DiseaseAnalysis,
                title = "질병 원인 분석",
                objective = "EGFR 858번 잔기의 변이를 확인하세요.",
                assistantLines = new[]
                {
                    "EGFR 키나아제 도메인을 불러왔어요. 858번 잔기부터 봅시다.",
                    "류신이 아르기닌으로 바뀌면서 활성 루프가 열린 채로 굳었습니다.",
                },
                hints = new[] { "858번 잔기를 클릭해 보세요. 붉게 점멸하고 있습니다." },
                llmContext = "EGFR L858R은 활성화 루프의 소수성 잔기가 큰 염기성 잔기로 바뀌어 " +
                             "비활성 형태를 불안정하게 만들고 리간드 비의존적 활성화를 일으킨다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest2_ProteinStructure,
                title = "단백질 구조 분석",
                objective = "pLDDT 색상으로 예측 신뢰도가 낮은 구간을 찾으세요.",
                assistantLines = new[]
                {
                    "파란색일수록 예측 신뢰도가 높습니다. 노란빛 구간은 해석에 주의하세요.",
                },
                hints = new[] { "리본에서 아미노산 단위로 내려가면 원자별로 확인할 수 있어요." },
                llmContext = "AlphaFold의 B-factor 필드에는 pLDDT가 저장된다. 90 이상은 매우 높음, " +
                             "70~90은 신뢰할 만함, 50~70은 낮음, 50 미만은 무질서 영역일 가능성이 크다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest3_TargetDiscovery,
                title = "치료 표적 발굴",
                objective = "ATP 결합 포켓을 찾아 고정하세요.",
                assistantLines = new[] { "ATP가 들어앉는 자리를 찾으면 그게 표적입니다." },
                hints = new[] { "790번 잔기 근처를 살펴보세요. 저항성 변이가 생기는 문지기 자리입니다." },
                llmContext = "T790M은 gatekeeper 잔기 변이로 ATP 친화도를 높여 1세대 억제제에 저항성을 만든다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest4_CandidateEvaluation,
                title = "후보물질 탐색·평가",
                objective = "저항성 변이까지 커버하는 후보를 고르세요.",
                assistantLines = new[] { "1세대 억제제는 T790M에서 막힙니다. 3세대를 검토해 보세요." },
                hints = new[] { "797번 시스테인에 공유결합하는 설계를 찾아보세요." },
                llmContext = "Osimertinib은 Cys797에 공유결합하는 3세대 억제제로 T790M 저항성을 우회한다.",
            },
            new QuestStageBriefing
            {
                stage = Stage.Quest5_Verification,
                title = "치료 효과 검증",
                objective = "효능과 독성 지표를 확인하세요.",
                assistantLines = new[] { "야생형 EGFR 억제는 피부 독성으로 이어집니다. 선택성을 확인하세요." },
                hints = new[] { "변이형 선택성이 높을수록 정상세포 생존율이 올라갑니다." },
                llmContext = "야생형 EGFR 억제는 피부 발진과 설사 같은 부작용을 일으켜 선택성이 중요하다.",
            },
        };

        quest.candidates = System.Array.Empty<CandidateCompound>();
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
        var follower = Object.FindFirstObjectByType<AIAssistantFollower>();

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
