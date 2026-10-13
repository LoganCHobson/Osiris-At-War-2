using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(menuName = "Osiris/Destruction Profile", fileName = "DestructionProfile")]
public class DestructionProfile : ScriptableObject
{
    [Header("Ship Size")]
    public float smallShipSize = 10f;
    public float largeShipSize = 70f;
    public float referenceSize = 27f;
    [Range(0.2f, 1.2f)] public float sizeScaleExponent = 0.7f;

    [Header("Explosions")]
    public ParticleSystem smallExplosion;
    public ParticleSystem mediumExplosion;
    public ParticleSystem[] breakupExplosions;
    public float smallExplosionScale = 2.5f;
    public float mediumExplosionScale = 5f;
    public float breakupExplosionScale = 13f;
    public float explosionLifetime = 6f;

    [Header("Persistent Effects")]
    public ParticleSystem[] hullFires;
    public ParticleSystem breachVent;
    public float fireScale = 2.5f;
    public float breachVentScale = 4f;

    [Header("Hull Damage")]
    public int effectsOnSmallShip = 2;
    public int effectsOnLargeShip = 9;
    [Range(0f, 1f)] public float firstDamageEffectAt = 0.85f;
    [Range(0f, 1f)] public float secondaryExplosionsBelow = 0.6f;
    public Vector2 secondaryExplosionInterval = new Vector2(7f, 1.2f);
    [Range(0f, 1f)] public float scorchedBrightness = 0.5f;
    [Range(0f, 1f)] public float lightsFlickerBelow = 0.4f;
    public float hitMemory = 8f;

    [Header("Death Throes")]
    public Vector2 dyingDuration = new Vector2(1.6f, 3.8f);
    public Vector2 chainInterval = new Vector2(0.45f, 0.08f);
    public float driftSpeedFactor = 0.5f;
    public float shudderStrength = 0.006f;
    [Range(0f, 1f)] public float chainFireChance = 0.3f;

    [Header("Death Throes Motion")]
    public float throeIntensity = 1f;
    public float throeAngularAcceleration = 16f;
    public float throeMaxAngularSpeed = 38f;
    public Vector2 sinkAcceleration = new Vector2(0.4f, 2.5f);
    public float maxSinkSpeed = 6f;
    [Range(0f, 1f)] public float joltChance = 0.35f;
    public float joltStrength = 14f;

    [Header("Breakup")]
    public Material interiorMaterial;
    public float[] pieceWeightsSmall = { 0.3f, 0.5f, 0.2f, 0f };
    public float[] pieceWeightsLarge = { 0.15f, 0.35f, 0.35f, 0.15f };
    [Range(0f, 45f)] public float cutTilt = 25f;
    [Range(0.05f, 1f)] public float largePartFraction = 0.35f;
    [Range(0f, 1f)] public float looseDebrisChance = 0.45f;
    public Vector2Int plateShards = new Vector2Int(3, 8);
    public Vector2 plateShardRadius = new Vector2(0.04f, 0.08f);
    public float separationSpeed = 2.5f;
    public float debrisSpeed = 9f;
    public float chunkSpin = 10f;
    public Vector2 debrisSpin = new Vector2(60f, 260f);
    public float wreckLifetime = 12f;
    public Vector2 debrisLifetime = new Vector2(4f, 7f);
    public float wreckFadeDuration = 2.5f;
    public Vector2 wreckExplosionInterval = new Vector2(0.6f, 2.5f);

    [Header("Persistent Wrecks")]
    public bool persistentWrecks = true;
    public Vector2 burnDuration = new Vector2(35f, 75f);
    public Vector2Int wreckFires = new Vector2Int(1, 3);
    public int maxPersistentChunks = 60;
    public float wreckDrag = 0.08f;
    public float wreckSpinDrag = 0.06f;
    public float residualSpin = 1.5f;
    public bool wrecksBlockLasers = true;
    public float jostleStrength = 3f;

    [Header("Flash")]
    public Color flashColor = new Color(1f, 0.7f, 0.4f);
    public float flashIntensity = 8f;
    public float flashDuration = 0.6f;
    public float flashRange = 2.5f;

    [Header("Camera Shake")]
    public float breakupShake = 1f;
    public float chainShake = 0.15f;

    [Header("Audio")]
    public AudioClip[] smallBlastClips;
    public AudioClip[] largeBlastClips;
    public AudioMixerGroup mixer;
    [Range(0f, 1f)] public float volume = 0.6f;
    public float audibleRange = 400f;

    public float SizeT(float size)
    {
        return Mathf.InverseLerp(smallShipSize, largeShipSize, size);
    }

    public int PickPieceCount(float sizeT)
    {
        int options = Mathf.Max(pieceWeightsSmall.Length, pieceWeightsLarge.Length);
        if (options == 0) return 2;

        float total = 0f;
        float[] weights = new float[options];
        for (int i = 0; i < options; i++)
        {
            float small = i < pieceWeightsSmall.Length ? pieceWeightsSmall[i] : 0f;
            float large = i < pieceWeightsLarge.Length ? pieceWeightsLarge[i] : 0f;
            weights[i] = Mathf.Max(0f, Mathf.Lerp(small, large, sizeT));
            total += weights[i];
        }
        if (total <= 0f) return 2;

        float roll = Random.value * total;
        for (int i = 0; i < options; i++)
        {
            roll -= weights[i];
            if (roll <= 0f) return i + 1;
        }
        return options;
    }

    public float SizeScale(float size)
    {
        return Mathf.Pow(Mathf.Max(0.01f, size) / referenceSize, sizeScaleExponent);
    }
}
