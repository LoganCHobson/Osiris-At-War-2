using SolarStudios;
using UnityEngine;
using UnityEngine.AI;

// Turns a warship that isn't moving to face its target - the ordered one (a player or AI focus-fire order),
// or else the nearest enemy ship in reach - so its own hull blocks as few of its guns as possible. Avanti
// ships point the bow at it, like a tank; Human ships turn broadside on, whichever side needs less turning
// (presenting a bigger target doing it - see TurretController aspect accuracy). Guns still fire at anything
// they have a clear shot at. Fitted by GameManager.ApplyAllegiance.
public class CombatFacing : MonoBehaviour
{
    public enum Style { Bow, Broadside }

    private const float PickInterval = 3f;      // A ship holds its chosen target; it doesn't swing to whatever is nearest.
    private const float ReachMultiplier = 1.4f; // Start turning for ships a little beyond gun range.
    private const float Tolerance = 2f;         // Turn until this close...
    private const float CapitalSlack = 25f;     // ...but only start turning once this far off (big ships)...
    private const float EscortSlack = 12f;      // ...or this far off (destroyers, corvettes).

    private bool turning;

    public Style style;

    private LayerMask hostileMask;
    private NavMeshAgent agent;
    private SpaceUnit unit;
    private HardpointManager hardpoints;
    private PlayerUnitStateMachine stateMachine;
    private UnitHealthManager health;
    private UnitHealthManager focus;
    private float nextPickAt;

    public static void Fit(GameObject ship, bool avanti, LayerMask hostileMask)
    {
        if (!ship.TryGetComponent(out NavMeshAgent _) || !ship.TryGetComponent(out SpaceUnit unit)) return;
        if (unit.shipType == ShipType.Station || unit.shipType == ShipType.Fighter || ship.TryGetComponent(out Squadron _)) return;

        if (!ship.TryGetComponent(out CombatFacing facing)) facing = ship.AddComponent<CombatFacing>();
        facing.style = avanti ? Style.Bow : Style.Broadside;
        ShipSpeed speed = ShipSpeed.For(ship.GetComponent<NavMeshAgent>());
        if (speed != null) speed.throttleWhileTurning = true; // No crabbing sideways under slow turn rates.
        facing.hostileMask = hostileMask;
    }

    // The heading this ship wants to fight on. Broadside ships take whichever beam heading is nearer the way
    // they're already travelling, so they run along the line instead of turning in place when they arrive.
    public static Vector3 FightingHeading(GameObject ship, Vector3 toEnemy, Vector3 travel)
    {
        toEnemy.y = 0f;
        if (toEnemy.sqrMagnitude < 0.0001f) return toEnemy;
        toEnemy.Normalize();
        if (!ship.TryGetComponent(out CombatFacing facing) || facing.style == Style.Bow) return toEnemy;

        Vector3 side = Vector3.Cross(Vector3.up, toEnemy);
        travel.y = 0f;
        Vector3 reference = travel.sqrMagnitude > 1f ? travel : ship.transform.forward;
        return Vector3.Dot(reference, side) >= 0f ? side : -side;
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        unit = GetComponent<SpaceUnit>();
        hardpoints = GetComponent<HardpointManager>();
        health = GetComponent<UnitHealthManager>();
        stateMachine = GetComponentInChildren<PlayerUnitStateMachine>();
    }

    private void Update()
    {
        if (health != null && health.IsDead) return;
        if (Moving()) return;

        if (Time.time >= nextPickAt || focus == null || focus.IsDead || !InReach(focus))
        {
            nextPickAt = Time.time + PickInterval;
            UnitHealthManager picked = PickFocus();
            if (picked != focus) turning = false;
            focus = picked;
        }
        if (focus == null || focus.IsDead) return;

        Vector3 toTarget = focus.transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 1f) return;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        Vector3 wanted = toTarget.normalized;
        if (style == Style.Broadside)
        {
            Vector3 side = Vector3.Cross(Vector3.up, wanted);
            wanted = Vector3.Dot(forward, side) >= 0f ? side : -side; // Whichever broadside is nearer.
        }

        float angle = Vector3.SignedAngle(forward, wanted, Vector3.up);
        float slack = Capital ? CapitalSlack : EscortSlack;
        if (!turning && Mathf.Abs(angle) < slack) return; // Close enough: don't fidget.
        turning = Mathf.Abs(angle) >= Tolerance;
        if (!turning) return;

        float turn = Mathf.Min(Mathf.Abs(angle), agent.angularSpeed * Time.deltaTime);
        transform.Rotate(0f, Mathf.Sign(angle) * turn, 0f, Space.World);
    }

    // Under way or carrying out an ordered turn: leave steering to the movement code.
    private bool Moving()
    {
        if (stateMachine != null && stateMachine.currentState is PlayerUnitMoveState) return true;
        if (agent == null || !agent.isOnNavMesh) return true;
        return agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 1f;
    }

    private bool Capital => unit != null && (unit.shipType == ShipType.Battleship || unit.shipType == ShipType.Cruiser || unit.shipType == ShipType.Carrier);

    // Carriers fight with their strike craft: they only turn for enemies inside their own gun range.
    private float Reach => (unit != null && unit.maxRange > 0f ? unit.maxRange : 120f) * (unit != null && unit.shipType == ShipType.Carrier ? 1f : ReachMultiplier);

    private bool InReach(UnitHealthManager other)
    {
        float reach = Reach * 1.15f; // A little slack so it doesn't drop a target on the edge.
        return (other.transform.position - transform.position).sqrMagnitude <= reach * reach;
    }

    private UnitHealthManager PickFocus()
    {
        if (focus != null && !focus.IsDead && InReach(focus) && (hardpoints == null || hardpoints.OrderedTarget == null)) return focus; // Stick with it.

        Transform ordered = hardpoints != null ? hardpoints.OrderedTarget : null;
        UnitHealthManager orderedShip = ordered != null ? ordered.GetComponentInParent<UnitHealthManager>() : null;
        if (orderedShip != null && !orderedShip.IsDead) return orderedShip;

        float reach = Reach;
        UnitHealthManager nearest = null;
        float best = reach * reach;
        foreach (UnitHealthManager other in UnitHealthManager.Active)
        {
            if (other == null || other.IsDead || ((1 << other.gameObject.layer) & hostileMask) == 0) continue;
            if (other.TryGetComponent(out Squadron _)) continue;
            float distance = (other.transform.position - transform.position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = other;
            }
        }
        return nearest;
    }
}
