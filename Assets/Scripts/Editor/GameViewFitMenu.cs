using UnityEditor;
using UnityEngine;

/// <summary>Fit the editor preview itself; runtime UI cannot compensate for a zoomed/cropped Game view.</summary>
[InitializeOnLoad]
public static class GameViewFitMenu
{
    static GameViewFitMenu()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Fit;
        };
        if (EditorApplication.isPlaying) EditorApplication.delayCall += Fit;
    }

    [MenuItem("Tools/Layout/Fit Game View")]
    public static void Fit()
    {
        foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
        {
            if (window.GetType().FullName != "UnityEditor.GameView") continue;
            var serialized = new SerializedObject(window);
            var zoom = serialized.FindProperty("m_ZoomArea");
            if (zoom == null) continue;
            var scale = zoom.FindPropertyRelative("m_Scale");
            var translation = zoom.FindPropertyRelative("m_Translation");
            var area = zoom.FindPropertyRelative("m_DrawArea");
            var hMin = zoom.FindPropertyRelative("m_HBaseRangeMin");
            var hMax = zoom.FindPropertyRelative("m_HBaseRangeMax");
            var vMin = zoom.FindPropertyRelative("m_VBaseRangeMin");
            var vMax = zoom.FindPropertyRelative("m_VBaseRangeMax");
            if (scale == null || translation == null || area == null || hMin == null || hMax == null || vMin == null || vMax == null) continue;
            Rect draw = area.rectValue;
            float width = hMax.floatValue - hMin.floatValue;
            float height = vMax.floatValue - vMin.floatValue;
            if (width <= 0f || height <= 0f || draw.width <= 0f || draw.height <= 0f) continue;
            float fit = Mathf.Min(draw.width / width, draw.height / height);
            scale.vector2Value = Vector2.one * fit;
            translation.vector2Value = draw.size * .5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            window.Repaint();
        }
    }
}
