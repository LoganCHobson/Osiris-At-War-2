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
    private float groupCap = float.PositiveInfinity;

    // Full speed with current engine damage, ignoring asteroids and group caps.
    public float CruiseSpeed => baseSpeed * engineFactor;

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

    // Ships moving as a group hold to the slowest member's pace so fighters don't race ahead of battleships.
    public void SetGroupCap(float cap)
    {
        groupCap = cap > 0f ? cap : float.PositiveInfinity;
        Apply();
    }

    public void ClearGroupCap()
    {
        if (float.IsPositiveInfinity(groupCap)) return;
        groupCap = float.PositiveInfinity;
        Apply();
    }

    // Hero speed buff (HeroForces), refreshed by the battle's hero ticker.
    private float heroFactor = 1f;

    private float abilityFactor = 1f;

    public void SetAbilityFactor(float factor)
    {
        if (Mathf.Approximately(factor, abilityFactor)) return;
        abilityFactor = factor;
        Apply();
    }

    public void SetHeroFactor(float factor)
    {
        if (Mathf.Approximately(factor, heroFactor)) return;
        heroFactor = factor;
        Apply();
    }

    // Warships don't crab sideways: while the bow is still swinging onto the course, they ease off the
    // throttle (down to a fifth of full speed when heading the wrong way), then open up once lined up.
    private float headingFactor = 1f;
    public bool throttleWhileTurning;

    private void Update()
    {
        if (!throttleWhileTurning || agent == null || !agent.isOnNavMesh) return;
        float wanted = 1f;
        Vector3 desired = agent.desiredVelocity;
        desired.y = 0f;
        if (agent.hasPath && desired.sqrMagnitude > 0.01f)
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            float angle = Vector3.Angle(forward, desired);
            wanted = Mathf.Lerp(1f, 0.2f, Mathf.InverseLerp(20f, 90f, angle));
        }
        if (Mathf.Abs(wanted - headingFactor) < 0.05f && !(wanted == 1f && headingFactor != 1f)) return;
        headingFactor = wanted;
        Apply();
    }

    private void Apply()
    {
        if (agent != null) agent.speed = Mathf.Min(baseSpeed * engineFactor * terrainFactor * heroFactor * abilityFactor * headingFactor, groupCap);
    }
}
