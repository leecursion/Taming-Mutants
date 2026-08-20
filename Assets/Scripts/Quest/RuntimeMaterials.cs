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
}
