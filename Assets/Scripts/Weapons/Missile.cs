using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// A missile or plasma torpedo in flight. It boosts clear of the launching ship, then homes on the hardpoint
// it was given (or another one on the same ship if that one dies). Conventional missiles ignore shield
// bubbles; torpedoes splash against them. Asteroids and wrecks stop both. Point-defence lasers aimed at it
// can shoot it down (see TurretController and Laser). Pooled, and drawn with the FTL glow material.
public class Missile : MonoBehaviour
{
    public static readonly List<Missile> Active = new List<Missile>();

    private const float BoostTime = 0.3f;
    private const float FuseRadius = 1.2f;

    // At most this many point-defence turrets can lock onto one missile, so salvos can saturate the defence.
    public const int MaxEngagers = 2;
    private const float EngageTimeout = 0.4f;

    // Point defence only engages missiles inside this share of its range - a last-ditch defence.
    public const float InterceptRangeFraction = 0.55f;

    private const string GlowMaterialPath = "FTL/FTLGlow";

    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    private static readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    private static readonly HitDistance hitOrder = new HitDistance();
    private static readonly Stack<Missile> pool = new Stack<Missile>();

    private static Transform poolRoot;
    private static Material glowMaterial;
    private static Texture2D glowTexture;
    private static Texture2D trailTexture;
    private static Mesh quad;
    private static Camera cam;

    private MissileArsenal.Warhead warhead;
    private int ownerLayer;
    private LayerMask hostileMask;
    private Transform target;
    private HardpointHealth targetHardpoint;
    private UnitHealthManager targetShip;
    private HardpointManager targetHardpoints;
    private Vector3 velocity;
    private Vector3 lastPos;
    private float age;
    private float lifetime;
    private float damageTaken;
    private float pulseSeed;
    private readonly Object[] engagers = new Object[MaxEngagers];
    private readonly float[] engagedAt = new float[MaxEngagers];

    private MeshRenderer head;
    private TrailRenderer trail;
    private MaterialPropertyBlock block;

    // Running totals for this battle (dev showcases read them).
    public static int Launched, Struck, ShotDown, Splashed, ShotDownByCIWS, Fizzled;

    public bool InFlight { get; private set; }
    public UnitHealthManager TargetShip => targetShip;
    public MissileArsenal.Warhead Warhead => warhead;
    public Vector3 Velocity => velocity;
    public float HitRadius => warhead != null ? warhead.hitRadius : 1f;

    // True if turrets aiming at mask (their enemies) should shoot this missile down.
    public bool IsHostileTo(LayerMask mask) => ((1 << ownerLayer) & mask) != 0;

    private class HitDistance : IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        Clear(default);
        SceneManager.sceneUnloaded -= Clear;
        SceneManager.sceneUnloaded += Clear;
    }

    private static void Clear(Scene scene)
    {
        Launched = Struck = ShotDown = Splashed = ShotDownByCIWS = Fizzled = 0;
        Active.Clear();
        pool.Clear();
        poolRoot = null;
    }

    // The nearest missile in flight within range that a turret aiming at hostileMask should (and still can) shoot at.
    public static Missile NearestHostile(Vector3 position, float range, LayerMask hostileMask, Object turret)
    {
        Missile nearest = null;
        float best = range * range;
        foreach (Missile missile in Active)
        {
            if (!missile.IsHostileTo(hostileMask) || !missile.CanEngage(turret)) continue;
            float distance = (missile.transform.position - position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = missile;
            }
        }
        return nearest;
    }

    public static Missile Fire(MissileArsenal.Warhead warhead, Vector3 position, Vector3 direction, UnitHealthManager targetShip, int ownerLayer, LayerMask hostileMask, float lifetime)
    {
        if (warhead == null || targetShip == null || !Ready()) return null;

        Missile missile = null;
        while (pool.Count > 0 && missile == null) missile = pool.Pop();
        if (missile == null) missile = Create();

        missile.Arm(warhead, position, direction, targetShip, ownerLayer, hostileMask, lifetime);
        return missile;
    }

    private bool CanEngage(Object turret)
    {
        for (int i = 0; i < MaxEngagers; i++)
        {
            if (engagers[i] == turret || engagers[i] == null || Time.time - engagedAt[i] > EngageTimeout) return true;
        }
        return false;
    }

    // Called every frame a turret stays locked on. Returns false if enough other turrets already have it.
    public bool Engage(Object turret)
    {
        int free = -1;
        for (int i = 0; i < MaxEngagers; i++)
        {
            if (engagers[i] == turret)
            {
                engagedAt[i] = Time.time;
                return true;
            }
            if (free < 0 && (engagers[i] == null || Time.time - engagedAt[i] > EngageTimeout)) free = i;
        }
        if (free < 0) return false;
        engagers[free] = turret;
        engagedAt[free] = Time.time;
        return true;
    }

    // Point-defence damage (a CIWS round, a laser bolt, a beam's burn). Returns true if that brought it down.
    public bool TakeHit(float damage, bool ciws)
    {
        if (!InFlight) return false;
        damageTaken += damage;
        if (damageTaken < warhead.interceptHealth) return false;

        ShotDown++;
        if (ciws) ShotDownByCIWS++;
        Explode(transform.position, 0.22f);
        Sfx.Play(Sfx.Cue.ExplosionSmall, transform.position, 0.45f, 1.35f);
        Recycle();
        return true;
    }

    private void Arm(MissileArsenal.Warhead armed, Vector3 position, Vector3 direction, UnitHealthManager ship, int owner, LayerMask hostile, float life)
    {
        warhead = armed;
        ownerLayer = owner;
        hostileMask = hostile;
        targetShip = ship;
        targetHardpoints = ship.GetComponent<HardpointManager>();
        SetTarget(targetHardpoints != null ? targetHardpoints.GetRandomHardpoint() : ship.transform);

        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.up;
        velocity = direction.normalized * warhead.speed * 0.35f;
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(velocity));
        lastPos = position;
        age = 0f;
        lifetime = life;
        damageTaken = 0f;
        System.Array.Clear(engagers, 0, MaxEngagers);
        pulseSeed = Random.value * 10f;

        head.transform.localScale = Vector3.one * warhead.headSize;
        trail.widthMultiplier = warhead.trailWidth;
        trail.time = warhead.trailTime;
        Glow(head, glowTexture, warhead.intensity);
        Glow(trail, trailTexture, warhead.intensity * 0.5f);

        gameObject.SetActive(true);
        trail.Clear();
        trail.emitting = true;

        InFlight = true;
        Active.Add(this);
        Launched++;
        Sfx.Play(warhead.passesThroughShields ? Sfx.Cue.MissileLaunch : Sfx.Cue.TorpedoLaunch, position);
    }

    private void SetTarget(Transform aim)
    {
        target = aim;
        targetHardpoint = aim != null ? aim.GetComponent<HardpointHealth>() : null;
    }

    private void FixedUpdate()
    {
        if (!InFlight) return;

        if (GameManager.CombatOver)
        {
            Recycle();
            return;
        }

        float dt = Time.fixedDeltaTime;
        age += dt;
        if (age >= lifetime)
        {
            Fizzled++;
            Explode(transform.position, 0.25f); // Out of fuel.
            Recycle();
            return;
        }

        KeepTarget();

        float speed = Mathf.MoveTowards(velocity.magnitude, warhead.speed, warhead.speed * 1.6f * dt);
        Vector3 heading = velocity.normalized;
        if (age > BoostTime && target != null)
        {
            Vector3 toTarget = target.position - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                heading = Vector3.RotateTowards(heading, toTarget.normalized, warhead.turnRate * Mathf.Deg2Rad * dt, 0f);
            }
        }

        velocity = heading * speed;
        Vector3 next = transform.position + velocity * dt;
        transform.SetPositionAndRotation(next, Quaternion.LookRotation(heading));

        if (CheckHit(lastPos, next)) return;
        lastPos = next;

        // Proximity fuse, for hardpoints with nothing solid around them to fly into.
        if (target != null && age > BoostTime && (target.position - next).sqrMagnitude < FuseRadius * FuseRadius)
        {
            Strike(null, next);
        }
    }

    private void LateUpdate()
    {
        if (!InFlight) return;
        if (cam == null) cam = Camera.main;
        if (cam != null) head.transform.rotation = cam.transform.rotation; // Billboard.

        if (warhead.pulse > 0f)
        {
            float throb = 1f + warhead.pulse * Mathf.Sin((age + pulseSeed) * 28f);
            head.transform.localScale = Vector3.one * warhead.headSize * throb;
        }
    }

    // If the hardpoint it was chasing is destroyed, pick another on the same ship; if the ship is gone,
    // keep flying straight until it hits something or runs out of fuel.
    private void KeepTarget()
    {
        bool hardpointLost = target == null || !target.gameObject.activeInHierarchy
            || (targetHardpoint != null && targetHardpoints != null && !targetHardpoints.hardpoints.Contains(targetHardpoint));
        if (!hardpointLost) return;

        bool shipAlive = targetShip != null && !targetShip.IsDead && targetHardpoints != null && targetHardpoints.hardpoints.Count > 0;
        SetTarget(shipAlive ? targetHardpoints.GetRandomHardpoint() : null);
    }

    private bool CheckHit(Vector3 from, Vector3 to)
    {
        Vector3 segment = to - from;
        float length = segment.magnitude;
        if (length <= 0f) return false;

        int mask = hostileMask | WreckChunk.BlockingMask | BattleTerrain.BlockingMask;
        if (!warhead.passesThroughShields) mask |= ShieldGenerator.Mask;

        int count = Physics.RaycastNonAlloc(from, segment / length, hitBuffer, length, mask);
        if (count == 0) return false;
        System.Array.Sort(hitBuffer, 0, count, hitOrder);

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];
            int layer = hit.collider.gameObject.layer;

            if (layer == ShieldGenerator.Layer)
            {
                ShieldGenerator shield = hit.collider.GetComponentInParent<ShieldGenerator>();
                if (shield == null || ((1 << shield.gameObject.layer) & hostileMask) == 0) continue;
                if (!shield.Absorb(warhead.damage * warhead.shieldMultiplier, hit.point)) continue; // Shield's down: on to the hull.

                Splashed++;
                Explode(hit.point, 1f);
                Sfx.Play(Sfx.Cue.HitShield, hit.point, 1f, 0.7f);
                Recycle();
                return true;
            }

            if (layer == WreckChunk.WreckLayer || layer == BattleTerrain.AsteroidLayer)
            {
                Explode(hit.point, 0.8f);
                Recycle();
                return true;
            }

            Strike(hit.collider, hit.point);
            return true;
        }
        return false;
    }

    private void Strike(Collider hitCollider, Vector3 point)
    {
        float damage = warhead.damage * warhead.hullMultiplier;

        HardpointHealth hardpoint = null;
        UnitHealthManager ship = targetShip;
        if (hitCollider != null)
        {
            if (!hitCollider.TryGetComponent(out hardpoint)) hardpoint = hitCollider.GetComponentInParent<HardpointHealth>();
            ship = hitCollider.GetComponentInParent<UnitHealthManager>();
        }

        // Guided: a hull hit on the ship it was chasing lands on the hardpoint it was aiming for.
        bool aimedHardpointAlive = targetHardpoint != null && targetHardpoints != null && targetHardpoints.hardpoints.Contains(targetHardpoint);
        if (hardpoint == null && ship == targetShip && aimedHardpointAlive) hardpoint = targetHardpoint;

        Struck++;
        if (hardpoint != null) hardpoint.DealDamage(damage);
        else if (ship != null && !ship.IsDead) ship.DealRandomDamage(damage);

        Explode(point, 1f);
        Sfx.Play(Sfx.Cue.ExplosionSmall, point, warhead.passesThroughShields ? 0.8f : 1f, warhead.passesThroughShields ? 1.1f : 0.85f);
        Recycle();
    }

    private void Explode(Vector3 point, float scale)
    {
        if (warhead.explosion == null) return;
        EffectPool.Play("Missile_" + warhead.name, warhead.explosion, point, Quaternion.identity, 2.5f, Vector3.one * warhead.explosionScale * scale);
    }

    private void Recycle()
    {
        if (!InFlight) return;
        InFlight = false;
        Active.Remove(this);
        trail.emitting = false;
        gameObject.SetActive(false);
        pool.Push(this);
    }

    private void OnDestroy()
    {
        Active.Remove(this);
    }

    // ---------------- Visuals ----------------

    private static bool Ready()
    {
        if (glowMaterial != null) return true;

        glowMaterial = Resources.Load<Material>(GlowMaterialPath);
        if (glowMaterial == null)
        {
            Debug.LogWarning($"Missile: missing Resources/{GlowMaterialPath} material - missiles can't be drawn, so none are fired.");
            return false;
        }

        glowTexture = MakeTexture(64, 64, (x, y) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y)), 2.2f));
        trailTexture = MakeTexture(4, 64, (x, y) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(y)), 2f));
        quad = BuildQuad();
        return true;
    }

    private static Missile Create()
    {
        if (poolRoot == null) poolRoot = new GameObject("Missiles").transform;

        GameObject body = new GameObject("Missile") { layer = 2 }; // Ignore Raycast: never clickable.
        body.SetActive(false);
        body.transform.SetParent(poolRoot, false);
        Missile missile = body.AddComponent<Missile>();
        missile.block = new MaterialPropertyBlock();

        GameObject glow = new GameObject("Glow") { layer = 2 };
        glow.transform.SetParent(body.transform, false);
        glow.AddComponent<MeshFilter>().sharedMesh = quad;
        missile.head = glow.AddComponent<MeshRenderer>();
        missile.head.sharedMaterial = glowMaterial;
        missile.head.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        missile.head.receiveShadows = false;

        TrailRenderer trail = body.AddComponent<TrailRenderer>();
        trail.sharedMaterial = glowMaterial;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.minVertexDistance = 0.6f;
        trail.numCapVertices = 2;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
        Gradient fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = fade;
        missile.trail = trail;

        return missile;
    }

    private void Glow(Renderer target, Texture texture, float intensity)
    {
        target.GetPropertyBlock(block);
        block.SetTexture(MainTexID, texture);
        block.SetColor(ColorID, warhead.color);
        block.SetFloat(IntensityID, intensity);
        target.SetPropertyBlock(block);
    }

    private static Mesh BuildQuad()
    {
        Mesh mesh = new Mesh { name = "MissileQuad" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
        return mesh;
    }

    private static Texture2D MakeTexture(int width, int height, System.Func<float, float, float> alphaAt)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "MissileGlow"
        };

        Color32[] pixels = new Color32[width * height];
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                float x = (px + 0.5f) / width * 2f - 1f;
                float y = (py + 0.5f) / height * 2f - 1f;
                pixels[py * width + px] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alphaAt(x, y)) * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
