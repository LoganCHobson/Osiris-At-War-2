using SolarStudios;
using UnityEngine;

public class Laser : MonoBehaviour
{
    public float speed = 1.0f;
    public float damage = 1f;
    public float timeOut = 10f;
    public ParticleSystem particle;
    public LayerMask layer;

    private Vector3 lastPos;
    private float lifeTimer;
    private ObjectPool sourcePool;

    private void OnEnable()
    {
        lastPos = transform.position;
        lifeTimer = 0f;
    }

    public void SetSourcePool(ObjectPool pool)
    {
        sourcePool = pool;
    }

    void FixedUpdate()
    {
        Vector3 move = transform.forward * speed * Time.fixedDeltaTime;
        transform.position += move;

        CheckHit(lastPos, transform.position);
        lastPos = transform.position;

        lifeTimer += Time.fixedDeltaTime;
        if (lifeTimer >= timeOut)
        {
            Recycle();
        }
    }

    void CheckHit(Vector3 from, Vector3 to)
    {
        if (Physics.Linecast(from, to, out RaycastHit hit, layer))
        {
            if (particle != null)
            {
                ParticleSystem impact = Instantiate(particle, hit.point, particle.transform.rotation);
                impact.Play();
                Destroy(impact.gameObject, 2f);
            }

            HardpointHealth hp = hit.collider.GetComponent<HardpointHealth>();
            if (hp == null)
            {
                hp = hit.collider.GetComponentInChildren<HardpointHealth>();

                if (hp == null)
                {
                    hp = hit.collider.GetComponentInParent<HardpointHealth>();
                }
            }

            if (hp != null)
            {
                hp.DealDamage(damage);
            }

            Recycle();
        }
    }

    private void Recycle()
    {
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
