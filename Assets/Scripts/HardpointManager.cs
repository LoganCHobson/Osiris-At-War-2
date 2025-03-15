using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class HardpointManager : MonoBehaviour
{
    public List<HardpointHealth> hardpoints = new List<HardpointHealth>();
    
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
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
