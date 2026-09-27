Shader "Osiris/PlanetOwnershipRing"
{
    Properties
    {
        _Color ("Ring Color", Color) = (0.25, 0.55, 1, 0.85)
        _InnerRadius ("Inner Radius", Range(0, 1)) = 0.7
        _OuterRadius ("Outer Radius", Range(0, 1)) = 0.95
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.2)) = 0.03
        _Glow ("Glow Intensity", Range(0, 5)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _InnerRadius;
                float _OuterRadius;
                float _EdgeSoftness;
                float _Glow;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 centered = IN.uv - 0.5;
                float dist = length(centered) * 2.0;

                float outerMask = 1.0 - smoothstep(_OuterRadius - _EdgeSoftness, _OuterRadius, dist);
                float innerMask = smoothstep(_InnerRadius - _EdgeSoftness, _InnerRadius, dist);
                float ring = saturate(innerMask * outerMask);

                clip(ring - 0.001);

                half4 col = _Color * _Glow;
                col.a = _Color.a * ring;
                return col;
            }
            ENDHLSL
        }
    }
}
