using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F-02.3 변이 부위 하이라이트 / F-02.4 상황 맥락 브리핑
/// 지정된 잔기 ID(residue id) 목록을 변이 부위로 표시(발광/펄스)하고,
/// 사용자가 해당 부위를 선택(포인터/시선/터치)하면 AI Co-Scientist에게
/// 브리핑을 요청하는 이벤트를 발생시킨다.
/// </summary>
public class MutationHighlighter : MonoBehaviour
{
    [Serializable]
    public class MutationSite
    {
        public int residueId;
        public string description; // 예: "EGFR L858R - 폐암 관련 활성화 돌연변이"
    }

    public List<MutationSite> mutationSites = new List<MutationSite>();
    public Color highlightColor = new Color(1f, 0.15f, 0.15f);
    public float pulseSpeed = 2f;

    public event Action<MutationSite> OnMutationSelected;

    private readonly Dictionary<int, List<AtomInfo>> _atomsByResidue = new Dictionary<int, List<AtomInfo>>();

    // ProteinLoader.OnLoaded 이후 호출하여 씬에 생성된 원자들을 잔기별로 인덱싱
    public void IndexAtoms(IEnumerable<AtomInfo> allAtoms)
    {
        _atomsByResidue.Clear();
        foreach (var atomInfo in allAtoms)
        {
            if (!_atomsByResidue.TryGetValue(atomInfo.ResidueId, out var list))
            {
                list = new List<AtomInfo>();
                _atomsByResidue[atomInfo.ResidueId] = list;
            }
            list.Add(atomInfo);

            // 변이 부위에 해당하면 펄스 하이라이트 컴포넌트 부착
            if (mutationSites.Exists(m => m.residueId == atomInfo.ResidueId))
            {
                var pulse = atomInfo.gameObject.GetComponent<PulseHighlight>();
                if (pulse == null) pulse = atomInfo.gameObject.AddComponent<PulseHighlight>();
                pulse.Init(highlightColor, pulseSpeed);
            }
        }
    }

    // 사용자가 특정 원자(변이 부위)를 선택했을 때 호출 (Raycast/시선/포인터 이벤트에서 연결)
    // 같은 잔기를 몇 번을 다시 선택해도 매번 이벤트와 시각 피드백(섬광)이 다시 발생한다.
    public void SelectResidue(int residueId)
    {
        var site = mutationSites.Find(m => m.residueId == residueId);
        if (site == null) return;

        // 재선택 여부를 사용자가 바로 알 수 있도록 해당 잔기 원자들을 짧게 섬광시킨다
        if (_atomsByResidue.TryGetValue(residueId, out var atoms))
        {
            foreach (var atomInfo in atoms)
            {
                if (atomInfo == null) continue;
                var pulse = atomInfo.GetComponent<PulseHighlight>();
                if (pulse != null) pulse.Flash();
            }
        }

        OnMutationSelected?.Invoke(site);
    }
}

/// <summary>변이 부위 원자를 부드럽게 펄스(발광)시키는 보조 컴포넌트.</summary>
public class PulseHighlight : MonoBehaviour
{
    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private Color _baseColor;
    private float _speed;
    private float _flashUntil;

    public void Init(Color color, float speed)
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
        _baseColor = color;
        _speed = speed;
    }

    /// <summary>선택 피드백: 잠시 흰색에 가깝게 밝아졌다가 원래 펄스로 돌아온다. 재호출 시 매번 다시 반짝인다.</summary>
    public void Flash(float duration = 0.5f)
    {
        _flashUntil = Time.time + duration;
    }

    private void Update()
    {
        if (_renderer == null) return;
        float t = (Mathf.Sin(Time.time * _speed) + 1f) * 0.5f;
        Color c = Color.Lerp(_baseColor * 0.6f, _baseColor, t);
        float emission = 1.5f;

        if (Time.time < _flashUntil)
        {
            c = Color.Lerp(c, Color.white, 0.7f);
            emission = 3f;
        }

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_BaseColor", c);
        _mpb.SetColor("_EmissionColor", c * emission);
        _renderer.SetPropertyBlock(_mpb);
    }
}