using UnityEngine;

// Tuning for the two missile types (Resources/Weapons/MissileArsenal). Conventional missiles fly straight
// through shield bubbles and hit the hull; plasma torpedoes tear shields apart but do less to bare hulls.
// Destroyers and corvettes carry them (see MissileLauncher).
[CreateAssetMenu(menuName = "Osiris/Missile Arsenal")]
public class MissileArsenal : ScriptableObject
{
    private const string ResourcePath = "Weapons/MissileArsenal";

    [System.Serializable]
    public class Warhead
    {
        public string name = "Missile";

        [Header("Launcher")]
        public int destroyerSalvo = 6;
        public int corvetteSalvo = 3;
        [Tooltip("Seconds between salvos.")]
        public float reload = 15f;
        [Tooltip("Seconds between missiles in one salvo.")]
        public float salvoSpacing = 0.15f;

        [Header("Flight")]
        public float speed = 42f;
        [Tooltip("Degrees per second the missile can turn while homing.")]
        public float turnRate = 120f;

        [Header("Damage")]
        public float damage = 9f;
        [Tooltip("Flies through shield bubbles and hits the hull underneath.")]
        public bool passesThroughShields = true;
        public float shieldMultiplier = 1f;
        public float hullMultiplier = 1f;

        [Header("Point defence")]
        [Tooltip("Point-defence damage needed to shoot it down (a corvette's point defence deals ~12 a second, a destroyer's ~8).")]
        public float interceptHealth = 2f;
        public float hitRadius = 1f;

        [Header("Look")]
        public Color color = new Color(1f, 0.6f, 0.25f);
        public float headSize = 1.6f;
        public float trailWidth = 0.6f;
        public float trailTime = 1.2f;
        public float intensity = 4f;
        [Tooltip("How much the glow throbs (0 = steady).")]
        [Range(0f, 1f)] public float pulse;
        public ParticleSystem explosion;
        public float explosionScale = 1f;

        public Warhead Copy(string newName)
        {
            Warhead copy = (Warhead)MemberwiseClone();
            copy.name = newName;
            return copy;
        }

        public int SalvoFor(ShipType type) =>
            type == ShipType.Destroyer ? destroyerSalvo : type == ShipType.Corvette ? corvetteSalvo : 0;
    }

    public Warhead conventional = new Warhead();

    // Strike craft ordnance, derived from the conventional missile so it shares its explosion and look.
    // Mini-missiles: fighters' and interceptors' - small, fast, very agile, a light punch.
    // Bombs: bombers' - slow, barely guided, dull, and heavy; released in a run's final approach.
    private Warhead miniMissile, bomb, avantiMiniMissile, avantiBomb;

    // Human craft: orange mini-missiles, red bombs. Avanti craft: blue mini-missiles, purple bombs.
    // Each strike-craft weapon explodes in its own colour: Human mini-missiles orange (the conventional
    // missile's own blast), Human bombs red, Avanti mini-missiles blue, Avanti bombs purple.
    public Warhead MiniMissileFor(bool avanti) => avanti
        ? avantiMiniMissile ??= Recolour(MakeMini(), "Avanti ", new Color(0.3f, 0.6f, 1f), plasma, new Color(0.35f, 0.6f, 1f))
        : miniMissile ??= MakeMini();
    public Warhead BombFor(bool avanti) => avanti
        ? avantiBomb ??= Recolour(MakeBomb(), "Avanti ", new Color(0.7f, 0.25f, 1f), plasma, new Color(0.75f, 0.3f, 1f))
        : bomb ??= Recolour(MakeBomb(), "", new Color(1f, 0.32f, 0.12f), conventional, new Color(1f, 0.22f, 0.12f));

    private static Warhead Recolour(Warhead w, string prefix, Color glow, Warhead blastSource, Color blast)
    {
        w.name = prefix + w.name; // Explosions are pooled by warhead name: keep each colour apart.
        w.color = glow;
        if (blastSource != null && blastSource.explosion != null) w.explosion = TintedExplosion(blastSource.explosion, blast);
        return w;
    }

    // A copy of an explosion effect recoloured to `tint` (kept hidden; EffectPool spawns from it). Brightness
    // and fade are kept - only the hue changes.
    private static ParticleSystem TintedExplosion(ParticleSystem template, Color tint)
    {
        ParticleSystem copy = Instantiate(template);
        copy.gameObject.SetActive(false);
        copy.name = template.name + "_" + ColorUtility.ToHtmlStringRGB(tint);
        DontDestroyOnLoad(copy.gameObject);
        foreach (ParticleSystem system in copy.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = system.main;
            main.startColor = Retint(main.startColor, tint);
            ParticleSystem.ColorOverLifetimeModule over = system.colorOverLifetime;
            if (over.enabled) over.color = Retint(over.color, tint);

            // The effect pack bakes its colour into the materials too (the fire explosion's HDR orange base
            // colour, the energy explosion's blue emission), and that wins over the particle colours. Recolour
            // a copy of each material the same way.
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) continue;
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                Material m = new Material(materials[i]) { name = materials[i].name + "_" + ColorUtility.ToHtmlStringRGB(tint) };
                foreach (string property in new[] { "_BaseColor", "_Color", "_EmissionColor", "_TintColor" })
                    if (m.HasProperty(property)) m.SetColor(property, Retint(m.GetColor(property), tint));
                materials[i] = m;
            }
            renderer.sharedMaterials = materials;
            if (renderer.trailMaterial != null)
            {
                Material trail = new Material(renderer.trailMaterial);
                foreach (string property in new[] { "_BaseColor", "_Color", "_EmissionColor", "_TintColor" })
                    if (trail.HasProperty(property)) trail.SetColor(property, Retint(trail.GetColor(property), tint));
                renderer.trailMaterial = trail;
            }
        }
        foreach (Light light in copy.GetComponentsInChildren<Light>(true)) light.color = Retint(light.color, tint);
        return copy;
    }

    private static ParticleSystem.MinMaxGradient Retint(ParticleSystem.MinMaxGradient source, Color tint)
    {
        switch (source.mode)
        {
            case ParticleSystemGradientMode.Color: return Retint(source.color, tint);
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Retint(source.colorMin, tint), Retint(source.colorMax, tint));
            case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(Retint(source.gradient, tint));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Retint(source.gradientMin, tint), Retint(source.gradientMax, tint));
            default: return source;
        }
    }

    // Keep the colour's brightness (and alpha), take the tint's hue.
    private static Color Retint(Color c, Color tint)
    {
        float brightness = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return new Color(tint.r * brightness, tint.g * brightness, tint.b * brightness, c.a);
    }

    private static Gradient Retint(Gradient g, Color tint)
    {
        if (g == null) return null;
        GradientColorKey[] keys = g.colorKeys;
        for (int i = 0; i < keys.Length; i++) keys[i].color = Retint(keys[i].color, tint);
        Gradient result = new Gradient { mode = g.mode };
        result.SetKeys(keys, g.alphaKeys);
        return result;
    }

    private Warhead MakeMini()
    {
        Warhead w = conventional.Copy("Mini-missile");
        w.speed = conventional.speed * 1.5f;
        w.turnRate = conventional.turnRate * 2.2f;
        w.damage = conventional.damage * 0.3f;
        w.interceptHealth = conventional.interceptHealth * 0.5f;
        w.headSize = conventional.headSize * 0.45f;
        w.trailWidth = conventional.trailWidth * 0.35f;
        w.trailTime = conventional.trailTime * 0.6f;
        w.explosionScale = conventional.explosionScale * 0.45f;
        w.hitRadius = conventional.hitRadius * 1.3f;
        return w;
    }

    private Warhead MakeBomb()
    {
        Warhead w = conventional.Copy("Bomb");
        w.speed = conventional.speed * 0.55f;
        w.turnRate = 14f;
        w.damage = conventional.damage * 1.6f;
        w.interceptHealth = conventional.interceptHealth * 1.5f;
        w.color = new Color(1f, 0.32f, 0.12f);
        w.headSize = conventional.headSize * 0.8f;
        w.trailWidth = conventional.trailWidth * 0.25f;
        w.trailTime = conventional.trailTime * 0.35f;
        w.intensity = conventional.intensity * 0.45f;
        w.pulse = 0.6f;
        w.explosionScale = conventional.explosionScale * 1.5f;
        return w;
    }

    public Warhead plasma = new Warhead
    {
        name = "Plasma Torpedo",
        destroyerSalvo = 3,
        corvetteSalvo = 2,
        reload = 15f,
        salvoSpacing = 0.25f,
        speed = 30f,
        turnRate = 70f,
        damage = 13f,
        passesThroughShields = false,
        shieldMultiplier = 1.75f,
        hullMultiplier = 0.6f,
        interceptHealth = 5f,
        hitRadius = 1.6f,
        color = new Color(0.45f, 0.65f, 1f),
        headSize = 3f,
        trailWidth = 1.4f,
        trailTime = 0.5f,
        intensity = 5f,
        pulse = 0.35f,
        explosionScale = 1.4f,
    };

    [Tooltip("Missile range as a multiple of the ship's gun range.")]
    public float rangeMultiplier = 1.1f;

    private static MissileArsenal instance;

    public static MissileArsenal Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<MissileArsenal>(ResourcePath);
            if (instance == null) instance = CreateInstance<MissileArsenal>();
            return instance;
        }
    }
}
