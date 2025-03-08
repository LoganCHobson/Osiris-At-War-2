using UnityEngine;
using UnityEngine.Events;

public class HardpointHealth : MonoBehaviour
{
    public float maxHealth;
    public float currentHealth;

    private UnitHealthManager healthManager;

    public UnityEvent dealDamage; //May not actually need this.
    public UnityEvent die;
    private void Start()
    {
        currentHealth = maxHealth;
        healthManager = GetComponentInParent<UnitHealthManager>();
    }

    public void DealDamage(float value)
    {
        if(currentHealth - value > 0)
        {
            dealDamage.Invoke();
            healthManager.SubtractHealth(value, this); //Actual health subtraction in this script is done by the manager.
        }
        else
        {
            die.Invoke();
            healthManager.HardpointDeath(this);
        }
        
    }

}
