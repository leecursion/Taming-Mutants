using System.Collections;
using UnityEngine;

/// <summary>Procedural experiment cues. No colliders, cloned atoms, or changes to scientific state.</summary>
public class MutationExperimentEffects : MonoBehaviour
{
    public static LineRenderer Line(Transform parent, string label, Color color, float width)
    {
        var go = new GameObject(label);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.sharedMaterial = RuntimeMaterials.LineUnlit;
        line.startColor = line.endColor = color;
        line.widthMultiplier = width;
        line.numCapVertices = 3;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    public static void Ring(LineRenderer line, float radius, float progress = 1f, float ripple = 0f)
    {
        const int count = 49;
        line.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            float a = i / (float)(count - 1) * Mathf.PI * 2f * progress;
            float r = radius * (1f + ripple * Mathf.Sin(a * 7f + Time.time * 3f));
            line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r);
        }
    }

    public static GameObject Play(Transform parent, Vector3 localPosition, float radius, string kind,
        Transform endpoint = null)
    {
        if (parent == null || !parent.gameObject.activeInHierarchy) return null;
        var go = new GameObject("MutationCue_" + kind);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var effect = go.AddComponent<MutationExperimentEffects>();
        effect.StartCoroutine(effect.Animate(radius, kind, endpoint));
        return go;
    }

    private IEnumerator Animate(float radius, string kind, Transform endpoint)
    {
        Color color = kind == "repel" ? new Color(1f, 0.4f, 0.3f) :
            kind == "break" ? new Color(1f, 0.7f, 0.2f) : new Color(0.25f, 1f, 0.8f);
        var line = Line(transform, kind, color, radius * 0.025f);
        float duration = kind == "shield" ? 3f : kind == "lock" ? 1.5f : 0.8f;
        Vector3 end = endpoint != null ? transform.InverseTransformPoint(endpoint.position) : Vector3.right * radius;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            Camera cam = Camera.main;
            if (kind != "break" && cam != null) transform.rotation = cam.transform.rotation;
            if (kind == "scan")
            {
                line.positionCount = 2;
                float y = Mathf.Lerp(radius, -radius, p);
                line.SetPosition(0, new Vector3(-radius, y, -radius));
                line.SetPosition(1, new Vector3(radius, y, -radius));
            }
            else if (kind == "shield")
            {
                Vector3[] outline = { new Vector3(0, 1, 0), new Vector3(.85f, .6f, 0),
                    new Vector3(.65f, -.45f, 0), new Vector3(0, -1, 0),
                    new Vector3(-.65f, -.45f, 0), new Vector3(-.85f, .6f, 0), new Vector3(0, 1, 0) };
                line.positionCount = 61;
                float trace = Mathf.Min(1f, p * 3f) * 6f;
                for (int i = 0; i < 61; i++)
                {
                    float f = i / 60f * trace;
                    int segment = Mathf.Min(5, Mathf.FloorToInt(f));
                    line.SetPosition(i, Vector3.Lerp(outline[segment], outline[segment + 1], f - segment) * radius);
                }
            }
            else if (kind == "lock")
            {
                // A closing contact outline remains visible even when ligand and pocket centers coincide.
                Ring(line, radius * Mathf.Lerp(1.2f, .8f, Mathf.Min(1f, p * 2f)), Mathf.Min(1f, p * 3f));
            }
            else if (kind == "break")
            {
                if (endpoint != null) end = transform.InverseTransformPoint(endpoint.position);
                line.positionCount = 17;
                for (int i = 0; i < 17; i++)
                {
                    float f = i / 16f;
                    Vector3 point = end * f;
                    if (kind == "break") point += Vector3.up * Mathf.Sin(f * 20f + p * 25f) * radius * .15f * p;
                    line.SetPosition(i, point);
                }
                // Retract the broken contact toward the pocket before the molecule departs.
                if (kind == "break") line.transform.localScale = Vector3.one * (1f - p);
            }
            else Ring(line, radius * Mathf.Lerp(.4f, 1.6f, p));
            color.a = Mathf.Min(1f, (1f - p) * 3f) * .8f;
            line.startColor = line.endColor = color;
            yield return null;
        }
        Destroy(gameObject);
    }

    private void OnDisable() { Destroy(gameObject); }
}
