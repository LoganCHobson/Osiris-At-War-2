using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UnitHealthManager : MonoBehaviour
{
    public float maxHealth;
    public float currentHealth;
    public Slider healthSlider;
    //private List<HardpointHealth> hardpoints = new List<HardpointHealth>();
    private HardpointManager hardpointManager;
    public UnityEvent onDie;

    private bool dead;

    private void Start()
    {
        hardpointManager = GetComponent<HardpointManager>();

        if (hardpointManager.hardpoints.Count > 0)
        {
            foreach (HardpointHealth health in hardpointManager.hardpoints)
            {
                maxHealth += health.maxHealth; //Our maximum health is represented by all of the hardpoints.
            }
            healthSlider.maxValue = maxHealth;
            healthSlider.value = maxHealth;
        }
        else
        {
            Debug.LogError("Could not find hardpoints");
        }


        currentHealth = maxHealth;
    }
    public void SubtractHealth(float value, HardpointHealth hardpoint)
    {
        hardpoint.currentHealth -= value;
        currentHealth -= value;
        healthSlider.value = currentHealth;
    }

    public void HardpointDeath(HardpointHealth hardpoint)
    {
        SubtractHealth(hardpoint.currentHealth, hardpoint);
        hardpointManager.hardpoints.Remove(hardpoint);
        //Destroy(hardpoint.gameObject);
    }

    public void DealRandomDamage(float damage) //Just to ensure the game doesn't go on FOREVER.
    {
        if (hardpointManager.hardpoints.Count > 0)
        {
            int rand = Random.Range(0, hardpointManager.hardpoints.Count);

            hardpointManager.hardpoints[rand].DealDamage(damage);
        }
    }

    private void Update()
    {
        if (hardpointManager.hardpoints.Count <= 0 && !dead)
        {
            dead = true;
            onDie.Invoke();
            Destroy(gameObject, 5);
        }

        if(healthSlider.gameObject.activeInHierarchy)
        {
            healthSlider.gameObject.transform.parent.LookAt(Camera.main.transform);
        }
    }

   

}
