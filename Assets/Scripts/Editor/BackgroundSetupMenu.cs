using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Taming Mutants > 배경(실험실 모델) 배치
///
/// Assets/Models/CHEMISTRYlabdemoscene.glb 를 씬에 인스턴스화한다.
/// .glb는 Unity 기본 임포터가 읽지 못하므로 glTFast 패키지(com.unity.cloud.gltfast)가
/// 필요하다 — Packages/manifest.json에 추가돼 있고, 패키지 설치가 끝나면 glb가
/// 자동으로 모델(프리팹)로 재임포트된다.
///
/// 씬 YAML을 직접 손대지 않고 메뉴로 배치하는 이유: 스크립티드 임포터가 만드는
/// 프리팹의 내부 fileID는 밖에서 알 수 없어 수동 참조가 깨지기 쉽다.
/// </summary>
public static class BackgroundSetupMenu
{
    private const string ModelPath = "Assets/Models/CHEMISTRYlabdemoscene.glb";
    private const string InstanceName = "Background_ChemistryLab";

    [MenuItem("Tools/Taming Mutants/배경(실험실 모델) 배치")]
    public static void PlaceBackground()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("배경 배치 실패",
                $"{ModelPath} 를 모델로 불러오지 못했습니다.\n\n" +
                "Window > Package Manager에서 glTFast 설치가 끝났는지 확인한 뒤,\n" +
                "Assets/Models 폴더의 glb를 우클릭 > Reimport 하고 다시 실행하세요.", "확인");
            return;
        }

        // 재실행 시 기존 배경을 교체한다 (중복 배치 방지)
        var existing = GameObject.Find(InstanceName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = InstanceName;
        instance.transform.position = Vector3.zero;
        Undo.RegisterCreatedObjectUndo(instance, "배경 배치");
        Selection.activeGameObject = instance;

        Debug.Log("[BackgroundSetup] 실험실 배경 모델을 씬에 배치했습니다. " +
                  "카메라(0, 1.5, -4)와 테이블(0, 0.8, -1) 기준으로 위치/회전/크기를 조정하고 씬을 저장하세요.");
    }
}
