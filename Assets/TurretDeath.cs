using UnityEngine;
using UnityEngine.Events;

public class TurretDeath : MonoBehaviour
{
    public UnityEvent onDeath;

    public float destroyTimer;
    void Start()
    {
        onDeath.Invoke();

        Destroy(gameObject, destroyTimer);
    }

   
    void Update()
    {
        
    }
}
