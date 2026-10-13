using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(UnitHealthManager))]
public class HullDamage : MonoBehaviour
{
    public static readonly List<HullDamage> Active = new List<HullDamage>();

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public DestructionProfile profile;
    public Transform gfxRoot;

    private UnitHealthManager health;
    private readonly List<MeshFilter> surfaces = new List<MeshFilter>();
    private readonly List<MeshRenderer> lodZero = new List<MeshRenderer>();
    private readonly List<Renderer> scorchable = new List<Renderer>();
    private readonly List<Renderer> lights = new List<Renderer>();
    private readonly List<GameObject> damageEffects = new List<GameObject>();
    private MaterialPropertyBlock block;

    private Transform effectsRoot;
    private Vector3 lastHitPoint;
    private float lastHitAt = float.NegativeInfinity;
    private float nextSecondaryAt;
    private float appliedScorch = -1f;
    private bool lightsOn = true;
    private bool lightsForcedOff;
    private float nextFlickerAt;
    private float flickerOverride;

    public float Size { get; private set; }
    public float SizeT { get; private set; }
    public float SizeScale { get; private set; }
    public Bounds LocalBounds { get; private set; }
    public Transform EffectsRoot => effectsRoot;
    public IReadOnlyList<MeshFilter> Surfaces => surfaces;
    public IReadOnlyList<MeshRenderer> LodZero => lodZero;
    public int DamageEffectCount => damageEffects.Count;
    public bool IsAlive => health != null && !health.IsDead;
    public float HealthFraction => health != null && health.maxHealth > 0f ? Mathf.Clamp01(health.currentHealth / health.maxHealth) : 1f;

    private void Awake()
    {
        health = GetComponent<UnitHealthManager>();
        block = new MaterialPropertyBlock();
        if (gfxRoot == null) gfxRoot = transform.Find("GFX");

        effectsRoot = new GameObject("DamageFX").transform;
        effectsRoot.SetParent(transform, false);
    }

    private void OnEnable()
    {
        Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    private void Start()
    {
        CollectRenderers();
        MeasureHull();

        foreach (HardpointHealth hardpoint in GetComponentsInChildren<HardpointHealth>(true))
        {
            HardpointHealth captured = hardpoint;
            hardpoint.dealDamage.AddListener(() => RegisterHit(captured.transform.position));
            hardpoint.die.AddListener(() => RegisterHit(captured.transform.position));
        }

        nextSecondaryAt = Time.time + Random.Range(1f, 3f);
    }

    private void CollectRenderers()
    {
        if (gfxRoot == null) return;

        LODGroup group = gfxRoot.GetComponentInChildren<LODGroup>(true);
        if (group != null && group.lodCount > 0)
        {
            foreach (Renderer renderer in group.GetLODs()[0].renderers)
            {
                if (renderer is MeshRenderer meshRenderer) lodZero.Add(meshRenderer);
            }
        }
        else
        {
            lodZero.AddRange(gfxRoot.GetComponentsInChildren<MeshRenderer>(true));
        }

        foreach (MeshRenderer renderer in lodZero)
        {
            if (IsLight(renderer)) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) surfaces.Add(filter);
        }

        foreach (Renderer renderer in gfxRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer)) continue;
            if (IsLight(renderer)) lights.Add(renderer);
            else scorchable.Add(renderer);
        }
    }

    private void MeasureHull()
    {
        bool any = false;
        Bounds bounds = new Bounds();

        foreach (MeshFilter filter in surfaces)
        {
            Bounds mesh = filter.sharedMesh.bounds;
            Vector3 min = mesh.min;
            Vector3 max = mesh.max;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                Vector3 local = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!any)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        if (!any) bounds = new Bounds(Vector3.zero, Vector3.one * 10f);

        LocalBounds = bounds;
        Vector3 worldSize = Vector3.Scale(bounds.size, transform.lossyScale);
        Size = Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z));

        if (profile != null)
        {
            SizeT = profile.SizeT(Size);
            SizeScale = profile.SizeScale(Size);
        }
    }

    [ContextMenu("Take Hit (15%)")]
    private void TakeHit()
    {
        if (health != null) health.DealRandomDamage(health.maxHealth * 0.15f);
    }

    public static bool IsLight(Renderer renderer)
    {
        return renderer.name.IndexOf("light", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void RegisterHit(Vector3 point)
    {
        lastHitPoint = point;
        lastHitAt = Time.time;
    }

    private void Update()
    {
        if (profile == null || health == null) return;

        if (health.IsDead)
        {
            UpdateLights(0f);
            return;
        }

        float healthFraction = HealthFraction;

        UpdateDamageEffects(healthFraction);
        UpdateSecondaryExplosions(healthFraction);
        UpdateScorch(healthFraction);
        UpdateLights(healthFraction);
    }

    private void UpdateDamageEffects(float healthFraction)
    {
        float progress = Mathf.InverseLerp(profile.firstDamageEffectAt, 0.05f, healthFraction);
        if (healthFraction > profile.firstDamageEffectAt) progress = 0f;

        int budget = Mathf.RoundToInt(Mathf.Lerp(profile.effectsOnSmallShip, profile.effectsOnLargeShip, SizeT));
        int wanted = Mathf.CeilToInt(progress * budget);

        if (damageEffects.Count < wanted)
        {
            Vector3 point = PickDamagePoint(out _);
            SpawnDamageEffect(point);
            Explode(profile.mediumExplosion, point, profile.mediumExplosionScale);
            DestructionFx.Blast(profile.smallBlastClips, point, profile, Mathf.Lerp(1.1f, 0.8f, SizeT));
        }
    }

    private void UpdateSecondaryExplosions(float healthFraction)
    {
        if (healthFraction > profile.secondaryExplosionsBelow || Time.time < nextSecondaryAt) return;

        float severity = Mathf.InverseLerp(profile.secondaryExplosionsBelow, 0f, healthFraction);
        float interval = Mathf.Lerp(profile.secondaryExplosionInterval.x, profile.secondaryExplosionInterval.y, severity);
        nextSecondaryAt = Time.time + interval * Random.Range(0.6f, 1.4f);

        Vector3 point = PickDamagePoint(out _);
        Explode(profile.smallExplosion, point, profile.smallExplosionScale);
        DestructionFx.Blast(profile.smallBlastClips, point, profile, Mathf.Lerp(1.2f, 0.9f, SizeT));
        flickerOverride = Time.time + 0.25f;
    }

    private void UpdateScorch(float healthFraction)
    {
        float brightness = Mathf.Lerp(profile.scorchedBrightness, 1f, healthFraction);
        if (Mathf.Abs(brightness - appliedScorch) < 0.02f) return;

        appliedScorch = brightness;
        foreach (Renderer renderer in scorchable)
        {
            ApplyScorch(renderer, brightness);
        }
    }

    private void ApplyScorch(Renderer renderer, float brightness)
    {
        if (renderer == null) return;

        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null) continue;

            int property = material.HasProperty(BaseColorId) ? BaseColorId : material.HasProperty(ColorId) ? ColorId : -1;
            if (property < 0) continue;

            Color original = material.GetColor(property);
            Color scorched = new Color(original.r * brightness, original.g * brightness, original.b * brightness, original.a);

            renderer.GetPropertyBlock(block, i);
            block.SetColor(property, scorched);
            renderer.SetPropertyBlock(block, i);
        }
    }

    private void UpdateLights(float healthFraction)
    {
        if (lights.Count == 0) return;

        bool shouldFlicker = lightsForcedOff || healthFraction < profile.lightsFlickerBelow || Time.time < flickerOverride;

        if (lightsForcedOff)
        {
            SetLights(false);
            return;
        }

        if (!shouldFlicker)
        {
            SetLights(true);
            return;
        }

        if (Time.time < nextFlickerAt) return;

        float severity = health.IsDead ? 1f : Mathf.InverseLerp(profile.lightsFlickerBelow, 0f, healthFraction);
        bool on = Random.value > Mathf.Lerp(0.15f, 0.65f, severity);
        SetLights(on);
        nextFlickerAt = Time.time + Random.Range(0.03f, on ? Mathf.Lerp(1.2f, 0.3f, severity) : 0.25f);
    }

    private void SetLights(bool on)
    {
        if (lightsOn == on) return;
        lightsOn = on;
        foreach (Renderer renderer in lights)
        {
            if (renderer != null) renderer.enabled = on;
        }
    }

    public void KillLights()
    {
        lightsForcedOff = true;
        SetLights(false);
    }

    public Vector3 PickDamagePoint(out Vector3 normal)
    {
        HullSurface.Sample(surfaces, out Vector3 best, out normal);

        if (Time.time - lastHitAt > profile.hitMemory) return best;

        float bestDistance = (best - lastHitPoint).sqrMagnitude;
        for (int i = 0; i < 5; i++)
        {
            HullSurface.Sample(surfaces, out Vector3 candidate, out Vector3 candidateNormal);
            float distance = (candidate - lastHitPoint).sqrMagnitude;
            if (distance < bestDistance)
            {
                best = candidate;
                normal = candidateNormal;
                bestDistance = distance;
            }
        }
        return best;
    }

    public Vector3 RandomSurfacePoint(out Vector3 normal)
    {
        HullSurface.Sample(surfaces, out Vector3 point, out normal);
        return point;
    }

    public GameObject SpawnDamageEffect(Vector3 point)
    {
        ParticleSystem fire = profile.hullFires != null && profile.hullFires.Length > 0 ? profile.hullFires[Random.Range(0, profile.hullFires.Length)] : null;
        GameObject effect = DestructionFx.Attach(fire, effectsRoot, point, profile.fireScale * SizeScale * Random.Range(0.75f, 1.25f));

        if (effect != null) damageEffects.Add(effect);
        return effect;
    }

    public void Explode(ParticleSystem template, Vector3 point, float scale)
    {
        DestructionFx.Explode(template, point, scale * SizeScale * Random.Range(0.8f, 1.2f), profile.explosionLifetime);
    }

    public void CopyScorch(Renderer source, Renderer target)
    {
        if (source == null || target == null) return;

        int count = Mathf.Min(source.sharedMaterials.Length, target.sharedMaterials.Length);
        for (int i = 0; i < count; i++)
        {
            source.GetPropertyBlock(block, i);
            target.SetPropertyBlock(block, i);
        }
    }
}
