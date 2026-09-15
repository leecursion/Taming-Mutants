Shader "Custom/AIAssistantCore"
{
    // AI 비서의 바깥 껍질(에너지 셸)용 셰이더.
    // 반투명한 구 안쪽이 비쳐 보이면서 가장자리가 밝게 타오르고,
    // 내부에 에너지 밴드가 흘러가는 효과를 낸다.
    // Hologram.shader와 같은 URP 규약을 따른다.
    //
    // _BaseColor / _EmissionColor는 AIAssistantVisual이 MaterialPropertyBlock으로
    // 상태 색을 덮어쓰는 통로이므로 이름을 바꾸지 말 것.

    Properties
    {
        _BaseColor ("Base Color", Color) = (0.2, 0.8, 1.0, 1.0)
        _EmissionColor ("Emission Color", Color) = (0.2, 0.8, 1.0, 1.0)
        _ShellAlpha ("Shell Alpha", Range(0, 1)) = 0.14
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _RimIntensity ("Rim Intensity", Range(0, 6)) = 2.6
        _BandDensity ("Band Density", Range(1, 80)) = 22
        _BandSpeed ("Band Speed", Range(-5, 5)) = -0.45
        _BandIntensity ("Band Intensity", Range(0, 1)) = 0.35
        _BandSharpness ("Band Sharpness", Range(0.5, 1)) = 0.82
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        // 뒷면 -> 앞면 순서로 두 번 그린다.
        // 한 번만 그리면 속이 빈 껍데기처럼 납작해 보이고, 두 겹으로 그려야
        // 반대편 실루엣이 비쳐서 유리구슬 같은 두께감이 생긴다.
        Pass
        {
            Name "ShellBack"
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SHELL_BACKFACE 1
            #include "AIAssistantCore.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShellFront"
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "AIAssistantCore.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
