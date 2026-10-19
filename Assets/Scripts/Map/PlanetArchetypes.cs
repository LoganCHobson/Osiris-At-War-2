using System.Collections.Generic;
using UnityEngine;

// Recipes for each kind of world. Every archetype has its own palette and layer settings, and each roll
// jitters hues, coverage and size within that archetype's range - two tundra worlds still look different.
public static class PlanetArchetypes
{
    // Name hints in the spirit of Empire at War's worlds. Planets whose name starts with one of these
    // lean toward that archetype.
    private static readonly Dictionary<string, PlanetArchetype> NameHints = new Dictionary<string, PlanetArchetype>
    {
        { "Coruscant", PlanetArchetype.Terran },
        { "Kamino", PlanetArchetype.Ocean },
        { "Geonosis", PlanetArchetype.Arid },
        { "Mustafar", PlanetArchetype.Volcanic },
        { "Hoth", PlanetArchetype.Tundra },
        { "Bespin", PlanetArchetype.GasGiant },
        { "Endor", PlanetArchetype.Taiga },
        { "Kashyyyk", PlanetArchetype.Jungle },
        { "Mygeeto", PlanetArchetype.Tundra },
        { "Felucia", PlanetArchetype.Exotic },
        { "Utapau", PlanetArchetype.Arid },
        { "Dagobah", PlanetArchetype.Swamp },
        { "Tatooine", PlanetArchetype.Desert },
        { "Naboo", PlanetArchetype.Terran },
        { "Alderaan", PlanetArchetype.Terran },
        { "Yavin", PlanetArchetype.Jungle },
        { "Sullust", PlanetArchetype.Volcanic },
        { "Corellia", PlanetArchetype.Terran },
        { "Dantooine", PlanetArchetype.Terran },
        { "Ryloth", PlanetArchetype.Desert },
        { "Kessel", PlanetArchetype.Barren },
        { "Polis Massa", PlanetArchetype.Barren },
        { "Ilum", PlanetArchetype.Tundra },
        { "Mon Calamari", PlanetArchetype.Ocean },
        { "Kuat", PlanetArchetype.Terran },
        { "Nal Hutta", PlanetArchetype.Swamp },
        { "Thyferra", PlanetArchetype.Jungle },
        { "Fondor", PlanetArchetype.Barren },
        { "Byss", PlanetArchetype.Toxic },
        { "Taris", PlanetArchetype.Terran }
    };

    // How often each archetype turns up when a planet has no name hint.
    private static readonly (PlanetArchetype archetype, float weight)[] RandomWeights =
    {
        (PlanetArchetype.Terran, 1.2f), (PlanetArchetype.Ocean, 0.8f), (PlanetArchetype.Desert, 1f),
        (PlanetArchetype.Arid, 0.8f), (PlanetArchetype.Tundra, 0.9f), (PlanetArchetype.Taiga, 0.8f),
        (PlanetArchetype.Jungle, 0.8f), (PlanetArchetype.Swamp, 0.5f),
        (PlanetArchetype.Volcanic, 0.6f), (PlanetArchetype.Barren, 0.9f), (PlanetArchetype.Toxic, 0.5f),
        (PlanetArchetype.GasGiant, 0.6f), (PlanetArchetype.Exotic, 0.7f)
    };

    public static string DisplayName(PlanetArchetype archetype)
    {
        switch (archetype)
        {
            case PlanetArchetype.GasGiant: return "Gas Giant";
            case PlanetArchetype.Terran: return "Temperate";
            default: return archetype.ToString();
        }
    }

    public static bool TryGetNameHint(string planetName, out PlanetArchetype archetype, out bool exactMatch)
    {
        archetype = PlanetArchetype.Terran;
        exactMatch = false;
        if (string.IsNullOrEmpty(planetName)) return false;

        foreach (KeyValuePair<string, PlanetArchetype> hint in NameHints)
        {
            if (planetName.StartsWith(hint.Key, System.StringComparison.OrdinalIgnoreCase))
            {
                archetype = hint.Value;
                exactMatch = planetName.Length == hint.Key.Length;
                return true;
            }
        }
        return false;
    }

    // Picks an archetype for a planet: famous names always get their world, "Tatooine Minor" style
    // offshoots follow the hint some of the time, everything else is a weighted roll.
    public static PlanetArchetype Choose(string planetName, System.Random random, float nameHintChance)
    {
        if (TryGetNameHint(planetName, out PlanetArchetype hinted, out bool exact) && (exact || random.NextDouble() < nameHintChance))
        {
            return hinted;
        }

        float total = 0f;
        foreach ((PlanetArchetype _, float weight) in RandomWeights) total += weight;

        double roll = random.NextDouble() * total;
        foreach ((PlanetArchetype archetype, float weight) in RandomWeights)
        {
            roll -= weight;
            if (roll <= 0) return archetype;
        }
        return PlanetArchetype.Terran;
    }

    // Deterministic look for a planet that has never been generated (keeps old maps varied).
    public static PlanetAppearance ForName(string planetName)
    {
        int seed = StableHash(planetName);
        System.Random random = new System.Random(seed);
        return Create(Choose(planetName, random, 0.6f), seed);
    }

    public static int StableHash(string text)
    {
        unchecked
        {
            int hash = 23;
            foreach (char c in text ?? "") hash = hash * 31 + c;
            return hash & 0x7fffffff;
        }
    }

    public static PlanetAppearance Create(PlanetArchetype archetype, int seed)
    {
        Roll r = new Roll(seed);
        PlanetAppearance a = new PlanetAppearance
        {
            generated = true,
            archetype = archetype,
            seed = seed,
            seedOffset = new Vector3(r.Range(-40f, 40f), r.Range(-40f, 40f), r.Range(-40f, 40f)),
            size = r.Range(0.9f, 1.1f),
            continentScale = r.Range(1.7f, 2.6f),
            coastlineWarp = r.Range(0.6f, 1.05f),
            cloudScale = r.Range(2.0f, 3.2f),
            cityScale = r.Range(5f, 8f),
            spin = r.Range(0.02f, 0.05f) * (r.Chance(0.15f) ? -1f : 1f),
            cloudDrift = r.Range(0.005f, 0.02f),
            surfaceDetail = r.Range(0.25f, 0.45f),
            atmosphereThickness = r.Range(0.09f, 0.14f),
            atmosphereIntensity = r.Range(0.85f, 1.1f),
            clouds = new Color(1f, 1f, 1f, 0.92f),
            cityLights = r.Jitter(new Color(1f, 0.74f, 0.38f), 0.03f, 0.1f, 0.05f),
            cityDay = new Color(0.5f, 0.5f, 0.53f, 0.55f),
            ice = r.Jitter(new Color(0.9f, 0.95f, 1f), 0.02f, 0.05f, 0.03f)
        };

        switch (archetype)
        {
            case PlanetArchetype.Terran:
                Palette(a, r, 0.04f,
                    new Color(0.02f, 0.07f, 0.22f), new Color(0.05f, 0.3f, 0.45f),
                    new Color(0.16f, 0.32f, 0.1f), new Color(0.3f, 0.38f, 0.17f), new Color(0.42f, 0.36f, 0.26f), new Color(0.92f, 0.92f, 0.94f));
                a.seaLevel = r.Range(0.45f, 0.56f);
                a.mountains = r.Range(0.35f, 0.6f);
                a.iceCaps = r.Range(0.1f, 0.2f);
                a.cloudCover = r.Range(0.35f, 0.55f);
                a.cityDensity = r.Range(0.12f, 0.35f);
                a.atmosphere = r.Jitter(new Color(0.35f, 0.6f, 1f), 0.03f, 0.1f, 0.05f);
                break;

            case PlanetArchetype.Ocean:
                Palette(a, r, 0.05f,
                    new Color(0.01f, 0.08f, 0.25f), new Color(0.05f, 0.42f, 0.55f),
                    new Color(0.55f, 0.52f, 0.36f), new Color(0.22f, 0.4f, 0.18f), new Color(0.3f, 0.35f, 0.25f), new Color(0.85f, 0.88f, 0.9f));
                a.seaLevel = r.Range(0.64f, 0.74f);
                a.mountains = r.Range(0.2f, 0.4f);
                a.iceCaps = r.Range(0.06f, 0.14f);
                a.cloudCover = r.Range(0.45f, 0.65f);
                a.cityDensity = r.Range(0.1f, 0.3f);
                a.atmosphere = r.Jitter(new Color(0.3f, 0.62f, 1f), 0.03f, 0.1f, 0.05f);
                break;

            case PlanetArchetype.Desert:
                Palette(a, r, 0.03f,
                    new Color(0.15f, 0.12f, 0.08f), new Color(0.35f, 0.3f, 0.2f),
                    new Color(0.82f, 0.66f, 0.42f), new Color(0.74f, 0.54f, 0.33f), new Color(0.56f, 0.38f, 0.24f), new Color(0.88f, 0.76f, 0.58f));
                a.seaLevel = r.Chance(0.3f) ? r.Range(0.22f, 0.3f) : 0f;
                a.mountains = r.Range(0.4f, 0.75f);
                a.iceCaps = r.Chance(0.25f) ? r.Range(0.03f, 0.07f) : 0f;
                a.cloudCover = r.Range(0.02f, 0.12f);
                a.cityDensity = r.Range(0.05f, 0.15f);
                a.atmosphere = r.Jitter(new Color(1f, 0.78f, 0.5f), 0.03f, 0.1f, 0.05f);
                a.atmosphereThickness = r.Range(0.07f, 0.1f);
                a.surfaceDetail = r.Range(0.4f, 0.6f);
                break;

            case PlanetArchetype.Arid:
                Palette(a, r, 0.04f,
                    new Color(0.18f, 0.1f, 0.06f), new Color(0.36f, 0.2f, 0.12f),
                    new Color(0.62f, 0.32f, 0.17f), new Color(0.5f, 0.23f, 0.12f), new Color(0.36f, 0.17f, 0.11f), new Color(0.72f, 0.48f, 0.32f));
                a.seaLevel = r.Chance(0.4f) ? r.Range(0.2f, 0.32f) : 0f;
                a.mountains = r.Range(0.7f, 0.95f);
                a.iceCaps = 0f;
                a.cloudCover = r.Range(0.04f, 0.18f);
                a.cityDensity = r.Range(0.05f, 0.2f);
                a.atmosphere = r.Jitter(new Color(1f, 0.55f, 0.35f), 0.03f, 0.1f, 0.05f);
                a.surfaceDetail = r.Range(0.4f, 0.6f);
                break;

            case PlanetArchetype.Tundra:
                Palette(a, r, 0.03f,
                    new Color(0.03f, 0.1f, 0.2f), new Color(0.12f, 0.3f, 0.42f),
                    new Color(0.72f, 0.8f, 0.86f), new Color(0.6f, 0.7f, 0.76f), new Color(0.48f, 0.56f, 0.62f), new Color(0.96f, 0.97f, 1f));
                a.seaLevel = r.Range(0.35f, 0.46f);
                a.mountains = r.Range(0.45f, 0.75f);
                a.iceCaps = r.Range(0.65f, 0.9f);
                a.cloudCover = r.Range(0.3f, 0.5f);
                a.cityDensity = r.Range(0.04f, 0.14f);
                a.cityLights = r.Jitter(new Color(0.45f, 0.85f, 1f), 0.03f, 0.1f, 0.05f);
                a.atmosphere = r.Jitter(new Color(0.55f, 0.78f, 1f), 0.03f, 0.1f, 0.05f);
                break;

            case PlanetArchetype.Taiga:
                Palette(a, r, 0.03f,
                    new Color(0.02f, 0.07f, 0.16f), new Color(0.07f, 0.22f, 0.32f),
                    new Color(0.07f, 0.2f, 0.11f), new Color(0.11f, 0.24f, 0.14f), new Color(0.3f, 0.31f, 0.27f), new Color(0.94f, 0.95f, 0.97f));
                a.seaLevel = r.Range(0.42f, 0.5f);
                a.mountains = r.Range(0.4f, 0.7f);
                a.iceCaps = r.Range(0.25f, 0.4f);
                a.cloudCover = r.Range(0.45f, 0.6f);
                a.cityDensity = r.Range(0.04f, 0.12f);
                a.atmosphere = r.Jitter(new Color(0.4f, 0.65f, 0.95f), 0.03f, 0.1f, 0.05f);
                break;

            case PlanetArchetype.Jungle:
                Palette(a, r, 0.04f,
                    new Color(0.02f, 0.1f, 0.15f), new Color(0.05f, 0.3f, 0.3f),
                    new Color(0.05f, 0.3f, 0.06f), new Color(0.11f, 0.37f, 0.08f), new Color(0.2f, 0.34f, 0.12f), new Color(0.35f, 0.4f, 0.25f));
                a.seaLevel = r.Range(0.35f, 0.5f);
                a.mountains = r.Range(0.25f, 0.5f);
                a.iceCaps = r.Range(0f, 0.05f);
                a.cloudCover = r.Range(0.55f, 0.75f);
                a.cityDensity = r.Range(0.03f, 0.1f);
                a.atmosphere = r.Jitter(new Color(0.45f, 0.8f, 0.7f), 0.03f, 0.1f, 0.05f);
                break;

            case PlanetArchetype.Swamp:
                Palette(a, r, 0.04f,
                    new Color(0.06f, 0.1f, 0.06f), new Color(0.15f, 0.21f, 0.11f),
                    new Color(0.17f, 0.23f, 0.11f), new Color(0.23f, 0.27f, 0.14f), new Color(0.29f, 0.29f, 0.19f), new Color(0.38f, 0.38f, 0.3f));
                a.seaLevel = r.Range(0.47f, 0.55f);
                a.mountains = r.Range(0.1f, 0.3f);
                a.iceCaps = 0f;
                a.cloudCover = r.Range(0.65f, 0.85f);
                a.clouds = r.Jitter(new Color(0.75f, 0.8f, 0.72f, 0.85f), 0.03f, 0.1f, 0.05f);
                a.cityDensity = r.Range(0f, 0.06f);
                a.atmosphere = r.Jitter(new Color(0.55f, 0.72f, 0.5f), 0.03f, 0.1f, 0.05f);
                a.surfaceDetail = r.Range(0.45f, 0.65f);
                break;

            case PlanetArchetype.Volcanic:
                Palette(a, r, 0.03f,
                    new Color(0.3f, 0.06f, 0.02f), new Color(0.5f, 0.12f, 0.02f),
                    new Color(0.11f, 0.09f, 0.08f), new Color(0.17f, 0.13f, 0.11f), new Color(0.24f, 0.19f, 0.17f), new Color(0.34f, 0.29f, 0.27f));
                a.seaLevel = r.Range(0.3f, 0.45f);
                a.mountains = r.Range(0.6f, 0.9f);
                a.iceCaps = 0f;
                a.lavaAmount = r.Range(0.6f, 0.95f);
                a.lava = r.Jitter(new Color(1f, 0.35f, 0.05f), 0.03f, 0.05f, 0.05f);
                a.cloudCover = r.Range(0.15f, 0.35f);
                a.clouds = r.Jitter(new Color(0.28f, 0.24f, 0.22f, 0.8f), 0.02f, 0.05f, 0.05f);
                a.cityDensity = r.Range(0f, 0.08f);
                a.atmosphere = r.Jitter(new Color(1f, 0.45f, 0.2f), 0.03f, 0.1f, 0.05f);
                break;

            case PlanetArchetype.Barren:
                Palette(a, r, 0.03f,
                    new Color(0.1f, 0.1f, 0.1f), new Color(0.2f, 0.2f, 0.2f),
                    new Color(0.36f, 0.34f, 0.32f), new Color(0.43f, 0.41f, 0.39f), new Color(0.5f, 0.48f, 0.46f), new Color(0.63f, 0.62f, 0.6f));
                a.seaLevel = 0f;
                a.mountains = r.Range(0.3f, 0.6f);
                a.iceCaps = r.Chance(0.3f) ? r.Range(0.04f, 0.1f) : 0f;
                a.craters = r.Range(0.7f, 1f);
                a.cloudCover = 0f;
                a.cityDensity = r.Range(0f, 0.05f);
                a.atmosphere = new Color(0.6f, 0.6f, 0.65f);
                a.atmosphereIntensity = r.Range(0.1f, 0.25f);
                a.atmosphereThickness = r.Range(0.04f, 0.06f);
                a.size = r.Range(0.7f, 0.85f);
                break;

            case PlanetArchetype.Toxic:
                Palette(a, r, 0.05f,
                    new Color(0.2f, 0.28f, 0.04f), new Color(0.45f, 0.55f, 0.1f),
                    new Color(0.45f, 0.42f, 0.12f), new Color(0.55f, 0.5f, 0.16f), new Color(0.4f, 0.36f, 0.18f), new Color(0.7f, 0.66f, 0.36f));
                a.seaLevel = r.Range(0.3f, 0.45f);
                a.mountains = r.Range(0.3f, 0.6f);
                a.iceCaps = 0f;
                a.cloudCover = r.Range(0.6f, 0.8f);
                a.clouds = r.Jitter(new Color(0.85f, 0.82f, 0.45f, 0.85f), 0.04f, 0.1f, 0.05f);
                a.cityDensity = r.Range(0f, 0.05f);
                a.atmosphere = r.Jitter(new Color(0.75f, 0.9f, 0.3f), 0.04f, 0.1f, 0.05f);
                a.atmosphereThickness = r.Range(0.13f, 0.18f);
                break;

            case PlanetArchetype.GasGiant:
                GasGiantPalette(a, r);
                a.seaLevel = 0f;
                a.gasBands = 1f;
                a.bandFrequency = r.Range(3.5f, 9f);
                a.continentScale = r.Range(1.3f, 1.9f);
                a.iceCaps = 0f;
                a.cloudCover = 0f;
                a.cityDensity = 0f;
                a.spin = r.Range(0.05f, 0.09f);
                a.size = r.Range(1.35f, 1.6f);
                a.atmosphereThickness = r.Range(0.1f, 0.14f);
                break;

            case PlanetArchetype.Exotic:
                ExoticPalette(a, r);
                a.seaLevel = r.Range(0.35f, 0.55f);
                a.mountains = r.Range(0.3f, 0.7f);
                a.iceCaps = r.Range(0f, 0.12f);
                a.cloudCover = r.Range(0.3f, 0.55f);
                a.cityDensity = r.Range(0f, 0.12f);
                break;
        }

        return a;
    }

    private static void Palette(PlanetAppearance a, Roll r, float hueJitter, Color deep, Color shallow, Color low, Color mid, Color high, Color peak)
    {
        a.deepWater = r.Jitter(deep, hueJitter, 0.12f, 0.08f);
        a.shallowWater = r.Jitter(shallow, hueJitter, 0.12f, 0.08f);
        a.lowland = r.Jitter(low, hueJitter, 0.12f, 0.1f);
        a.midland = r.Jitter(mid, hueJitter, 0.12f, 0.1f);
        a.highland = r.Jitter(high, hueJitter, 0.12f, 0.1f);
        a.peaks = r.Jitter(peak, hueJitter * 0.5f, 0.05f, 0.05f);
    }

    private static void GasGiantPalette(PlanetAppearance a, Roll r)
    {
        int scheme = r.Int(4);
        Color[] cols = scheme switch
        {
            0 => new[] { new Color(0.85f, 0.65f, 0.45f), new Color(0.68f, 0.43f, 0.27f), new Color(0.95f, 0.88f, 0.74f), new Color(0.62f, 0.28f, 0.18f), new Color(1f, 0.82f, 0.6f) },  // Bespin-like.
            1 => new[] { new Color(0.35f, 0.55f, 0.82f), new Color(0.22f, 0.36f, 0.68f), new Color(0.78f, 0.88f, 0.96f), new Color(0.9f, 0.95f, 1f), new Color(0.55f, 0.75f, 1f) },     // Ice giant.
            2 => new[] { new Color(0.55f, 0.4f, 0.65f), new Color(0.38f, 0.25f, 0.5f), new Color(0.85f, 0.75f, 0.9f), new Color(0.95f, 0.6f, 0.75f), new Color(0.8f, 0.6f, 1f) },     // Violet.
            _ => new[] { new Color(0.4f, 0.65f, 0.58f), new Color(0.25f, 0.45f, 0.42f), new Color(0.8f, 0.9f, 0.82f), new Color(0.9f, 0.85f, 0.55f), new Color(0.6f, 0.95f, 0.85f) } // Teal.
        };

        a.lowland = r.Jitter(cols[0], 0.03f, 0.1f, 0.08f);
        a.midland = r.Jitter(cols[1], 0.03f, 0.1f, 0.08f);
        a.highland = r.Jitter(cols[2], 0.02f, 0.05f, 0.05f);
        a.peaks = r.Jitter(cols[3], 0.03f, 0.1f, 0.05f); // Storm colour.
        a.atmosphere = r.Jitter(cols[4], 0.03f, 0.1f, 0.05f);
    }

    // Alien worlds: a random base hue with seas, land and sky pushed around the colour wheel.
    private static void ExoticPalette(PlanetAppearance a, Roll r)
    {
        float hue = r.Range(0f, 1f);
        float waterHue = Mathf.Repeat(hue + r.Range(0.35f, 0.65f), 1f);
        float skyHue = Mathf.Repeat(hue + r.Range(-0.2f, 0.2f), 1f);

        a.deepWater = Color.HSVToRGB(waterHue, 0.8f, 0.18f);
        a.shallowWater = Color.HSVToRGB(waterHue, 0.65f, 0.45f);
        a.lowland = Color.HSVToRGB(hue, r.Range(0.55f, 0.8f), r.Range(0.35f, 0.5f));
        a.midland = Color.HSVToRGB(Mathf.Repeat(hue + 0.06f, 1f), r.Range(0.5f, 0.75f), r.Range(0.4f, 0.55f));
        a.highland = Color.HSVToRGB(Mathf.Repeat(hue + 0.12f, 1f), r.Range(0.3f, 0.5f), r.Range(0.45f, 0.6f));
        a.peaks = Color.HSVToRGB(Mathf.Repeat(hue + 0.15f, 1f), 0.15f, 0.88f);
        a.clouds = Color.HSVToRGB(skyHue, 0.15f, 0.95f);
        a.clouds.a = 0.85f;
        a.atmosphere = Color.HSVToRGB(skyHue, 0.6f, 1f);
        a.cityLights = Color.HSVToRGB(Mathf.Repeat(hue + 0.5f, 1f), 0.6f, 1f);
    }

    // Small seeded random helper so a seed always produces the same planet.
    private class Roll
    {
        private readonly System.Random random;

        public Roll(int seed)
        {
            random = new System.Random(seed);
        }

        public float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        public bool Chance(float probability) => random.NextDouble() < probability;
        public int Int(int max) => random.Next(max);

        public Color Jitter(Color color, float hue, float saturation, float value)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            h = Mathf.Repeat(h + Range(-hue, hue), 1f);
            s = Mathf.Clamp01(s * (1f + Range(-saturation, saturation)));
            v = Mathf.Clamp01(v * (1f + Range(-value, value)));
            Color result = Color.HSVToRGB(h, s, v);
            result.a = color.a;
            return result;
        }
    }
}
