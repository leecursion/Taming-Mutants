Shader "Custom/SpaceSkybox"
{
    // WindowBackdrop_Space(Custom/SpaceBackdrop)와 같은 별하늘을, 상자 안쪽이 아니라
    // 씬 전체의 RenderSettings.skybox로 쓸 수 있게 만든 버전.
    //
    // SpaceBackdrop은 메시 UV로 별 격자를 그려서 창문 상자 안쪽에서만 쓸 수 있다.
    // 이 셰이더는 카메라에서 본 방향 벡터(스카이박스 메시는 카메라 중심으로 그려지므로
    // 오브젝트 공간 좌표가 곧 방향이다)를 3차원 격자에 그대로 넣어, 카메라가 어디에 있든
    // (창문 배경 상자를 벗어나도) 이음매 없이 같은 별하늘이 보이게 한다.
    Properties
    {
        _SkyColor ("Sky Color", Color) = (0.015, 0.02, 0.05, 1)
        _StarColor ("Star Color", Color) = (1, 1, 1, 1)
        _StarDensity ("Star Density (grid cells)", Float) = 40
        _StarSize ("Star Size (0-1 of cell)", Range(0.02, 0.5)) = 0.12
        _StarChance ("Star Chance (0-1)", Range(0, 1)) = 0.08
        _TwinkleSpeed ("Twinkle Speed", Float) = 1.4
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 dir : TEXCOORD0; };

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
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                // 스카이박스 패스는 카메라 이동을 지운 행렬로 그리므로, 오브젝트 공간 좌표가
                // 곧 카메라에서 본 방향이다 (SpaceBackdrop의 메시 UV 대신 이걸 격자에 쓴다).
                OUT.dir = IN.positionOS.xyz;
                return OUT;
            }

            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.3));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);
                float3 grid = dir * _StarDensity;
                float3 cell = floor(grid);
                float3 local = frac(grid) - 0.5;

                float h = hash31(cell);
                float isStar = step(1.0 - _StarChance, h);

                float dist = length(local);
                float star = smoothstep(_StarSize, _StarSize * 0.35, dist) * isStar;

                float phase = hash31(cell + 19.7) * 6.2831;
                float twinkle = 0.35 + 0.65 * (0.5 + 0.5 * sin(_Time.y * _TwinkleSpeed + phase));

                float3 col = _SkyColor.rgb + _StarColor.rgb * star * twinkle;
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
