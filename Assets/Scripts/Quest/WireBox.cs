using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 얇은 큐브 12개로 와이어프레임 박스를 런타임 생성하는 공용 헬퍼.
/// CompoundSlot(칸 프레임)과 CompoundSelectionPanel(2x2 외곽 박스)이 함께 사용한다.
/// 런타임 프리미티브라 머티리얼 의존성이 없고, 비균등 크기(가로/세로/깊이)를 지원한다.
/// </summary>
public static class WireBox
{
    public static List<Renderer> Build(Transform parent, Vector3 size, float thickness)
    {
        var renderers = new List<Renderer>(12);
        Vector3 h = size * 0.5f;

        // 각 축 방향 모서리 4개씩
        for (int axis = 0; axis < 3; axis++)
        {
            for (int s1 = -1; s1 <= 1; s1 += 2)
            {
                for (int s2 = -1; s2 <= 1; s2 += 2)
                {
                    var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    edge.name = "FrameEdge";
                    Object.Destroy(edge.GetComponent<Collider>());
                    edge.transform.SetParent(parent, false);

                    Vector3 pos;
                    Vector3 scale = Vector3.one * thickness;
                    if (axis == 0) { pos = new Vector3(0, s1 * h.y, s2 * h.z); scale.x = size.x + thickness; }
                    else if (axis == 1) { pos = new Vector3(s1 * h.x, 0, s2 * h.z); scale.y = size.y + thickness; }
                    else { pos = new Vector3(s1 * h.x, s2 * h.y, 0); scale.z = size.z + thickness; }

                    edge.transform.localPosition = pos;
                    edge.transform.localScale = scale;
                    renderers.Add(edge.GetComponent<Renderer>());
                }
            }
        }
        return renderers;
    }

    public static void SetColor(List<Renderer> renderers, Color color, float emission = 0.6f)
    {
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", color);
            mpb.SetColor("_EmissionColor", color * emission);
            r.SetPropertyBlock(mpb);
        }
    }
}
