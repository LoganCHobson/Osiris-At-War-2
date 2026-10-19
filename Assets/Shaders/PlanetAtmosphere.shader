Shader "Osiris/PlanetAtmosphere"
{
    // Additive glow just outside a galactic map planet's silhouette. Drawn on a slightly larger sphere
    // (built by PlanetVisual) using the same properties as the planet body.
    Properties
    {
        _AtmoColor ("Atmosphere", Color) = (0.35, 0.6, 1, 1)
        _AtmoThickness ("Atmosphere Thickness", Range(0, 0.5)) = 0.12
        _AtmoIntensity ("Atmosphere Intensity", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Front

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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            Varyings vert(float4 positionOS : POSITION)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(positionOS.xyz);
                output.positionOS = positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Work in the shell's object space: shell radius 0.5, planet radius 0.5 / (1 + thickness).
                float3 rayOrigin = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 rayDir = normalize(input.positionOS - rayOrigin);
                float along = dot(-rayOrigin, rayDir);
                float3 closest = rayOrigin + rayDir * along;
                float distanceFromCentre = length(closest);

                float planetRadius = 0.5 / (1.0 + _AtmoThickness);
                float3 L = dot(_PlanetSunDirection.xyz, _PlanetSunDirection.xyz) > 0.001
                    ? normalize(_PlanetSunDirection.xyz)
                    : GetMainLight().direction;
                float3 limbWS = normalize(TransformObjectToWorldDir(closest));

                float glow = PlanetHalo(distanceFromCentre, planetRadius, limbWS, L);
                return half4(_AtmoColor.rgb * glow, 0);
            }
            ENDHLSL
        }
    }
}
