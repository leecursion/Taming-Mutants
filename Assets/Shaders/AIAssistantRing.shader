Shader "Custom/AIAssistantRing"
{
    // 비서 주위를 도는 궤도 링용 셰이더.
    // 토러스 메시의 UV를 그대로 쓴다 — u는 링을 한 바퀴 도는 방향, v는 튜브 단면 방향.
    // 균일한 실린더 대신 흘러가는 점선으로 보이게 해서 "데이터가 돌고 있다"는 인상을 준다.
    //
    // _BaseColor / _EmissionColor는 AIAssistantVisual이 MaterialPropertyBlock으로
    // 상태 색을 덮어쓰는 통로이므로 이름을 바꾸지 말 것.

    Properties
    {
        _BaseColor ("Base Color", Color) = (0.2, 0.8, 1.0, 1.0)
        _EmissionColor ("Emission Color", Color) = (0.2, 0.8, 1.0, 1.0)
        _Intensity ("Intensity", Range(0, 6)) = 2.2
        _DashCount ("Dash Count", Range(1, 96)) = 26
        _DashFill ("Dash Fill", Range(0.05, 1)) = 0.55
        _DashSoftness ("Dash Softness", Range(0.001, 0.5)) = 0.14
        _ScrollSpeed ("Scroll Speed", Range(-2, 2)) = 0.12
        _InnerFade ("Inner Fade", Range(0, 1)) = 0.65
        _RimBoost ("Rim Boost", Range(0, 3)) = 0.8
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        // 가산 합성 — 빛나는 띠처럼 뒤가 비쳐 보인다.
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
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 viewDirWS   : TEXCOORD2;
            };

            float4 _BaseColor;
            float4 _EmissionColor;
            float  _Intensity;
            float  _DashCount;
            float  _DashFill;
            float  _DashSoftness;
            float  _ScrollSpeed;
            float  _InnerFade;
            float  _RimBoost;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.uv = IN.uv;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(posInputs.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 점선: 한 주기 안에서 중심으로부터의 거리로 마스크를 만들면
                // 양쪽 끝이 대칭으로 부드럽게 끊긴다 (한쪽만 부드러우면 끊긴 티가 난다).
                float phase = frac(IN.uv.x * _DashCount + _Time.y * _ScrollSpeed * _DashCount);
                float distanceToCenter = abs(phase - 0.5);
                float half_fill = _DashFill * 0.5;
                float dash = 1.0 - smoothstep(half_fill - _DashSoftness, half_fill + _DashSoftness, distanceToCenter);

                // 튜브 단면: v=0이 링의 바깥쪽 적도, v=0.5가 안쪽.
                // 바깥쪽을 밝게 해서 납작한 띠처럼 보이게 한다.
                float facing = cos(IN.uv.y * 6.2831853) * 0.5 + 0.5;
                float tube = lerp(1.0 - _InnerFade, 1.0, facing);

                float3 normal = normalize(IN.normalWS);
                float3 viewDir = normalize(IN.viewDirWS);
                float rim = pow(1.0 - saturate(dot(normal, viewDir)), 2.0) * _RimBoost;

                float mask = dash * tube;
                float3 color = (_BaseColor.rgb + _EmissionColor.rgb * 0.25) * _Intensity * (1.0 + rim);

                return half4(color, saturate(mask));
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
