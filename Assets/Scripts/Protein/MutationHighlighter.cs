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
        [Tooltip("대사에서 번호 대신 부를 이름. 예: '고장 난 스위치 자리'. " +
                 "비워두면 '858번 잔기'처럼 번호로 부른다.")]
        public string alias;

        /// <summary>
        /// 비서가 이 자리를 부를 때 쓰는 이름.
        ///
        /// 별명이 있으면 별명을 앞세우고 번호를 괄호로 덧붙인다 — 중학생에게 "858번"은 기억에
        /// 남지 않지만, 화면의 숫자표(<see cref="ResidueNumberTag"/>)와 짝을 맞추려면 번호도
        /// 한 번은 들려줘야 한다.
        /// </summary>
        public string SpokenName =>
            string.IsNullOrWhiteSpace(alias) ? $"{residueId}번 잔기" : $"{alias}({residueId}번)";
    }

    public List<MutationSite> mutationSites = new List<MutationSite>();
    public Color highlightColor = new Color(1f, 0.15f, 0.15f);
    public float pulseSpeed = 2f;

    [Header("고장 표현")]
    [Tooltip("변이 부위를 규칙적인 맥동 대신 불규칙 플리커 + 미세 경련으로 표시한다. " +
             "규칙적으로 밝아졌다 어두워지는 것은 충전 표시등·병원 모니터처럼 '정상 작동 중'으로 " +
             "읽혀, 정작 알려야 할 '여기가 고장났다'와 정반대 인상을 준다. 끄면 예전의 사인 맥동.")]
    public bool showAsMalfunction = true;
    [Tooltip("경련 진폭(구조 로컬 단위). 결합 실린더는 함께 움직이지 않으므로 원자 반지름보다 " +
             "훨씬 작아야 한다 — 크게 주면 떠는 게 아니라 결합에서 뽑혀 나온 것처럼 보인다. " +
             "0이면 색만 지직거린다.")]
    public float malfunctionJitter = 0.012f;

    [Header("잔기 번호 표시")]
    [Tooltip("변이 부위 옆에 잔기 번호를 띄운다. 비서가 '858번 자리'라고 말할 때 " +
             "화면에서 그 번호를 눈으로 찾을 수 있게 하는 유일한 수단이다.")]
    public bool showNumberTags = true;
    [Tooltip("숫자표 폰트. 한글 별명까지 띄우려면 한글 글리프가 있는 폰트를 지정한다 — " +
             "비우면 내장 LegacyRuntime을 쓰고, 한글이 깨지므로 별명 없이 번호만 표시한다.")]
    public Font tagFont;
    [Tooltip("숫자표 색. 변이 강조색과 같은 계열로 두면 숫자와 반짝이는 자리가 한눈에 묶인다.")]
    public Color tagColor = new Color(1f, 0.45f, 0.35f);
    [Tooltip("잔기에서 숫자까지 지시선 길이(구조 로컬 단위). 구조 바깥쪽으로 뻗는다.")]
    public float tagLeaderLength = 0.7f;
    [Tooltip("화면상 글자 크기. 카메라 거리 1 단위당 글자 높이(월드 단위)라 거리와 무관하게 " +
             "화면에서 차지하는 비율이 일정하다 — FOV 60도 기준 0.045면 화면 높이의 약 4%.")]
    public float tagApparentSize = 0.045f;
    [Tooltip("번호표를 리본 단계에서만 표시하기 위해 참조한다. 비우면 같은 오브젝트에서, " +
             "그래도 없으면 씬에서 찾는다. 끝내 못 찾으면 번호표는 항상 표시된다.")]
    public StructureLevelController levelController;

    public event Action<MutationSite> OnMutationSelected;

    private readonly Dictionary<int, List<AtomInfo>> _atomsByResidue = new Dictionary<int, List<AtomInfo>>();
    private readonly List<ResidueNumberTag> _tags = new List<ResidueNumberTag>();

    private void OnEnable()
    {
        if (levelController == null) levelController = GetComponent<StructureLevelController>();
        if (levelController == null)
            levelController = FindFirstObjectByType<StructureLevelController>(FindObjectsInactive.Include);

        if (levelController != null) levelController.OnLevelChanged += HandleLevelChanged;
        ApplyTagVisibility();
    }

    private void OnDisable()
    {
        if (levelController != null) levelController.OnLevelChanged -= HandleLevelChanged;
    }

    private void HandleLevelChanged(StructureLevelController.ViewLevel level) => ApplyTagVisibility();

    /// <summary>
    /// 번호표는 리본 단계에서만 보여준다.
    ///
    /// 리본은 구조 전체가 한눈에 들어오는 단계라 "이 많은 것 중 어디"라는 질문이 실제로 있고,
    /// 번호표가 그 답이다. 반면 나선·아미노산 단계는 사용자가 이미 그 구간만 골라 확대한
    /// 상태라 번호표가 답할 질문이 남아 있지 않고, 지시선이 확대된 구조를 가로질러 시야만 막는다.
    /// </summary>
    private void ApplyTagVisibility()
    {
        bool visible = levelController == null ||
                       levelController.CurrentLevel == StructureLevelController.ViewLevel.Ribbon;

        foreach (var tag in _tags)
            if (tag != null) tag.SetVisible(visible);
    }

    /// <summary>
    /// 씬의 하이라이터를 찾고, 없으면 단백질 로더 위에 만들어 준다.
    ///
    /// 이 컴포넌트는 어느 씬에도 저장돼 있지 않았다 — 그래서 변이 부위 펄스도,
    /// 도입 시나리오의 <see cref="ScenarioAction.FlashMutationSite"/>("858번 자리를 보세요")도
    /// 참조가 null이라 통째로 아무 일도 하지 않았다. 씬 저장을 잊었다는 이유로 연출이
    /// 조용히 사라지지 않도록, 필요한 쪽이 이걸 불러 확보한다.
    ///
    /// 단백질 로더의 오브젝트에 붙이는 이유: 원자가 그 아래 생성되므로 번호표를 붙일
    /// 부모가 같고, 비서가 <c>follower.FocusOn(highlighter.transform)</c>으로 쳐다볼 때도
    /// 자연스럽게 구조 쪽을 본다. StructureLevelController도 같은 오브젝트에 있어
    /// 번호표의 리본 단계 전용 표시가 GetComponent 한 번으로 이어진다.
    /// </summary>
    public static MutationHighlighter EnsureFor(ProteinLoader loader)
    {
        var existing = FindFirstObjectByType<MutationHighlighter>(FindObjectsInactive.Include);
        if (existing != null) return existing;
        if (loader == null) return null;

        return loader.gameObject.AddComponent<MutationHighlighter>();
    }

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
            if (mutationSites.Exists(m => m != null && m.residueId == atomInfo.ResidueId))
            {
                var pulse = atomInfo.gameObject.GetComponent<PulseHighlight>();
                if (pulse == null) pulse = atomInfo.gameObject.AddComponent<PulseHighlight>();

                if (showAsMalfunction)
                    pulse.Init(highlightColor, pulseSpeed, PulseStyle.Malfunction,
                               malfunctionJitter, SeedFor(atomInfo.ResidueId));
                else
                    pulse.Init(highlightColor, pulseSpeed);
            }
        }

        RebuildNumberTags();
    }

    /// <summary>
    /// 잔기 하나가 통째로 같은 박자로 떨도록, 잔기 번호에서 고정된 seed를 만든다.
    /// 잔기가 다르면 박자도 달라야 한다 — 변이 부위가 둘 이상인 사건(EGFR 858/790,
    /// CFTR 507/509)에서 둘이 똑같이 껌뻑이면 한 덩어리로 보인다.
    ///
    /// 황금비 소수부를 곱하는 것은 연속한 번호(507, 508, 509)가 비슷한 값으로 몰리지 않게
    /// 흩뜨리기 위한 것이다. Perlin 노이즈는 정수 좌표 근처에서 값이 뭉치므로 소수부가 필요하다.
    ///
    /// 나선 단계의 표적 잔기 띠(<see cref="StructureLevelController"/>)도 같은 함수를 쓴다 —
    /// 같은 잔기는 어느 단계에서든 같은 박자로 지직거려야 "저게 그 자리"로 이어진다.
    /// </summary>
    public static float SeedFor(int residueId) => residueId * 0.6180339887f % 1f * 97f;

    // --- 잔기 번호 표시 ---

    /// <summary>
    /// 변이 부위마다 번호표를 다시 만든다. 구조가 바뀌면(퀘스트 전환) 잔기 위치도 통째로 달라지므로
    /// 남은 태그는 버리고 새로 세운다.
    /// </summary>
    private void RebuildNumberTags()
    {
        foreach (var tag in _tags)
            if (tag != null) Destroy(tag.gameObject);
        _tags.Clear();

        if (!showNumberTags) return;

        // 별명은 한글이라 내장 폰트로는 네모로 나온다. 전용 폰트가 지정된 경우에만 띄운다.
        Font font = tagFont != null ? tagFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bool captionsReadable = tagFont != null;

        foreach (var site in mutationSites)
        {
            if (site == null) continue;
            if (!_atomsByResidue.TryGetValue(site.residueId, out var atoms) || atoms.Count == 0) continue;

            Transform root = atoms[0].transform.parent;
            if (root == null) continue;

            // 원자는 단백질 루트의 직계 자식으로 생성되므로 localPosition이 곧 구조 로컬 좌표다.
            Vector3 center = Vector3.zero;
            foreach (var atom in atoms) center += atom.transform.localPosition;
            center /= atoms.Count;

            _tags.Add(ResidueNumberTag.Create(
                root, center, site.residueId,
                captionsReadable ? site.alias : null,
                tagColor, font, Camera.main, tagLeaderLength, tagApparentSize));
        }

        // 방금 만든 표는 기본적으로 켜져 있다. 구조를 다시 읽는 시점이 항상 리본 단계라는
        // 보장은 없으므로(퀘스트 전환 중 재로드 등) 현재 단계 기준으로 한 번 맞춰 준다.
        ApplyTagVisibility();
    }

    private ResidueNumberTag FindTag(int residueId)
    {
        foreach (var tag in _tags)
            if (tag != null && tag.ResidueId == residueId) return tag;

        return null;
    }

    // 사용자가 특정 원자(변이 부위)를 선택했을 때 호출 (Raycast/시선/포인터 이벤트에서 연결)
    // 같은 잔기를 몇 번을 다시 선택해도 매번 이벤트와 시각 피드백(섬광)이 다시 발생한다.
    public void SelectResidue(int residueId)
    {
        var site = mutationSites.Find(m => m.residueId == residueId);
        if (site == null) return;

        // 재선택 여부를 사용자가 바로 알 수 있도록 해당 잔기 원자들을 짧게 섬광시킨다
        FlashResidue(residueId);

        OnMutationSelected?.Invoke(site);
    }

    /// <summary>
    /// 선택 이벤트 없이 시각 피드백만 준다.
    ///
    /// 도입 시나리오처럼 비서가 "여기가 문제야" 하고 짚어 보이는 연출에 쓴다.
    /// <see cref="SelectResidue"/>를 대신 부르면 OnMutationSelected가 발생하고,
    /// 비서가 그걸 사용자의 선택으로 받아 SayNow로 설명을 끼워 넣으면서
    /// 재생 중이던 시나리오 대사 큐를 통째로 날려버린다.
    /// </summary>
    public void FlashResidue(int residueId)
    {
        // 번호표는 원자가 꺼져 있는 리본/나선 단계에서도 살아 있다 — 도입 시나리오가 재생되는
        // 단계가 바로 그 리본 단계라, 여기서 반응하는 건 사실상 번호표뿐인 경우가 많다.
        ResidueNumberTag tag = FindTag(residueId);
        if (tag != null) tag.Pop();

        // 리본/나선 조각에 붙은 표시도 함께 반짝인다. 아래 원자 표시는 아미노산 단계에서만
        // 살아 있으므로, 도입 시나리오가 재생되는 리본 단계에서 실제로 반응하는 건 이쪽이다.
        if (levelController != null) levelController.FlashTargetResidue(residueId);

        if (!_atomsByResidue.TryGetValue(residueId, out var atoms)) return;

        foreach (var atomInfo in atoms)
        {
            if (atomInfo == null) continue;
            var pulse = atomInfo.GetComponent<PulseHighlight>();
            if (pulse != null) pulse.Flash();
        }
    }

    /// <summary>등록된 모든 변이 부위를 한꺼번에 반짝인다. 선택 이벤트는 발생하지 않는다.</summary>
    public void FlashAllSites()
    {
        foreach (var site in mutationSites)
            if (site != null) FlashResidue(site.residueId);

        foreach (var tag in _tags)
            if (tag != null) tag.SetHighlighted(false);
    }

    /// <summary>
    /// 변이 부위 <b>하나</b>만 짚는다. 비서가 "858번 자리를 보세요"처럼 번호를 특정해 말하는
    /// 대사에 맞춰 그 번호표만 튀어오르게 하고, 나머지 번호표는 강조를 끈다.
    ///
    /// <see cref="FlashAllSites"/>로 전부 반짝이면 "여러 개 중 어느 것"이라는 질문이 그대로
    /// 남는다 — 번호를 말하는 대사에서는 정확히 그 하나만 반응해야 말과 화면이 맞는다.
    /// </summary>
    public void FocusResidue(int residueId)
    {
        foreach (var tag in _tags)
            if (tag != null) tag.SetHighlighted(tag.ResidueId == residueId);

        FlashResidue(residueId);
    }

    /// <summary>등록된 변이 부위 중 이 번호에 해당하는 것. 없으면 null.</summary>
    public MutationSite FindSite(int residueId) => mutationSites.Find(m => m != null && m.residueId == residueId);
}

/// <summary>
/// 맥동 표시의 성격. 같은 "밝아졌다 어두워짐"이라도 규칙적인지 아닌지에 따라 정반대로 읽힌다.
/// </summary>
public enum PulseStyle
{
    /// <summary>규칙적인 사인 맥동. 충전 표시등·저장 중 아이콘처럼 "정상 작동 중"으로 읽힌다.
    /// 후보물질의 반응기(warhead)처럼 "여기가 일하는 부분"을 알리는 표시에 쓴다.</summary>
    Steady,

    /// <summary>
    /// 불규칙 플리커 + 미세 경련. "여기가 고장났다"로 읽힌다.
    ///
    /// 변이 부위에 규칙적인 맥동을 쓰면 병원 모니터처럼 "잘 돌아가는 중"으로 보인다 —
    /// 정작 알려야 하는 건 정반대다. 나가기 직전의 형광등처럼 밝기가 들쭉날쭉하고 가끔 뚝
    /// 끊기며, 그 자리만 미세하게 떤다. 색을 쓰지 않는 "떨림" 쪽이 특히 값진데, 색 채널은
    /// 이미 CPK 원소색·pLDDT 신뢰도색·포켓 표시색이 나눠 쓰고 있어 포화 상태이기 때문이다.
    /// </summary>
    Malfunction,
}

/// <summary>변이 부위 원자를 펄스(발광)시키는 보조 컴포넌트.
/// <see cref="PulseStyle"/>에 따라 "정상 작동 중"으로도, "고장 난 자리"로도 보이게 한다.</summary>
public class PulseHighlight : MonoBehaviour
{
    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private Color _highlightColor;
    private Color _restColor;
    private float _speed;
    private float _flashUntil;

    private PulseStyle _style;
    private float _seed;
    private float _jitterAmplitude;
    private Vector3 _restLocalPosition;
    private bool _restCaptured;

    public void Init(Color color, float speed) => Init(color, speed, PulseStyle.Steady);

    /// <param name="seed">플리커·경련의 리듬을 정하는 값. 같은 잔기의 원자들에 <b>같은 값</b>을
    /// 주면 잔기 하나가 통째로 같은 박자로 떤다 — 원자마다 다른 값을 주면 잔기가 지지직거리는
    /// 게 아니라 화면 노이즈처럼 보인다.</param>
    /// <param name="jitterAmplitude">경련 진폭(로컬 단위). 0이면 색만 지직거린다.</param>
    public void Init(Color color, float speed, PulseStyle style, float jitterAmplitude = 0f, float seed = 0f)
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
        _highlightColor = color;
        _speed = speed;
        _style = style;
        _jitterAmplitude = jitterAmplitude;
        _seed = seed;

        // 플리커가 "꺼지는" 순간 돌아갈 원래 색(pLDDT/CPK). 검게 만들지 않고 이 색으로 되돌려야
        // 원자는 제자리에 그대로 있고 강조 표시만 끊긴 것으로 읽힌다.
        if (_renderer != null)
        {
            _renderer.GetPropertyBlock(_mpb);
            Color captured = _mpb.GetColor("_BaseColor");
            _restColor = captured.a > 0f ? captured : color * 0.3f;
        }

        // 경련의 기준점. 이미 흔들려 있는 상태에서 다시 잡으면 원점이 조금씩 밀려나므로 한 번만 기억한다.
        if (!_restCaptured)
        {
            _restLocalPosition = transform.localPosition;
            _restCaptured = true;
        }
    }

    /// <summary>선택 피드백: 잠시 흰색에 가깝게 밝아졌다가 원래 펄스로 돌아온다. 재호출 시 매번 다시 반짝인다.</summary>
    public void Flash(float duration = 0.5f)
    {
        _flashUntil = Time.time + duration;
    }

    private void OnDisable()
    {
        // 리본 단계로 올라가며 원자가 꺼질 때 흔들린 자리에 그대로 멈추면, 다시 내려왔을 때
        // 결합 실린더와 어긋난 채로 굳어 보인다.
        if (_restCaptured) transform.localPosition = _restLocalPosition;
    }

    private void Update()
    {
        if (_renderer == null) return;

        Color c;
        float emission;

        if (_style == PulseStyle.Malfunction)
        {
            float level = FlickerLevel(Time.time);
            c = Color.Lerp(_restColor, _highlightColor, level);
            emission = 0.3f + level * 2.2f;
            ApplyJitter(Time.time);
        }
        else
        {
            float t = (Mathf.Sin(Time.time * _speed) + 1f) * 0.5f;
            c = Color.Lerp(_highlightColor * 0.6f, _highlightColor, t);
            emission = 1.5f;
        }

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

    /// <summary>
    /// 0(꺼짐)~1(최대)을 불규칙하게 오가는 밝기.
    ///
    /// 난수를 매 프레임 굴리지 않고 시간과 seed만으로 결정되는 함수로 만든다. 같은 seed를 받은
    /// 원자들은 정확히 같은 파형을 계산하므로, 잔기 열댓 개의 원자가 따로 놀지 않고 하나의
    /// 덩어리로 함께 껌뻑인다 — 각자 난수를 굴렸다면 화면 노이즈로 보였을 것이다.
    /// </summary>
    private float FlickerLevel(float time)
    {
        float t = time * Mathf.Max(_speed, 0.01f);

        // Perlin 노이즈는 실제로는 0~1을 다 쓰지 않고 가운데 언저리에 몰린다. 그대로 쓰면
        // 밝기 변화가 밋밋해지므로 실사용 구간을 0~1로 다시 펴서 대비를 살린다.
        float wave = Mathf.InverseLerp(0.28f, 0.72f, Mathf.PerlinNoise(_seed, t * 3f));
        float level = Mathf.Lerp(0.25f, 1f, wave);

        // 접촉이 끊기는 순간. 임계값을 높게 잡아 "가끔"만 일어나게 한다 — 쉴 새 없이 껌뻑이면
        // 눈에 거슬리기만 하고 정작 어느 자리인지는 알려주지 못한다.
        if (Mathf.PerlinNoise(_seed + 57.1f, t * 7f) > 0.72f) level *= 0.1f;

        return level;
    }

    /// <summary>
    /// 그 자리만 미세하게 떨게 한다. 주변 잔기는 가만히 있는데 여기만 떨면, 색을 한 방울도
    /// 쓰지 않고 "이 자리가 이상하다"가 성립한다.
    ///
    /// 결합 실린더는 함께 움직이지 않으므로 진폭은 원자 반지름보다 훨씬 작아야 한다 — 크게 주면
    /// 떠는 게 아니라 원자가 결합에서 뽑혀 나온 것처럼 보인다.
    /// </summary>
    private void ApplyJitter(float time)
    {
        if (_jitterAmplitude <= 0f || !_restCaptured) return;

        float t = time * Mathf.Max(_speed, 0.01f) * 6f;
        var offset = new Vector3(
            Mathf.PerlinNoise(_seed, t) - 0.5f,
            Mathf.PerlinNoise(_seed + 11.3f, t) - 0.5f,
            Mathf.PerlinNoise(_seed + 23.7f, t) - 0.5f);

        transform.localPosition = _restLocalPosition + offset * (2f * _jitterAmplitude);
    }
}