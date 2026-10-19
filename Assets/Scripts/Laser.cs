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

    private void OnEnable()
    {
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

    public void Launch(Vector3 launchVelocity)
    {
        velocity = launchVelocity;
        if (velocity.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(velocity);
        }
    }

    void FixedUpdate()
    {
        if (spent) return;

        if (GameManager.CombatOver)
        {
            Recycle();
            return;
        }

        transform.position += velocity * Time.fixedDeltaTime;

        if (CheckIntercept(lastPos, transform.position)) return;
        if (CheckHit(lastPos, transform.position)) return;
        lastPos = transform.position;

        lifeTimer += Time.fixedDeltaTime;
        if (lifeTimer >= timeOut)
        {
            Recycle();
        }
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

        int count = Physics.RaycastNonAlloc(from, segment / length, hitBuffer, length, layer | WreckChunk.BlockingMask | BattleTerrain.BlockingMask | ShieldGenerator.Mask);
        if (count == 0) return false;
        System.Array.Sort(hitBuffer, 0, count, hitOrder);

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = hitBuffer[i];
            if (candidate.collider.gameObject.layer != ShieldGenerator.Layer) return Impact(candidate);

            ShieldGenerator shield = candidate.collider.GetComponentInParent<ShieldGenerator>();
            if (shield == null || ((1 << shield.gameObject.layer) & layer) == 0) continue;
            if (shield.Absorb(damage, candidate.point))
            {
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
            Recycle();
            return true;
        }

        if (hitCollider.TryGetComponent(out HardpointHealth hp)
            || (hp = hitCollider.GetComponentInChildren<HardpointHealth>()) != null
            || (hp = hitCollider.GetComponentInParent<HardpointHealth>()) != null)
        {
            if (craft != null && fromWarship) Fighter.TakeShipHit(hp);
            else hp.DealDamage(damage);
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
