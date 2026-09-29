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

        transform.position += velocity * Time.fixedDeltaTime;

        if (CheckHit(lastPos, transform.position)) return;
        lastPos = transform.position;

        lifeTimer += Time.fixedDeltaTime;
        if (lifeTimer >= timeOut)
        {
            Recycle();
        }
    }

    bool CheckHit(Vector3 from, Vector3 to)
    {
        if (!Physics.Linecast(from, to, out RaycastHit hit, layer)) return false;

        if (particle != null)
        {
            EffectPool.Play(effectKey, particle, hit.point, particle.transform.rotation, particleLifetime);
        }

        Collider hitCollider = hit.collider;
        if (hitCollider.TryGetComponent(out HardpointHealth hp)
            || (hp = hitCollider.GetComponentInChildren<HardpointHealth>()) != null
            || (hp = hitCollider.GetComponentInParent<HardpointHealth>()) != null)
        {
            hp.DealDamage(damage);
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
