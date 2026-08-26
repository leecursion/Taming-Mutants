#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Taming Mutants > p53 열안정성 퀘스트 오브젝트 생성
///
/// 사건 5(p53 Y220C) 전용 컴포넌트 세 개를 씬에 배치하고 서로 잇는다:
///   ThermalStabilityHUD        — 온도/안정성/wobble/p53 총량/DNA 결합능 HUD
///   ThermalStabilityController — 20~60°C 슬라이더 + wobble/투명도/응집 파티클 + 카메라 전환
///   P53QuestDirector           — 안정화제 도킹 성공 후 37°C Before/After + DNA/사량체 결합 연출
///
/// 이 셋은 다른 퀘스트(KRAS/EGFR/ABL1/CFTR)와 같은 ProteinAnchor_Main/DockingQuestController를
/// 공유하지만, DockingQuestCatalog.OnQuestStarted로 "지금 로드된 퀘스트가 p53_y220c일 때만"
/// 반응하도록 스스로 걸어 잠그므로 다른 퀘스트 진행에는 영향이 없다.
///
/// 여러 번 실행해도 안전하다 — 이미 있으면 참조만 다시 잇는다.
/// </summary>
public static class P53ThermalSetupMenu
{
    private const string HudName = "ThermalStabilityHUD";
    private const string ControllerName = "ThermalStabilityController";
    private const string DirectorName = "P53QuestDirector";

    [MenuItem("Tools/Taming Mutants/p53 열안정성 퀘스트 오브젝트 생성")]
    public static void Setup()
    {
        // Play 모드 중에는 EditorSceneManager.MarkSceneDirty가 예외를 던진다 — 그 전에
        // 만든 오브젝트들은 만들어지긴 하지만 Play를 멈추는 순간 전부 사라진다(씬에 저장되지
        // 않는 런타임 오브젝트라서). 조용히 실패하는 대신 여기서 먼저 막는다.
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("p53 퀘스트 오브젝트 생성 실패",
                "Play 모드 중에는 실행할 수 없습니다.\n\n" +
                "Play를 멈춘 뒤(편집 모드) 다시 실행하세요 — Play 중에 만든 오브젝트는 " +
                "Play를 멈추는 순간 사라져 씬에 남지 않습니다.", "확인");
            return;
        }

        var proteinLoader = Object.FindFirstObjectByType<ProteinLoader>(FindObjectsInactive.Include);
        var levelController = proteinLoader != null ? proteinLoader.GetComponent<StructureLevelController>() : null;
        var dockingController = Object.FindFirstObjectByType<DockingQuestController>(FindObjectsInactive.Include);
        var catalog = Object.FindFirstObjectByType<DockingQuestCatalog>(FindObjectsInactive.Include);

        if (proteinLoader == null || dockingController == null || catalog == null)
        {
            EditorUtility.DisplayDialog("p53 퀘스트 오브젝트 생성 실패",
                "씬에서 ProteinLoader / DockingQuestController / DockingQuestCatalog 중 " +
                "하나 이상을 찾지 못했습니다.\n\n먼저 다른 퀘스트가 정상 동작하는 씬(Lab_Desktop)에서 실행하세요.",
                "확인");
            return;
        }

        var hudGo = FindOrCreate(HudName);
        var hud = GetOrAdd<ThermalStabilityHUD>(hudGo);

        var controllerGo = FindOrCreate(ControllerName);
        var thermal = GetOrAdd<ThermalStabilityController>(controllerGo);
        thermal.proteinLoader = proteinLoader;
        thermal.levelController = levelController;
        thermal.questCatalog = catalog;
        thermal.hud = hud;
        if (Camera.main != null) thermal.targetCamera = Camera.main;

        var directorGo = FindOrCreate(DirectorName);
        var director = GetOrAdd<P53QuestDirector>(directorGo);
        director.dockingController = dockingController;
        director.thermal = thermal;
        director.hud = hud;
        director.dbdAnchor = proteinLoader.transform;

        dockingController.thermal = thermal;
        dockingController.hud = hud;
        EditorUtility.SetDirty(dockingController);

        EditorUtility.SetDirty(thermal);
        EditorUtility.SetDirty(director);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Selection.activeGameObject = directorGo;
        Debug.Log("[P53ThermalSetup] p53 열안정성 퀘스트 오브젝트를 배치했습니다. " +
                  "Tools > Taming Mutants > 인트로 + 퀘스트 카탈로그 생성 을 아직 안 돌렸다면 " +
                  "그것도 함께 실행해 사건 5를 퀘스트 보드에 등록하세요.");
    }

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
}
#endif
