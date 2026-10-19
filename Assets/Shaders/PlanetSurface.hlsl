#ifndef OSIRIS_PLANET_SURFACE_INCLUDED
#define OSIRIS_PLANET_SURFACE_INCLUDED

// Shared procedural planet surface, used by the galactic map planets (Osiris/Planet, Osiris/PlanetAtmosphere)
// and the battle sky (Osiris/SkyPlanet) so a world looks the same in both places.
//
// A planet is built from stacked, seeded noise layers - terrain/continents, biome colour bands, ice caps,
// craters, lava, gas bands, city sprawl (daytime grey + night lights), clouds and atmosphere - all driven by the
// properties below. PlanetAppearance.cs fills them in per planet.

#ifndef PLANET_OCTAVES
#define PLANET_OCTAVES 5
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _DeepColor;
    float4 _ShallowColor;
    float4 _LowColor;
    float4 _MidColor;
    float4 _HighColor;
    float4 _PeakColor;
    float4 _IceColor;
    float4 _CloudColor;
    float4 _AtmoColor;
    float4 _CityLightColor;
    float4 _CityDayColor;
    float4 _LavaColor;
    float4 _SeedOffset;
    float _SeaLevel;
    float _ContinentScale;
    float _Warp;
    float _Mountains;
    float _IceCap;
    float _CloudCover;
    float _CloudScale;
    float _CityDensity;
    float _CityScale;
    float _AtmoThickness;
    float _AtmoIntensity;
    float _Bands;
    float _BandFreq;
    float _Lava;
    float _Craters;
    float _Spin;
    float _CloudSpin;
    float _DetailStrength;
    // Battle sky only.
    float4 _SkyCenterDir;
    float4 _SkyPoleDir;
    float4 _SkySunDir;
    float _SkyAngularRadius;
CBUFFER_END

// Direction the galactic map's planets are lit from (set by PlanetVisual).
float4 _PlanetSunDirection;

// ---- Noise ----

float3 PlanetHash33(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)),
               dot(p, float3(269.5, 183.3, 246.1)),
               dot(p, float3(113.5, 271.9, 124.6)));
    return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
}

// 3D gradient noise, roughly [-1, 1].
float PlanetNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

    float n000 = dot(PlanetHash33(i + float3(0, 0, 0)), f - float3(0, 0, 0));
    float n100 = dot(PlanetHash33(i + float3(1, 0, 0)), f - float3(1, 0, 0));
    float n010 = dot(PlanetHash33(i + float3(0, 1, 0)), f - float3(0, 1, 0));
    float n110 = dot(PlanetHash33(i + float3(1, 1, 0)), f - float3(1, 1, 0));
    float n001 = dot(PlanetHash33(i + float3(0, 0, 1)), f - float3(0, 0, 1));
    float n101 = dot(PlanetHash33(i + float3(1, 0, 1)), f - float3(1, 0, 1));
    float n011 = dot(PlanetHash33(i + float3(0, 1, 1)), f - float3(0, 1, 1));
    float n111 = dot(PlanetHash33(i + float3(1, 1, 1)), f - float3(1, 1, 1));

    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);
    return lerp(lerp(nx00, nx10, u.y), lerp(nx01, nx11, u.y), u.z) * 1.4;
}

float PlanetFbm(float3 p, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < octaves; i++)
    {
        sum += PlanetNoise(p) * amplitude;
        p = p * 2.03 + float3(17.1, 3.7, 9.3);
        amplitude *= 0.5;
    }
    return sum;
}

// Sharp mountain ridges, [0, 1].
float PlanetRidged(float3 p, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float weight = 1.0;
    for (int i = 0; i < octaves; i++)
    {
        float n = 1.0 - abs(PlanetNoise(p));
        n *= n * weight;
        weight = saturate(n * 2.0);
        sum += n * amplitude;
        p = p * 2.1 + float3(5.3, 11.9, 2.2);
        amplitude *= 0.5;
    }
    return sum;
}

// Distance to the nearest scattered feature point (x) and a random value for that cell (y).
float2 PlanetVoronoi(float3 p)
{
    float3 cell = floor(p);
    float best = 8.0;
    float id = 0.0;
    for (int z = -1; z <= 1; z++)
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        float3 neighbour = cell + float3(x, y, z);
        float3 jitter = PlanetHash33(neighbour) * 0.5 + 0.5;
        float d = length(neighbour + jitter - p);
        if (d < best)
        {
            best = d;
            id = jitter.x;
        }
    }
    return float2(best, id);
}

float3 PlanetRotateY(float3 p, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    return float3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z);
}

// ---- Surface ----

struct PlanetSample
{
    float3 albedo;
    float3 emission;   // Always glows (lava).
    float cityLights;  // Glows on the night side.
    float water;
    float cloud;
};

// p: unit direction from the planet centre, in the planet's own frame (y = north pole).
PlanetSample SamplePlanet(float3 p)
{
    PlanetSample s;
    float time = _Time.y;

    float3 sp = PlanetRotateY(p, time * _Spin);
    float3 q = sp * _ContinentScale + _SeedOffset.xyz;

    // Layer 1: continents - domain-warped noise gives organic coastlines rather than blobs.
    float3 warp = float3(PlanetFbm(q + float3(1.7, 9.2, 3.1), 3),
                         PlanetFbm(q + float3(8.3, 2.8, 5.4), 3),
                         PlanetFbm(q + float3(4.1, 6.6, 7.7), 3));
    float3 wq = q + warp * _Warp;
    float height = PlanetFbm(wq, PLANET_OCTAVES) * 0.5 + 0.5;
    float ridges = PlanetRidged(wq * 2.3, max(PLANET_OCTAVES - 1, 2));
    float elevation = height + (ridges - 0.35) * _Mountains * saturate((height - _SeaLevel) * 6.0);

    float detail = PlanetFbm(q * 9.0 + 13.1, 3);
    float moisture = PlanetFbm(q * 1.3 + 41.3, 3) * 0.5 + 0.5;

    // Layer 2: biome bands by altitude, broken up by moisture and fine detail.
    float landT = saturate((elevation - _SeaLevel) / 0.32);
    float3 land = lerp(_LowColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.5, landT));
    land = lerp(land, _HighColor.rgb, smoothstep(0.45, 0.85, landT));
    land = lerp(land, _PeakColor.rgb, smoothstep(0.82, 1.0, landT));
    land *= lerp(0.82, 1.18, moisture);
    land *= 1.0 + detail * _DetailStrength;

    // Layer 3: oceans, shallow shelves to deep trenches.
    float depth = saturate((_SeaLevel - elevation) / 0.22);
    float3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, smoothstep(0.0, 0.6, depth));
    water *= 1.0 + detail * 0.08;

    float landMask = smoothstep(_SeaLevel - 0.004, _SeaLevel + 0.004, elevation);
    float3 albedo = lerp(water, land, landMask);

    // Layer 4: gas giant bands replace the solid surface.
    if (_Bands > 0.001)
    {
        float turbulence = PlanetFbm(float3(q.x * 0.7, q.y * 3.0, q.z * 0.7), PLANET_OCTAVES);
        float band = sin((sp.y * _BandFreq + turbulence * 1.1) * 6.2831) * 0.5 + 0.5;
        float fine = PlanetFbm(float3(q.x * 1.5, q.y * 14.0, q.z * 1.5), 3) * 0.5 + 0.5;
        float3 bandColor = lerp(_LowColor.rgb, _MidColor.rgb, band);
        bandColor = lerp(bandColor, _HighColor.rgb, smoothstep(0.55, 0.85, fine));
        float storm = smoothstep(0.82, 0.9, PlanetFbm(q * 2.2 + 7.0, 3) * 0.5 + 0.5);
        bandColor = lerp(bandColor, _PeakColor.rgb, storm);
        albedo = lerp(albedo, bandColor, _Bands);
        landMask *= 1.0 - _Bands;
    }

    // Layer 5: craters on airless rocks.
    if (_Craters > 0.001)
    {
        float2 v = PlanetVoronoi(sp * _ContinentScale * 2.5 + _SeedOffset.zxy);
        float radius = lerp(0.18, 0.42, v.y);
        float r = v.x / radius;
        float bowl = (1.0 - smoothstep(0.0, 1.0, r)) * 0.45;
        float rim = smoothstep(0.75, 0.95, r) * (1.0 - smoothstep(0.95, 1.2, r)) * 0.35;
        albedo *= 1.0 + (rim - bowl) * _Craters * step(0.35, v.y);
    }

    // Layer 6: ice caps (latitude, roughened) that also freeze the seas.
    float iceMask = smoothstep(1.0 - _IceCap, 1.0 - _IceCap + 0.07, abs(sp.y) + detail * 0.09) * (1.0 - _Bands);
    albedo = lerp(albedo, _IceColor.rgb, iceMask);

    // Layer 7: lava rivers.
    float3 emission = 0;
    if (_Lava > 0.001)
    {
        float veins = 1.0 - abs(PlanetNoise(wq * 4.5));
        float lavaMask = smoothstep(0.9, 0.985, veins) * _Lava * landMask + (1.0 - landMask) * _Lava;
        albedo = lerp(albedo, albedo * 0.25, saturate(lavaMask));
        emission = _LavaColor.rgb * saturate(lavaMask) * 2.5;
    }

    // Layer 8: cities - grey sprawl by day, street grids and lights by night.
    float cityField = PlanetFbm(sp * _CityScale + _SeedOffset.zxy * 1.3, 4) * 0.5 + 0.5;
    float cityMask = smoothstep(1.0 - _CityDensity, 1.0 - _CityDensity + 0.12, cityField);
    cityMask *= landMask * (1.0 - iceMask * 0.7);
    float3 grid = abs(frac(sp * _CityScale * 7.0) - 0.5);
    float streets = smoothstep(0.43, 0.5, max(max(grid.x, grid.y), grid.z));
    float sparkle = PlanetNoise(sp * _CityScale * 28.0) * 0.5 + 0.5;
    albedo = lerp(albedo, _CityDayColor.rgb * (0.75 + 0.5 * sparkle), cityMask * _CityDayColor.a);

    s.cityLights = cityMask * (0.25 + 0.75 * sparkle * sparkle + streets * 0.8);
    s.emission = emission;
    s.water = (1.0 - landMask) * (1.0 - iceMask) * (1.0 - step(0.001, _Lava));

    // Layer 9: clouds, drifting a little faster than the ground.
    float3 cp = PlanetRotateY(p, time * (_Spin + _CloudSpin));
    float3 cq = cp * _CloudScale + _SeedOffset.yzx;
    float3 cloudWarp = float3(PlanetFbm(cq * 0.6 + 2.3, 3), PlanetFbm(cq * 0.6 + 7.9, 3), PlanetFbm(cq * 0.6 + 4.4, 3));
    float cloudNoise = PlanetFbm(cq + cloudWarp * 1.2, PLANET_OCTAVES) * 0.5 + 0.5;
    s.cloud = smoothstep(1.0 - _CloudCover, 1.0 - _CloudCover + 0.22, cloudNoise) * saturate(_CloudCover * 3.0);

    s.albedo = albedo;
    return s;
}

// n: surface normal, L: direction to the sun, V: direction to the viewer (all in the same space).
float3 ShadePlanet(PlanetSample s, float3 n, float3 L, float3 V)
{
    float ndl = dot(n, L);
    float lit = saturate(ndl);
    float day = smoothstep(-0.12, 0.22, ndl);

    float3 color = s.albedo * (lit * 1.05 + 0.02);

    float3 halfway = normalize(L + V);
    color += pow(saturate(dot(n, halfway)), 70.0) * 0.55 * s.water * (1.0 - s.cloud) * lit * float3(1.0, 0.96, 0.88);

    float3 cloudLit = _CloudColor.rgb * (lit * 1.1 + 0.015);
    color = lerp(color, cloudLit, s.cloud * _CloudColor.a);

    color += _CityLightColor.rgb * s.cityLights * (1.0 - day) * (1.0 - s.cloud * 0.7) * 1.8;
    color += s.emission * (1.0 - s.cloud * 0.6);

    // Atmosphere: hazy limb that brightens toward the lit side.
    float fresnel = pow(1.0 - saturate(dot(n, V)), 2.5);
    float atmoLit = saturate(ndl * 0.8 + 0.35);
    color = lerp(color, _AtmoColor.rgb * atmoLit, saturate(fresnel * _AtmoIntensity * 0.85));
    color += _AtmoColor.rgb * pow(fresnel, 4.0) * atmoLit * _AtmoIntensity * 0.6;
    return color;
}

// Glow just outside the silhouette. distanceFromCentre/planetRadius are in the same units.
float PlanetHalo(float distanceFromCentre, float planetRadius, float3 limbDirection, float3 L)
{
    float outer = planetRadius * (1.0 + _AtmoThickness);
    float x = saturate((distanceFromCentre - planetRadius) / max(outer - planetRadius, 1e-4));
    float glow = pow(1.0 - x, 2.4) * step(planetRadius, distanceFromCentre);
    return glow * saturate(dot(limbDirection, L) * 0.8 + 0.4) * _AtmoIntensity;
}

#endif
