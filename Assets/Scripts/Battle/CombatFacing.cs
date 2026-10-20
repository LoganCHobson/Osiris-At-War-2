using SolarStudios;
using UnityEngine;
using UnityEngine.AI;

// Turns a warship that isn't moving to face its target - the ordered one, or else the nearest enemy ship in
// reach. Avanti guns only fire forward, so they point the bow at it like a tank; Human ships turn broadside
// on, whichever side needs less turning, bringing more turrets clear of their own hull (and presenting a
// bigger target doing it - see TurretController aspect accuracy). Fitted by GameManager.ApplyAllegiance.
public class CombatFacing : MonoBehaviour
{
    public enum Style { Bow, Broadside }

    private const float PickInterval = 0.5f;
    private const float ReachMultiplier = 1.4f; // Start turning for ships a little beyond gun range.
    private const float Tolerance = 2f;

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
        facing.hostileMask = hostileMask;
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

        if (Time.time >= nextPickAt)
        {
            nextPickAt = Time.time + PickInterval;
            focus = PickFocus();
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
        if (Mathf.Abs(angle) < Tolerance) return;

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

    private UnitHealthManager PickFocus()
    {
        Transform ordered = hardpoints != null ? hardpoints.OrderedTarget : null;
        UnitHealthManager orderedShip = ordered != null ? ordered.GetComponentInParent<UnitHealthManager>() : null;
        if (orderedShip != null && !orderedShip.IsDead) return orderedShip;

        float reach = (unit != null && unit.maxRange > 0f ? unit.maxRange : 120f) * ReachMultiplier;
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
