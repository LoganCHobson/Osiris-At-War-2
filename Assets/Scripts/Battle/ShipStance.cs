using SolarStudios;
using UnityEngine;
using UnityEngine.AI;

// What one of your ships does on its own between orders (Y cycles it for the selection):
//   Hold       - stays exactly where you put it and fires at what comes into range (the default).
//   Aggressive - goes after the nearest enemy warship in sight and closes to firing range.
//   Skirmish   - keeps enemies at the edge of its own range: backs off when they close in, edges forward
//                when they're just out of reach. Good for long-ranged ships.
// Stances only act while the ship is idle, so any move you give it always wins; guarding ships are left
// to their guard duty.
public class ShipStance : MonoBehaviour
{
    public enum Kind { Hold, Aggressive, Skirmish }

    private const float ThinkInterval = 0.6f;
    private const float SightMultiplier = 2.5f;   // How far beyond its gun range it notices enemies.
    private const float CloseTo = 0.75f;          // Aggressive: close to this share of range.
    private const float TooClose = 0.65f;         // Skirmish: back off inside this share of range...
    private const float KeepAt = 0.92f;           // ...to about this share of range.

    public Kind Current { get; private set; } = Kind.Hold;

    private SpaceUnit unit;
    private UnitHealthManager health;
    private float nextThinkAt;

    public static void Fit(GameObject ship)
    {
        if (ship.GetComponent<ShipStance>() == null) ship.AddComponent<ShipStance>();
    }

    public void Set(Kind kind)
    {
        Current = kind;
        nextThinkAt = 0f;
    }

    private void Awake()
    {
        unit = GetComponent<SpaceUnit>();
        health = GetComponent<UnitHealthManager>();
    }

    private void Update()
    {
        if (Current == Kind.Hold || Time.time < nextThinkAt) return;
        nextThinkAt = Time.time + ThinkInterval * Random.Range(0.85f, 1.15f);

        if (unit == null || !unit.IsMobile || (health != null && health.IsDead)) return;
        if (PlayerSpaceManager.PlayerRetreating || GameManager.CombatOver) return;
        if ((Object)unit.stateMachine.currentState == unit.moveState) return; // Carrying out an order.
        if (TryGetComponent(out ShipGuard guard) && guard.enabled) return;

        float range = unit.maxRange > 0f ? unit.maxRange : 50f;
        UnitHealthManager enemy = NearestEnemy(range * SightMultiplier, out float distance);
        if (enemy == null) return;

        Vector3 from = enemy.transform.position;
        Vector3 away = transform.position - from;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        away.Normalize();

        switch (Current)
        {
            case Kind.Aggressive:
                if (distance > range * 0.95f) MoveTo(from + away * range * CloseTo, from);
                break;

            case Kind.Skirmish:
                if (distance < range * TooClose || distance > range * 1.05f) MoveTo(from + away * range * KeepAt, from);
                break;
        }
    }

    private UnitHealthManager NearestEnemy(float sight, out float nearest)
    {
        nearest = float.MaxValue;
        UnitHealthManager best = null;
        foreach (UnitHealthManager other in UnitHealthManager.Active)
        {
            if (other == null || other.IsDead || other.isAttackerSide == health.isAttackerSide) continue;
            if (other.IsFreeUnit || other.GetComponent<Squadron>() != null) continue; // Warships and stations only.

            float distance = Vector3.Distance(other.transform.position, transform.position);
            if (distance < nearest && distance <= sight)
            {
                nearest = distance;
                best = other;
            }
        }
        return best;
    }

    private void MoveTo(Vector3 point, Vector3 lookAt)
    {
        point.y = transform.position.y;
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, 60f, NavMesh.AllAreas)) point = hit.position;
        if (Vector3.Distance(Flat(point), Flat(transform.position)) < 6f) return;

        PlayerUnitMoveState move = unit.moveState;
        move.ClearDestinations();
        move.AddDestination(point);
        move.SetFacing(lookAt - point);
        unit.stateMachine.SetState(move);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
