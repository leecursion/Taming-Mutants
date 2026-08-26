using UnityEngine;

/// <summary>
/// F-04.2 후보물질 선택 박스 1칸.
/// 와이어프레임 박스 대신, 위에서 비추는 스포트라이트 + 바닥 글로우로
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
    private Color _idleColor;
    private float _spinSpeed;

    private static readonly Color HoverColor = new Color(0.4f, 1f, 1f);
    // cellFrameColor는 원래 와이어프레임용으로 잡힌 값이라 그대로 쓰면 빛으로는 흐릿하다.
    // 스포트라이트/글로우에 입힐 때만 밝기를 끌어올린다.
    private const float ColorBoost = 1.8f;
    private const float GlowAlpha = 0.55f;

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

    private void Update()
    {
        // 박스 안에서 분자가 천천히 자전해 3D 형태를 파악하기 쉽게 한다.
        if (MoleculeRoot != null)
            MoleculeRoot.transform.Rotate(Vector3.up, _spinSpeed * Time.deltaTime, Space.Self);
    }

    public void SetHovered(bool hovered)
    {
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

        if (_spotLight != null) _spotLight.color = boosted;
        if (_floorGlow != null) _floorGlow.color = new Color(boosted.r, boosted.g, boosted.b, GlowAlpha);
    }

    /// <summary>
    /// 와이어프레임 큐브 대신, 칸 위에서 아래로 비추는 스포트라이트 + 바닥에 눕힌 글로우 원반으로
    /// 자리를 표시한다. 실제로 막힌 상자는 없지만, 빛의 원기둥이 그 자리를 대신 규정한다.
    /// </summary>
    private void BuildLightPresentation(float size)
    {
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
        float desiredDiameter = size * 0.75f;
        glowGo.transform.localScale = Vector3.one * (desiredDiameter / nativeSize);
    }
}
