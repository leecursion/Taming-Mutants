using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 배경 장식용 DNA 이중나선을 절차적으로 만든다. 실제 단백질/서열 데이터 없이
/// 나선 방정식만으로 그리므로, 인체·세포·DNA 3D 에셋이 아직 없는 씬에서도
/// "미시 세계로 들어와 있다"는 배경 테마를 채울 수 있다.
///
/// ProteinLoader가 원자를 구체로, 결합을 실린더로 만드는 것과 같은 방식(프리미티브 +
/// MaterialPropertyBlock 틴트, RuntimeMaterials 공용 머티리얼)을 그대로 따른다 — 새 에셋
/// 없이 기존 코드 스타일과 맞춘다. 배경일 뿐이라 콜라이더는 만들자마자 지운다 —
/// 남겨두면 StructureLevelController/MouseWorldSelector의 클릭 레이캐스트가 이걸 맞고
/// 엉뚱하게 반응한다.
/// </summary>
public class DnaHelixBackdrop : MonoBehaviour
{
    [Header("형태")]
    [Tooltip("염기쌍(가로대) 개수")]
    public int basePairCount = 46;
    [Tooltip("나선 반지름(m)")]
    public float radius = 1.4f;
    [Tooltip("염기쌍 하나당 축 방향 상승 거리(m)")]
    public float riseStep = 0.22f;
    [Tooltip("염기쌍 하나당 회전각(도). 실제 B-DNA는 10.5bp/turn ≈ 34.3도")]
    public float twistDegreesPerStep = 34.3f;
    [Tooltip("몇 염기쌍마다 가로대(염기쌍 결합)를 놓을지. 1이면 매번 — 사다리처럼 빽빽해진다.")]
    public int rungEvery = 3;

    [Header("두께")]
    public float backboneNodeScale = 0.16f;
    public float backboneBarThickness = 0.06f;
    public float rungThickness = 0.045f;

    [Header("색상 (배경이라 옅게 — RuntimeMaterials.Transparent 사용)")]
    public Color strandColorA = new Color(0.35f, 0.75f, 1f, 0.55f);
    public Color strandColorB = new Color(0.55f, 0.9f, 1f, 0.55f);
    public Color rungColor = new Color(0.7f, 0.75f, 0.85f, 0.35f);
    [Tooltip("발광 세기 배수. 색상 알파와 별개로 URP Bloom에 걸리는 밝기를 정한다.")]
    public float emissionIntensity = 1.1f;

    [Header("강조 코돈 (선택)")]
    [Tooltip("이 인덱스의 염기쌍을 강조색으로 점멸시킨다. 음수면 강조 없음.")]
    public int highlightIndex = -1;
    public Color highlightColor = new Color(1f, 0.4f, 0.25f, 0.9f);
    public float highlightPulseSpeed = 2f;

    [Header("연출")]
    [Tooltip("초당 나선 축 자전(도). 은은하게 살아있는 느낌을 준다. 0이면 고정.")]
    public float autoRotateSpeed = 2.5f;

    private Renderer _highlightRenderer;
    private MaterialPropertyBlock _mpb;

    private void Awake() => Build();

    private void Update()
    {
        if (autoRotateSpeed != 0f)
            transform.Rotate(Vector3.up, autoRotateSpeed * Time.deltaTime, Space.Self);

        if (_highlightRenderer == null) return;

        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        float t = (Mathf.Sin(Time.time * highlightPulseSpeed) + 1f) * 0.5f;
        Color c = Color.Lerp(rungColor, highlightColor, t);

        _highlightRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_BaseColor", c);
        _mpb.SetColor("_EmissionColor", c * emissionIntensity);
        _highlightRenderer.SetPropertyBlock(_mpb);
    }

    private void Build()
    {
        var strandA = new List<Vector3>(basePairCount);
        var strandB = new List<Vector3>(basePairCount);

        for (int i = 0; i < basePairCount; i++)
        {
            float angle = i * twistDegreesPerStep * Mathf.Deg2Rad;
            float y = i * riseStep;

            Vector3 a = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius + Vector3.up * y;
            Vector3 b = new Vector3(Mathf.Cos(angle + Mathf.PI), 0f, Mathf.Sin(angle + Mathf.PI)) * radius + Vector3.up * y;

            strandA.Add(a);
            strandB.Add(b);

            SpawnNode(a, strandColorA);
            SpawnNode(b, strandColorB);

            if (i > 0)
            {
                SpawnBar(strandA[i - 1], a, backboneBarThickness, strandColorA);
                SpawnBar(strandB[i - 1], b, backboneBarThickness, strandColorB);
            }

            if (i % Mathf.Max(rungEvery, 1) == 0)
            {
                GameObject rung = SpawnBar(a, b, rungThickness, rungColor);
                if (i == highlightIndex) _highlightRenderer = rung.GetComponent<Renderer>();
            }
        }

        // 나선의 세로 중심이 이 오브젝트의 피벗에 오도록 전부 절반만큼 내린다.
        // 그래야 autoRotateSpeed 자전이 밑동이 아니라 나선 중앙을 축으로 돈다.
        float totalHeight = (basePairCount - 1) * riseStep;
        foreach (Transform child in transform)
            child.localPosition -= Vector3.up * (totalHeight * 0.5f);
    }

    private void SpawnNode(Vector3 localPos, Color color)
    {
        GameObject node = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        node.name = "Node";
        StripCollider(node);
        node.transform.SetParent(transform, false);
        node.transform.localPosition = localPos;
        node.transform.localScale = Vector3.one * backboneNodeScale;
        Tint(node, color);
    }

    private GameObject SpawnBar(Vector3 a, Vector3 b, float thickness, Color color)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bar.name = "Bar";
        StripCollider(bar);
        bar.transform.SetParent(transform, false);

        Vector3 mid = (a + b) / 2f;
        bar.transform.localPosition = mid;
        bar.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        float length = Vector3.Distance(a, b);
        bar.transform.localScale = new Vector3(thickness, length / 2f, thickness);

        Tint(bar, color);
        return bar;
    }

    private static void Tint(GameObject go, Color color)
    {
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = RuntimeMaterials.Transparent;

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", color);
        mpb.SetColor("_EmissionColor", color);
        renderer.SetPropertyBlock(mpb);
    }

    private static void StripCollider(GameObject go)
    {
        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
    }
}
