using UnityEngine;

public class Laser : MonoBehaviour
{
    public float speed = 1.0f;
    public float damage = 1f;
    public float timeOut = 10f;
    public LayerMask layer;

    private Vector3 lastPos;

    void Start()
    {
        lastPos = transform.position;
        Destroy(gameObject, timeOut);
    }

    void FixedUpdate()
    {
        Vector3 move = transform.forward * speed * Time.fixedDeltaTime;
        transform.position += move;

        CheckHit(lastPos, transform.position);
        lastPos = transform.position;
    }

    void CheckHit(Vector3 from, Vector3 to)
    {
        if (Physics.Linecast(from, to, out RaycastHit hit, layer))
        {
            Debug.Log("Hit: " + hit.collider.gameObject.name);
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
                Destroy(gameObject); 
            }
        }

        Debug.DrawLine(from, to, Color.red, 1f);
    }
}
