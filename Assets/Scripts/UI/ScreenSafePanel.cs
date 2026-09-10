using UnityEngine;

/// <summary>Keep an entire overlay control, including its labels, inside the screen safe area.</summary>
[DefaultExecutionOrder(500)]
public class ScreenSafePanel : MonoBehaviour
{
    private readonly Vector3[] _corners = new Vector3[4];
    private RectTransform _rect;
    private Vector3 _scale;

    private void Awake() { _rect = GetComponent<RectTransform>(); _scale = transform.localScale; }

    private void LateUpdate()
    {
        if (_rect == null) return;
        transform.localScale = _scale;
        Rect safe = Screen.safeArea;
        const float margin = 20f;
        safe = Rect.MinMaxRect(safe.xMin + margin, safe.yMin + margin, safe.xMax - margin, safe.yMax - margin);
        _rect.GetWorldCorners(_corners);
        float width = _corners[2].x - _corners[0].x;
        float height = _corners[2].y - _corners[0].y;
        float fit = Mathf.Min(1f, safe.width / Mathf.Max(width, 1f), safe.height / Mathf.Max(height, 1f));
        transform.localScale = _scale * Mathf.Max(.01f, fit);
        _rect.GetWorldCorners(_corners);
        float dx = _corners[0].x < safe.xMin ? safe.xMin - _corners[0].x :
            _corners[2].x > safe.xMax ? safe.xMax - _corners[2].x : 0f;
        float dy = _corners[0].y < safe.yMin ? safe.yMin - _corners[0].y :
            _corners[2].y > safe.yMax ? safe.yMax - _corners[2].y : 0f;
        transform.position += new Vector3(dx, dy, 0f);
    }
}
