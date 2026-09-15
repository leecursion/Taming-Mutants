using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 주쇄 좌표에서 이차구조(알파나선 / 베타가닥 / 루프)를 DSSP(Kabsch &amp; Sander, 1983) 방식으로
/// 판정한다. PDB/AlphaFold 구조 페이지에 표시되는 이차구조가 바로 이 알고리즘의 출력이므로,
/// 여기서 나오는 나선/가닥 구간은 추정이 아니라 원 데이터가 실제로 말하는 구간이다.
///
/// 절차는 원 논문 그대로다.
///  1. 아마이드 수소 H를 N 위에 놓는다 — 앞 잔기의 O→C 방향으로 1.0 Å.
///  2. C=O(i)와 N–H(j) 사이 정전기 에너지를 재고, -0.5 kcal/mol보다 낮으면 수소결합으로 본다.
///  3. 4-turn이 연달아 두 번 나오면 알파나선(H), 3-turn이면 3_10 나선(G), 5-turn이면 파이나선(I).
///  4. 두 잔기가 평행/역평행 브리지 조건을 만족하면 베타 가닥(E).
///
/// 반환값은 <see cref="BackboneChain.Residues"/>와 같은 길이/순서다.
///
/// 이전에는 Cα끼리의 거리와 가상 이면각만 보는 P-SEA 계열 근사를 썼는데, 그 방식은
/// 나선/가닥 경계를 한두 잔기씩 어긋나게 잡고 루프의 우연한 규칙성까지 가닥으로 오인해서
/// 리본이 실제 폴드와 다른 모양으로 나왔다. 주쇄 원자가 JSON에 이미 다 들어 있으므로
/// 근사를 쓸 이유가 없다.
/// </summary>
public static class SecondaryStructureAssigner
{
    public enum Type { Loop, Helix, Strand }

    // E = q1*q2*(1/r(ON) + 1/r(CH) - 1/r(OH) - 1/r(CN)) * f,  q1 = 0.42e, q2 = 0.20e, f = 332
    private const float CouplingConstant = 0.42f * 0.20f * 332f;
    private const float HBondEnergyCutoff = -0.5f;  // kcal/mol
    private const float MinPairDistance = 0.5f;     // Å — 좌표 이상으로 1/r이 폭주하는 것 방어
    private const float AmideHydrogenBondLength = 1.0f; // Å, N–H

    // 이보다 Cα가 멀면 주쇄끼리 수소결합할 수 없다. n² 순회에서 대부분을 걷어내는 필터이자
    // 브리지 후보 쌍을 추리는 기준.
    private const float MaxCaContactDistance = 9f;  // Å

    /// <summary>
    /// 홀로 남은 브리지 잔기 하나(DSSP의 'B')는 리본에서 화살표 한 조각으로 튀어 보일 뿐
    /// 시트로 읽히지 않는다. 사다리(ladder)를 이루는 길이 2 이상만 가닥으로 그린다.
    /// </summary>
    private const int MinStrandRun = 2;

    public static Type[] Assign(BackboneChain chain)
    {
        int n = chain.Count;
        var result = new Type[n];
        if (n == 0) return result;

        Vector3[] hydrogens = PlaceAmideHydrogens(chain);
        HashSet<int> bonds = FindHydrogenBonds(chain, hydrogens, out List<Vector2Int> contacts);

        bool HBond(int acceptor, int donor) =>
            acceptor >= 0 && donor >= 0 && acceptor < n && donor < n &&
            bonds.Contains(acceptor * n + donor);

        var helix = new bool[n];
        var strand = new bool[n];

        // --- 나선: n-turn 두 개가 겹치면 최소 나선 하나 ---
        // 4-turn(i)와 4-turn(i+1)이 함께 있으면 i+1..i+4가 알파나선이다. 3_10(3-turn)과
        // 파이(5-turn)도 같은 규칙이며, 카툰에서는 셋 다 나선 리본으로 그린다.
        MarkHelixTurns(chain, HBond, helix, turn: 4);
        MarkHelixTurns(chain, HBond, helix, turn: 3);
        MarkHelixTurns(chain, HBond, helix, turn: 5);

        // --- 베타 브리지 ---
        foreach (Vector2Int pair in contacts)
        {
            int i = pair.x, j = pair.y;
            if (!HasNeighbors(chain, i) || !HasNeighbors(chain, j)) continue;

            bool parallel = (HBond(i - 1, j) && HBond(j, i + 1)) ||
                            (HBond(j - 1, i) && HBond(i, j + 1));
            bool antiparallel = (HBond(i, j) && HBond(j, i)) ||
                                (HBond(i - 1, j + 1) && HBond(j - 1, i + 1));

            if (!parallel && !antiparallel) continue;
            strand[i] = true;
            strand[j] = true;
        }

        // DSSP의 우선순위도 나선이 먼저다 — 나선 양끝은 브리지 조건에 걸리는 일이 있는데,
        // 그걸 가닥으로 그리면 나선 중간에 화살표가 박힌 것처럼 보인다.
        for (int i = 0; i < n; i++)
            result[i] = helix[i] ? Type.Helix : strand[i] ? Type.Strand : Type.Loop;

        DropShortStrandRuns(result, chain);
        return result;
    }

    /// <summary>
    /// n-turn(i) = C=O(i)와 N–H(i+n)의 수소결합. 연달아 두 개면 그 사이 잔기가 나선이다.
    /// </summary>
    private static void MarkHelixTurns(BackboneChain chain, System.Func<int, int, bool> hBond,
                                       bool[] helix, int turn)
    {
        for (int i = 0; i + turn + 1 < chain.Count; i++)
        {
            if (!chain.SameFragment(i, i + turn + 1)) continue;
            if (!hBond(i, i + turn) || !hBond(i + 1, i + turn + 1)) continue;

            for (int k = i + 1; k <= i + turn; k++) helix[k] = true;
        }
    }

    /// <summary>브리지 판정은 i-1, i, i+1을 모두 쓰므로 서열상 양옆이 이어져 있어야 한다.</summary>
    private static bool HasNeighbors(BackboneChain chain, int i)
    {
        return i - 1 >= 0 && i + 1 < chain.Count &&
               chain.SameFragment(i - 1, i) && chain.SameFragment(i, i + 1);
    }

    /// <summary>
    /// 아마이드 수소를 N 위에 놓는다. 원 논문과 같이 앞 잔기의 카보닐 O→C 방향으로 1.0 Å —
    /// 펩타이드 결합이 평면이라 이 방향이 실제 N–H와 거의 일치한다.
    /// 조각의 첫 잔기와 프롤린은 줄 수소가 없으므로 공여자에서 빠진다(NaN으로 표시).
    /// </summary>
    private static Vector3[] PlaceAmideHydrogens(BackboneChain chain)
    {
        var hydrogens = new Vector3[chain.Count];
        for (int i = 0; i < chain.Count; i++)
        {
            BackboneChain.Residue res = chain.Residues[i];
            bool canDonate = res.hasN && res.resName != "PRO" &&
                             i > 0 && chain.SameFragment(i - 1, i) &&
                             chain.Residues[i - 1].hasC && chain.Residues[i - 1].hasO;

            if (!canDonate)
            {
                hydrogens[i] = new Vector3(float.NaN, float.NaN, float.NaN);
                continue;
            }

            BackboneChain.Residue prev = chain.Residues[i - 1];
            Vector3 dir = (prev.c - prev.o).normalized;
            hydrogens[i] = res.n + dir * (AmideHydrogenBondLength * BackboneChain.SceneUnitsPerAngstrom);
        }
        return hydrogens;
    }

    /// <summary>
    /// 공여자(N–H)마다 에너지가 가장 낮은 두 수용자(C=O)만 결합으로 인정한다 — DSSP와 같은 규칙으로,
    /// 임계값만 보고 전부 인정하면 한 N–H가 세 곳 이상과 결합한 것처럼 되어 가짜 나선/시트가 생긴다.
    /// 반환값은 acceptor*n + donor 키 집합이고, contacts에는 브리지 후보(서열상 3 이상 떨어진
    /// Cα 근접 쌍)를 함께 담아 준다.
    /// </summary>
    private static HashSet<int> FindHydrogenBonds(BackboneChain chain, Vector3[] hydrogens,
                                                  out List<Vector2Int> contacts)
    {
        int n = chain.Count;
        var bonds = new HashSet<int>();
        contacts = new List<Vector2Int>();

        float maxCa = MaxCaContactDistance * BackboneChain.SceneUnitsPerAngstrom;
        float maxCaSqr = maxCa * maxCa;

        for (int donor = 0; donor < n; donor++)
        {
            if (float.IsNaN(hydrogens[donor].x)) continue;

            BackboneChain.Residue d = chain.Residues[donor];
            float bestEnergy = 0f, secondEnergy = 0f;
            int bestIndex = -1, secondIndex = -1;

            for (int acceptor = 0; acceptor < n; acceptor++)
            {
                // 서열상 바로 옆 잔기와는 기하적으로 늘 가까워 의미 있는 결합이 아니다.
                if (Mathf.Abs(acceptor - donor) < 2 && chain.SameFragment(acceptor, donor)) continue;

                BackboneChain.Residue a = chain.Residues[acceptor];
                if (!a.hasC || !a.hasO) continue;
                if ((a.ca - d.ca).sqrMagnitude > maxCaSqr) continue;

                float energy = HBondEnergy(a.c, a.o, d.n, hydrogens[donor]);
                if (energy < bestEnergy)
                {
                    secondEnergy = bestEnergy; secondIndex = bestIndex;
                    bestEnergy = energy; bestIndex = acceptor;
                }
                else if (energy < secondEnergy)
                {
                    secondEnergy = energy; secondIndex = acceptor;
                }
            }

            if (bestIndex >= 0 && bestEnergy < HBondEnergyCutoff) bonds.Add(bestIndex * n + donor);
            if (secondIndex >= 0 && secondEnergy < HBondEnergyCutoff) bonds.Add(secondIndex * n + donor);
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 3; j < n; j++)
            {
                if ((chain.Residues[i].ca - chain.Residues[j].ca).sqrMagnitude > maxCaSqr) continue;
                contacts.Add(new Vector2Int(i, j));
            }
        }

        return bonds;
    }

    private static float HBondEnergy(Vector3 c, Vector3 o, Vector3 n, Vector3 h)
    {
        float rON = Angstroms(o, n);
        float rCH = Angstroms(c, h);
        float rOH = Angstroms(o, h);
        float rCN = Angstroms(c, n);

        if (rON < MinPairDistance || rCH < MinPairDistance ||
            rOH < MinPairDistance || rCN < MinPairDistance) return 0f;

        return CouplingConstant * (1f / rON + 1f / rCH - 1f / rOH - 1f / rCN);
    }

    private static float Angstroms(Vector3 a, Vector3 b)
    {
        return Vector3.Distance(a, b) / BackboneChain.SceneUnitsPerAngstrom;
    }

    private static void DropShortStrandRuns(Type[] types, BackboneChain chain)
    {
        int i = 0;
        while (i < types.Length)
        {
            if (types[i] != Type.Strand) { i++; continue; }

            int j = i;
            while (j < types.Length && types[j] == Type.Strand && chain.SameFragment(i, j)) j++;

            if (j - i < MinStrandRun)
                for (int k = i; k < j; k++) types[k] = Type.Loop;

            i = j;
        }
    }
}
