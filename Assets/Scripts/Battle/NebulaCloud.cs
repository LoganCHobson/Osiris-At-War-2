using System.Collections.Generic;
using UnityEngine;

// The look of a nebula or ion storm, Empire-at-War style: silky, swirling filaments of gas with lit edges
// and dark folds, several colours woven through one cloud (each nebula picks a palette), gathered in lobes
// with gaps between. Built from camera-facing strand sprites (Resources/Battle/NebulaGas, four variants)
// that turn slowly, so it reads as a volume from the RTS camera and from the cinematic angles alike.
// Ion storms use the same gas in cold blue-white and crackle with lightning.
public class NebulaCloud : MonoBehaviour
{
    private const int TextureSize = 128;

    private static readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();

    private ParticleSystem gas;
    private ParticleSystem haze;
    private ParticleSystem flashes;
    private Light lightning;
    private bool storm;
    private float radius;
    private float nextFlashAt;
    private float flashUntil;
    private System.Random random;

    public static NebulaCloud Build(Transform parent, BattleHazards.Kind kind, Vector2 centre, float radius)
    {
        GameObject go = new GameObject(kind == BattleHazards.Kind.Nebula ? "Nebula" : "IonStorm");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(centre.x, 0f, centre.y);
        NebulaCloud cloud = go.AddComponent<NebulaCloud>();
        cloud.storm = kind == BattleHazards.Kind.IonStorm;
        cloud.radius = radius;
        cloud.random = new System.Random(Mathf.RoundToInt(centre.x * 31f + centre.y * 17f));
        cloud.Fill();
        return cloud;
    }

    private float Next() => (float)random.NextDouble();

    // Palettes from the EAW look: several colours woven through one cloud, with pale lit highlights.
    private static readonly Color[][] NebulaPalettes =
    {
        new[] { new Color(0.95f, 0.45f, 0.18f), new Color(0.25f, 0.38f, 0.9f), new Color(0.15f, 0.62f, 0.55f), new Color(0.7f, 0.28f, 0.2f) }, // Rust, blue, teal.
        new[] { new Color(0.15f, 0.6f, 0.55f), new Color(0.2f, 0.45f, 0.75f), new Color(0.9f, 0.5f, 0.25f), new Color(0.12f, 0.4f, 0.35f) }, // Teal-heavy.
        new[] { new Color(0.85f, 0.38f, 0.2f), new Color(0.55f, 0.6f, 0.75f), new Color(0.6f, 0.25f, 0.18f), new Color(0.35f, 0.4f, 0.6f) }, // Rust and steel.
    };
    private static readonly Color[] StormPalette =
        { new Color(0.35f, 0.55f, 1f), new Color(0.45f, 0.85f, 1f), new Color(0.75f, 0.85f, 1f), new Color(0.25f, 0.35f, 0.8f) };
    private static readonly Color Highlight = new Color(0.88f, 0.9f, 1f);

    private void Fill()
    {
        haze = MakeSystem("Haze", 2, 120);
        gas = MakeSystem("Gas", 0, 200);
        Color[] palette = storm ? StormPalette : NebulaPalettes[random.Next(NebulaPalettes.Length)];

        // Gas gathers in a few lobes with darker gaps between, not one even ball.
        float ceiling = ShipAltitude.MaxAltitude * 0.5f;
        Vector3[] lobes = new Vector3[3];
        for (int i = 0; i < lobes.Length; i++)
        {
            float angle = Next() * Mathf.PI * 2f;
            float distance = Mathf.Lerp(0.15f, 0.5f, Next()) * radius;
            lobes[i] = new Vector3(Mathf.Cos(angle) * distance, Mathf.Lerp(-ceiling * 0.4f, ceiling * 0.4f, Next()), Mathf.Sin(angle) * distance);
        }

        // Soft glowing haze: the cloud's body, filling the gaps so the filaments sit in a lit volume.
        int hazePuffs = Mathf.Clamp(Mathf.RoundToInt(radius * 0.3f), 24, 60);
        for (int i = 0; i < hazePuffs; i++)
        {
            Vector3 lobe = lobes[i % lobes.Length];
            Vector3 jitter = new Vector3(Gaussian() * radius * 0.38f, Gaussian() * ceiling * 0.6f, Gaussian() * radius * 0.38f);
            Color glow = Color.Lerp(palette[random.Next(palette.Length)], palette[random.Next(palette.Length)], Next());
            glow.a = Mathf.Lerp(0.12f, 0.28f, Next());
            Emit(haze, transform.position + lobe + jitter, Mathf.Lerp(radius * 0.7f, radius * 1.3f, Next()), glow);
        }

        int puffs = Mathf.Clamp(Mathf.RoundToInt(radius * 0.22f), 18, 44);
        for (int i = 0; i < puffs; i++)
        {
            Vector3 lobe = lobes[i % lobes.Length];
            Vector3 jitter = new Vector3(Gaussian() * radius * 0.28f, Gaussian() * ceiling * 0.45f, Gaussian() * radius * 0.28f);
            Vector3 position = transform.position + lobe + jitter;

            bool highlight = Next() < 0.1f;
            Color color = highlight ? Highlight : palette[random.Next(palette.Length)] * Mathf.Lerp(0.85f, 1.15f, Next());
            color.a = highlight ? Mathf.Lerp(0.35f, 0.55f, Next()) : Mathf.Lerp(0.45f, 0.8f, Next());
            Emit(gas, position, Mathf.Lerp(radius * 0.6f, radius * 1.2f, Next()), color);
        }

        if (storm)
        {
            flashes = MakeSystem("Lightning", 1, 32);
            GameObject lightObject = new GameObject("LightningLight");
            lightObject.transform.SetParent(transform, false);
            lightning = lightObject.AddComponent<Light>();
            lightning.type = LightType.Point;
            lightning.color = new Color(0.7f, 0.85f, 1f);
            lightning.range = radius * 2.2f;
            lightning.intensity = 0f;
            nextFlashAt = Time.time + 1f;
        }
    }

    private float Gaussian()
    {
        float u = Mathf.Max(1e-6f, Next()), v = Next();
        return Mathf.Sqrt(-2f * Mathf.Log(u)) * Mathf.Cos(2f * Mathf.PI * v);
    }

    private ParticleSystem MakeSystem(string name, int textureKind, int capacity)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1f;
        main.maxParticles = capacity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = textureKind == 1 ? 0.35f : 1e6f;
        main.scalingMode = ParticleSystemScalingMode.Shape;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        if (textureKind == 1)
        {
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
        }

        if (textureKind == 0 && GasAtlas != null)
        {
            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            sheet.cycleCount = 1;
        }

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = MaterialFor(textureKind);
        renderer.sortMode = ParticleSystemSortMode.Distance;
        if (textureKind == 2) renderer.sortingFudge = 60f; // Haze always draws behind the filaments.
        renderer.maxParticleSize = 100f; // Allow puffs to fill the screen when the camera flies through.
        renderer.minParticleSize = 0f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        system.Play();
        return system;
    }

    private void Emit(ParticleSystem system, Vector3 position, float size, Color color)
    {
        ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
        {
            position = position,
            startSize = size,
            startColor = color,
            rotation = Next() * 360f,
            angularVelocity = (Next() - 0.5f) * (storm ? 6f : 3f), // Slow swirl.
            velocity = Vector3.zero,
            applyShapeToPosition = false
        };
        system.Emit(emit, 1);
    }

    private void Update()
    {
        if (!storm || flashes == null) return;

        if (lightning != null) lightning.intensity = Time.time < flashUntil ? Mathf.Lerp(0f, 6f, (flashUntil - Time.time) / 0.25f) : 0f;
        if (Time.time < nextFlashAt) return;
        nextFlashAt = Time.time + Mathf.Lerp(0.6f, 2.4f, Next());

        // A crackle somewhere inside the storm.
        float distance = Next() * radius * 0.8f;
        float angle = Next() * Mathf.PI * 2f;
        Vector3 at = transform.position + new Vector3(Mathf.Cos(angle) * distance, Mathf.Lerp(-ShipAltitude.MaxAltitude * 0.7f, ShipAltitude.MaxAltitude * 0.7f, Next()), Mathf.Sin(angle) * distance);
        int bolts = 1 + random.Next(3);
        for (int i = 0; i < bolts; i++)
        {
            Emit(flashes, at + Random.insideUnitSphere * radius * 0.15f, Mathf.Lerp(radius * 0.2f, radius * 0.45f, Next()), new Color(0.85f, 0.95f, 1f, 0.9f));
        }
        if (lightning != null)
        {
            lightning.transform.position = at;
            flashUntil = Time.time + 0.25f;
        }
    }

    // ---------------- Textures ----------------

    // Filament gas, 2x2 strand variants (Resources/Battle/NebulaGas); falls back to a generated puff.
    private static Texture2D gasAtlas;
    private static bool gasLoaded;
    private static Texture2D GasAtlas
    {
        get
        {
            if (!gasLoaded) { gasAtlas = Resources.Load<Texture2D>("Battle/NebulaGas"); gasLoaded = true; }
            return gasAtlas;
        }
    }

    private static Material MaterialFor(int kind)
    {
        if (materials.TryGetValue(kind, out Material material) && material != null) return material;
        Shader shader = Shader.Find("Sprites/Default");
        Texture texture = kind == 0 && GasAtlas != null ? GasAtlas : TextureFor(kind);
        material = new Material(shader) { mainTexture = texture, renderQueue = 3000 };
        materials[kind] = material;
        return material;
    }

    // 0: a soft, lumpy cloud puff; 1: a jagged electric flash.
    private static Texture2D TextureFor(int kind)
    {
        if (textures.TryGetValue(kind, out Texture2D texture) && texture != null) return texture;

        texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = kind == 0 ? "NebulaPuff" : "IonFlash" };
        Color32[] pixels = new Color32[TextureSize * TextureSize];
        float seed = kind == 0 ? 11.3f : 47.9f;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float u = x / (float)(TextureSize - 1) * 2f - 1f;
                float v = y / (float)(TextureSize - 1) * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float alpha;
                if (kind != 1)
                {
                    float noise = Fractal(u * 2.2f + seed, v * 2.2f - seed, 5);
                    float falloff = Mathf.Clamp01(1f - r);
                    alpha = Mathf.Clamp01(falloff * falloff * (0.35f + noise * 1.1f));
                }
                else
                {
                    // Branching bright streaks fading from the centre.
                    float angle = Mathf.Atan2(v, u);
                    float streak = Mathf.Pow(Mathf.Abs(Mathf.Sin(angle * 3f + Fractal(r * 4f, angle, 3) * 3f)), 18f);
                    alpha = Mathf.Clamp01((streak * 0.9f + Mathf.Clamp01(1f - r * 3f)) * Mathf.Clamp01(1f - r));
                }
                pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        textures[kind] = texture;
        return texture;
    }

    private static float Fractal(float x, float y, int octaves)
    {
        float total = 0f, amplitude = 0.5f, frequency = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            total += Mathf.PerlinNoise(x * frequency + 100f, y * frequency + 100f) * amplitude;
            norm += amplitude;
            amplitude *= 0.5f;
            frequency *= 2.1f;
        }
        return total / norm;
    }
}
