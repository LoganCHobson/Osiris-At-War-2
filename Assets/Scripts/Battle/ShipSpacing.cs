using UnityEngine;
using UnityEngine.AI;

// Ship-to-ship avoidance that understands height, replacing the NavMeshAgent's built-in (flat) avoidance.
// Ships are treated as flat cylinders: two only interact when their hulls overlap in height, so a fleet
// flying 60 units up passes straight over one parked on the plane.
//   - Separation: overlapping hulls push apart; small ships give way to big ones.
//   - Look-ahead: a moving ship that's about to run into another sidesteps early instead of shoving.
// Movement goes through NavMeshAgent.Move, so nobody is ever pushed off the navmesh or into asteroids.
// Strike craft (airborne squadron anchors) are skipped - their fighters fly freely.
public class ShipSpacing : MonoBehaviour
{
    private const float Margin = 4f;
    private const float SeparationRate = 4f;     // Share of the overlap resolved per second.
    private const float LookAhead = 2.5f;         // Seconds.
    private const float SidestepStrength = 0.6f;  // Share of own speed spent sidestepping.
    private const float MovingSpeed = 1.5f;

    private static ShipSpacing instance;

    public static void Ensure()
    {
        if (instance == null) instance = new GameObject("ShipSpacing").AddComponent<ShipSpacing>();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        var ships = ShipAltitude.All;
        int count = ships.Count;

        for (int i = 0; i < count; i++)
        {
            ShipAltitude a = ships[i];
            if (!Usable(a)) continue;

            Vector3 pa = a.transform.position;
            Vector3 va = a.Agent.velocity;
            va.y = 0f;
            bool aMoving = va.sqrMagnitude > MovingSpeed * MovingSpeed;

            for (int j = i + 1; j < count; j++)
            {
                ShipAltitude b = ships[j];
                if (!Usable(b)) continue;
                if (Mathf.Abs(a.Altitude - b.Altitude) >= a.HalfHeight + b.HalfHeight) continue; // Different heights - pass over/under.

                Vector3 pb = b.transform.position;
                Vector3 offset = pb - pa;
                offset.y = 0f;
                float reach = a.Radius + b.Radius + Margin;
                float distanceSqr = offset.sqrMagnitude;

                // Separation.
                if (distanceSqr < reach * reach)
                {
                    float distance = Mathf.Sqrt(distanceSqr);
                    Vector3 away = distance > 0.01f ? offset / distance : Tiebreak(a, b);
                    float overlap = reach - distance;
                    float push = overlap * Mathf.Min(1f, SeparationRate * dt);

                    float massA = a.Radius * a.Radius;
                    float massB = b.Radius * b.Radius;
                    float shareA = massB / (massA + massB);
                    Nudge(a, -away * push * shareA);
                    Nudge(b, away * push * (1f - shareA));
                    continue;
                }

                // Look-ahead: both ships check each other.
                Vector3 vb = b.Agent.velocity;
                vb.y = 0f;
                if (aMoving) Sidestep(a, va, offset, vb, reach, dt);
                if (vb.sqrMagnitude > MovingSpeed * MovingSpeed) Sidestep(b, vb, -offset, va, reach, dt);
            }
        }
    }

    // `toOther` is the other ship's offset from this one; velocities are flat.
    private static void Sidestep(ShipAltitude self, Vector3 velocity, Vector3 toOther, Vector3 otherVelocity, float reach, float dt)
    {
        if (toOther.sqrMagnitude > (velocity.magnitude * LookAhead + reach) * (velocity.magnitude * LookAhead + reach)) return;

        Vector3 relative = velocity - otherVelocity;
        float speedSqr = relative.sqrMagnitude;
        if (speedSqr < 0.01f) return;

        float t = Mathf.Clamp(Vector3.Dot(toOther, relative) / speedSqr, 0f, LookAhead);
        if (t <= 0f) return;

        Vector3 closest = toOther - relative * t; // Where the other ship will be relative to us at closest approach.
        float miss = closest.magnitude;
        if (miss >= reach) return;

        Vector3 side = miss > 0.01f ? -closest / miss : Vector3.Cross(Vector3.up, velocity.normalized);
        float urgency = (1f - miss / reach) * (1f - t / LookAhead);
        Nudge(self, side * velocity.magnitude * SidestepStrength * urgency * dt);
    }

    private static Vector3 Tiebreak(ShipAltitude a, ShipAltitude b)
    {
        float angle = (a.GetInstanceID() * 0.618f + b.GetInstanceID() * 0.382f) % (Mathf.PI * 2f);
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    private static bool Usable(ShipAltitude ship)
    {
        if (ship == null || ship.Airborne) return false;
        NavMeshAgent agent = ship.Agent;
        return agent != null && agent.enabled && agent.updatePosition && agent.isOnNavMesh;
    }

    private static void Nudge(ShipAltitude ship, Vector3 delta)
    {
        if (delta.sqrMagnitude < 0.000001f) return;
        ship.Agent.Move(delta);
    }
}
