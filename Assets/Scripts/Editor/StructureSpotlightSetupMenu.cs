#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Taming Mutants > 구조 스포트라이트 배치
///
/// 실험실 배경 때문에 구조(리본/Helix/아미노산)가 묻히는 문제를 보완하기 위해
/// 전용 스포트라이트를 씬에 배치한다. StructureSpotlight가 ProteinLoader의 렌더러 경계를
/// 스스로 추적하므로, 레벨 전환이나 다른 단백질 로딩에도 따로 손댈 필요가 없다.
/// </summary>
public static class StructureSpotlightSetupMenu
{
    private const string LightName = "StructureSpotlight";

    [MenuItem("Tools/Taming Mutants/구조 스포트라이트 배치")]
    public static void Setup()
    {
        var proteinLoader = Object.FindFirstObjectByType<ProteinLoader>();
        if (proteinLoader == null)
        {
            EditorUtility.DisplayDialog("구조 스포트라이트 배치 실패",
                "씬에서 ProteinLoader를 찾지 못했습니다.\n\n단백질이 로드되는 씬(Lab_Desktop)에서 실행하세요.", "확인");
            return;
        }

        var existing = GameObject.Find(LightName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var go = new GameObject(LightName);
        Undo.RegisterCreatedObjectUndo(go, "구조 스포트라이트 배치");

        var spotlight = go.AddComponent<StructureSpotlight>();
        spotlight.proteinLoader = proteinLoader;

        Selection.activeGameObject = go;
        Debug.Log("[StructureSpotlightSetup] 구조 전용 스포트라이트를 배치했습니다. " +
                  "Play 상태에서 구조가 밝게 도드라지는지 확인하고, 필요하면 StructureSpotlight의 " +
                  "intensity / lightColor를 씬에서 눈으로 보며 조정하세요.");
    }
}
#endif
