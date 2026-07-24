Shader "Custom/Hologram"
{
    // 테이블, DNA 이중나선, 퀘스트 공간 UI 패널 등에 공통으로 적용하는 홀로그램 셰이더.
    // Passthrough 배경 위에서 반투명 + 가장자리 발광(Fresnel) + 스캔라인 흐름 효과를 낸다.
    // URP 프로젝트 기준 (Universal Render Pipeline).

    Properties
    {
        _HologramColor ("Hologram Color", Color) = (0.2, 0.8, 1.0, 1.0)
        _BaseAlpha ("Base Alpha", Range(0,1)) = 0.35
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.5
        _FresnelIntensity ("Fresnel Intensity", Range(0,5)) = 1.8
        _ScanlineSpeed ("Scanline Speed", Range(0, 10)) = 1.5
        _ScanlineDensity ("Scanline Density", Range(1, 200)) = 40
        _ScanlineIntensity ("Scanline Intensity", Range(0,1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            float4 _HologramColor;
            float  _BaseAlpha;
            float  _FresnelPower;
            float  _FresnelIntensity;
            float  _ScanlineSpeed;
            float  _ScanlineDensity;
            float  _ScanlineIntensity;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(posInputs.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normal = normalize(IN.normalWS);
                float3 viewDir = normalize(IN.viewDirWS);

                // Fresnel: 가장자리일수록 밝게 발광 (홀로그램 특유의 테두리 효과)
                float fresnel = pow(1.0 - saturate(dot(normal, viewDir)), _FresnelPower) * _FresnelIntensity;

                // 스캔라인: 월드 Y좌표 + 시간 기반으로 위아래 흐르는 라인 패턴
                float scan = frac(IN.positionWS.y * _ScanlineDensity * 0.05 - _Time.y * _ScanlineSpeed);
                float scanline = smoothstep(0.95, 1.0, scan) * _ScanlineIntensity;

                float3 color = _HologramColor.rgb * (1.0 + fresnel);
                float alpha = saturate(_BaseAlpha + fresnel * 0.5 + scanline);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
