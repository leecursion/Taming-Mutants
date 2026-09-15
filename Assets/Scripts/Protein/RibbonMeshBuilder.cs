using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 주쇄 좌표와 DSSP 이차구조로 진짜 카툰(리본) 지오메트리를 만든다.
/// PyMOL/ChimeraX가 그리는 것과 같은 표현이다 — 알파나선은 납작한 띠가 나선을 그리며 감기고,
/// 베타가닥은 끝이 화살촉인 평평한 판, 나머지 루프는 가느다란 관.
///
/// 예전에는 Cα를 실린더로 이어 붙이기만 해서, 나선이든 가닥이든 굵기가 같은 지그재그 철사로
/// 보였다. 폴드를 읽으려면 "여기는 나선, 저기는 시트"가 형태 자체로 드러나야 한다.
///
/// 만드는 순서(Carson &amp; Bugg, 1986의 리본 구성법):
///  1. Cα를 가이드 점으로 삼고 Catmull-Rom 스플라인으로 잔기 사이를 부드럽게 잇는다.
///  2. 각 잔기에서 카보닐(C→O) 방향을 리본의 "너비 축"으로 쓴다. 베타 시트에서 카보닐은
///     옆 가닥을 향하고(=시트 평면), 알파나선에서는 나선 축을 향하므로, 이 축을 따라
///     띠를 펴면 실제 단백질에서 관찰되는 리본 면 방향이 그대로 나온다.
///  3. 카보닐은 가닥에서 잔기마다 180도씩 뒤집히므로, 앞 잔기와 내적이 음수면 방향을 뒤집어
///     띠가 반 바퀴씩 꼬이는 것을 막는다.
///  4. 단면을 이차구조별로 바꿔 가며(원 / 타원 / 사각 / 화살촉) 관을 훑어 메시를 만든다.
///
/// 결과는 잔기 하나당 메시 하나로 쪼개 돌려준다. 클릭 판정(어느 잔기를 눌렀는가)과
/// 색·점멸이 잔기 단위로 걸려 있어서인데, 이웃 조각끼리 경계 링의 정점을 같은 좌표로
/// 공유하므로 이어 붙은 자리에 틈이 보이지 않는다.
/// </summary>
public static class RibbonMeshBuilder
{
    public struct Style
    {
        public float coilRadius;          // 루프 관의 반지름
        public float helixHalfWidth;      // 나선 띠 너비의 절반
        public float strandHalfWidth;     // 가닥 판 너비의 절반
        public float arrowHalfWidth;      // 화살촉이 가장 넓은 곳의 절반
        public float ribbonHalfThickness; // 납작한 띠의 두께 절반 (나선/가닥 공용)
        public float arrowResidues;       // 화살촉이 차지하는 잔기 수
        public int samplesPerResidue;     // 잔기당 스플라인 분할 수 (짝수여야 조각 경계가 링에 맞는다)
        public int sides;                 // 단면 둘레 분할 수

        /// <summary>
        /// 굵기 기준값 하나로 나머지 치수를 정한다. 비율은 PyMOL 기본 카툰(나선 폭 ≈ 2.2 Å,
        /// 가닥 폭 ≈ 2.0 Å, 화살촉 ≈ 3.4 Å, 띠 두께 ≈ 0.6 Å, 루프 관은 띠보다 가늘게)에 맞췄다.
        /// 루프가 띠만큼 굵으면 나선/가닥이 형태로 구분되지 않아 예전의 철사 트레이스로 되돌아간다.
        /// </summary>
        public static Style FromRadius(float radius)
        {
            float r = Mathf.Max(radius, 0.001f);
            return new Style
            {
                coilRadius = r * 0.50f,
                helixHalfWidth = r * 1.40f,
                strandHalfWidth = r * 1.25f,
                arrowHalfWidth = r * 2.10f,
                ribbonHalfThickness = r * 0.35f,
                arrowResidues = 1.6f,
                samplesPerResidue = 6,
                sides = 16,
            };
        }
    }

    public struct Piece
    {
        public int resId;
        public SecondaryStructureAssigner.Type type;
        public Mesh mesh;
    }

    // 단면 모양은 초타원 |x/a|^(1/p) + |y/b|^(1/p) = 1 로 통일한다. p = 1이면 타원(원),
    // 0에 가까울수록 모서리가 살아 사각형이 된다. 한 가지 식으로 원·타원·사각을 다 만들 수 있어
    // 이차구조가 바뀌는 자리에서도 정점 수가 같아 그대로 이어 붙는다.
    private const float RoundExponent = 1f;
    private const float StrandExponent = 0.28f;

    // 나선/가닥 가이드 점 다듬기. 가닥은 잔기마다 ±1 Å씩 주름(pleat)이 지는데 그대로 두면
    // 판이 물결친다. 나선은 Cα 자체가 이미 매끈해서 살짝만 당겨 스플라인 과도 팽창만 잡는다.
    private const int StrandSmoothPasses = 2;
    private const float StrandSmoothCenterWeight = 0.5f;
    private const float HelixSmoothCenterWeight = 0.7f;

    public static List<Piece> Build(BackboneChain chain, SecondaryStructureAssigner.Type[] ss,
                                    Style style, ICollection<int> onlyResIds = null)
    {
        var pieces = new List<Piece>();
        if (chain == null || chain.Count == 0 || ss == null || ss.Length != chain.Count) return pieces;

        int samples = Mathf.Max(2, style.samplesPerResidue);
        if (samples % 2 != 0) samples++;
        style.samplesPerResidue = samples;
        style.sides = Mathf.Max(6, style.sides);

        foreach (Vector2Int fragment in chain.Fragments)
            BuildFragment(chain, ss, style, fragment.x, fragment.y, onlyResIds, pieces);

        return pieces;
    }

    private static void BuildFragment(BackboneChain chain, SecondaryStructureAssigner.Type[] ss,
                                      Style style, int start, int end,
                                      ICollection<int> onlyResIds, List<Piece> pieces)
    {
        int m = end - start;
        if (m < 2) return; // 잔기 하나짜리 조각은 방향도 길이도 없어 그릴 리본이 없다

        Vector3[] guide = BuildGuidePoints(chain, ss, start, m);
        Vector3[] side = BuildSideVectors(chain, start, m);
        int[] strandEnd = BuildStrandElementEnds(ss, start, m);

        int S = style.samplesPerResidue;
        int K = style.sides;
        int ringCount = (m - 1) * S + 1;

        var ringPos = new Vector3[ringCount * K];
        var ringNormal = new Vector3[ringCount * K];
        var centers = new Vector3[ringCount];
        var tangents = new Vector3[ringCount];
        var shape = new Vector2[K];

        for (int i = 0; i < ringCount; i++)
        {
            float u = i / (float)S;

            Vector3 center = SamplePosition(guide, m, u);
            Vector3 tangent = SampleTangent(guide, m, u);
            if (tangent.sqrMagnitude < 1e-10f) tangent = Vector3.forward;
            tangent.Normalize();

            Vector3 width = SampleSide(side, m, u);
            width -= tangent * Vector3.Dot(width, tangent);
            if (width.sqrMagnitude < 1e-10f)
            {
                width = Vector3.Cross(tangent, Vector3.up);
                if (width.sqrMagnitude < 1e-10f) width = Vector3.Cross(tangent, Vector3.right);
            }
            width.Normalize();
            Vector3 normal = Vector3.Cross(tangent, width).normalized;
            width = Vector3.Cross(normal, tangent).normalized;

            centers[i] = center;
            tangents[i] = tangent;

            ProfileAt(ss, style, start, m, strandEnd, u, out float halfW, out float halfT, out float exponent);
            FillCrossSection(shape, halfW, halfT, exponent);

            for (int k = 0; k < K; k++)
            {
                Vector2 p = shape[k];
                Vector2 n2 = CrossSectionNormal(shape, k);
                ringPos[i * K + k] = center + width * p.x + normal * p.y;
                ringNormal[i * K + k] = (width * n2.x + normal * n2.y).normalized;
            }
        }

        for (int r = 0; r < m; r++)
        {
            int resId = chain.Residues[start + r].resId;
            if (onlyResIds != null && !onlyResIds.Contains(resId)) continue;

            int lo = Mathf.Max(0, r * S - S / 2);
            int hi = Mathf.Min(ringCount - 1, r * S + S / 2);
            if (hi <= lo) continue;

            // 조각 끝이거나, 표시 구간 필터에 잘려 옆 잔기가 빠진 자리면 단면을 막는다.
            bool capStart = lo == 0 || !IsIncluded(chain, onlyResIds, start + r - 1, start, end);
            bool capEnd = hi == ringCount - 1 || !IsIncluded(chain, onlyResIds, start + r + 1, start, end);

            Mesh mesh = BuildPieceMesh(ringPos, ringNormal, centers, tangents, K, S, lo, hi, capStart, capEnd);
            mesh.name = $"Ribbon_{resId}";
            pieces.Add(new Piece { resId = resId, type = ss[start + r], mesh = mesh });
        }
    }

    private static bool IsIncluded(BackboneChain chain, ICollection<int> onlyResIds,
                                   int index, int fragmentStart, int fragmentEnd)
    {
        if (index < fragmentStart || index >= fragmentEnd) return false;
        return onlyResIds == null || onlyResIds.Contains(chain.Residues[index].resId);
    }

    // --- 가이드 점 / 방향 ---

    private static Vector3[] BuildGuidePoints(BackboneChain chain, SecondaryStructureAssigner.Type[] ss,
                                              int start, int m)
    {
        var guide = new Vector3[m];
        for (int r = 0; r < m; r++) guide[r] = chain.Residues[start + r].ca;

        Smooth(guide, ss, start, m, SecondaryStructureAssigner.Type.Strand,
               StrandSmoothCenterWeight, StrandSmoothPasses);
        Smooth(guide, ss, start, m, SecondaryStructureAssigner.Type.Helix,
               HelixSmoothCenterWeight, 1);

        return guide;
    }

    private static void Smooth(Vector3[] guide, SecondaryStructureAssigner.Type[] ss, int start, int m,
                               SecondaryStructureAssigner.Type target, float centerWeight, int passes)
    {
        float sideWeight = (1f - centerWeight) * 0.5f;

        for (int p = 0; p < passes; p++)
        {
            var src = (Vector3[])guide.Clone();
            for (int r = 1; r < m - 1; r++)
            {
                if (ss[start + r] != target) continue;
                guide[r] = src[r - 1] * sideWeight + src[r] * centerWeight + src[r + 1] * sideWeight;
            }
        }
    }

    /// <summary>
    /// 리본의 너비 축. 잔기의 카보닐(C→O)에서 얻고, 앞 잔기와 반대 방향이면 뒤집는다 —
    /// 베타가닥에서는 카보닐이 잔기마다 정반대를 향하므로 이 보정이 없으면 판이 잔기마다
    /// 180도씩 꼬여 리본이 아니라 뒤틀린 리본 조각들로 보인다.
    /// </summary>
    private static Vector3[] BuildSideVectors(BackboneChain chain, int start, int m)
    {
        var side = new Vector3[m];
        Vector3 previous = Vector3.zero;

        for (int r = 0; r < m; r++)
        {
            BackboneChain.Residue res = chain.Residues[start + r];
            Vector3 carbonyl = res.hasC && res.hasO ? res.o - res.c : Vector3.zero;

            if (carbonyl.sqrMagnitude < 1e-10f) carbonyl = previous; // 주쇄가 빠진 잔기는 앞 방향 유지
            if (carbonyl.sqrMagnitude < 1e-10f) carbonyl = Vector3.up;

            carbonyl.Normalize();
            if (previous != Vector3.zero && Vector3.Dot(carbonyl, previous) < 0f) carbonyl = -carbonyl;

            side[r] = carbonyl;
            previous = carbonyl;
        }

        return side;
    }

    /// <summary>각 가닥 잔기가 속한 베타 요소의 마지막 잔기 인덱스 — 화살촉을 어디서 시작할지 정한다.</summary>
    private static int[] BuildStrandElementEnds(SecondaryStructureAssigner.Type[] ss, int start, int m)
    {
        var ends = new int[m];
        for (int r = 0; r < m; r++) ends[r] = r;

        int i = 0;
        while (i < m)
        {
            if (ss[start + i] != SecondaryStructureAssigner.Type.Strand) { i++; continue; }

            int j = i;
            while (j < m && ss[start + j] == SecondaryStructureAssigner.Type.Strand) j++;
            for (int k = i; k < j; k++) ends[k] = j - 1;
            i = j;
        }

        return ends;
    }

    // --- 단면 ---

    private static void ProfileAt(SecondaryStructureAssigner.Type[] ss, Style style, int start, int m,
                                  int[] strandEnd, float u,
                                  out float halfWidth, out float halfThickness, out float exponent)
    {
        int nearest = Mathf.Clamp(Mathf.FloorToInt(u + 0.5f), 0, m - 1);

        switch (ss[start + nearest])
        {
            case SecondaryStructureAssigner.Type.Helix:
                halfWidth = style.helixHalfWidth;
                halfThickness = style.ribbonHalfThickness;
                exponent = RoundExponent;
                return;

            case SecondaryStructureAssigner.Type.Strand:
                halfThickness = style.ribbonHalfThickness;
                exponent = StrandExponent;

                // 요소가 끝나는 잔기 경계까지 남은 거리(잔기 단위)로 화살촉을 만든다.
                // arrowResidues 지점에서 폭이 한 번에 넓어졌다가(화살촉 밑변) 끝으로 갈수록 좁아진다.
                float remaining = (strandEnd[nearest] + 0.5f) - u;
                if (remaining >= style.arrowResidues)
                {
                    halfWidth = style.strandHalfWidth;
                    return;
                }

                // 뾰족한 끝은 잔기 경계 바로 앞 샘플에서 완성돼야 한다 — 경계 자체는 이미 다음
                // 잔기(루프)의 몫이라 거기까지 늘여 잡으면 화살촉이 뭉툭하게 잘린 채 끝난다.
                // 끝 폭을 루프 관 반지름에 맞춰, 화살촉 끝에서 관이 그대로 이어지게 한다.
                float tipAt = 1f / Mathf.Max(style.samplesPerResidue, 1);
                float span = Mathf.Max(style.arrowResidues - tipAt, 0.01f);
                float taper = Mathf.Clamp01((remaining - tipAt) / span);
                halfWidth = Mathf.Lerp(style.coilRadius, style.arrowHalfWidth, taper);
                return;

            default:
                halfWidth = style.coilRadius;
                halfThickness = style.coilRadius;
                exponent = RoundExponent;
                return;
        }
    }

    private static void FillCrossSection(Vector2[] shape, float halfWidth, float halfThickness, float exponent)
    {
        int k = shape.Length;
        for (int i = 0; i < k; i++)
        {
            float theta = 2f * Mathf.PI * i / k;
            float c = Mathf.Cos(theta);
            float s = Mathf.Sin(theta);
            shape[i] = new Vector2(
                halfWidth * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), exponent),
                halfThickness * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), exponent));
        }
    }

    /// <summary>
    /// 단면 곡선의 바깥 방향. 이웃 점의 차분에서 구하므로 원이든 사각이든 같은 코드로 처리되고,
    /// 모서리에서는 인접 점이 촘촘해 자연스럽게 각이 살아난다.
    /// </summary>
    private static Vector2 CrossSectionNormal(Vector2[] shape, int k)
    {
        int n = shape.Length;
        Vector2 tangent = shape[(k + 1) % n] - shape[(k - 1 + n) % n];
        var normal = new Vector2(tangent.y, -tangent.x);

        if (normal.sqrMagnitude < 1e-12f) normal = shape[k];
        if (Vector2.Dot(normal, shape[k]) < 0f) normal = -normal;
        if (normal.sqrMagnitude < 1e-12f) return Vector2.right;

        return normal.normalized;
    }

    // --- 스플라인 ---

    private static Vector3 SamplePosition(Vector3[] guide, int m, float u)
    {
        int i = Mathf.Clamp(Mathf.FloorToInt(u), 0, m - 2);
        float t = Mathf.Clamp01(u - i);

        Vector3 p0 = guide[Mathf.Max(i - 1, 0)];
        Vector3 p1 = guide[i];
        Vector3 p2 = guide[i + 1];
        Vector3 p3 = guide[Mathf.Min(i + 2, m - 1)];

        return 0.5f * ((2f * p1) +
                       (-p0 + p2) * t +
                       (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) +
                       (-p0 + 3f * p1 - 3f * p2 + p3) * (t * t * t));
    }

    private static Vector3 SampleTangent(Vector3[] guide, int m, float u)
    {
        int i = Mathf.Clamp(Mathf.FloorToInt(u), 0, m - 2);
        float t = Mathf.Clamp01(u - i);

        Vector3 p0 = guide[Mathf.Max(i - 1, 0)];
        Vector3 p1 = guide[i];
        Vector3 p2 = guide[i + 1];
        Vector3 p3 = guide[Mathf.Min(i + 2, m - 1)];

        return 0.5f * ((-p0 + p2) +
                       2f * t * (2f * p0 - 5f * p1 + 4f * p2 - p3) +
                       3f * t * t * (-p0 + 3f * p1 - 3f * p2 + p3));
    }

    private static Vector3 SampleSide(Vector3[] side, int m, float u)
    {
        int i = Mathf.Clamp(Mathf.FloorToInt(u), 0, m - 2);
        float t = Mathf.Clamp01(u - i);
        return Vector3.Lerp(side[i], side[i + 1], t);
    }

    // --- 메시 ---

    private static Mesh BuildPieceMesh(Vector3[] ringPos, Vector3[] ringNormal,
                                       Vector3[] centers, Vector3[] tangents,
                                       int K, int S, int lo, int hi, bool capStart, bool capEnd)
    {
        int rings = hi - lo + 1;

        var vertices = new List<Vector3>(rings * K + 2 * (K + 1));
        var normals = new List<Vector3>(vertices.Capacity);
        var uvs = new List<Vector2>(vertices.Capacity);
        var triangles = new List<int>(rings * K * 6 + 2 * K * 3);

        for (int i = 0; i < rings; i++)
        {
            for (int k = 0; k < K; k++)
            {
                vertices.Add(ringPos[(lo + i) * K + k]);
                normals.Add(ringNormal[(lo + i) * K + k]);
                uvs.Add(new Vector2(k / (float)K, (lo + i) / (float)S));
            }
        }

        // 옆면. 정점 감기 순서는 Unity 관례(법선 = Cross(v1-v0, v2-v0))에 맞춰 바깥을 향하게 잡았다.
        for (int i = 0; i < rings - 1; i++)
        {
            for (int k = 0; k < K; k++)
            {
                int k2 = (k + 1) % K;
                int a = i * K + k;
                int b = i * K + k2;
                int c = (i + 1) * K + k;
                int d = (i + 1) * K + k2;

                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }
        }

        if (capStart) AddCap(vertices, normals, uvs, triangles, ringPos, centers, tangents, K, lo, front: false);
        if (capEnd) AddCap(vertices, normals, uvs, triangles, ringPos, centers, tangents, K, hi, front: true);

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>사슬(또는 표시 구간)이 끊기는 자리를 평평하게 막는다 — 안 막으면 관 속이 들여다보인다.</summary>
    private static void AddCap(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
                               List<int> triangles, Vector3[] ringPos, Vector3[] centers,
                               Vector3[] tangents, int K, int ring, bool front)
    {
        Vector3 capNormal = front ? tangents[ring] : -tangents[ring];

        int center = vertices.Count;
        vertices.Add(centers[ring]);
        normals.Add(capNormal);
        uvs.Add(new Vector2(0.5f, 0.5f));

        int first = vertices.Count;
        for (int k = 0; k < K; k++)
        {
            vertices.Add(ringPos[ring * K + k]);
            normals.Add(capNormal);
            uvs.Add(new Vector2(k / (float)K, front ? 1f : 0f));
        }

        for (int k = 0; k < K; k++)
        {
            int a = first + k;
            int b = first + (k + 1) % K;
            if (front) { triangles.Add(center); triangles.Add(a); triangles.Add(b); }
            else { triangles.Add(center); triangles.Add(b); triangles.Add(a); }
        }
    }
}
