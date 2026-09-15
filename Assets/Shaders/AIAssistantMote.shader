Shader "Custom/AIAssistantMote"
{
    // 비서 주위를 떠다니는 입자용 셰이더.
    // 텍스처 없이 UV만으로 가장자리가 부드러운 점을 그린다 —
    // 파티클 텍스처 에셋을 따로 만들거나 임포트할 필요가 없다.
    // 파티클 시스템이 넘겨주는 정점 색(COLOR)으로 수명에 따른 페이드를 받는다.

    Properties
    {
        _BaseColor ("Base Color", Color) = (0.4, 0.9, 1.0, 1.0)
        _Softness ("Softness", Range(0.5, 6)) = 2.4
        _Intensity ("Intensity", Range(0, 4)) = 1.4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
            };

            float4 _BaseColor;
            float  _Softness;
            float  _Intensity;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 사각형 UV 중심에서의 거리로 원형 그라데이션을 만든다.
                float distanceFromCenter = length(IN.uv - 0.5) * 2.0;
                float falloff = pow(saturate(1.0 - distanceFromCenter), _Softness);

                float3 color = _BaseColor.rgb * IN.color.rgb * _Intensity;
                return half4(color, falloff * IN.color.a * _BaseColor.a);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
