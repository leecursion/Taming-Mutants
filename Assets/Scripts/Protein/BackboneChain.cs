using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ProteinData(평평한 원자 목록)를 "잔기별 주쇄"로 다시 묶은 사슬.
///
/// 리본을 진짜 카툰 표현으로 그리려면 Cα만으로는 부족하다. 이차구조 판정(DSSP)에는
/// N–H와 C=O 사이의 수소결합 에너지가 필요하고, 리본의 납작한 면이 어느 쪽을 향할지는
/// 카보닐(C→O) 방향이 정한다. 다행히 이 프로젝트의 구조 JSON에는 N, CA, C, O가 잔기마다
/// 모두 들어 있으므로(전처리 단계에서 is_backbone으로 표시됨) 그대로 쓸 수 있다.
///
/// 좌표는 <see cref="ProteinLoader"/>가 원자를 배치할 때와 같은 규칙(옹스트롬 × 0.1 후
/// CenterOffset 차감)으로 맞춰 담는다. 그래야 리본이 원자 표시와 정확히 겹친다.
/// 거리 계산이 옹스트롬 단위여야 하는 곳(수소결합 에너지)은 <see cref="SceneUnitsPerAngstrom"/>으로
/// 되돌려서 쓴다.
/// </summary>
public class BackboneChain
{
    /// <summary>씬 1 unit = 10 Å. ProteinLoader가 좌표에 곱하는 0.1과 같은 값이다.</summary>
    public const float SceneUnitsPerAngstrom = 0.1f;

    public struct Residue
    {
        public int resId;
        public string resName;
        public Vector3 ca;
        public Vector3 n;
        public Vector3 c;
        public Vector3 o;
        public bool hasCa;
        public bool hasN;
        public bool hasC;
        public bool hasO;
    }

    /// <summary>res_id 오름차순. Cα가 없는 잔기(리간드/물 등)는 담지 않는다.</summary>
    public readonly List<Residue> Residues = new List<Residue>();

    /// <summary>서열이 끊기지 않고 이어지는 구간 [start, endExclusive). 리본은 조각별로 따로 그린다.</summary>
    public readonly List<Vector2Int> Fragments = new List<Vector2Int>();

    public int Count => Residues.Count;

    private int[] _fragmentOf;

    /// <summary>
    /// 두 잔기가 같은 연속 조각에 속하는가. DSSP의 i±1 관계나 리본의 스플라인 연결은
    /// 서열이 실제로 이어진 구간 안에서만 의미가 있다 — 결실 잔기(F508del)나 cryo-EM에서
    /// 못 잡은 루프를 사이에 두고 이으면 없는 구조를 지어내게 된다.
    /// </summary>
    public bool SameFragment(int a, int b)
    {
        if (_fragmentOf == null) return false;
        if (a < 0 || b < 0 || a >= _fragmentOf.Length || b >= _fragmentOf.Length) return false;
        return _fragmentOf[a] == _fragmentOf[b];
    }

    public static BackboneChain Extract(ProteinLoader.ProteinData data, Vector3 centerOffset)
    {
        var chain = new BackboneChain();
        if (data == null || data.atoms == null) return chain;

        var byResId = new Dictionary<int, Residue>();

        foreach (var atom in data.atoms)
        {
            int slot = SlotOf(atom.name);
            if (slot < 0) continue; // 곁사슬과 OXT 등 말단 원자는 리본에 쓰지 않는다

            byResId.TryGetValue(atom.res_id, out Residue res);
            res.resId = atom.res_id;
            res.resName = atom.res_name;

            Vector3 pos = new Vector3(atom.x, atom.y, atom.z) * SceneUnitsPerAngstrom - centerOffset;

            // 같은 원자가 두 번 나오면(대체 배좌 altloc A/B가 함께 담긴 구조 — 예: 9S9O의
            // 182/238/248/256번 잔기) 먼저 나온 쪽만 쓴다. 나중 것으로 덮으면 한 잔기의
            // 주쇄가 서로 다른 배좌에서 온 원자로 섞여 결합 기하가 어그러진다.
            switch (slot)
            {
                case 0: if (!res.hasCa) { res.ca = pos; res.hasCa = true; } break;
                case 1: if (!res.hasN) { res.n = pos; res.hasN = true; } break;
                case 2: if (!res.hasC) { res.c = pos; res.hasC = true; } break;
                case 3: if (!res.hasO) { res.o = pos; res.hasO = true; } break;
            }

            byResId[atom.res_id] = res;
        }

        var ids = new List<int>(byResId.Keys);
        ids.Sort();

        foreach (int id in ids)
        {
            Residue res = byResId[id];
            if (res.hasCa) chain.Residues.Add(res); // Cα가 없으면 리본 위 자리를 정할 수 없다
        }

        chain.BuildFragments();
        return chain;
    }

    private static int SlotOf(string atomName)
    {
        switch (atomName)
        {
            case "CA": return 0;
            case "N": return 1;
            case "C": return 2;
            case "O": return 3;
            default: return -1;
        }
    }

    private void BuildFragments()
    {
        _fragmentOf = new int[Residues.Count];
        if (Residues.Count == 0) return;

        int start = 0;
        for (int i = 1; i <= Residues.Count; i++)
        {
            bool breakHere = i == Residues.Count || Residues[i].resId != Residues[i - 1].resId + 1;
            if (!breakHere) continue;

            Fragments.Add(new Vector2Int(start, i));
            for (int k = start; k < i; k++) _fragmentOf[k] = Fragments.Count - 1;
            start = i;
        }
    }
}
