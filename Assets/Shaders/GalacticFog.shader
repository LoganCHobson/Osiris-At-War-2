Shader "Osiris/GalacticFog"
{
    Properties
    {
        _MaskTex ("Visibility Mask", 2D) = "black" {}
        _FogColor ("Fog Color", Color) = (0.02, 0.02, 0.06, 0.88)
        _NebulaColor ("Nebula Color A", Color) = (0.22, 0.1, 0.38, 1)
        _NebulaColor2 ("Nebula Color B", Color) = (0.05, 0.18, 0.32, 1)
        _NoiseScale ("Nebula Scale", Float) = 0.035
        _NoiseSpeed ("Nebula Drift Speed", Float) = 0.6
        _NoiseStrength ("Nebula Strength", Range(0, 1)) = 0.6
        _EdgeColor ("Sensor Edge Color", Color) = (0.35, 0.75, 1, 1)
        _EdgeWidth ("Sensor Edge Width", Range(0.02, 0.5)) = 0.18
        _EdgeStrength ("Sensor Edge Strength", Range(0, 3)) = 0.9
        _EdgePulseSpeed ("Sensor Edge Pulse Speed", Float) = 1.7
        _ClearThreshold ("Clear Threshold", Range(0.1, 1)) = 0.55
        [HideInInspector] _FogTime ("Fog Time", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
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
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_ST;
                float4 _FogColor;
                float4 _NebulaColor;
                float4 _NebulaColor2;
                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseStrength;
                float4 _EdgeColor;
                float _EdgeWidth;
                float _EdgeStrength;
                float _EdgePulseSpeed;
                float _ClearThreshold;
                float _FogTime;
            CBUFFER_END

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int octave = 0; octave < 4; octave++)
                {
                    value += amplitude * ValueNoise(p);
                    p = p * 2.03 + float2(17.1, 9.2);
                    amplitude *= 0.5;
                }
                return value;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, IN.uv).r;

                float t = _FogTime * _NoiseSpeed;
                float2 p = IN.positionWS.xz * _NoiseScale;
                float n1 = Fbm(p + float2(t * 0.10, t * 0.06));
                float n2 = Fbm(p * 2.1 + float2(-t * 0.07, t * 0.09) + n1 * 1.5);

                float fog = 1.0 - smoothstep(0.0, _ClearThreshold, mask);

                float3 nebula = lerp(_NebulaColor.rgb, _NebulaColor2.rgb, n2);
                float3 color = lerp(_FogColor.rgb, nebula, saturate(n1 * _NoiseStrength * 1.4));
                float density = lerp(1.0 - _NoiseStrength * 0.6, 1.0, n1);
                float alpha = fog * _FogColor.a * density;

                float edge = saturate(1.0 - abs(mask - _ClearThreshold * 0.5) / _EdgeWidth);
                float pulse = 0.75 + 0.25 * sin(_FogTime * _EdgePulseSpeed + n2 * 8.0);
                edge = edge * edge * _EdgeStrength * pulse;

                color += _EdgeColor.rgb * edge;
                alpha = saturate(alpha + edge * _EdgeColor.a * 0.6);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
