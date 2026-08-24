using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cα 트레이스만으로 이차구조(알파나선 / 베타가닥 / 루프)를 추정한다.
///
/// 이 프로젝트의 구조 JSON(AlphaFold 파싱 결과)에는 DSSP 같은 사전 계산된 이차구조 정보가
/// 없다. 그래서 P-SEA(Labesse &amp; Mornon, 1997)처럼 Cα 좌표만으로 판단하는 방식을 단순화해 쓴다:
/// 알파나선은 Cα(i)-Cα(i+3) 거리가 좁고(~5.0-5.7Å) 네 점(i-1..i+2)의 가상 이면각이 -40~-60도
/// 부근으로 매우 규칙적이다. 베타가닥은 거리가 넓게 펴져 있고(~8-10.5Å) 이면각이 ±160도 부근이다.
///
/// 임계값은 감으로 정한 문헌값이 아니라, 이 프로젝트의 KRAS 구조(structures/P01116.json)에서
/// 실제로 위 두 값을 계산해 실측한 뒤(퀘스트 데이터에 이미 있는 Switch-II/α3 헬릭스 구간을
/// 정답으로 삼아 대조) 잡았다. 완벽한 DSSP 대체는 아니지만, 리본 단계에서 "전체 폴드에
/// 나선/판/루프가 어떻게 배치돼 있는지"를 보여주는 교육 목적에는 충분하다.
/// </summary>
public static class SecondaryStructureAssigner
{
    public enum Type { Loop, Helix, Strand }

    // StructureLevelController.ExtractCaTrace가 넘기는 위치는 옹스트롬 좌표에 0.1을 곱한
    // 씬 단위(ProteinLoader.SpawnStructure와 동일 스케일)다. 아래 거리 임계값은 실측 옹스트롬
    // 값이므로, 트레이스에서 잰 거리를 이 상수로 나눠 옹스트롬으로 되돌린 뒤 비교해야 한다.
    // 이걸 빼먹으면 모든 거리가 임계값보다 한 자릿수 작게 나와 전부 Loop로만 분류된다.
    private const float SceneUnitsPerAngstrom = 0.1f;

    private const float HelixD3Min = 4.7f;
    private const float HelixD3Max = 6.6f;
    private const float HelixTorsionMin = -100f;
    private const float HelixTorsionMax = -15f;

    private const float StrandD3Min = 7.3f;
    private const float StrandD3Max = 11.5f;
    private const float StrandTorsionPositiveMin = 90f;   // 펴진 형태는 이면각이 +90~+180 부근이거나
    private const float StrandTorsionNegativeMax = -150f; // -180 가까이(랩어라운드)로도 나타난다

    // 한두 잔기짜리 우연한 일치는 실제 이차구조가 아니라 잡음일 가능성이 높다.
    // DSSP도 최소 길이 미만은 이차구조로 보지 않는 것과 같은 취지.
    private const int MinHelixRun = 3;
    private const int MinStrandRun = 2;

    /// <summary>
    /// trace: res_id 오름차순으로 정렬된 (res_id, 로컬 위치) Cα 트레이스
    /// (StructureLevelController.ExtractCaTrace와 같은 형식). 반환값은 trace와 같은 길이.
    /// </summary>
    public static Type[] Assign(List<KeyValuePair<int, Vector3>> trace)
    {
        int n = trace.Count;
        var raw = new Type[n];

        for (int i = 0; i < n; i++)
            raw[i] = Classify(trace, i);

        return SmoothShortRuns(raw);
    }

    private static Type Classify(List<KeyValuePair<int, Vector3>> trace, int i)
    {
        float? d3 = Distance(trace, i, 3);
        float? torsion = Torsion(trace, i);

        if (d3.HasValue && torsion.HasValue &&
            d3.Value >= HelixD3Min && d3.Value <= HelixD3Max &&
            torsion.Value >= HelixTorsionMin && torsion.Value <= HelixTorsionMax)
            return Type.Helix;

        if (d3.HasValue && torsion.HasValue &&
            d3.Value >= StrandD3Min && d3.Value <= StrandD3Max &&
            (torsion.Value >= StrandTorsionPositiveMin || torsion.Value <= StrandTorsionNegativeMax))
            return Type.Strand;

        return Type.Loop;
    }

    // 짧은 조각은 루프로 되돌린다. 리본 색이 한두 잔기마다 깜빡이며 튀어 보이는 것도 막아준다.
    private static Type[] SmoothShortRuns(Type[] raw)
    {
        var result = (Type[])raw.Clone();
        int n = result.Length;

        int i = 0;
        while (i < n)
        {
            Type t = result[i];
            int j = i;
            while (j < n && result[j] == t) j++;

            int minRun = t == Type.Helix ? MinHelixRun : t == Type.Strand ? MinStrandRun : 0;
            if (t != Type.Loop && (j - i) < minRun)
                for (int k = i; k < j; k++) result[k] = Type.Loop;

            i = j;
        }

        return result;
    }

    /// <summary>
    /// 서열상 delta만큼 떨어진 잔기의 위치. res_id가 실제로 i+delta여야만(사슬이 끊기지 않아야만)
    /// 값을 반환한다 — 리스트 인덱스만 보고 건너뛰면 결측 잔기 경계에서 엉뚱한 값이 나온다.
    /// </summary>
    private static bool TryGet(List<KeyValuePair<int, Vector3>> trace, int i, int delta, out Vector3 pos)
    {
        pos = default;
        int j = i + delta;
        if (i < 0 || i >= trace.Count || j < 0 || j >= trace.Count) return false;
        if (trace[j].Key != trace[i].Key + delta) return false;

        pos = trace[j].Value;
        return true;
    }

    private static float? Distance(List<KeyValuePair<int, Vector3>> trace, int i, int delta)
    {
        if (!TryGet(trace, i, 0, out Vector3 a) || !TryGet(trace, i, delta, out Vector3 b)) return null;
        return Vector3.Distance(a, b) / SceneUnitsPerAngstrom;
    }

    /// <summary>잔기 i의 가상 이면각 — 네 점 Cα(i-1), Cα(i), Cα(i+1), Cα(i+2)로 정의한다.</summary>
    private static float? Torsion(List<KeyValuePair<int, Vector3>> trace, int i)
    {
        if (!TryGet(trace, i, -1, out Vector3 p0)) return null;
        if (!TryGet(trace, i, 0, out Vector3 p1)) return null;
        if (!TryGet(trace, i, 1, out Vector3 p2)) return null;
        if (!TryGet(trace, i, 2, out Vector3 p3)) return null;
        return DihedralDegrees(p0, p1, p2, p3);
    }

    private static float DihedralDegrees(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        Vector3 b0 = p0 - p1;
        Vector3 b1 = p2 - p1;
        Vector3 b2 = p3 - p2;

        Vector3 n1 = Vector3.Cross(b0, b1);
        Vector3 n2 = Vector3.Cross(p1 - p2, b2);
        Vector3 m1 = Vector3.Cross(n1, b1.normalized);

        float x = Vector3.Dot(n1, n2);
        float y = Vector3.Dot(m1, n2);
        return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
    }
}
