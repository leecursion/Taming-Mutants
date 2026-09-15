using UnityEngine;

/// <summary>
/// F-04.2 후보물질 선택 박스 1칸.
/// 와이어프레임 박스 대신, 위에서 비추는 스포트라이트 + 분할 홀로그램 링으로
/// "빛으로 자리를 표시한 전시대" 느낌을 낸다. 안에는 화합물 3D 분자와 이름 라벨을 담고,
/// 레이캐스트 선택을 위한(비가시) BoxCollider를 가진다.
/// CompoundSelectionPanel이 생성/배치하며 직접 씬에 붙일 필요는 없다.
/// </summary>
public class CompoundSlot : MonoBehaviour
{
    public CompoundData Data { get; private set; }
    public GameObject MoleculeRoot { get; private set; }
    /// <summary>박스 안 표시용 축소 전(월드 도킹용) 스케일 → 항상 1. 도킹 클론은 이 값 기준으로 복제.</summary>
    public float DisplayFitScale { get; private set; } = 1f;

    private Light _spotLight;
    private SpriteRenderer _floorGlow;
    private Transform _platform;
    private LineRenderer[] _platformArcs;
    private Color _accentColor;
    private float _presentationSize;
    private float _hoverAmount;
    private bool _hovered;
    private Color _idleColor;
    private float _spinSpeed;

    private static readonly Color HoverColor = new Color(0.4f, 1f, 1f);
    // cellFrameColor는 원래 와이어프레임용으로 잡힌 값이라 그대로 쓰면 빛으로는 흐릿하다.
    // 스포트라이트/글로우에 입힐 때만 밝기를 끌어올린다.
    private const float ColorBoost = 1.8f;
    private const float GlowAlpha = 0.18f;

    public void Init(CompoundData data, GameObject moleculeRoot, float boxSize, Color idleColor, float spinSpeed)
    {
        Data = data;
        MoleculeRoot = moleculeRoot;
        _idleColor = idleColor;
        _spinSpeed = spinSpeed;

        // 분자를 박스 크기에 맞게 축소
        float radius = CompoundMoleculeBuilder.LocalRadius(moleculeRoot);
        DisplayFitScale = radius > 0.0001f ? (boxSize * 0.42f) / radius : 1f;
        moleculeRoot.transform.localScale = Vector3.one * DisplayFitScale;
        moleculeRoot.transform.localPosition = Vector3.zero;

        BuildLightPresentation(boxSize);
        SetAccentColor(_idleColor);

        var col = gameObject.AddComponent<BoxCollider>();
        col.size = Vector3.one * boxSize;
    }

    public bool Inspecting { get; set; }

    private void Update()
    {
        // 박스 안에서 분자가 천천히 자전해 3D 형태를 파악하기 쉽게 한다.
        if (MoleculeRoot != null && !Inspecting)
            MoleculeRoot.transform.Rotate(Vector3.up, _spinSpeed * Time.deltaTime, Space.Self);

        if (_platform == null) return;
        _hoverAmount = Mathf.MoveTowards(_hoverAmount, _hovered || Inspecting ? 1f : 0f, Time.deltaTime * 4f);
        _platform.localRotation = Quaternion.Euler(-90f, 0f, Time.time * 12f);
        float breath = .5f + .5f * Mathf.Sin(Time.time * 1.6f);
        _platform.localScale = Vector3.one * (1f + _hoverAmount * .05f);
        for (int i = 0; i < _platformArcs.Length; i++)
        {
            Color c = _accentColor;
            c.a = (i < 3 ? .42f : .22f) + _hoverAmount * .2f + breath * .07f;
            _platformArcs[i].startColor = _platformArcs[i].endColor = c;
        }
        if (_floorGlow != null)
            _floorGlow.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b,
                GlowAlpha + breath * .035f + _hoverAmount * .07f);
        if (_spotLight != null) _spotLight.intensity = 1.8f + _hoverAmount * .6f;
    }

    public void SetHovered(bool hovered)
    {
        _hovered = hovered;
        SetAccentColor(hovered ? HoverColor : _idleColor);
    }

    /// <summary>도킹 결과에 따라 조명/글로우 색을 고정한다 (정답: 녹색 / 오답: 결과색).</summary>
    public void SetResultColor(Color color)
    {
        _idleColor = color;
        SetAccentColor(color);
    }

    private void SetAccentColor(Color color)
    {
        Color boosted = new Color(
            Mathf.Clamp01(color.r * ColorBoost), Mathf.Clamp01(color.g * ColorBoost), Mathf.Clamp01(color.b * ColorBoost));

        _accentColor = boosted;
        if (_spotLight != null) _spotLight.color = boosted;
        if (_floorGlow != null) _floorGlow.color = new Color(boosted.r, boosted.g, boosted.b, GlowAlpha);
    }

    /// <summary>
    /// 와이어프레임 큐브 대신, 칸 위에서 아래로 비추는 스포트라이트 + 은은한 글로우와 회전하는 분할 링으로
    /// 자리를 표시한다. 얇은 이중 링이 전시 영역을 표시하고 이름표 영역은 비워 둔다.
    /// </summary>
    private void BuildLightPresentation(float size)
    {
        _presentationSize = size;
        var platformGo = new GameObject("HolographicPlatform");
        _platform = platformGo.transform;
        _platform.SetParent(transform, false);
        _platform.localPosition = new Vector3(0f, -size * .51f, 0f);
        _platform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        _platformArcs = new LineRenderer[6];
        for (int i = 0; i < _platformArcs.Length; i++)
        {
            var arc = MutationExperimentEffects.Line(_platform, "PlatformArc", Color.cyan,
                size * (i < 3 ? .005f : .003f));
            MutationExperimentEffects.Ring(arc, _presentationSize * (i < 3 ? .35f : .29f), i < 3 ? .24f : .18f);
            arc.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 3) * 120f + (i < 3 ? 0f : 35f));
            _platformArcs[i] = arc;
        }

        var lightGo = new GameObject("Spotlight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, size * 0.9f, 0f);
        lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 로컬 forward가 -Y(아래)를 향하게

        _spotLight = lightGo.AddComponent<Light>();
        _spotLight.type = LightType.Spot;
        _spotLight.range = size * 3f;
        _spotLight.spotAngle = 68f;
        _spotLight.intensity = 2.4f;
        _spotLight.shadows = LightShadows.None;

        var glowGo = new GameObject("FloorGlow");
        glowGo.transform.SetParent(transform, false);
        glowGo.transform.localPosition = new Vector3(0f, -size * 0.52f, 0f);
        glowGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // 바닥에 눕혀 위를 보게

        _floorGlow = glowGo.AddComponent<SpriteRenderer>();
        _floorGlow.sprite = HoloSpriteFactory.Glow();

        // 예전엔 1.5×size라 반지름이 박스 절반(0.5×size)보다 커서 앞쪽 가장자리(이름표가 있는
        // 자리)까지 번져, 텍스트가 이 글로우 뒤에 가려 보이는 문제가 있었다. 박스 발밑 정도로만
        // 좁혀 이름표 자리를 침범하지 않게 한다.
        float nativeSize = Mathf.Max(_floorGlow.sprite.bounds.size.x, 0.0001f);
        float desiredDiameter = size * 0.65f;
        glowGo.transform.localScale = Vector3.one * (desiredDiameter / nativeSize);
    }
}
