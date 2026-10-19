Shader "Osiris/DropZoneOverlay"
{
    // Red hatched areas on the battle plane where reinforcements can't be dropped. _MaskTex is 1 where
    // dropping is forbidden; bilinear filtering of the mask gives the soft border its outline.
    Properties
    {
        _MaskTex ("No-Drop Mask", 2D) = "black" {}
        _Color ("Color", Color) = (1, 0.22, 0.18, 1)
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.16
        _EdgeAlpha ("Edge Alpha", Range(0, 1)) = 0.85
        _StripeAlpha ("Stripe Alpha", Range(0, 1)) = 0.12
        _StripeSpacing ("Stripe Spacing", Float) = 28
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_ST;
                float4 _Color;
                float _FillAlpha;
                float _EdgeAlpha;
                float _StripeAlpha;
                float _StripeSpacing;
                float _Fade;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(float4 positionOS : POSITION, float2 uv : TEXCOORD0)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv).r;
                float inside = smoothstep(0.45, 0.55, mask);
                float edge = pow(saturate(1.0 - abs(mask - 0.5) * 2.0), 2.0);

                float stripe = step(0.5, frac((input.positionWS.x + input.positionWS.z) / _StripeSpacing - _Time.y * 0.4));
                float alpha = inside * (_FillAlpha + stripe * _StripeAlpha) + edge * _EdgeAlpha;
                return half4(_Color.rgb, saturate(alpha) * _Fade);
            }
            ENDHLSL
        }
    }
}
