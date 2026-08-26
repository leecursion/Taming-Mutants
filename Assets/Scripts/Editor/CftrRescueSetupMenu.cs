#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Taming Mutants > CFTR 구조/potentiator 퀘스트 오브젝트 생성
///
/// 사건 4(CFTR F508del) 전용 컴포넌트 세 개를 씬에 배치하고 서로 잇는다:
///   CftrHUD              — Surface CFTR / Channel activity / ER stress 경고 HUD
///   CftrRescueController — DNA→결실→8EJ1 인트로, wobble/ICL4 하이라이트/QC 파티클,
///                          화합물 결과별 반응(8EJ1→8EIQ 구조 스왑, gate/Cl- flow 등)
///   CftrFinaleController — potentiator 성공 후 ASL/점액/cilia 도착 연출(Level 3&4)
///
/// 이 셋은 다른 퀘스트(KRAS/EGFR/ABL1/p53)와 같은 ProteinAnchor_Main/DockingQuestController를
/// 공유하지만, DockingQuestCatalog.OnQuestStarted로 "지금 로드된 퀘스트가 cftr_f508del일 때만"
/// 반응하도록 스스로 걸어 잠그므로(P53ThermalSetupMenu와 같은 패턴) 다른 퀘스트 진행에는 영향이 없다.
///
/// 여러 번 실행해도 안전하다 — 이미 있으면 참조만 다시 잇는다.
/// </summary>
public static class CftrRescueSetupMenu
{
    private const string HudName = "CftrHUD";
    private const string ControllerName = "CftrRescueController";
    private const string FinaleName = "CftrFinaleController";

    [MenuItem("Tools/Taming Mutants/CFTR 구조_potentiator 퀘스트 오브젝트 생성")]
    public static void Setup()
    {
        // Play 모드 중에 만든 오브젝트는 Play를 멈추는 순간 사라져 씬에 남지 않는다 — p53 메뉴와 동일한 안전장치.
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("CFTR 퀘스트 오브젝트 생성 실패",
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
            EditorUtility.DisplayDialog("CFTR 퀘스트 오브젝트 생성 실패",
                "씬에서 ProteinLoader / DockingQuestController / DockingQuestCatalog 중 " +
                "하나 이상을 찾지 못했습니다.\n\n먼저 다른 퀘스트가 정상 동작하는 씬(Lab_Desktop)에서 실행하세요.",
                "확인");
            return;
        }

        var hudGo = FindOrCreate(HudName);
        var hud = GetOrAdd<CftrHUD>(hudGo);

        var controllerGo = FindOrCreate(ControllerName);
        var rescue = GetOrAdd<CftrRescueController>(controllerGo);
        rescue.proteinLoader = proteinLoader;
        rescue.levelController = levelController;
        rescue.questCatalog = catalog;
        rescue.hud = hud;

        var finaleGo = FindOrCreate(FinaleName);
        var finale = GetOrAdd<CftrFinaleController>(finaleGo);
        finale.dockingController = dockingController;
        finale.proteinLoader = proteinLoader;
        finale.hud = hud;

        dockingController.cftr = rescue;
        EditorUtility.SetDirty(dockingController);

        EditorUtility.SetDirty(rescue);
        EditorUtility.SetDirty(finale);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Selection.activeGameObject = controllerGo;
        Debug.Log("[CftrRescueSetup] CFTR F508del 퀘스트 오브젝트를 배치했습니다. " +
                  "Tools > Taming Mutants > 인트로 + 퀘스트 카탈로그 생성 을 아직 안 돌렸다면 " +
                  "그것도 함께 실행해 사건 4가 퀘스트 보드에 등록됐는지 확인하세요.");
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
