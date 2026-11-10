using SolarStudios;
using UnityEngine;

public class Laser : MonoBehaviour
{
    public float speed = 1.0f;
    public float damage = 1f;
    public float hullDamageMultiplier = 0.35f;
    public float timeOut = 10f;
    public ParticleSystem particle;
    public float particleLifetime = 2f;
    public LayerMask layer;
    [System.NonSerialized] public Missile intercept; // Point defence: the missile this shot was aimed at.
    [System.NonSerialized] public bool fromWarship; // Light warship guns kill fighters in Fighter.ShipHitsToKill hits.

    private const float CraftHitEffectScale = 0.3f;

    // Where shots end up (dev stats): on a hardpoint's own collider, on bare hull, on a shield bubble, on a
    // wreck/rock/fighter, or nowhere (flew out of range).
    public static int StatHardpoint, StatHullFoundHardpointBelow, StatHull, StatShield, StatOther, StatExpired;

    // Dev diagnosis of misses: what each shot was fired at and how close it came.
    [System.NonSerialized] public Transform intended;
    [System.NonSerialized] public bool intendedIsCraft;
    [System.NonSerialized] public float fireDistance, targetSpeed, shooterSpeed;
    private float closestSqr = float.MaxValue;
    public class MissStats { public int count; public double closest, fireDistance, boltSpeed, targetSpeed, shooterSpeed; public int near, mid, wild, noTarget; }
    public static readonly MissStats MissShip = new MissStats(), MissCraft = new MissStats();
    public static double SpeedSum; public static int SpeedCount; public static float SpeedMax;

    public static void ResetStats()
    {
        StatHardpoint = StatHullFoundHardpointBelow = StatHull = StatShield = StatOther = StatExpired = 0;
        foreach (MissStats m in new[] { MissShip, MissCraft }) { m.count = m.near = m.mid = m.wild = m.noTarget = 0; m.closest = m.fireDistance = m.boltSpeed = m.targetSpeed = m.shooterSpeed = 0; }
        SpeedSum = 0; SpeedCount = 0; SpeedMax = 0;
    }

    private void RecordMiss()
    {
        MissStats m = intendedIsCraft ? MissCraft : MissShip;
        m.count++;
        if (intended == null || closestSqr == float.MaxValue) { m.noTarget++; return; }
        float closest = Mathf.Sqrt(closestSqr);
        m.closest += closest;
        m.fireDistance += fireDistance;
        m.boltSpeed += velocity.magnitude;
        m.targetSpeed += targetSpeed;
        m.shooterSpeed += shooterSpeed;
        if (closest < 3f) m.near++; else if (closest < 10f) m.mid++; else m.wild++;
    }

    private Vector3 lastPos;
    private Vector3 velocity;
    private float lifeTimer;
    private bool spent;
    private ObjectPool sourcePool;
    private string effectKey;

    private void Awake()
    {
        effectKey = gameObject.name.Replace("(Clone)", "").Trim();
    }

    private void OnDisable()
    {
        LaserSystem.Unregister(this);
    }

    // How LaserSystem draws this bolt: an energy streak in the bolt's colour, as thick as its model was. The
    // model itself (a stretched sphere) is switched off.
    public Color BoltColor { get; private set; } = new Color(1f, 0.2f, 0.1f);
    public float BoltWidth { get; private set; } = 0.06f;
    public Vector3 Velocity => velocity;
    public bool Spent => spent;
    private bool lookTaken;

    private void TakeLook()
    {
        if (lookTaken) return;
        lookTaken = true;
        MeshRenderer bolt = GetComponent<MeshRenderer>();
        if (bolt == null) return;
        Material material = bolt.sharedMaterial;
        if (material != null)
        {
            if (material.HasProperty("_BaseColor")) BoltColor = material.GetColor("_BaseColor");
            else if (material.HasProperty("_Color")) BoltColor = material.color;
        }
        Vector3 scale = transform.lossyScale;
        BoltWidth = Mathf.Max(0.02f, Mathf.Min(scale.x, scale.y));
        bolt.enabled = false;
    }

    private void OnEnable()
    {
        TakeLook();
        LaserSystem.Register(this); // Flown and hit-tested in one batch with every other bolt (see LaserSystem).
        intended = null;
        closestSqr = float.MaxValue;
        lastPos = transform.position;
        velocity = transform.forward * speed;
        lifeTimer = 0f;
        spent = false;
        intercept = null;
        fromWarship = false;
    }

    public void SetSourcePool(ObjectPool pool)
    {
        sourcePool = pool;
    }

    // Who fired it, for DamageLedger (dev balance runs).
    [System.NonSerialized] public UnitHealthManager source;
    [System.NonSerialized] public DamageLedger.Weapon weapon = DamageLedger.Weapon.Battery;

    public void Launch(Vector3 launchVelocity)
    {
        velocity = launchVelocity;
        float s = launchVelocity.magnitude;
        SpeedSum += s; SpeedCount++; SpeedMax = Mathf.Max(SpeedMax, s);
        if (velocity.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(velocity);
        }
    }

    public int HitMask => layer | WreckChunk.BlockingMask | BattleTerrain.BlockingMask | ShieldGenerator.Mask;

    // LaserSystem, step 1: move this frame's distance. `from`/`to` are the segment to hit-test.
    public void Advance(float dt, out Vector3 from, out Vector3 to)
    {
        from = lastPos;
        if (spent)
        {
            to = from;
            return;
        }
        if (GameManager.CombatOver)
        {
            to = from;
            Recycle();
            return;
        }
        transform.position += velocity * dt;
        to = transform.position;
    }

    // LaserSystem, step 2: act on the batched raycast's hits for this bolt.
    public void Resolve(Vector3 from, Vector3 to, Unity.Collections.NativeArray<RaycastHit> results, int start, int max, float dt)
    {
        if (spent) return;
        if (CheckIntercept(from, to)) return;

        int count = 0;
        for (int i = 0; i < max && count < hitBuffer.Length; i++)
        {
            RaycastHit hit = results[start + i];
            if (hit.collider == null) break;
            hitBuffer[count++] = hit;
        }
        if (count > 0 && ResolveHits(count)) return;

        lastPos = to;
        lifeTimer += dt;
        if (intended != null && intended.gameObject.activeInHierarchy)
        {
            Vector3 seg = to - from;
            Vector3 p = intended.position;
            float t = seg.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - from, seg) / seg.sqrMagnitude) : 0f;
            closestSqr = Mathf.Min(closestSqr, (from + seg * t - p).sqrMagnitude);
        }
        if (lifeTimer >= timeOut) { StatExpired++; RecordMiss(); Recycle(); }
    }

    private static readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    private static readonly HitDistance hitOrder = new HitDistance();

    private class HitDistance : System.Collections.Generic.IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    bool CheckHit(Vector3 from, Vector3 to)
    {
        Vector3 segment = to - from;
        float length = segment.magnitude;
        if (length <= 0f) return false;

        int count = Physics.RaycastNonAlloc(from, segment / length, hitBuffer, length, HitMask);
        if (count == 0) return false;
        return ResolveHits(count);
    }

    // The nearest thing hit along the segment wins; friendly shield bubbles are passed through.
    private bool ResolveHits(int count)
    {
        System.Array.Sort(hitBuffer, 0, count, hitOrder);

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = hitBuffer[i];
            if (candidate.collider.gameObject.layer != ShieldGenerator.Layer) return Impact(candidate);

            ShieldGenerator shield = candidate.collider.GetComponentInParent<ShieldGenerator>();
            if (shield == null || ((1 << shield.gameObject.layer) & layer) == 0) continue;
            if (shield.Absorb(damage, candidate.point))
            {
                StatShield++;
                Sfx.Play(Sfx.Cue.HitShield, candidate.point);
                Recycle();
                return true;
            }
        }
        return false;
    }

    // Point-defence shots can knock down the missile they were fired at.
    bool CheckIntercept(Vector3 from, Vector3 to)
    {
        if (intercept == null) return false;
        if (!intercept.InFlight)
        {
            intercept = null;
            return false;
        }

        Vector3 missile = intercept.transform.position;
        Vector3 segment = to - from;
        float along = segment.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(missile - from, segment) / segment.sqrMagnitude) : 0f;
        Vector3 closest = from + segment * along;
        if ((missile - closest).sqrMagnitude > intercept.HitRadius * intercept.HitRadius) return false;

        if (particle != null)
        {
            EffectPool.Play(effectKey + "_small", particle, closest, particle.transform.rotation, particleLifetime, particle.transform.localScale * CraftHitEffectScale);
        }
        intercept.TakeHit(damage, false);
        Recycle();
        return true;
    }

    bool Impact(RaycastHit hit)
    {
        Collider hitCollider = hit.collider;
        Fighter craft = hitCollider.GetComponentInParent<Fighter>();

        if (particle != null)
        {
            if (craft != null) EffectPool.Play(effectKey + "_small", particle, hit.point, particle.transform.rotation, particleLifetime, particle.transform.localScale * CraftHitEffectScale);
            else EffectPool.Play(effectKey, particle, hit.point, particle.transform.rotation, particleLifetime);
        }
        int hitLayer = hitCollider.gameObject.layer;
        if (hitLayer == WreckChunk.WreckLayer || hitLayer == BattleTerrain.AsteroidLayer) // Wrecks and rocks soak the shot.
        {
            StatOther++;
            Recycle();
            return true;
        }

        if (craft == null) HullScarring.Impact(hitCollider, hit.point, velocity, damage); // Sustained fire scars the hull.

        bool ownHardpoint = hitCollider.TryGetComponent(out HardpointHealth hp) || (hp = hitCollider.GetComponentInParent<HardpointHealth>()) != null;
        bool belowHardpoint = !ownHardpoint && (hp = hitCollider.GetComponentInChildren<HardpointHealth>()) != null;
        if (craft != null) StatOther++;
        else if (ownHardpoint) StatHardpoint++;
        else if (belowHardpoint) StatHullFoundHardpointBelow++;
        else StatHull++;
        if (ownHardpoint || belowHardpoint)
        {
            if (craft != null && fromWarship) Fighter.TakeShipHit(hp);
            else hp.DealDamage(damage);
            if (craft == null) Sfx.Play(Sfx.Cue.HitHull, hit.point);
        }
        else
        {
            UnitHealthManager healthManager = hitCollider.GetComponentInParent<UnitHealthManager>();
            healthManager?.DealRandomDamage(damage * hullDamageMultiplier);
        }

        Recycle();
        return true;
    }

    private void Recycle()
    {
        spent = true;

        if (sourcePool != null)
        {
            sourcePool.Recycle(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
