using UnityEngine;

public class Laser : MonoBehaviour
{
    public int layerMask;

    public float damage;
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.layer == layerMask)
        {
            if(other.gameObject.TryGetComponent(out HardpointHealth health))
            {
                health.DealDamage(damage);
            }
            else
            {
                other.transform.root.gameObject.GetComponent<UnitHealthManager>().DealRandomDamage(damage);
            }
           
            Debug.Log("Hit target");
            Destroy(gameObject);
        }
        else
        {
           
            Debug.Log("Hit "+ other.gameObject.name + "Layer did not match. Found " + other.gameObject.layer + ", Expected " + layerMask);
            Debug.Log("Missed!");

            Destroy(gameObject);
        }
       
    }


    void Start()
    {
        Destroy(gameObject, 5);
    }
}
