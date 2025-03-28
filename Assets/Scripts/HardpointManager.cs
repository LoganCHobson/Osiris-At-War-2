using System.Collections.Generic;
using UnityEngine;

public class HardpointManager : MonoBehaviour
{
    public List<HardpointHealth> hardpoints = new List<HardpointHealth>();

    void Start()
    {
        GetHardpoints(transform);
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void GetHardpoints(Transform parent)
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

    public Transform GetRandomHardpoint()
    {
        if (hardpoints.Count > 0)
        {
            int rand = Random.Range(0, hardpoints.Count);
            return hardpoints[rand].gameObject.transform;
        }
        return null;
    }

  

    public void AssignTarget(Transform target)
    {
        foreach (HardpointHealth health in hardpoints)
        {
            if(health.gameObject.TryGetComponent(out TurretController turret))
            {
                turret.target = target;
            }
            else
            {
                continue;
            }
        }
    }

    public List<T> GetSpecificHardpoints<T>() where T : Component //This is neat. It will allow us to pass in whatever hardpoint we want and return out the ones it finds.
    {
        List<T> specificHardpoints = new List<T>();

        foreach (HardpointHealth hardpoint in hardpoints)
        {
            T component = hardpoint.gameObject.GetComponent<T>();
            if (component != null)
            {
                specificHardpoints.Add(component);
            }
        }

        return specificHardpoints;
    }

    public void ToggleHighlight(bool value)
    {
        foreach (HardpointHealth hardpointHealth in hardpoints)
        {
            hardpointHealth.ToggleVisual(value);
        }
    }


}
