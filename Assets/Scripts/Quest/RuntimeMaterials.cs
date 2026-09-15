using UnityEngine;

/// <summary>
/// 런타임 생성 오브젝트용 공용 머티리얼.
/// Bond.prefab 등이 홀로그램 머티리얼을 쓰고 있어도, 리본/Helix/결합처럼
/// "실제(불투명)"로 보여야 하는 지오메트리에 이 머티리얼을 덮어씌운다.
/// URP Lit은 MPB의 _BaseColor 틴트를 그대로 반영하므로 기존 색 지정 코드와 호환된다.
/// </summary>
public static class RuntimeMaterials
{
    private static Material _solid;

    public static Material Solid
    {
        get
        {
            if (_solid == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard"); // 비-URP 폴백
                _solid = new Material(shader) { name = "RuntimeSolid" };
                // ClickHintPulse 등이 MPB로 넣는 _EmissionColor가 실제로 빛나려면 키워드가 필요.
                // 기본 발광색은 검정이라 켜두어도 다른 사용처(결합 등)의 외형은 변하지 않는다.
                _solid.EnableKeyword("_EMISSION");
            }
            return _solid;
        }
    }

    public static void ApplySolid(GameObject go)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = Solid;
    }

    private static Material _transparent;

    /// <summary>MPB의 _BaseColor 알파로 투명도를 조절할 수 있는 URP Lit 변형.
    /// ThermalStabilityController(p53 wobble)와 ProteinLoader.Fade*(CFTR 구조 전환 페이드)가 같이 쓴다.</summary>
    public static Material Transparent
    {
        get
        {
            if (_transparent == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    _transparent = new Material(shader) { name = "RuntimeTransparent" };
                    _transparent.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent
                    _transparent.SetFloat("_Blend", 0f);   // 0 = Alpha
                    _transparent.SetOverrideTag("RenderType", "Transparent");
                    _transparent.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _transparent.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _transparent.SetInt("_ZWrite", 0);
                    _transparent.DisableKeyword("_ALPHATEST_ON");
                    _transparent.EnableKeyword("_ALPHABLEND_ON");
                    _transparent.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    _transparent.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    _transparent.EnableKeyword("_EMISSION");
                }
            }
            return _transparent;
        }
    }

    private static Material _lineUnlit;

    /// <summary>
    /// LineRenderer용 무광 투명 머티리얼. URP Lit은 정점 색을 읽지 않아 LineRenderer의
    /// startColor/endColor가 그대로 무시되고, 지시선 하나 색을 바꾸려고 태그마다 머티리얼
    /// 인스턴스를 만들면 드로우콜이 잔기 수만큼 늘어난다. Sprites/Default는 정점 색을 쓰므로
    /// 머티리얼 하나를 모두가 공유하면서 색은 각자 지정할 수 있다.
    /// <see cref="ResidueNumberTag"/>의 지시선이 쓴다.
    /// </summary>
    public static Material LineUnlit
    {
        get
        {
            if (_lineUnlit == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                _lineUnlit = new Material(shader) { name = "RuntimeLineUnlit" };
            }
            return _lineUnlit;
        }
    }
}
