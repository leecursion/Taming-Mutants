#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Taming Mutants > DNA 이중나선 배경 배치
///
/// 창문 밖 별하늘(WindowBackdropSetupMenu가 만든 WindowBackdrop 상자) 안쪽 빈 공간에
/// 절차적 DNA 이중나선을 몇 개 띄운다. 배경이 그냥 빈 우주보다 이 프로젝트(돌연변이·유전자
/// 퀘스트)의 주제와 맞게 보이도록 하는 장식용 세트 드레싱이다.
///
/// Background_ChemistryLab 경계를 재사용해 그 바깥, WindowBackdrop 상자 안쪽 여백에
/// 배치한다 — 정확한 창문 위치는 모델을 열어보지 않는 한 알 수 없으므로, 건물을 넉넉히
/// 감싸는 자리에 띄워 두고 실제로 보이는 창문 쪽으로 손으로 옮기는 편이 안전하다.
/// </summary>
public static class DnaHelixBackdropSetupMenu
{
    private const string BackgroundName = "Background_ChemistryLab";
    private const string RootName = "DnaHelixBackdrops";

    [MenuItem("Tools/Taming Mutants/DNA 이중나선 배경 배치")]
    public static void Setup()
    {
        Bounds bounds = ResolveBounds();

        var existing = GameObject.Find(RootName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "DNA 이중나선 배경 배치");

        float outsideMargin = 3f; // WindowBackdrop 상자 안쪽 여백(margin=6) 절반 정도로, 벽에 닿지 않게
        float armLength = bounds.extents.z + outsideMargin;

        // 서로 다른 자리·크기·자전 속도로 두 개를 두어 한 덩어리처럼 뭉쳐 보이지 않게 한다.
        BuildHelix(root.transform, "DnaHelix_Far",
            bounds.center + new Vector3(bounds.extents.x * 0.5f, bounds.extents.y * 0.6f, armLength),
            scale: 1.4f, autoRotateSpeed: 1.6f);

        BuildHelix(root.transform, "DnaHelix_Near",
            bounds.center + new Vector3(-bounds.extents.x * 0.7f, bounds.extents.y * 0.2f, armLength * 0.75f),
            scale: 0.8f, autoRotateSpeed: -2.4f);

        EditorUtility.SetDirty(root);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Selection.activeGameObject = root;
        Debug.Log("[DnaHelixBackdropSetup] DNA 이중나선 배경 2개를 배치했습니다. " +
                  "실제 창문이 보이는 방향과 어긋나면 DnaHelixBackdrops 하위 오브젝트를 " +
                  "손으로 옮기세요. 형태/색은 각 DnaHelixBackdrop 컴포넌트에서 조정합니다.");
    }

    /// <summary>Background_ChemistryLab이 있으면 그 경계, 없으면 원점 주변 임의 크기로 대체한다.</summary>
    private static Bounds ResolveBounds()
    {
        var background = GameObject.Find(BackgroundName);
        if (background == null)
        {
            Debug.LogWarning($"[DnaHelixBackdropSetup] '{BackgroundName}'를 찾지 못해 " +
                              "원점 기준 기본 크기로 배치합니다. 위치가 어색하면 손으로 옮기세요.");
            return new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f));
        }

        var renderers = background.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(background.transform.position, new Vector3(10f, 4f, 10f));

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    private static void BuildHelix(Transform parent, string name, Vector3 position, float scale, float autoRotateSpeed)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;

        var helix = Undo.AddComponent<DnaHelixBackdrop>(go);
        helix.autoRotateSpeed = autoRotateSpeed;

        EditorUtility.SetDirty(helix);
    }
}
#endif
