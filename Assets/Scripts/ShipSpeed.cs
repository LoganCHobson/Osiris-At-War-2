using UnityEngine;
using UnityEngine.AI;

// Combines everything that changes how fast a ship flies (engine damage, asteroid fields) so they stack
// instead of overwriting each other's agent.speed.
[DisallowMultipleComponent]
public class ShipSpeed : MonoBehaviour
{
    private NavMeshAgent agent;
    private float baseSpeed;
    private float engineFactor = 1f;
    private float terrainFactor = 1f;

    public static ShipSpeed For(NavMeshAgent agent)
    {
        if (agent == null) return null;

        ShipSpeed speed = agent.GetComponent<ShipSpeed>();
        if (speed == null)
        {
            speed = agent.gameObject.AddComponent<ShipSpeed>();
            speed.agent = agent;
            speed.baseSpeed = agent.speed;
        }
        return speed;
    }

    public void SetEngineFactor(float factor)
    {
        engineFactor = factor;
        Apply();
    }

    public void SetTerrainFactor(float factor)
    {
        if (Mathf.Approximately(factor, terrainFactor)) return;
        terrainFactor = factor;
        Apply();
    }

    private void Apply()
    {
        if (agent != null) agent.speed = baseSpeed * engineFactor * terrainFactor;
    }
}
