using UnityEngine;

// Values are saved in scenes, so keep them fixed (8 was Ecumenopolis, since removed).
public enum PlanetArchetype
{
    Terran = 0,
    Ocean = 1,
    Desert = 2,
    Arid = 3,
    Tundra = 4,
    Taiga = 5,
    Jungle = 6,
    Swamp = 7,
    Volcanic = 9,
    Barren = 10,
    Toxic = 11,
    GasGiant = 12,
    Exotic = 13
}

// Everything that makes one planet look different from another: palette, surface layers, clouds, cities,
// atmosphere and size. Generated from an archetype + seed by PlanetArchetypes, stored on the Planet, and
// fed to both the galactic map shader and the battle sky so the two always match.
[System.Serializable]
public class PlanetAppearance
{
    public bool generated;
    public PlanetArchetype archetype;
    public int seed;
    [Tooltip("Scale of the planet on the galactic map and in the battle sky.")]
    public float size = 1f;

    [Header("Palette")]
    public Color deepWater = new Color(0.02f, 0.06f, 0.2f);
    public Color shallowWater = new Color(0.06f, 0.25f, 0.45f);
    public Color lowland = new Color(0.2f, 0.35f, 0.12f);
    public Color midland = new Color(0.35f, 0.42f, 0.2f);
    public Color highland = new Color(0.45f, 0.38f, 0.3f);
    public Color peaks = new Color(0.9f, 0.9f, 0.92f);
    public Color ice = new Color(0.9f, 0.95f, 1f);
    [Tooltip("Alpha = cloud opacity.")]
    public Color clouds = new Color(1f, 1f, 1f, 0.9f);
    public Color atmosphere = new Color(0.35f, 0.6f, 1f);
    public Color cityLights = new Color(1f, 0.75f, 0.4f);
    [Tooltip("Alpha = how strongly cities grey out the ground by day.")]
    public Color cityDay = new Color(0.5f, 0.5f, 0.52f, 0.6f);
    public Color lava = new Color(1f, 0.35f, 0.05f);

    [Header("Surface layers")]
    public Vector3 seedOffset;
    [Range(0f, 1f)] public float seaLevel = 0.5f;
    public float continentScale = 2.2f;
    public float coastlineWarp = 0.8f;
    [Range(0f, 1f)] public float mountains = 0.5f;
    [Range(0f, 1f)] public float iceCaps = 0.15f;
    [Range(0f, 1f)] public float lavaAmount;
    [Range(0f, 1f)] public float craters;
    [Range(0f, 1f)] public float gasBands;
    public float bandFrequency = 6f;
    [Range(0f, 1f)] public float surfaceDetail = 0.35f;

    [Header("Clouds, cities, atmosphere")]
    [Range(0f, 1f)] public float cloudCover = 0.45f;
    public float cloudScale = 2.5f;
    [Range(0f, 1.3f)] public float cityDensity = 0.1f;
    public float cityScale = 6f;
    [Range(0f, 0.5f)] public float atmosphereThickness = 0.12f;
    [Range(0f, 2f)] public float atmosphereIntensity = 1f;
    public float spin = 0.03f;
    public float cloudDrift = 0.01f;

    private static readonly int DeepColorID = Shader.PropertyToID("_DeepColor");
    private static readonly int ShallowColorID = Shader.PropertyToID("_ShallowColor");
    private static readonly int LowColorID = Shader.PropertyToID("_LowColor");
    private static readonly int MidColorID = Shader.PropertyToID("_MidColor");
    private static readonly int HighColorID = Shader.PropertyToID("_HighColor");
    private static readonly int PeakColorID = Shader.PropertyToID("_PeakColor");
    private static readonly int IceColorID = Shader.PropertyToID("_IceColor");
    private static readonly int CloudColorID = Shader.PropertyToID("_CloudColor");
    private static readonly int AtmoColorID = Shader.PropertyToID("_AtmoColor");
    private static readonly int CityLightColorID = Shader.PropertyToID("_CityLightColor");
    private static readonly int CityDayColorID = Shader.PropertyToID("_CityDayColor");
    private static readonly int LavaColorID = Shader.PropertyToID("_LavaColor");
    private static readonly int SeedOffsetID = Shader.PropertyToID("_SeedOffset");
    private static readonly int SeaLevelID = Shader.PropertyToID("_SeaLevel");
    private static readonly int ContinentScaleID = Shader.PropertyToID("_ContinentScale");
    private static readonly int WarpID = Shader.PropertyToID("_Warp");
    private static readonly int MountainsID = Shader.PropertyToID("_Mountains");
    private static readonly int IceCapID = Shader.PropertyToID("_IceCap");
    private static readonly int CloudCoverID = Shader.PropertyToID("_CloudCover");
    private static readonly int CloudScaleID = Shader.PropertyToID("_CloudScale");
    private static readonly int CityDensityID = Shader.PropertyToID("_CityDensity");
    private static readonly int CityScaleID = Shader.PropertyToID("_CityScale");
    private static readonly int AtmoThicknessID = Shader.PropertyToID("_AtmoThickness");
    private static readonly int AtmoIntensityID = Shader.PropertyToID("_AtmoIntensity");
    private static readonly int BandsID = Shader.PropertyToID("_Bands");
    private static readonly int BandFreqID = Shader.PropertyToID("_BandFreq");
    private static readonly int LavaID = Shader.PropertyToID("_Lava");
    private static readonly int CratersID = Shader.PropertyToID("_Craters");
    private static readonly int SpinID = Shader.PropertyToID("_Spin");
    private static readonly int CloudSpinID = Shader.PropertyToID("_CloudSpin");
    private static readonly int DetailStrengthID = Shader.PropertyToID("_DetailStrength");

    public string DisplayName => PlanetArchetypes.DisplayName(archetype);

    public PlanetAppearance Clone()
    {
        return (PlanetAppearance)MemberwiseClone();
    }

    public void ApplyTo(MaterialPropertyBlock block)
    {
        Write(block.SetColor, block.SetFloat, block.SetVector);
    }

    public void ApplyTo(Material material)
    {
        Write(material.SetColor, material.SetFloat, material.SetVector);
    }

    private void Write(System.Action<int, Color> setColor, System.Action<int, float> setFloat, System.Action<int, Vector4> setVector)
    {
        setColor(DeepColorID, deepWater);
        setColor(ShallowColorID, shallowWater);
        setColor(LowColorID, lowland);
        setColor(MidColorID, midland);
        setColor(HighColorID, highland);
        setColor(PeakColorID, peaks);
        setColor(IceColorID, ice);
        setColor(CloudColorID, clouds);
        setColor(AtmoColorID, atmosphere);
        setColor(CityLightColorID, cityLights);
        setColor(CityDayColorID, cityDay);
        setColor(LavaColorID, lava);
        setVector(SeedOffsetID, seedOffset);
        setFloat(SeaLevelID, seaLevel);
        setFloat(ContinentScaleID, continentScale);
        setFloat(WarpID, coastlineWarp);
        setFloat(MountainsID, mountains);
        setFloat(IceCapID, iceCaps);
        setFloat(CloudCoverID, cloudCover);
        setFloat(CloudScaleID, cloudScale);
        setFloat(CityDensityID, cityDensity);
        setFloat(CityScaleID, cityScale);
        setFloat(AtmoThicknessID, atmosphereThickness);
        setFloat(AtmoIntensityID, atmosphereIntensity);
        setFloat(BandsID, gasBands);
        setFloat(BandFreqID, bandFrequency);
        setFloat(LavaID, lavaAmount);
        setFloat(CratersID, craters);
        setFloat(SpinID, spin);
        setFloat(CloudSpinID, cloudDrift);
        setFloat(DetailStrengthID, surfaceDetail);
    }
}
