using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class ShipGuard : MonoBehaviour
{
    public const float EscortGap = 12f;

    public float checkInterval = 0.5f;
    public float retargetCooldown = 6f;
    public float repositionThreshold = 20f;
    public float leashDistance = 150f;
    public float engageRangeFraction = 0.85f;

    public UnitHealthManager Ward { get; private set; }
    public UnitHealthManager Engaged { get; private set; }

    private SpaceUnit unit;
    private UnitHealthManager self;
    private HardpointManager hardpoints;
    private float lockedUntil;
    private float nextCheckAt;
    private Vector3 escortOffset;
    private Vector3 lastOrdered = Vector3.positiveInfinity;

    public static void Assign(IReadOnlyList<SpaceUnit> guards, UnitHealthManager ward)
    {
        if (ward == null) return;

        List<SpaceUnit> escorts = new List<SpaceUnit>();
        foreach (SpaceUnit guard in guards)
        {
            if (guard != null && guard.gameObject != ward.gameObject) escorts.Add(guard);
        }
        if (escorts.Count == 0) return;

        float wardRadius = Footprint(ward.gameObject);
        float spacing = FleetFormation.Spacing(escorts);

        for (int i = 0; i < escorts.Count; i++)
        {
            SpaceUnit escort = escorts[i];
            float ring = wardRadius + Footprint(escort.gameObject) + EscortGap;
            float step = Mathf.Min(Mathf.Rad2Deg * spacing / Mathf.Max(1f, ring), escorts.Count > 1 ? 300f / (escorts.Count - 1) : 0f);
            float angle = 180f + (i - (escorts.Count - 1) * 0.5f) * step;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * ring;

            ShipGuard guard = escort.GetComponent<ShipGuard>();
            if (guard == null) guard = escort.gameObject.AddComponent<ShipGuard>();
            guard.Begin(ward, offset);
        }
    }

    public static void Cancel(SpaceUnit unit)
    {
        if (unit != null && unit.TryGetComponent(out ShipGuard guard))
        {
            guard.Stop();
        }
    }

    private void Awake()
    {
        unit = GetComponent<SpaceUnit>();
        self = GetComponent<UnitHealthManager>();
        hardpoints = GetComponent<HardpointManager>();
    }

    private void Begin(UnitHealthManager ward, Vector3 offset)
    {
        Ward = ward;
        escortOffset = offset;
        Engaged = null;
        lockedUntil = 0f;
        nextCheckAt = 0f;
        lastOrdered = Vector3.positiveInfinity;
        enabled = true;
    }

    public void Stop()
    {
        Ward = null;
        Engaged = null;
        enabled = false;
    }

    private void Update()
    {
        if (Time.time < nextCheckAt) return;
        nextCheckAt = Time.time + checkInterval;

        if (Ward == null || Ward.IsDead || (self != null && self.IsDead) || unit == null || unit.moveState == null)
        {
            Stop();
            return;
        }

        UpdateEngagement();
        Reposition();
    }

    private void UpdateEngagement()
    {
        bool engagedAlive = Engaged != null && !Engaged.IsDead;
        if (engagedAlive && (Time.time < lockedUntil || IsAttacking(Engaged, Ward)))
        {
            if (!AimedAt(Engaged)) Aim(Engaged);
            return;
        }

        UnitHealthManager attacker = NearestAttacker();
        if (attacker != null)
        {
            Engaged = attacker;
            lockedUntil = Time.time + retargetCooldown;
            Aim(attacker);
            return;
        }

        if (!engagedAlive || Time.time >= lockedUntil)
        {
            Engaged = null;
        }
    }

    private UnitHealthManager NearestAttacker()
    {
        UnitHealthManager nearest = null;
        float best = float.MaxValue;
        Vector3 wardPosition = Ward.transform.position;

        foreach (UnitHealthManager candidate in UnitHealthManager.Active)
        {
            if (candidate == null || candidate.IsDead || candidate.gameObject.layer == gameObject.layer) continue;
            if (!IsAttacking(candidate, Ward)) continue;

            float distance = (candidate.transform.position - wardPosition).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    private static bool IsAttacking(UnitHealthManager attacker, UnitHealthManager victim)
    {
        HardpointManager attackerHardpoints = attacker.GetComponent<HardpointManager>();
        if (attackerHardpoints == null) return false;

        foreach (TurretController turret in attackerHardpoints.GetSpecificHardpoints<TurretController>())
        {
            if (turret != null && turret.target != null && turret.target.IsChildOf(victim.transform)) return true;
        }
        return false;
    }

    private bool AimedAt(UnitHealthManager target)
    {
        if (hardpoints == null) return true;

        foreach (TurretController turret in hardpoints.GetSpecificHardpoints<TurretController>())
        {
            if (turret != null && turret.target != null && turret.target.IsChildOf(target.transform)) return true;
        }
        return false;
    }

    private void Aim(UnitHealthManager target)
    {
        if (hardpoints == null) return;

        HardpointManager targetHardpoints = target.GetComponent<HardpointManager>();
        Transform aimPoint = targetHardpoints != null ? targetHardpoints.GetRandomHardpoint() : null;
        if (aimPoint != null)
        {
            hardpoints.AssignTarget(aimPoint);
        }
    }

    private void Reposition()
    {
        Vector3 wardPosition = Ward.transform.position;
        Vector3 goal;
        Vector3 face;

        if (Engaged != null && OutOfRange(Engaged))
        {
            Vector3 enemyPosition = Engaged.transform.position;
            Vector3 approach = Flat(transform.position - enemyPosition);
            approach = approach.sqrMagnitude > 0.01f ? approach.normalized : Flat(wardPosition - enemyPosition).normalized;
            goal = enemyPosition + approach * Range() * engageRangeFraction;

            Vector3 fromWard = Flat(goal - wardPosition);
            if (fromWard.magnitude > leashDistance)
            {
                goal = wardPosition + fromWard.normalized * leashDistance;
            }
            face = Flat(enemyPosition - goal);
        }
        else
        {
            Quaternion wardYaw = Quaternion.Euler(0f, Ward.transform.eulerAngles.y, 0f);
            goal = wardPosition + wardYaw * escortOffset;
            face = Engaged != null ? Flat(Engaged.transform.position - goal) : Flat(Ward.transform.forward);
        }

        bool moving = (Object)unit.stateMachine.currentState == unit.moveState;
        if (moving && Vector3.Distance(goal, lastOrdered) < repositionThreshold) return;
        if (!moving && Vector3.Distance(transform.position, goal) < repositionThreshold) return;

        lastOrdered = goal;
        unit.moveState.ClearDestinations();
        unit.moveState.AddDestination(Snap(goal));
        unit.moveState.SetFacing(face);
        unit.moveState.IgnoreObstacle(Ward.transform);

        if (!moving)
        {
            unit.stateMachine.SetState(unit.moveState);
        }
    }

    private bool OutOfRange(UnitHealthManager target)
    {
        return Vector3.Distance(transform.position, target.transform.position) > Range() * FleetFormation.InRangeFraction;
    }

    private float Range()
    {
        return unit != null && unit.maxRange > 0f ? unit.maxRange : FleetFormation.DefaultRange;
    }

    private static float Footprint(GameObject target)
    {
        NavMeshAgent agent = target.GetComponent<NavMeshAgent>();
        if (agent != null) return agent.radius;

        Bounds bounds = new Bounds(target.transform.position, Vector3.zero);
        bool found = false;
        foreach (Collider collider in target.GetComponentsInChildren<Collider>())
        {
            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }
        return found ? Mathf.Max(bounds.extents.x, bounds.extents.z) : FleetFormation.DefaultRadius;
    }

    private static Vector3 Snap(Vector3 point)
    {
        return NavMesh.SamplePosition(point, out NavMeshHit hit, 50f, NavMesh.AllAreas) ? hit.position : point;
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }
}
