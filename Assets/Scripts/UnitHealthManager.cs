using System.Collections.Generic;
using UnityEngine;

public class UnitHealthManager : MonoBehaviour
{
    public float maxHealth;
    public float currentHealth;

    public List<HardpointHealth> hardpoints = new List<HardpointHealth>();


    private void Start()
    {
        foreach (Transform child in transform)
        {
            if (child.TryGetComponent(out HardpointHealth health))
            {
                hardpoints.Add(health);
            }
        }
        if(hardpoints.Count > 0)
        {
            foreach (HardpointHealth health in hardpoints)
            {
                maxHealth += health.maxHealth; //Our maximum health is represented by all of the hardpoints.
            }
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
    }

    public void HardpointDeath(HardpointHealth hardpoint)
    {
        hardpoints.Remove(hardpoint);
        Destroy(hardpoint.gameObject);
    }

    public void DealRandomDamage(float damage) //Just to ensure the game doesn't go on FOREVER.
    {
        if (hardpoints.Count > 0)
        {
            int rand = Random.Range(0, hardpoints.Count);

            hardpoints[rand].DealDamage(damage);
        }
    }

    private void Update()
    {
        if (hardpoints.Count <= 0)
        {
            Destroy(gameObject);
        }
    }
}
