using UnityEngine;

/// <summary>
/// F-03.3 신뢰도 시각화
/// AlphaFold의 pLDDT(원자 B-factor 필드에 저장됨) 값을 공식 색상 스케일로 변환한다.
/// 90+: 파랑(매우높음) / 70-90: 하늘색(높음) / 50-70: 노랑(낮음) / 50 미만: 주황(매우낮음)
/// </summary>
[RequireComponent(typeof(Renderer))]
public class PLDDTColorizer : MonoBehaviour
{
    private static readonly Color VeryHigh = new Color(0.00f, 0.33f, 0.85f); // 파랑
    private static readonly Color High     = new Color(0.42f, 0.80f, 0.98f); // 하늘색
    private static readonly Color Low      = new Color(0.98f, 0.85f, 0.20f); // 노랑
    private static readonly Color VeryLow  = new Color(0.95f, 0.45f, 0.10f); // 주황

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;

    public float CurrentPLDDT { get; private set; }

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
    }

    public void ApplyConfidence(float plddt)
    {
        CurrentPLDDT = plddt;
        Color c = GetColorForPLDDT(plddt);

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_BaseColor", c); // URP Lit 셰이더 기준. Built-in이면 "_Color" 사용
        _renderer.SetPropertyBlock(_mpb);
    }

    public static Color GetColorForPLDDT(float plddt)
    {
        if (plddt >= 90f) return VeryHigh;
        if (plddt >= 70f) return High;
        if (plddt >= 50f) return Low;
        return VeryLow;
    }
}
