using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CompoundData로부터 실제 3D 분자(원자 구체 + 결합 실린더)를 생성한다.
/// ProteinLoader와 같은 Atom/Bond 프리팹을 재사용하되,
/// 화합물 표시용으로는 pLDDT 색 대신 원소별 CPK 색을 입히고
/// 단백질 원자 선택 로직과 섞이지 않도록 AtomInfo/Collider를 제거한다.
/// </summary>
public static class CompoundMoleculeBuilder
{
    public const float AngstromToScene = 0.1f; // ProteinLoader.SpawnStructure와 동일 스케일

    /// <summary>원소 기호 → CPK 색상.</summary>
    public static Color ElementColor(string element)
    {
        switch (element)
        {
            case "C": return new Color(0.35f, 0.35f, 0.38f);
            case "N": return new Color(0.19f, 0.31f, 0.97f);
            case "O": return new Color(0.95f, 0.15f, 0.10f);
            case "S": return new Color(1.00f, 0.85f, 0.15f);
            case "F": return new Color(0.35f, 0.90f, 0.35f);
            case "Cl": return new Color(0.15f, 0.80f, 0.15f);
            case "H": return new Color(0.95f, 0.95f, 0.95f);
            default: return new Color(0.80f, 0.45f, 0.80f);
        }
    }

    /// <summary>
    /// 분자 루트 GameObject를 생성해 반환한다. 루트의 로컬 원점이 분자의 기하 중심이 된다.
    /// warheadAtoms 리스트에는 is_warhead 원자의 Transform이 담긴다(발광/공유결합 연출용).
    ///
    /// shellMaterial(Custom/Hologram 권장)을 넘기면 "홀로-오브" 스타일로 빌드된다:
    /// CPK 색의 불투명 코어(기존 Atom 프리팹, 0.72배) 바깥에 같은 색의 프레넬 림 셸(1.6배)을 씌워
    /// MR 홀로그램 느낌을 내면서도 원소 구분이 그대로 읽힌다. null이면 기존 단색 구체 스타일.
    /// </summary>
    public static GameObject Build(CompoundData data, GameObject atomPrefab, GameObject bondPrefab,
                                   float atomScale, float bondRadiusScale, Transform parent,
                                   List<Transform> warheadAtoms = null,
                                   Material shellMaterial = null, float shellScale = 1.6f)
    {
        var root = new GameObject($"Compound_{data.id}");
        root.transform.SetParent(parent, false);

        // 기하 중심을 루트 원점으로 맞춰 슬롯 박스 중앙 배치가 쉬워지게 한다.
        Vector3 center = Vector3.zero;
        foreach (var a in data.atoms) center += new Vector3(a.x, a.y, a.z);
        if (data.atoms.Count > 0) center /= data.atoms.Count;

        var positions = new List<Vector3>(data.atoms.Count);
        foreach (var atom in data.atoms)
        {
            Vector3 pos = (new Vector3(atom.x, atom.y, atom.z) - center) * AngstromToScene;
            positions.Add(pos);

            GameObject go = Object.Instantiate(atomPrefab, root.transform);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * (shellMaterial != null ? atomScale * 0.72f : atomScale);
            StripProteinComponents(go);

            Color color = ElementColor(atom.element);
            Color displayColor = atom.is_warhead ? new Color(1f, 0.55f, 0.1f) : color;

            if (atom.is_warhead)
            {
                // Warhead(반응기)는 앰버색 펄스 발광으로 강조
                var pulse = go.AddComponent<PulseHighlight>();
                pulse.Init(displayColor, 3f);
                if (warheadAtoms != null) warheadAtoms.Add(go.transform);
            }
            else
            {
                Tint(go, color);
            }

            if (shellMaterial != null)
                AttachHoloShell(go.transform, displayColor, shellScale, shellMaterial);
        }

        foreach (var bond in data.bonds)
        {
            if (bond.a < 0 || bond.b < 0 || bond.a >= positions.Count || bond.b >= positions.Count) continue;
            Vector3 a = positions[bond.a];
            Vector3 b = positions[bond.b];

            GameObject seg = Object.Instantiate(bondPrefab, root.transform);
            seg.transform.localPosition = (a + b) * 0.5f;
            // a/b는 루트 로컬 좌표 — 판넬이 사선(diagonalYaw)으로 회전한 뒤에 빌드돼도 정확히 잇도록 로컬 회전 사용
            seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            seg.transform.localScale = new Vector3(bondRadiusScale, Vector3.Distance(a, b) * 0.5f, bondRadiusScale);
            StripProteinComponents(seg);
            RuntimeMaterials.ApplySolid(seg); // Bond.prefab의 홀로그램 재질은 틴트를 무시하므로 교체
            Tint(seg, new Color(0.75f, 0.78f, 0.82f));
        }

        return root;
    }

    /// <summary>분자의 로컬 바운딩 반지름(루트 스케일 1 기준). 슬롯 박스에 맞춰 축소할 때 사용.</summary>
    public static float LocalRadius(GameObject moleculeRoot)
    {
        float maxSqr = 0f;
        foreach (Transform child in moleculeRoot.transform)
        {
            float d = child.localPosition.sqrMagnitude;
            if (d > maxSqr) maxSqr = d;
        }
        return Mathf.Sqrt(maxSqr) + 0.05f; // 원자 반지름 여유분
    }

    // 코어 원자에 프레넬 림 셸을 씌운다. 셸은 코어의 자식이라 위치/스케일을 자동 추종.
    private static void AttachHoloShell(Transform core, Color color, float shellScale, Material shellMaterial)
    {
        var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.name = "HoloShell";
        Object.Destroy(shell.GetComponent<Collider>());
        shell.transform.SetParent(core, false);
        shell.transform.localScale = Vector3.one * shellScale;

        var renderer = shell.GetComponent<Renderer>();
        renderer.sharedMaterial = shellMaterial;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_HologramColor", color);
        renderer.SetPropertyBlock(mpb);
    }

    private static void StripProteinComponents(GameObject go)
    {
        // 화합물 표시용 복제는 단백질 선택 레이캐스트(MouseWorldSelector 등)에 걸리면 안 된다.
        var info = go.GetComponent<AtomInfo>();
        if (info != null) Object.Destroy(info);
        var colorizer = go.GetComponent<PLDDTColorizer>();
        if (colorizer != null) Object.Destroy(colorizer);
        foreach (var col in go.GetComponentsInChildren<Collider>())
            Object.Destroy(col);
    }

    public static void Tint(GameObject go, Color color)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;
        var mpb = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", color); // URP Lit 기준 (PLDDTColorizer와 동일 관례)
        renderer.SetPropertyBlock(mpb);
    }
}
