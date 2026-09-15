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
        // Success shields are UI-only; never spawn one over the experiment structure.
        if (kind == "shield") return null;
        if (parent == null || !parent.gameObject.activeInHierarchy) return null;
        var go = new GameObject("MutationCue_" + kind);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var effect = go.AddComponent<MutationExperimentEffects>();
        effect.StartCoroutine(effect.Animate(radius, kind, endpoint));
        return go;
    }

    private IEnumerator AnimateSuccessLasers(float radius)
    {
        const int count = 14;
        const float duration = 1.5f;
        var cores = new LineRenderer[count];
        var glows = new LineRenderer[count];
        for (int i = 0; i < count; i++)
        {
            cores[i] = Line(transform, "LaserCore", Color.white, radius * .035f);
            glows[i] = Line(transform, "LaserGlow", Color.cyan, radius * .12f);
            cores[i].positionCount = glows[i].positionCount = 2;
            cores[i].numCapVertices = glows[i].numCapVertices = 0;
        }
        Camera cam = Camera.main;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (cam != null) transform.rotation = cam.transform.rotation;
            for (int i = 0; i < count; i++)
            {
                // Stagger the emission, then let each beam's tail chase its head outward.
                float age = t - (i % 4) * .055f;
                bool visible = age >= 0f && age < 1.25f;
                cores[i].enabled = glows[i].enabled = visible;
                if (!visible) continue;
                float p = Mathf.Clamp01(age / 1.25f);
                float fade = Mathf.Clamp01(age / .035f) * (1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(.45f, 1.25f, age)));
                float angle = (i + .15f) * Mathf.PI * 2f / count;
                // A shallow cone gives the outward spray depth while remaining legible.
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle),
                    -.15f - (i % 3) * .12f).normalized;
                float reach = 6f + (i % 3) * 1.1f;
                float head = .15f + reach * (1f - Mathf.Pow(1f - p, 3f));
                float tailProgress = Mathf.Clamp01((age - .16f) / 1.09f);
                float tail = .1f + reach * tailProgress * tailProgress;
                Vector3 start = direction * radius * Mathf.Min(tail, head);
                Vector3 end = direction * radius * head;
                cores[i].widthMultiplier = radius * Mathf.Lerp(.045f, .012f, p);
                glows[i].widthMultiplier = radius * Mathf.Lerp(.16f, .04f, p);
                cores[i].SetPosition(0, start);
                cores[i].SetPosition(1, end);
                glows[i].SetPosition(0, start);
                glows[i].SetPosition(1, end);
                cores[i].startColor = new Color(.85f, 1f, 1f, fade);
                cores[i].endColor = new Color(.4f, 1f, .9f, fade * .75f);
                glows[i].startColor = new Color(.15f, 1f, .8f, fade * .25f);
                glows[i].endColor = new Color(.1f, .85f, 1f, 0f);
            }
            yield return null;
        }
        Destroy(gameObject);
    }

    // Outcome cues use separate strokes so broken contacts never resemble a successful lock.
    private IEnumerator AnimateOutcome(float radius, string kind)
    {
        Color tint = kind == "repulsion" || kind == "mismatch" ? new Color(.95f, .4f, .65f) :
            kind == "collision" ? new Color(1f, .35f, .18f) : new Color(1f, .72f, .25f);
        int count = kind == "scattered" ? 6 : 8;
        var strokes = new LineRenderer[count];
        for (int i = 0; i < count; i++)
        {
            strokes[i] = Line(transform, "CueStroke", tint, radius * .025f);
            strokes[i].positionCount = 2;
        }
        float duration = .75f;
        Camera cam = Camera.main;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            if (cam != null) transform.rotation = cam.transform.rotation;
            float fade = Mathf.Clamp01(p * 12f) * Mathf.Pow(1f - p, .7f);
            for (int i = 0; i < count; i++)
            {
                LineRenderer stroke = strokes[i];
                float angle = i * Mathf.PI * 2f / count;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 tangent = new Vector3(-direction.y, direction.x, 0f);
                Vector3 a, b;
                if (kind == "collision")
                {
                    float travel = 1f - Mathf.Pow(1f - p, 3f);
                    float reach = 1.9f;
                    float variation = 1f + (i % 3) * .22f;
                    a = direction * radius * (.2f + travel * reach) * variation;
                    b = a + direction * radius * .55f * (1f - p);
                }
                else if (kind == "repulsion")
                {
                    // Two wave fronts peel away from the rejected contact.
                    stroke.positionCount = 17;
                    float side = i % 2 == 0 ? 1f : -1f;
                    for (int j = 0; j < 17; j++)
                    {
                        float arc = Mathf.Lerp(-1.1f, 1.1f, j / 16f);
                        stroke.SetPosition(j, new Vector3(side * (Mathf.Cos(arc) + p * 2f + i * .12f),
                            Mathf.Sin(arc), 0f) * radius);
                    }
                    a = b = Vector3.zero;
                }
                else if (kind == "scattered")
                {
                    // Several disconnected attachment sites, away from the central pocket.
                    stroke.positionCount = 17;
                    Vector3 center = direction * radius * (1.4f + (i % 2) * .6f);
                    for (int j = 0; j < 17; j++)
                    {
                        float arc = j / 16f * Mathf.PI * 2f;
                        stroke.SetPosition(j, center + new Vector3(Mathf.Cos(arc), Mathf.Sin(arc), 0f) * radius * .22f * (1f + p));
                    }
                    a = b = Vector3.zero;
                }
                else if (kind == "mismatch")
                {
                    a = direction * radius * (1f + p);
                    b = a + (tangent + Vector3.up) * radius * .5f;
                }
                else
                {
                    // Dashed bonds: transient contacts form, then separate; partial recovery
                    // stops short of a full ring, and pathway blocking stays outside the pocket.
                    bool partial = kind == "partial" || kind == "fragment";
                    float spread = kind == "blocked" ? 1.3f : .55f + p * p;
                    a = direction * radius * spread;
                    b = a + (partial || kind == "blocked" ? tangent : direction) * radius * .4f * (1f - p * .7f);
                    if (kind == "unstable") a += tangent * Mathf.Sin(t * 35f + i) * radius * .12f;
                    if (partial && i > 4) { stroke.enabled = false; continue; }
                }
                if (stroke.positionCount == 2)
                {
                    stroke.SetPosition(0, a);
                    stroke.SetPosition(1, b);
                }
                Color head = tint;
                head.a = fade;
                Color tail = tint;
                tail.a = fade * .55f;
                stroke.startColor = head;
                stroke.endColor = tail;
            }
            yield return null;
        }
        Destroy(gameObject);
    }

    private IEnumerator Animate(float radius, string kind, Transform endpoint)
    {
        if (kind == "successRays")
        {
            yield return AnimateSuccessLasers(radius);
            yield break;
        }
        if (kind == "collision" || kind == "repulsion" ||
            kind == "mismatch" || kind == "scattered" || kind == "partial" ||
            kind == "fragment" || kind == "blocked" || kind == "unstable" || kind == "unbound")
        {
            yield return AnimateOutcome(radius, kind);
            yield break;
        }
        Color color = kind == "repel" ? new Color(1f, 0.4f, 0.3f) :
            kind == "break" ? new Color(1f, 0.7f, 0.2f) : new Color(0.25f, 1f, 0.8f);
        var line = Line(transform, kind, color, radius * 0.025f);
        float duration = kind == "lock" ? 1.5f : 0.8f;
        Vector3 end = endpoint != null ? transform.InverseTransformPoint(endpoint.position) : Vector3.right * radius;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            Camera cam = Camera.main;
            // "scan"은 카메라로 돌려세우지 않는다. 후보 칸(Slot)의 축을 그대로 써야 스캔 선이
            // 칸의 사선 배치(CompoundSelectionPanel.diagonalYaw)와 같은 기울기로 지나간다.
            // 카메라 정렬을 걸면 칸만 사선이고 선은 화면 수평이라 둘이 어긋나 보인다.
            if (kind != "break" && kind != "scan" && cam != null) transform.rotation = cam.transform.rotation;
            if (kind == "scan")
            {
                // 칸의 로컬 축 위에서 긋는다 — 칸이 사선으로 놓인 만큼 선도 함께 기운다.
                line.positionCount = 2;
                float y = Mathf.Lerp(radius, -radius, p);
                line.SetPosition(0, new Vector3(-radius, y, -radius));
                line.SetPosition(1, new Vector3(radius, y, -radius));
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
