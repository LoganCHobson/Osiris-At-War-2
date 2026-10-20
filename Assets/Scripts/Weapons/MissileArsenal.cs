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
        public int destroyerSalvo = 4;
        public int corvetteSalvo = 2;
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

        public int SalvoFor(ShipType type) =>
            type == ShipType.Destroyer ? destroyerSalvo : type == ShipType.Corvette ? corvetteSalvo : 0;
    }

    public Warhead conventional = new Warhead();

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
