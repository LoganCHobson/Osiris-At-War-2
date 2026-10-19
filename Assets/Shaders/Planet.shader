Shader "Osiris/Planet"
{
    // Galactic map planet body. Values normally come from PlanetAppearance via a property block.
    Properties
    {
        _DeepColor ("Deep Water", Color) = (0.02, 0.06, 0.2, 1)
        _ShallowColor ("Shallow Water", Color) = (0.06, 0.25, 0.45, 1)
        _LowColor ("Lowland", Color) = (0.2, 0.35, 0.12, 1)
        _MidColor ("Midland", Color) = (0.35, 0.42, 0.2, 1)
        _HighColor ("Highland", Color) = (0.45, 0.38, 0.3, 1)
        _PeakColor ("Peaks", Color) = (0.9, 0.9, 0.92, 1)
        _IceColor ("Ice", Color) = (0.9, 0.95, 1, 1)
        _CloudColor ("Clouds (A = opacity)", Color) = (1, 1, 1, 0.9)
        _AtmoColor ("Atmosphere", Color) = (0.35, 0.6, 1, 1)
        _CityLightColor ("City Lights", Color) = (1, 0.75, 0.4, 1)
        _CityDayColor ("City Day (A = strength)", Color) = (0.5, 0.5, 0.52, 0.7)
        _LavaColor ("Lava", Color) = (1, 0.35, 0.05, 1)
        _SeedOffset ("Seed Offset", Vector) = (0, 0, 0, 0)
        _SeaLevel ("Sea Level", Range(0, 1)) = 0.5
        _ContinentScale ("Continent Scale", Float) = 2.2
        _Warp ("Coastline Warp", Float) = 0.8
        _Mountains ("Mountains", Range(0, 1)) = 0.5
        _IceCap ("Ice Caps", Range(0, 1)) = 0.15
        _CloudCover ("Cloud Cover", Range(0, 1)) = 0.45
        _CloudScale ("Cloud Scale", Float) = 2.5
        _CityDensity ("City Density", Range(0, 1.3)) = 0.1
        _CityScale ("City Scale", Float) = 6
        _AtmoThickness ("Atmosphere Thickness", Range(0, 0.5)) = 0.12
        _AtmoIntensity ("Atmosphere Intensity", Range(0, 2)) = 1
        _Bands ("Gas Bands", Range(0, 1)) = 0
        _BandFreq ("Band Frequency", Float) = 6
        _Lava ("Lava", Range(0, 1)) = 0
        _Craters ("Craters", Range(0, 1)) = 0
        _Spin ("Spin", Float) = 0.03
        _CloudSpin ("Cloud Drift", Float) = 0.01
        _DetailStrength ("Surface Detail", Range(0, 1)) = 0.35
        [HideInInspector] _SkyCenterDir ("", Vector) = (0, -1, 0, 0)
        [HideInInspector] _SkyPoleDir ("", Vector) = (0, 0, 1, 0)
        [HideInInspector] _SkySunDir ("", Vector) = (-0.7, 0.3, 0.3, 0)
        [HideInInspector] _SkyAngularRadius ("", Float) = 0.66
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PlanetSurface.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 p = normalize(input.positionOS);
                float3 n = normalize(input.normalWS);
                float3 L = dot(_PlanetSunDirection.xyz, _PlanetSunDirection.xyz) > 0.001
                    ? normalize(_PlanetSunDirection.xyz)
                    : GetMainLight().direction;
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));

                PlanetSample s = SamplePlanet(p);
                return half4(ShadePlanet(s, n, L, V), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PlanetSurface.hlsl"

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PlanetSurface.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings vert(float4 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
