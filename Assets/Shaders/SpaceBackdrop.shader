Shader "Custom/SpaceBackdrop"
{
    // 창문 밖 배경용 우주 별하늘. 실내(상자 안쪽)에서만 보이도록 Cull Front를 쓴다 —
    // WindowBackdropSetupMenu가 만드는 큰 상자의 안쪽 벽에 입혀 창문 너머로 어두운 밤하늘과
    // 아주 작게 반짝이는 별들이 보이게 한다. 텍스처 없이 해시 기반 절차적 별로 그린다.
    Properties
    {
        _SkyColor ("Sky Color", Color) = (0.015, 0.02, 0.05, 1)
        _StarColor ("Star Color", Color) = (1, 1, 1, 1)
        _StarDensity ("Star Density (grid tiles)", Float) = 55
        _StarSize ("Star Size (0-1 of cell)", Range(0.02, 0.5)) = 0.10
        _StarChance ("Star Chance (0-1)", Range(0, 1)) = 0.10
        _TwinkleSpeed ("Twinkle Speed", Float) = 1.4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Front

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyColor;
                float4 _StarColor;
                float _StarDensity;
                float _StarSize;
                float _StarChance;
                float _TwinkleSpeed;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.uv = IN.uv;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 grid = IN.uv * _StarDensity;
                float2 cell = floor(grid);
                float2 local = frac(grid) - 0.5;

                float h = hash21(cell);
                float isStar = step(1.0 - _StarChance, h);

                // 격자 중앙에 별이 딱딱 맞춰 놓인 것처럼 보이지 않도록 셀마다 위치를 살짝 흔든다.
                float2 jitter = float2(hash21(cell + 7.1), hash21(cell + 3.7)) - 0.5;
                float dist = length(local - jitter * 0.5);

                // 아주 작게: 코어는 작은 점, smoothstep으로 가장자리만 살짝 부드럽게.
                float star = smoothstep(_StarSize, _StarSize * 0.35, dist) * isStar;

                float phase = hash21(cell + 19.7) * 6.2831;
                float twinkle = 0.35 + 0.65 * (0.5 + 0.5 * sin(_Time.y * _TwinkleSpeed + phase));

                float3 col = _SkyColor.rgb + _StarColor.rgb * star * twinkle;
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
