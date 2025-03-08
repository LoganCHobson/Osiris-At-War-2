using UnityEngine;

public class Laser : MonoBehaviour
{
    public LayerMask layerMask;
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.layer == layerMask)
        {
            Debug.Log("Hit target");
            Destroy(gameObject);
        }
        Debug.Log("Missed!");

        Destroy(gameObject);
    }


    void Start()
    {
        Destroy(gameObject, 5);
    }
}
