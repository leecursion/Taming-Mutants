#ifndef AI_ASSISTANT_CORE_INCLUDED
#define AI_ASSISTANT_CORE_INCLUDED

// AIAssistantCore.shader의 두 패스(뒷면/앞면)가 공유하는 본체.
// SHELL_BACKFACE가 정의되면 뒷면 패스로 취급해 노멀을 뒤집고 조금 어둡게 그린다.

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
    float3 positionOS  : TEXCOORD2;
};

float4 _BaseColor;
float4 _EmissionColor;
float  _ShellAlpha;
float  _RimPower;
float  _RimIntensity;
float  _BandDensity;
float  _BandSpeed;
float  _BandIntensity;
float  _BandSharpness;

Varyings vert(Attributes IN)
{
    Varyings OUT;
    VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
    OUT.positionHCS = posInputs.positionCS;
    OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
    OUT.viewDirWS = GetWorldSpaceViewDir(posInputs.positionWS);
    OUT.positionOS = IN.positionOS.xyz;
    return OUT;
}

half4 frag(Varyings IN) : SV_Target
{
    float3 normal = normalize(IN.normalWS);
    float3 viewDir = normalize(IN.viewDirWS);

#ifdef SHELL_BACKFACE
    normal = -normal;                 // 뒷면은 노멀이 반대로 향해 있다
    const float faceWeight = 0.55;    // 뒤쪽 껍질은 살짝 죽여야 앞면이 살아난다
#else
    const float faceWeight = 1.0;
#endif

    // 가장자리일수록 밝게 타오르는 프레넬
    float rim = pow(1.0 - saturate(dot(normal, viewDir)), _RimPower) * _RimIntensity;

    // 오브젝트 공간 Y를 따라 흐르는 에너지 밴드.
    // 월드 좌표가 아니라 오브젝트 좌표를 쓰므로 비서가 움직여도 무늬가 몸에 붙어 있다.
    float wave = sin(IN.positionOS.y * _BandDensity + _Time.y * _BandSpeed * 6.2831853);
    float band = smoothstep(_BandSharpness, 1.0, wave) * _BandIntensity;

    float3 color = _BaseColor.rgb * (1.0 + rim * 1.1 + band * 2.2) + _EmissionColor.rgb * band * 0.4;
    float alpha = saturate(_ShellAlpha + rim * 0.65 + band * 0.45) * faceWeight;

    return half4(color, alpha);
}

#endif
