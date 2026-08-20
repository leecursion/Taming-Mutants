#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Taming Mutants > 창문 우주 배경 마감
///
/// 실험실 창문 밖으로 기본 씬 화면이 그대로 보이는 문제를 고친다.
/// Background_ChemistryLab(BackgroundSetupMenu가 배치한 실험실 glb 모델)의 실제 렌더러 경계를
/// 측정해, 그 전체를 넉넉히 감싸는 큰 상자를 만들고 안쪽 면에 절차적 별하늘(Custom/SpaceBackdrop)을
/// 입힌다. 상자 머티리얼이 Cull Front라 실내(상자 안쪽)에서만 보이므로, 창문이 어느 방향에 있든
/// 그 너머로는 이 별하늘이 보이고 상자 바깥에서는 아무것도 보이지 않는다.
///
/// 이전 버전에서 만들었던 벽-천장 인방(lintel) 트림은 창문 위에 원치 않는 막대 오브젝트로
/// 보인다는 피드백에 따라 제거했다. Setup()을 다시 실행하면 과거에 만들어졌던 트림도 함께 지운다.
/// </summary>
public static class WindowBackdropSetupMenu
{
    private const string BackgroundName = "Background_ChemistryLab";
    private const string BackdropName = "WindowBackdrop";
    private const string LegacyLintelName = "Ceiling_WallTrim"; // 이전 버전 산출물 — 있으면 정리한다
    private const string MaterialFolder = "Assets/Materials";
    private const string BackdropMaterialPath = MaterialFolder + "/WindowBackdrop_Space.mat";
    private const string ShaderName = "Custom/SpaceBackdrop";

    [MenuItem("Tools/Taming Mutants/창문 우주 배경 마감")]
    public static void Setup()
    {
        var background = GameObject.Find(BackgroundName);
        if (background == null)
        {
            EditorUtility.DisplayDialog("창문 배경 마감 실패",
                $"'{BackgroundName}' 오브젝트를 찾지 못했습니다.\n\n" +
                "Tools > Taming Mutants > 배경(실험실 모델) 배치 를 먼저 실행하세요.", "확인");
            return;
        }

        var renderers = background.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            EditorUtility.DisplayDialog("창문 배경 마감 실패",
                $"'{BackgroundName}'에서 렌더러를 찾지 못했습니다. glb 임포트가 끝났는지 확인하세요.", "확인");
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        RemoveLegacyLintel();

        Material backdropMat = LoadOrCreateMaterial();
        BuildBackdrop(bounds, backdropMat);

        Debug.Log("[WindowBackdropSetup] 창문 밖 우주 배경(별하늘)을 배치했습니다. " +
                  $"WindowBackdrop 오브젝트 또는 {BackdropMaterialPath} 머티리얼의 " +
                  "Star Density / Star Size / Star Chance / Twinkle Speed를 씬에서 눈으로 보며 조정하세요.");
    }

    // 이전 버전이 만든 벽-천장 인방(lintel) 트림 오브젝트/머티리얼을 정리한다.
    private static void RemoveLegacyLintel()
    {
        var existing = GameObject.Find(LegacyLintelName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);
    }

    private static void BuildBackdrop(Bounds bounds, Material material)
    {
        var existing = GameObject.Find(BackdropName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = BackdropName;
        Undo.RegisterCreatedObjectUndo(box, "창문 배경 배치");
        Object.DestroyImmediate(box.GetComponent<Collider>());

        // 넉넉하게 키워둘수록 어떤 각도에서 봐도(카메라가 많이 움직여도) 배경판 가장자리가
        // 시야에 걸려 경계가 다시 드러나는 일이 없다.
        const float margin = 6f; // 모델 바깥으로 확보할 여유(unit)
        Vector3 size = bounds.size + Vector3.one * margin * 2f;
        box.transform.position = bounds.center;
        box.transform.localScale = size;

        var renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static Material LoadOrCreateMaterial()
    {
        Shader shader = Shader.Find(ShaderName);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(BackdropMaterialPath);

        if (material == null)
        {
            if (shader == null)
            {
                Debug.LogWarning($"[WindowBackdropSetup] 셰이더 '{ShaderName}'를 찾지 못해 " +
                                 "URP Lit 단색(짙은 남색)으로 대체합니다 (별 없음).");
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
            material = new Material(shader) { name = "WindowBackdrop_Space" };
            EnsureFolder(MaterialFolder);
            AssetDatabase.CreateAsset(material, BackdropMaterialPath);
        }

        // 재실행할 때마다 최신 튜닝값으로 갱신한다(기존 에셋이 있어도 값이 뒤처지지 않게).
        if (material.HasProperty("_SkyColor"))
        {
            material.SetColor("_SkyColor", new Color(0.015f, 0.02f, 0.05f));
            material.SetColor("_StarColor", Color.white);
            material.SetFloat("_StarDensity", 55f);
            material.SetFloat("_StarSize", 0.10f);   // 작을수록 별이 더 작게 보인다(요청: "아주 작게")
            material.SetFloat("_StarChance", 0.10f);
            material.SetFloat("_TwinkleSpeed", 1.4f);
        }
        // 단색 폴백(URP Lit)일 때는 별 없이 짙은 남색 하늘 톤 하나로 칠한다.
        else if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", new Color(0.03f, 0.04f, 0.09f));
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int slash = folder.LastIndexOf('/');
        string parent = folder.Substring(0, slash);
        string leaf = folder.Substring(slash + 1);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
#endif
