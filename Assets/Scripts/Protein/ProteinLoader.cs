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

    private readonly List<GameObject> _spawnedAtoms = new List<GameObject>();
    private readonly List<GameObject> _spawnedBonds = new List<GameObject>();
    // 잔기 필터 표시(SetVisibleResidues)용 — _spawnedAtoms/_spawnedBonds와 인덱스 병렬
    private readonly List<int> _spawnedAtomResIds = new List<int>();
    private readonly List<Vector2Int> _spawnedBondResIds = new List<Vector2Int>();

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

        var positions = new List<Vector3>();
        var records = new List<AtomRecord>();

        foreach (var atom in data.atoms)
        {
            if (showBackboneOnly && !atom.is_backbone) continue;

            Vector3 pos = new Vector3(atom.x, atom.y, atom.z) * 0.1f; // Angstrom -> 씬 스케일 축소
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

    // 원자간 거리 기반 결합 추정 (O(n^2) 이지만 로딩 시 1회만 수행)
    private void BuildBonds(List<Vector3> positions, List<AtomRecord> records)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            for (int j = i + 1; j < positions.Count; j++)
            {
                float dist = Vector3.Distance(positions[i], positions[j]) * 10f; // 다시 Angstrom 단위로 환산
                if (dist <= bondCovalentMaxDistance)
                {
                    CreateBond(positions[i], positions[j], records[i].res_id, records[j].res_id);
                }
            }
        }
    }

    private void CreateBond(Vector3 a, Vector3 b, int resIdA, int resIdB)
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
        bond.transform.up = (b - a).normalized;
        float length = Vector3.Distance(a, b);
        bond.transform.localScale = new Vector3(bondRadiusScale, length / 2f, bondRadiusScale);
        _spawnedBonds.Add(bond);
        _spawnedBondResIds.Add(new Vector2Int(resIdA, resIdB));
    }

    private void ClearPrevious()
    {
        foreach (var g in _spawnedAtoms) if (g) Destroy(g);
        foreach (var g in _spawnedBonds) if (g) Destroy(g);
        _spawnedAtoms.Clear();
        _spawnedBonds.Clear();
        _spawnedAtomResIds.Clear();
        _spawnedBondResIds.Clear();
    }

    // 리본/Helix 표시 레벨(StructureLevelController)에서 아미노산 단계로 넘어갈 때만
    // 원자/결합을 보이게 하기 위한 토글. 로드 직후에는 SetAtomsVisible(false)로 숨겨둔다.
    public void SetAtomsVisible(bool visible)
    {
        foreach (var g in _spawnedAtoms) if (g) g.SetActive(visible);
        foreach (var g in _spawnedBonds) if (g) g.SetActive(visible);
    }

    /// <summary>
    /// 지정한 잔기(res_id) 집합에 속한 원자만 표시한다 (아미노산 단계의 부분 표시용).
    /// 결합은 양 끝 원자의 잔기가 모두 집합에 있을 때만 표시. null이면 전체 표시.
    /// </summary>
    public void SetVisibleResidues(ICollection<int> residues)
    {
        if (residues == null) { SetAtomsVisible(true); return; }

        for (int i = 0; i < _spawnedAtoms.Count; i++)
            if (_spawnedAtoms[i])
                _spawnedAtoms[i].SetActive(residues.Contains(_spawnedAtomResIds[i]));

        for (int i = 0; i < _spawnedBonds.Count; i++)
            if (_spawnedBonds[i])
                _spawnedBonds[i].SetActive(residues.Contains(_spawnedBondResIds[i].x) &&
                                           residues.Contains(_spawnedBondResIds[i].y));
    }
}
