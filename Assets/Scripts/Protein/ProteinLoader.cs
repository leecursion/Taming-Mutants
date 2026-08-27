using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// F-03.1 데이터 동적 로딩
/// StreamingAssets 또는 원격 URL에서 layered JSON(파이썬 사전 파싱 결과)을 읽어와
/// 원자(Atom)를 구체로, 공유결합을 실린더로 인스턴스화한다.
/// Quest(Android)에서는 StreamingAssets 경로가 jar:file:// 형태이므로
/// File.ReadAllText가 아닌 UnityWebRequest를 사용해야 한다.
/// </summary>
public class ProteinLoader : MonoBehaviour
{
    [Header("데이터 소스")]
    [Tooltip("StreamingAssets 기준 상대 경로, 예: structures/P00533.json")]
    public string streamingAssetsRelativePath;
    [Tooltip("원격 서버에서 직접 JSON을 받아올 경우 URL (비워두면 StreamingAssets 사용)")]
    public string remoteJsonUrl;
    [Tooltip("씬 시작 시 자동 로드. DockingQuestCatalog가 퀘스트별 구조를 로드하는 씬에서는 꺼진다")]
    public bool loadOnStart = true;

    [Header("렌더링 설정")]
    public GameObject atomPrefab;      // Sphere + PLDDTColorizer 부착된 프리팹
    public GameObject bondPrefab;      // 얇은 Cylinder 프리팹
    [Tooltip("원자 구체 지름 (unit). 결합 길이가 ~0.15unit이므로 0.15 이상이면 이웃 원자와 겹쳐 보인다")]
    public float atomScale = 0.13f;
    public float bondCovalentMaxDistance = 1.9f; // Angstrom 기준 결합으로 볼 최대 거리
    [Tooltip("결합 실린더 반지름(X/Z). bondPrefab 기본 Cylinder는 반지름 0.5unit이라 그대로 두면 원자보다 훨씬 두꺼워짐")]
    public float bondRadiusScale = 0.05f;
    [Tooltip("결합 실린더를 홀로그램 대신 불투명(실제) 재질로 표시")]
    public bool solidBonds = true;

    [Header("레이어 필터 (F-03.2 분자 레이어 분해)")]
    public bool showBackboneOnly = false; // true면 N, CA, C, O 만 표시

    public event Action<ProteinData> OnLoaded;

    /// <summary>
    /// 마지막으로 로드한 구조에 적용한 중심 보정값(0.1 스케일 적용 후 기준).
    ///
    /// PDB 원본 좌표는 결정학 좌표계의 임의 위치에 있어 원점과 거리가 멀 때가 많다
    /// (예: -20~-40 Å대). 그대로 배치하면 단백질 전체가 ProteinAnchor_Main의 원점에서
    /// 1~2m씩 벗어난 자리에 생기는데, 씬의 카메라 앵커는 그 원점을 기준으로 잡혀 있어
    /// 줌인해도 구조가 화면 밖에 있는 것처럼 보인다. CenterOffset을 빼서 항상 원점 근처에
    /// 오도록 맞춘다. StructureLevelController가 리본/Helix를 지을 때도 이 값을 같이 빼야
    /// 원자와 리본이 어긋나지 않는다.
    /// </summary>
    public Vector3 CenterOffset { get; private set; }

    private readonly List<GameObject> _spawnedAtoms = new List<GameObject>();
    private readonly List<GameObject> _spawnedBonds = new List<GameObject>();
    // 잔기 필터 표시(SetVisibleResidues)용 — _spawnedAtoms/_spawnedBonds와 인덱스 병렬
    private readonly List<int> _spawnedAtomResIds = new List<int>();
    // 결합의 양 끝 원자 인덱스(_spawnedAtoms 기준) — 결합 표시는 원자 표시 상태를 그대로 따른다
    private readonly List<Vector2Int> _spawnedBondAtomIndices = new List<Vector2Int>();

    [Serializable]
    public class AtomRecord
    {
        public string name;
        public string element;
        public float x, y, z;
        public float bfactor;   // AlphaFold: pLDDT 값이 저장되는 필드
        public string res_name;
        public int res_id;
        public bool is_backbone;
    }

    [Serializable]
    public class ProteinData
    {
        public List<AtomRecord> atoms;
    }

    private bool _loadRequested;

    private void Start()
    {
        // 인트로 동안에는 이 오브젝트가 꺼져 있어서 Start가 늦게 불린다.
        // 그 사이 QuestSession이 이미 Reload를 불렀다면 다시 읽지 않는다 —
        // 원자 2천 개의 결합 계산은 O(n²)이라 한 번 더 도는 비용이 크다.
        // loadOnStart는 DockingQuestCatalog처럼 로드를 직접 주도하는 씬에서 꺼진다.
        if (loadOnStart && !_loadRequested) Reload();
    }

    /// <summary>
    /// 현재 설정된 경로로 구조를 다시 읽어온다.
    ///
    /// 퀘스트를 고른 뒤에 <see cref="streamingAssetsRelativePath"/>를 바꾸는 흐름
    /// (QuestSession.StartQuest)이 있어서 Start 이후에도 다시 로드할 수 있어야 한다.
    /// 이전 로딩이 진행 중일 수 있으므로 코루틴을 먼저 정리한다 —
    /// 그러지 않으면 두 구조의 원자가 한 앵커 아래 섞여 쌓인다.
    /// </summary>
    public void Reload()
    {
        // 꺼진 오브젝트에서는 코루틴을 시작할 수 없어 콘솔 에러만 남는다.
        // 인트로 동안(무대 숨김) 들어오는 요청은 경로 변경만 반영된 상태로 두고,
        // 무대를 켜는 쪽(QuestSession.StartQuest)이 다시 Reload를 부르는 흐름에 맡긴다.
        if (!gameObject.activeInHierarchy) return;

        _loadRequested = true;
        StopAllCoroutines();
        StartCoroutine(LoadRoutine());
    }

    /// <summary>
    /// 런타임에 다른 구조로 교체 로드 (퀘스트 전환용).
    /// 기존 원자/결합은 SpawnStructure의 ClearPrevious로 제거되고
    /// 완료 시 OnLoaded가 다시 발생하므로 리본/하이라이트 등 구독자도 재구축된다.
    /// </summary>
    public void LoadStructure(string relativePath)
    {
        streamingAssetsRelativePath = relativePath;
        remoteJsonUrl = null; // 명시적 StreamingAssets 로드이므로 원격 URL 우선순위 해제
        Reload();
    }

    private IEnumerator LoadRoutine()
    {
        string url = !string.IsNullOrEmpty(remoteJsonUrl)
            ? remoteJsonUrl
            : System.IO.Path.Combine(Application.streamingAssetsPath, streamingAssetsRelativePath);

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[ProteinLoader] 로딩 실패: {req.error} ({url})");
                yield break;
            }

            ProteinData data = JsonUtility.FromJson<ProteinData>(req.downloadHandler.text);
            SpawnStructure(data);
            OnLoaded?.Invoke(data);
        }
    }

    private void SpawnStructure(ProteinData data)
    {
        ClearPrevious();

        CenterOffset = ComputeCenterOffset(data);

        var positions = new List<Vector3>();
        var records = new List<AtomRecord>();

        foreach (var atom in data.atoms)
        {
            if (showBackboneOnly && !atom.is_backbone) continue;

            // Angstrom -> 씬 스케일 축소 후, 구조 전체 중심을 원점으로 당긴다.
            Vector3 pos = new Vector3(atom.x, atom.y, atom.z) * 0.1f - CenterOffset;
            GameObject go = Instantiate(atomPrefab, transform);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * atomScale;

            var colorizer = go.GetComponent<PLDDTColorizer>();
            if (colorizer != null) colorizer.ApplyConfidence(atom.bfactor);

            var info = go.GetComponent<AtomInfo>();
            if (info != null) info.Set(atom.name, atom.element, atom.res_name, atom.res_id, atom.bfactor);

            _spawnedAtoms.Add(go);
            _spawnedAtomResIds.Add(atom.res_id);
            positions.Add(pos);
            records.Add(atom);
        }

        BuildBonds(positions, records);
    }

    /// <summary>
    /// 구조 전체의 중심(0.1 스케일 적용 후 기준)을 구한다. CA(주쇄 알파탄소)가 있으면 그
    /// 평균만 쓴다 — 원자 전체로 평균 내면 한쪽에 몰린 곁사슬이나 헤테로 원자에 중심이
    /// 끌려갈 수 있어서, 사슬을 따라 고르게 분포한 CA가 구조의 "몸통" 중심을 더 안정적으로
    /// 대표한다. CA가 하나도 없는 데이터라면(예외적인 경우) 전체 원자 평균으로 대체한다.
    /// StructureLevelController.ExtractCaTrace도 같은 원자 목록에서 같은 계산을 하므로,
    /// 원자와 리본이 항상 같은 기준으로 어긋남 없이 맞는다.
    /// </summary>
    public static Vector3 ComputeCenterOffset(ProteinData data)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (var atom in data.atoms)
        {
            if (atom.name != "CA") continue;
            sum += new Vector3(atom.x, atom.y, atom.z) * 0.1f;
            count++;
        }

        if (count == 0)
        {
            foreach (var atom in data.atoms)
            {
                sum += new Vector3(atom.x, atom.y, atom.z) * 0.1f;
                count++;
            }
        }

        return count > 0 ? sum / count : Vector3.zero;
    }

    // 원자간 거리 기반 결합 추정 (O(n^2) 이지만 로딩 시 1회만 수행)
    private void BuildBonds(List<Vector3> positions, List<AtomRecord> records)
    {
        var linkedResiduePairs = new HashSet<long>();

        for (int i = 0; i < positions.Count; i++)
        {
            for (int j = i + 1; j < positions.Count; j++)
            {
                float dist = Vector3.Distance(positions[i], positions[j]) * 10f; // 다시 Angstrom 단위로 환산
                if (dist <= bondCovalentMaxDistance)
                {
                    CreateBond(positions[i], positions[j], i, j);
                    if (records[i].res_id != records[j].res_id)
                        linkedResiduePairs.Add(PackPair(records[i].res_id, records[j].res_id));
                }
            }
        }

        EnsureChainConnectivity(positions, records, linkedResiduePairs);
    }

    /// <summary>
    /// 서열상 이웃한 잔기(res_id 연속) 사이에 결합이 하나도 생기지 않았으면
    /// 백본 C–N(없으면 CA–CA)을 강제로 이어준다. 거리 임계값에 걸리지 않는 특이 좌표나
    /// 전처리 편차가 있어도 사슬이 중간에 끊겨 보이는 일이 없게 하는 보증 장치.
    /// </summary>
    private void EnsureChainConnectivity(List<Vector3> positions, List<AtomRecord> records,
                                         HashSet<long> linkedResiduePairs)
    {
        var backboneOfRes = new Dictionary<int, int[]>(); // res_id -> [C, N, CA] 원자 인덱스 (-1 = 없음)
        for (int i = 0; i < records.Count; i++)
        {
            if (!backboneOfRes.TryGetValue(records[i].res_id, out int[] slots))
                backboneOfRes[records[i].res_id] = slots = new[] { -1, -1, -1 };
            if (records[i].name == "C") slots[0] = i;
            else if (records[i].name == "N") slots[1] = i;
            else if (records[i].name == "CA") slots[2] = i;
        }

        var resIds = new List<int>(backboneOfRes.Keys);
        resIds.Sort();

        for (int k = 0; k < resIds.Count - 1; k++)
        {
            int r0 = resIds[k], r1 = resIds[k + 1];
            if (r1 != r0 + 1) continue; // 서열상 떨어진 잔기(별개 조각)까지 잇지는 않는다
            if (linkedResiduePairs.Contains(PackPair(r0, r1))) continue;

            int[] a = backboneOfRes[r0], b = backboneOfRes[r1];
            int ia = a[0] >= 0 ? a[0] : a[2]; // C 없으면 CA
            int ib = b[1] >= 0 ? b[1] : b[2]; // N 없으면 CA
            if (ia < 0 || ib < 0) continue;

            CreateBond(positions[ia], positions[ib], ia, ib);
        }
    }

    private static long PackPair(int a, int b)
    {
        return ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
    }

    private void CreateBond(Vector3 a, Vector3 b, int atomIndexA, int atomIndexB)
    {
        GameObject bond = Instantiate(bondPrefab, transform);
        if (solidBonds)
        {
            RuntimeMaterials.ApplySolid(bond);
            var renderer = bond.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", new Color(0.75f, 0.78f, 0.82f));
                renderer.SetPropertyBlock(mpb);
            }
        }
        Vector3 mid = (a + b) / 2f;
        bond.transform.localPosition = mid;
        // a/b는 부모(앵커) 로컬 좌표 — transform.up(월드 기준) 대신 로컬 회전으로 정렬해야
        // 로드 시점에 앵커가 회전해 있어도(우클릭 드래그 회전 등) 결합이 원자 사이를 정확히 잇는다.
        bond.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        float length = Vector3.Distance(a, b);
        bond.transform.localScale = new Vector3(bondRadiusScale, length / 2f, bondRadiusScale);
        _spawnedBonds.Add(bond);
        _spawnedBondAtomIndices.Add(new Vector2Int(atomIndexA, atomIndexB));
    }

    private void ClearPrevious()
    {
        foreach (var g in _spawnedAtoms) if (g) Destroy(g);
        foreach (var g in _spawnedBonds) if (g) Destroy(g);
        _spawnedAtoms.Clear();
        _spawnedBonds.Clear();
        _spawnedAtomResIds.Clear();
        _spawnedBondAtomIndices.Clear();
    }

    // 리본/Helix 표시 레벨(StructureLevelController)에서 아미노산 단계로 넘어갈 때만
    // 원자/결합을 보이게 하기 위한 토글. 로드 직후에는 SetAtomsVisible(false)로 숨겨둔다.
    public void SetAtomsVisible(bool visible)
    {
        foreach (var g in _spawnedAtoms) if (g) g.SetActive(visible);
        foreach (var g in _spawnedBonds) if (g) g.SetActive(visible);
    }

    /// <summary>
    /// 현재 스폰된 원자/결합 전체를 알파 페이드시킨다 (예: CFTR corrector 성공 시
    /// 8EJ1→8EIQ 구조 스왑을 "뚝 끊기지 않게" 보여주는 연출). ThermalStabilityController의
    /// 알파 처리와 같은 MaterialPropertyBlock 기법을 RuntimeMaterials.Transparent로 공유한다.
    /// LoadStructure를 부르는 쪽(예: CftrRescueController)이
    /// FadeOutRoutine → LoadStructure → (OnLoaded 후) FadeInRoutine 순으로 조합해서 쓴다.
    /// </summary>
    public IEnumerator FadeOutRoutine(float duration) { yield return FadeAllRoutine(1f, 0f, duration); }
    public IEnumerator FadeInRoutine(float duration) { yield return FadeAllRoutine(0f, 1f, duration); }
    public void FadeIn(float duration) { StartCoroutine(FadeInRoutine(duration)); }

    private IEnumerator FadeAllRoutine(float from, float to, float duration)
    {
        var renderers = new List<Renderer>();
        foreach (var g in _spawnedAtoms) { if (!g) continue; var r = g.GetComponent<Renderer>(); if (r) renderers.Add(r); }
        foreach (var g in _spawnedBonds) { if (!g) continue; var r = g.GetComponent<Renderer>(); if (r) renderers.Add(r); }
        if (renderers.Count == 0) yield break;

        foreach (var r in renderers) r.sharedMaterial = RuntimeMaterials.Transparent;

        var mpb = new MaterialPropertyBlock();
        void ApplyAlpha(float alpha)
        {
            foreach (var r in renderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                Color c = mpb.GetColor("_BaseColor");
                if (c.a <= 0f) c = new Color(0.75f, 0.78f, 0.82f, 1f); // 색이 안 잡혀 있으면 은은한 회색
                c.a = alpha;
                mpb.SetColor("_BaseColor", c);
                r.SetPropertyBlock(mpb);
            }
        }

        ApplyAlpha(from);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            ApplyAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        ApplyAlpha(to);
    }

    /// <summary>
    /// 지정한 잔기(res_id) 집합에 속한 원자만 표시한다 (아미노산 단계의 부분 표시용).
    /// null이면 전체 표시.
    /// 결합이 하나도 없이 홀로 남는 원자(퀘스트 범위 필터 경계에서 결합 상대가 잘려나간 경우)는
    /// 사슬/포켓 형태를 읽는 데 도움이 안 되므로 함께 숨겨, 남은 원자가 모두 결합으로 연결되게 한다.
    /// </summary>
    public void SetVisibleResidues(ICollection<int> residues)
    {
        if (residues == null) { SetAtomsVisible(true); return; }

        for (int i = 0; i < _spawnedAtoms.Count; i++)
            if (_spawnedAtoms[i])
                _spawnedAtoms[i].SetActive(residues.Contains(_spawnedAtomResIds[i]));

        // 결합은 양 끝 원자가 모두 보일 때만 표시한다 — 표시 경계에 걸친 결합이 허공에 뜨지 않는다.
        var hasVisibleBond = new bool[_spawnedAtoms.Count];
        for (int i = 0; i < _spawnedBonds.Count; i++)
        {
            if (!_spawnedBonds[i]) continue;
            Vector2Int pair = _spawnedBondAtomIndices[i];
            bool visible = _spawnedAtoms[pair.x] && _spawnedAtoms[pair.x].activeSelf &&
                           _spawnedAtoms[pair.y] && _spawnedAtoms[pair.y].activeSelf;
            _spawnedBonds[i].SetActive(visible);
            if (visible)
            {
                hasVisibleBond[pair.x] = true;
                hasVisibleBond[pair.y] = true;
            }
        }

        for (int i = 0; i < _spawnedAtoms.Count; i++)
            if (_spawnedAtoms[i] && _spawnedAtoms[i].activeSelf && !hasVisibleBond[i])
                _spawnedAtoms[i].SetActive(false);
    }
}
