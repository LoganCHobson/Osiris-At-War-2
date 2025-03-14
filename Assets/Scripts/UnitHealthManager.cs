using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class UnitHealthManager : MonoBehaviour
{
    public float maxHealth;
    public float currentHealth;

    public List<HardpointHealth> hardpoints = new List<HardpointHealth>();

    public UnityEvent onDie;

    private bool dead;

    private void Start()
    {
        GetHardpoints(transform);
        if (hardpoints.Count > 0)
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
        SubtractHealth(hardpoint.currentHealth, hardpoint);
        hardpoints.Remove(hardpoint);
        //Destroy(hardpoint.gameObject);
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
        if (hardpoints.Count <= 0 && !dead)
        {
            dead = true;
            onDie.Invoke();
            Destroy(gameObject, 5);
        }
    }

    void GetHardpoints(Transform parent)
    {
        foreach (Transform child in parent)
        {
            if (child.TryGetComponent(out HardpointHealth health))
            {
                hardpoints.Add(health);
            }
            GetHardpoints(child);
        }
    }
}
