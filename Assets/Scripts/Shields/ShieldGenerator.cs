using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(UnitHealthManager))]
public class ShieldGenerator : MonoBehaviour
{
    private const int MaxHits = 8;

    private static readonly int HitsId = Shader.PropertyToID("_Hits");
    private static readonly int HitDataId = Shader.PropertyToID("_HitData");
    private static readonly int BubbleScaleId = Shader.PropertyToID("_BubbleScale");
    private static readonly int ShieldTimeId = Shader.PropertyToID("_ShieldTime");
    private static readonly int FractionId = Shader.PropertyToID("_Fraction");

    private static int shieldLayer = -2;
    private static Mesh sphereMesh;

    public static int Layer
    {
        get
        {
            if (shieldLayer == -2) shieldLayer = LayerMask.NameToLayer("Shield");
            return shieldLayer;
        }
    }

    public static int Mask => Layer >= 0 ? 1 << Layer : 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        shieldLayer = -2;
    }

    [Header("Capacity")]
    [Range(0f, 2f)] public float capacityFraction = 0.32f;
    [Range(0.3f, 1.5f)] public float hullHealthMultiplier = 0.8f;

    [Header("Recharge")]
    public float rechargeDelay = 5f;
    [Range(0f, 1f)] public float rechargeRate = 0.06f;
    public float collapseCooldown = 15f;
    [Range(0f, 1f)] public float restoreFraction = 0.3f;

    [Header("Visuals")]
    public Material shieldMaterial;
    public float padding = 1.18f;
    [Range(0.05f, 1f)] public float minimumThickness = 0.4f;
    public float rippleLife = 1.1f;
    public Vector2 rippleReach = new Vector2(0.06f, 0.13f);
    public float minimumReach = 2f;
    [Range(0.1f, 0.5f)] public float maximumReachFraction = 0.28f;
    public float minimumStrength = 0.85f;

    [Header("Interface")]
    public Slider shieldSlider;

    public float Capacity { get; private set; }
    public float Current { get; private set; }
    public bool IsUp => built && !collapsed && !GeneratorDestroyed && Current > 0f && !health.IsDead;

    // The shield is projected by a generator hardpoint on the ship's spine. Knock it out and the shield is
    // gone for good: no recharge, no Shield Surge.
    public HardpointHealth Generator { get; private set; }
    public bool GeneratorDestroyed { get; private set; }

    public static HardpointHealth LiveGenerator(GameObject ship)
    {
        return ship != null && ship.TryGetComponent(out ShieldGenerator shield) && !shield.GeneratorDestroyed ? shield.Generator : null;
    }

    private ShipAbility surgeAbility;
    private float SurgeFactor
    {
        get
        {
            if (surgeAbility == null) surgeAbility = GetComponent<ShipAbility>();
            return surgeAbility != null ? surgeAbility.ShieldRegenMultiplier : 1f;
        }
    }

    // Shield Surge: brings a collapsed shield straight back and tops it up by this share of capacity.
    public void Surge(float fraction)
    {
        if (!built || health == null || health.IsDead || GeneratorDestroyed) return;
        if (collapsed)
        {
            collapsed = false;
            bubbleCollider.enabled = true;
        }
        Current = Mathf.Min(Capacity, Current + Capacity * fraction);
        RefreshSlider();
    }
    public float Fraction => Capacity > 0f ? Current / Capacity : 0f;

    private UnitHealthManager health;
    private HullDamage hull;
    private Transform bubble;
    private MeshRenderer bubbleRenderer;
    private MeshCollider bubbleCollider;
    private MaterialPropertyBlock block;
    private readonly Vector4[] hits = new Vector4[MaxHits];
    private readonly Vector4[] hitData = new Vector4[MaxHits];
    private int nextHit;
    private float lastHitAt = float.NegativeInfinity;
    private float lastRippleAt = float.NegativeInfinity;
    private float collapsedUntil;
    private bool collapsed;
    private bool built;
    private float size = 10f;

    private void Awake()
    {
        health = GetComponent<UnitHealthManager>();
        hull = GetComponent<HullDamage>();
        block = new MaterialPropertyBlock();

        float total = 0f;
        int count = 0;
        foreach (HardpointHealth hardpoint in GetComponentsInChildren<HardpointHealth>(true))
        {
            hardpoint.maxHealth *= hullHealthMultiplier;
            total += hardpoint.maxHealth;
            count++;
        }
        BuildGenerator(count > 0 ? total / count : 100f);
    }

    // A hardpoint on top of the hull, sharing the look (health marker, layer) of the ship's other hardpoints.
    private void BuildGenerator(float hitPoints)
    {
        HardpointHealth template = null;
        foreach (HardpointHealth hardpoint in GetComponentsInChildren<HardpointHealth>(true))
        {
            if (template == null || (template.healthVisual == null && hardpoint.healthVisual != null)) template = hardpoint;
        }

        Bounds local = HullBounds();
        GameObject node = new GameObject("ShieldGenerator");
        node.layer = template != null ? template.gameObject.layer : gameObject.layer;
        node.transform.SetParent(transform, false);
        node.transform.localPosition = new Vector3(local.center.x, local.max.y * 0.9f + local.center.y * 0.1f, local.center.z - local.extents.z * 0.15f);

        float reach = Mathf.Max(local.size.x, local.size.z);
        SphereCollider collider = node.AddComponent<SphereCollider>();
        collider.radius = Mathf.Max(0.5f, reach * 0.06f);
        if (template != null && template.TryGetComponent(out Collider source)) collider.isTrigger = source.isTrigger;

        HardpointHealth generator = node.AddComponent<HardpointHealth>();
        generator.maxHealth = hitPoints;
        generator.currentHealth = hitPoints;
        generator.die = new UnityEngine.Events.UnityEvent();
        generator.dealDamage = new UnityEngine.Events.UnityEvent();
        generator.die.AddListener(OnGeneratorDestroyed);

        // Copy the template's health marker so the generator can be seen and clicked like the others.
        if (template != null && template.healthVisual != null)
        {
            Canvas canvas = template.healthVisual.GetComponentInParent<Canvas>(true);
            if (canvas != null)
            {
                Image[] originals = canvas.GetComponentsInChildren<Image>(true);
                int index = System.Array.IndexOf(originals, template.healthVisual);
                GameObject copy = Instantiate(canvas.gameObject, node.transform);
                copy.transform.localPosition = canvas.transform.localPosition;
                Image[] copies = copy.GetComponentsInChildren<Image>(true);
                if (index >= 0 && index < copies.Length) generator.healthVisual = copies[index];
            }
        }

        Generator = generator;
        if (TryGetComponent(out HardpointManager manager) && !manager.hardpoints.Contains(generator)) manager.hardpoints.Add(generator);
    }

    private Bounds HullBounds()
    {
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.GetComponentInParent<Canvas>() != null) continue;
            Bounds world = renderer.bounds;
            Vector3 min = transform.InverseTransformPoint(world.min), max = transform.InverseTransformPoint(world.max);
            Bounds part = new Bounds((min + max) * 0.5f, new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), Mathf.Abs(max.z - min.z)));
            if (!any) { bounds = part; any = true; }
            else bounds.Encapsulate(part);
        }
        return any ? bounds : new Bounds(Vector3.zero, Vector3.one * 10f);
    }

    private void OnGeneratorDestroyed()
    {
        if (GeneratorDestroyed) return;
        GeneratorDestroyed = true;
        Vector3 point = Generator != null ? Generator.transform.position : transform.position;
        if (built && Current > 0f) AddRipple(point, 1.6f, Mathf.Max(size * 0.25f, minimumReach * 1.5f)); // One last flare as it fails.
        Current = 0f;
        collapsed = true;
        collapsedUntil = float.PositiveInfinity;
        if (bubbleCollider != null) bubbleCollider.enabled = false;
        RefreshSlider();
    }

    private IEnumerator Start()
    {
        yield return null;
        Build();
    }

    private void Build()
    {
        Capacity = Mathf.Max(1f, health.maxHealth * capacityFraction);
        Current = Capacity;

        Bounds bounds = hull != null ? hull.LocalBounds : new Bounds(Vector3.zero, Vector3.one * 10f);
        Vector3 extent = bounds.size;
        float longest = Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z));
        extent = new Vector3(Mathf.Max(extent.x, longest * minimumThickness), Mathf.Max(extent.y, longest * minimumThickness), Mathf.Max(extent.z, longest * minimumThickness));

        GameObject shell = new GameObject("Shield");
        if (Layer >= 0) shell.layer = Layer;
        bubble = shell.transform;
        bubble.SetParent(transform, false);
        bubble.localPosition = bounds.center;
        bubble.localRotation = Quaternion.identity;
        bubble.localScale = extent * padding;

        Mesh mesh = SphereMesh();
        shell.AddComponent<MeshFilter>().sharedMesh = mesh;
        bubbleRenderer = shell.AddComponent<MeshRenderer>();
        bubbleRenderer.sharedMaterial = shieldMaterial;
        bubbleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bubbleRenderer.receiveShadows = false;
        bubbleRenderer.enabled = false;

        bubbleCollider = shell.AddComponent<MeshCollider>();
        bubbleCollider.sharedMesh = mesh;
        bubbleCollider.convex = true;

        Vector3 worldSize = Vector3.Scale(extent * padding, transform.lossyScale);
        size = Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z));

        built = true;
        RefreshSlider();
    }

    private static Mesh SphereMesh()
    {
        if (sphereMesh != null) return sphereMesh;
        GameObject temporary = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereMesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temporary);
        return sphereMesh;
    }

    public bool Absorb(float damage, Vector3 point)
    {
        if (!IsUp) return false;

        DamageLedger.Shield(health, Mathf.Min(damage, Current));
        Current -= damage;
        lastHitAt = Time.time;

        float strength = Mathf.Clamp(0.55f + damage / Mathf.Max(1f, Capacity * 0.03f), 0.55f, 1.6f);
        AddRipple(point, strength, Mathf.Lerp(rippleReach.x, rippleReach.y, Mathf.InverseLerp(0.55f, 1.6f, strength)) * size);

        if (Current <= 0f) Collapse(point);
        RefreshSlider();
        return true;
    }

    public void Drain(float damage)
    {
        if (!IsUp) return;
        Current -= damage;
        lastHitAt = Time.time;
        if (Current <= 0f) Collapse(bubble.position);
        RefreshSlider();
    }

    private void Collapse(Vector3 point)
    {
        Current = 0f;
        collapsed = true;
        collapsedUntil = Time.time + collapseCooldown;
        if (bubbleCollider != null) bubbleCollider.enabled = false;
        AddRipple(point, 1.4f, Mathf.Max(size * 0.18f, minimumReach * 1.2f));
    }

    private void AddRipple(Vector3 point, float strength, float reach)
    {
        if (bubble == null) return;

        reach = Mathf.Min(Mathf.Max(reach, minimumReach), size * maximumReachFraction);
        strength = Mathf.Max(strength, minimumStrength);

        Vector3 local = bubble.InverseTransformPoint(point);
        if (local.sqrMagnitude > 0.0001f) local = local.normalized * 0.5f;

        hits[nextHit] = new Vector4(local.x, local.y, local.z, 0f);
        hitData[nextHit] = new Vector4(Time.time, strength, reach, 0f);
        nextHit = (nextHit + 1) % MaxHits;
        lastRippleAt = Time.time;
    }

    private void Update()
    {
        if (!built) return;

        if (health.IsDead)
        {
            if (bubbleCollider.enabled) bubbleCollider.enabled = false;
            if (bubbleRenderer.enabled && Time.time - lastRippleAt > rippleLife) bubbleRenderer.enabled = false;
            Render();
            return;
        }

        if (BattleHazards.InNebula(transform.position))
        {
            // Shields can't hold inside a nebula.
            if (Current > 0f)
            {
                Current = Mathf.Max(0f, Current - Capacity * BattleHazards.NebulaDrainPerSecond * Time.deltaTime);
                RefreshSlider();
            }
            Render();
            return;
        }

        if (GeneratorDestroyed)
        {
            Render();
            return;
        }

        if (collapsed)
        {
            if (Time.time >= collapsedUntil)
            {
                collapsed = false;
                Current = Capacity * restoreFraction;
                bubbleCollider.enabled = true;
                RefreshSlider();
            }
        }
        else if (Current < Capacity && Time.time - lastHitAt >= rechargeDelay)
        {
            Current = Mathf.Min(Capacity, Current + Capacity * rechargeRate * (1f + HeroForces.BuffFor(this, HeroBuff.ShieldRegen)) * SurgeFactor * Time.deltaTime);
            RefreshSlider();
        }

        Render();
    }

    private void Render()
    {
        bool visible = Time.time - lastRippleAt <= rippleLife;
        if (bubbleRenderer.enabled != visible) bubbleRenderer.enabled = visible;
        if (!visible) return;

        block.SetVectorArray(HitsId, hits);
        block.SetVectorArray(HitDataId, hitData);
        block.SetVector(BubbleScaleId, bubble.lossyScale);
        block.SetFloat(ShieldTimeId, Time.time);
        block.SetFloat(FractionId, Fraction);
        bubbleRenderer.SetPropertyBlock(block);
    }

    private void RefreshSlider()
    {
        if (shieldSlider == null) return;
        shieldSlider.maxValue = Capacity;
        shieldSlider.value = Current;
    }
}
