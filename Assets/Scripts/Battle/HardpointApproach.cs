using SolarStudios;
using UnityEngine;

// When you order one of your ships onto a specific hardpoint it can't see - the target's own hull is in
// the way - the ship repositions once to expose it. The side a hardpoint faces is read from where it sits
// on its hull: a ventral battery faces down (so we dive under it), a dorsal turret up (we climb over it),
// a flank mount sideways (we swing round to that flank at our current range). Combined with automatic
// pitch/roll this brings our guns onto it. It acts once per order and only while the ship is idle, so it
// never overrides a move you give it afterwards.
public class HardpointApproach : MonoBehaviour
{
    private const float CheckInterval = 0.75f;
    private const float VerticalBias = 0.45f;   // How strongly a hardpoint must face up/down to go over/under.
    private const float VerticalClearance = 45f;

    private HardpointManager hardpoints;
    private SpaceUnit unit;
    private ShipAltitude altitude;
    private UnitHealthManager health;
    private Transform handled;
    private float nextCheckAt;

    public static void Fit(GameObject ship)
    {
        if (ship.GetComponent<HardpointApproach>() == null && ship.GetComponent<Squadron>() == null) ship.AddComponent<HardpointApproach>();
    }

    private void Awake()
    {
        hardpoints = GetComponent<HardpointManager>();
        unit = GetComponent<SpaceUnit>();
        health = GetComponent<UnitHealthManager>();
    }

    private void Update()
    {
        if (Time.time < nextCheckAt || hardpoints == null || unit == null || !unit.IsMobile) return;
        nextCheckAt = Time.time + CheckInterval * Random.Range(0.85f, 1.15f);
        if (health != null && health.IsDead) return;

        Transform target = hardpoints.OrderedTarget;
        if (target == null || !target.gameObject.activeInHierarchy || target == handled) return;
        if (hardpoints.OrderedWholeShip || target.GetComponentInParent<HardpointHealth>() == null) return; // Whole-ship order, not a hardpoint.
        if ((Object)unit.stateMachine.currentState == unit.moveState) return; // Still carrying out a move.

        UnitHealthManager owner = target.GetComponentInParent<UnitHealthManager>();
        if (owner == null || owner.IsDead) return;
        if (!Hidden(target, owner)) return;

        handled = target;
        Expose(target, owner);
    }

    // True if the first thing between us and the hardpoint is the rest of its own ship.
    private bool Hidden(Transform target, UnitHealthManager owner)
    {
        Vector3 from = transform.position;
        Vector3 to = target.position;
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        if (distance < 1f) return false;

        RaycastHit[] hits = Physics.RaycastAll(from, direction / distance, distance);
        float nearest = float.MaxValue;
        bool blockedByHull = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.root == transform.root || hit.distance >= nearest) continue;
            if (hit.transform.root != owner.transform.root) continue;

            nearest = hit.distance;
            blockedByHull = !hit.transform.IsChildOf(target) && hit.collider.GetComponentInParent<HardpointHealth>() == null;
        }
        return blockedByHull;
    }

    private void Expose(Transform target, UnitHealthManager owner)
    {
        if (altitude == null) altitude = GetComponent<ShipAltitude>();

        Vector3 outward = target.position - owner.transform.position;
        if (outward.sqrMagnitude < 0.01f) return;
        outward.Normalize();

        float targetAltitude = ShipAltitude.AltitudeOf(owner.transform);
        if (altitude != null && Mathf.Abs(outward.y) >= VerticalBias)
        {
            // Over or under it.
            altitude.SetTargetAltitude(targetAltitude + Mathf.Sign(outward.y) * (VerticalClearance + altitude.HalfHeight));
            return;
        }

        // Round to the flank it faces, keeping our current range.
        Vector3 flat = new Vector3(outward.x, 0f, outward.z);
        if (flat.sqrMagnitude < 0.01f) return;
        flat.Normalize();

        Vector3 toUs = transform.position - owner.transform.position;
        toUs.y = 0f;
        float range = Mathf.Max(toUs.magnitude, 40f);
        Vector3 destination = owner.transform.position + flat * range;
        destination.y = transform.position.y;
        if (UnityEngine.AI.NavMesh.SamplePosition(destination, out UnityEngine.AI.NavMeshHit hit, 100f, UnityEngine.AI.NavMesh.AllAreas))
        {
            destination = hit.position;
        }

        PlayerUnitMoveState move = unit.moveState;
        move.ClearDestinations();
        move.AddDestination(destination);
        move.SetFacing(owner.transform.position - destination);
        unit.stateMachine.SetState(move);
    }
}
