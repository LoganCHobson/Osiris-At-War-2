Shader "Osiris/ShieldRipple"
{
    Properties
    {
        [HDR] _ShieldColor ("Shield Color", Color) = (0.7, 1.6, 3.4, 1)
        [HDR] _DepletedColor ("Depleted Color", Color) = (2.2, 0.7, 0.25, 1)
        _HexDensity ("Hex Density", Vector) = (54, 27, 0, 0)
        _Life ("Ripple Life", Float) = 1.1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ShieldRipple"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_HITS 8

            CBUFFER_START(UnityPerMaterial)
                float4 _ShieldColor;
                float4 _DepletedColor;
                float4 _HexDensity;
                float _Life;
            CBUFFER_END

            float4 _Hits[MAX_HITS];
            float4 _HitData[MAX_HITS];
            float4 _BubbleScale;
            float _ShieldTime;
            float _Fraction;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float HexEdge(float2 uv)
            {
                const float2 s = float2(1.0, 1.7320508);
                float4 centers = floor(float4(uv, uv - float2(0.5, 1.0)) / s.xyxy) + 0.5;
                float4 offsets = float4(uv - centers.xy * s, uv - (centers.zw + 0.5) * s);
                float2 cell = dot(offsets.xy, offsets.xy) < dot(offsets.zw, offsets.zw) ? offsets.xy : offsets.zw;
                float2 a = abs(cell);
                float distanceToEdge = max(dot(a, normalize(s)), a.x);
                return smoothstep(0.36, 0.5, distanceToEdge);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.positionOS);
                float2 uv = float2(atan2(direction.z, direction.x) / 6.2831853 + 0.5, asin(clamp(direction.y, -1.0, 1.0)) / 3.1415927 + 0.5);
                float hex = HexEdge(uv * _HexDensity.xy);

                float glow = 0.0;
                [unroll]
                for (int i = 0; i < MAX_HITS; i++)
                {
                    float4 hit = _Hits[i];
                    float4 data = _HitData[i];
                    float age = _ShieldTime - data.x;
                    float alive = step(0.0, age) * step(age, _Life) * step(0.001, data.y);
                    float t = saturate(age / _Life);

                    float reach = max(data.z, 0.01);
                    float d = length((input.positionOS - hit.xyz) * _BubbleScale.xyz);
                    float radius = reach * (1.0 - (1.0 - t) * (1.0 - t));
                    float width = reach * 0.14 + 0.05;
                    float ring = exp(-((d - radius) * (d - radius)) / (width * width));
                    float core = exp(-d / (reach * 0.22)) * (1.0 - t) * (1.0 - t);
                    float segment = saturate(1.0 - d / reach);
                    float fade = 1.0 - t;

                    glow += alive * segment * fade * data.y * (ring * (0.35 + 0.65 * hex) + core * (0.6 + 0.4 * hex));
                }

                float3 color = lerp(_DepletedColor.rgb, _ShieldColor.rgb, saturate(_Fraction));
                return float4(color * glow, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
