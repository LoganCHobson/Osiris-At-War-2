Shader "Osiris/SkyPlanet"
{
    // The planet a battle is fought over, painted into the sky behind everything. Drawn as a full-screen
    // triangle at the far plane (so it only covers sky) and ray-traced against a sphere at infinity.
    // Uses the same surface as the galactic map planet, so the world matches what you clicked on.
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
        _Spin ("Spin", Float) = 0.003
        _CloudSpin ("Cloud Drift", Float) = 0.002
        _DetailStrength ("Surface Detail", Range(0, 1)) = 0.35
        _SkyCenterDir ("Planet Direction", Vector) = (0, -1, 0, 0)
        _SkyPoleDir ("Planet North Pole", Vector) = (0, 0, 1, 0)
        _SkySunDir ("Sun Direction", Vector) = (-0.75, 0.35, 0.3, 0)
        _SkyAngularRadius ("Angular Radius (sin)", Range(0.05, 0.99)) = 0.66
    }

    SubShader
    {
        // First thing drawn after the skybox, before lasers/shields/effects.
        Tags { "RenderType"="Transparent" "Queue"="Transparent-499" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Skybox" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #define PLANET_OCTAVES 8
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PlanetSurface.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 rayWS : TEXCOORD0;
            };

            Varyings vert(float4 positionOS : POSITION)
            {
                // The mesh is a single triangle already in clip space, covering the screen.
                Varyings output;
                output.positionCS = float4(positionOS.xy, UNITY_RAW_FAR_CLIP_VALUE, 1.0);
                float4 farWS = mul(UNITY_MATRIX_I_VP, float4(positionOS.xy, UNITY_RAW_FAR_CLIP_VALUE, 1.0));
                output.rayWS = farWS.xyz / farWS.w - _WorldSpaceCameraPos;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 rd = normalize(input.rayWS);
                float3 centre = normalize(_SkyCenterDir.xyz);
                float radius = _SkyAngularRadius;

                // Sphere of the given angular size at distance 1 along centre.
                float along = dot(rd, centre);
                float distanceFromCentre = sqrt(saturate(1.0 - along * along));
                float pixel = max(fwidth(distanceFromCentre), 1e-5);
                if (along <= 0.0) return 0; // Looking away from the planet.

                float coverage = saturate((radius - distanceFromCentre) / pixel + 0.5);

                float3 L = normalize(_SkySunDir.xyz);
                float3 color = 0;

                if (coverage > 0.0)
                {
                    float inside = sqrt(max(radius * radius - min(distanceFromCentre, radius) * min(distanceFromCentre, radius), 0.0));
                    float3 hit = rd * (along - inside);
                    float3 n = normalize(hit - centre);

                    // Planet frame: y = north pole.
                    float3 up = normalize(_SkyPoleDir.xyz);
                    float3 right = normalize(cross(up, centre));
                    float3 forward = cross(right, up);
                    float3 p = float3(dot(n, right), dot(n, up), dot(n, forward));

                    PlanetSample s = SamplePlanet(p);
                    color = ShadePlanet(s, n, L, -rd);
                }

                float3 limb = normalize(rd * along - centre);
                float halo = PlanetHalo(distanceFromCentre, radius, limb, L) * (1.0 - coverage);
                return half4(color * coverage + _AtmoColor.rgb * halo, coverage);
            }
            ENDHLSL
        }
    }
}
