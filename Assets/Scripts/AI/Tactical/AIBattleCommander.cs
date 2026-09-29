using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class AIBattleCommander : MonoBehaviour
{
    public enum Stance { Engage, Press, FallBack }

    public float decisionInterval = 0.75f;
    public float formationSpacing = 15f;
    public float repositionThreshold = 8f;
    public float focusFireWindow = 10f;
    public float defaultRange = 50f;

    public Faction Faction { get; private set; }
    public bool IsAttackerSide { get; private set; }
    public Stance CurrentStance { get; private set; } = Stance.Engage;
    public float ForceRatio { get; private set; } = 1f;
    public int ReserveCount => reserve.Count;
    public int FieldCount => units.Count;

    private class Unit
    {
        public UnitHealthManager health;
        public SpaceUnit spaceUnit;
        public HardpointManager hardpoints;
        public Ship ship;
        public float dps;
        public UnitHealthManager target;
        public Transform aimPoint;
        public Vector3 lastDestination = Vector3.positiveInfinity;
    }

    private class Contact
    {
        public UnitHealthManager health;
        public HardpointManager hardpoints;
        public float dps;
        public float power;
        public float range;
        public float priority;
        public float incomingDps;
    }

    private readonly List<Ship> reserve = new List<Ship>();
    private readonly List<Unit> units = new List<Unit>();
    private readonly List<Contact> enemies = new List<Contact>();
    private readonly List<UnitHealthManager> friendlyDefenses = new List<UnitHealthManager>();

    private AIPersonality personality;
    private Transform spawnPoint;
    private int maxShipsOnField;
    private float nextDecisionAt;
    private float reinforceReadyAt;

    private Vector3 ourCenter;
    private Vector3 enemyCenter;
    private Vector3 toEnemy;
    private Vector3 lateral;
    private float ourRange;

    public void Initialize(Faction faction, bool attackerSide, List<Ship> roster, Transform spawn, int maxShips)
    {
        Faction = faction;
        IsAttackerSide = attackerSide;
        spawnPoint = spawn;
        maxShipsOnField = maxShips;
        personality = ResolvePersonality(faction);

        reserve.AddRange(roster);

        Vector3 origin = SpawnPosition();
        Vector3 forward = spawnPoint != null ? spawnPoint.forward : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
        int initial = Mathf.Min(maxShipsOnField, reserve.Count);
        float spacing = SpawnSpacing(initial);

        for (int i = 0; i < initial; i++)
        {
            Spawn(reserve[0], origin + side * SlotOffset(i) * spacing, Quaternion.LookRotation(forward));
        }
    }

    private float SpawnSpacing(int count)
    {
        float spacing = formationSpacing;
        for (int i = 0; i < count && i < reserve.Count; i++)
        {
            if (reserve[i] != null)
            {
                spacing = Mathf.Max(spacing, FleetFormation.Spacing(reserve[i].prefab));
            }
        }
        return spacing;
    }

    private float FieldSpacing()
    {
        List<SpaceUnit> spaceUnits = new List<SpaceUnit>();
        foreach (Unit unit in units)
        {
            if (unit.spaceUnit != null) spaceUnits.Add(unit.spaceUnit);
        }
        return spaceUnits.Count > 0 ? Mathf.Max(formationSpacing, FleetFormation.Spacing(spaceUnits)) : formationSpacing;
    }

    private static AIPersonality ResolvePersonality(Faction faction)
    {
        if (faction != null && faction.personality != null) return faction.personality;

        FactionBrain brain = AIDirector.Instance != null ? AIDirector.Instance.BrainFor(faction) : null;
        if (brain != null) return brain.personality;

        return ScriptableObject.CreateInstance<AIPersonality>();
    }

    private void Update()
    {
        if (Time.time < nextDecisionAt) return;
        nextDecisionAt = Time.time + decisionInterval;

        PruneUnits();
        Scan();
        if (enemies.Count == 0) return;

        ComputeGeometry();
        EvaluateStance();
        Reinforce();
        AssignTargets();
        Maneuver();
    }

    private void PruneUnits()
    {
        int before = units.Count;
        units.RemoveAll(u => u.health == null || u.health.IsDead);

        if (units.Count < before)
        {
            reinforceReadyAt = Mathf.Max(reinforceReadyAt, Time.time + personality.reinforcementDelay);
        }

        foreach (Unit unit in units)
        {
            unit.dps = Dps(unit.hardpoints);
        }
    }

    private void Scan()
    {
        enemies.Clear();
        friendlyDefenses.Clear();

        foreach (UnitHealthManager health in UnitHealthManager.Active)
        {
            if (health.IsDead) continue;

            bool hostile = health.IsDefense ? IsAttackerSide : health.isAttackerSide != IsAttackerSide;
            if (!hostile)
            {
                if (health.IsDefense) friendlyDefenses.Add(health);
                continue;
            }

            HardpointManager hardpoints = health.GetComponent<HardpointManager>();
            if (hardpoints == null || hardpoints.hardpoints.Count == 0) continue;

            SpaceUnit spaceUnit = health.GetComponent<SpaceUnit>();
            enemies.Add(new Contact
            {
                health = health,
                hardpoints = hardpoints,
                dps = Dps(hardpoints),
                power = BasePower(health) * HealthFraction(health),
                range = spaceUnit != null && spaceUnit.maxRange > 0f ? spaceUnit.maxRange : defaultRange
            });
        }
    }

    private void ComputeGeometry()
    {
        ourCenter = units.Count > 0 ? Centroid(units) : SpawnPosition();

        Vector3 sum = Vector3.zero;
        int ships = 0;
        foreach (Contact contact in enemies)
        {
            if (contact.health.IsDefense) continue;
            sum += contact.health.transform.position;
            ships++;
        }

        if (ships == 0)
        {
            foreach (Contact contact in enemies)
            {
                sum += contact.health.transform.position;
            }
            ships = enemies.Count;
        }
        enemyCenter = sum / ships;

        toEnemy = enemyCenter - ourCenter;
        toEnemy.y = 0f;
        toEnemy = toEnemy.sqrMagnitude > 0.01f ? toEnemy.normalized : (spawnPoint != null ? spawnPoint.forward : Vector3.forward);
        lateral = Vector3.Cross(Vector3.up, toEnemy).normalized;

        float totalRange = 0f;
        foreach (Unit unit in units)
        {
            totalRange += unit.spaceUnit != null && unit.spaceUnit.maxRange > 0f ? unit.spaceUnit.maxRange : defaultRange;
        }
        ourRange = units.Count > 0 ? totalRange / units.Count : defaultRange;
    }

    private void EvaluateStance()
    {
        float ours = 0f;
        foreach (Unit unit in units)
        {
            ours += (unit.ship != null ? unit.ship.combatPower : Strength.DefaultShipPower) * HealthFraction(unit.health);
        }
        foreach (UnitHealthManager defense in friendlyDefenses)
        {
            ours += BasePower(defense) * HealthFraction(defense);
        }

        float theirs = 0f;
        foreach (Contact contact in enemies)
        {
            theirs += contact.power;
        }

        float ratio = theirs <= 0f ? 10f : ours / theirs;
        ForceRatio = Mathf.Lerp(ForceRatio, ratio, 0.5f);

        if (!personality.canManageStance)
        {
            CurrentStance = Stance.Engage;
            return;
        }

        float aggression = Mathf.Max(0.1f, personality.aggression);
        float pressAt = personality.pressAdvantageRatio / aggression;
        float fallBackAt = personality.fallBackRatio / aggression;
        bool canRegroup = reserve.Count > 0 || friendlyDefenses.Count > 0;

        switch (CurrentStance)
        {
            case Stance.Press:
                if (ForceRatio < pressAt * 0.85f) CurrentStance = Stance.Engage;
                break;
            case Stance.FallBack:
                if (!canRegroup || ForceRatio > fallBackAt * 1.3f) CurrentStance = Stance.Engage;
                break;
            default:
                if (ForceRatio >= pressAt) CurrentStance = Stance.Press;
                else if (ForceRatio <= fallBackAt && canRegroup) CurrentStance = Stance.FallBack;
                break;
        }
    }

    private void Reinforce()
    {
        if (reserve.Count == 0 || units.Count >= maxShipsOnField || Time.time < reinforceReadyAt) return;

        int count = Mathf.Min(maxShipsOnField - units.Count, reserve.Count);
        Vector3 drop = ChooseDropZone();
        Quaternion facing = Quaternion.LookRotation(toEnemy);
        float spacing = SpawnSpacing(count);

        for (int i = 0; i < count; i++)
        {
            Spawn(reserve[0], drop + lateral * SlotOffset(i) * spacing, facing);
        }

        reinforceReadyAt = Time.time + personality.reinforcementDelay;
    }

    private Vector3 ChooseDropZone()
    {
        Vector3 fallback = SpawnPosition();
        if (!personality.canPickDropZones) return fallback;

        float safeDistance = 0f;
        foreach (Contact contact in enemies)
        {
            safeDistance = Mathf.Max(safeDistance, contact.range * 1.15f);
        }

        Vector3 anchor = Anchor();
        Vector3[] candidates =
        {
            ourCenter - toEnemy * ourRange * 0.6f,
            ourCenter + lateral * ourRange * 1.2f,
            ourCenter - lateral * ourRange * 1.2f,
            anchor - toEnemy * ourRange * 0.3f,
            fallback
        };

        Vector3 best = fallback;
        float bestScore = float.MinValue;

        foreach (Vector3 candidate in candidates)
        {
            if (NearestEnemyDistance(candidate) < safeDistance) continue;

            float score = CurrentStance switch
            {
                Stance.Press => -Vector3.Distance(candidate, enemyCenter),
                Stance.FallBack => -Vector3.Distance(candidate, anchor),
                _ => -Vector3.Distance(candidate, ourCenter)
            };

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private void AssignTargets()
    {
        foreach (Contact contact in enemies)
        {
            contact.incomingDps = 0f;

            float distance = Vector3.Distance(contact.health.transform.position, ourCenter);
            float proximity = 1f / (1f + distance / Mathf.Max(1f, ourRange * 2f));
            float health = Mathf.Max(1f, contact.health.currentHealth);
            contact.priority = (contact.dps + contact.power * 0.05f) / health * proximity;

            if (contact.health.IsDefense && HasEnemyShips())
            {
                contact.priority *= 0.35f;
            }
        }

        enemies.Sort((a, b) => b.priority.CompareTo(a.priority));

        foreach (Unit unit in units)
        {
            if (unit.hardpoints == null) continue;

            Contact chosen = personality.canFocusFire ? PickFocusTarget() : NearestContact(unit.health.transform.position);
            if (chosen == null) continue;

            chosen.incomingDps += unit.dps;
            AimAt(unit, chosen);
        }
    }

    private Contact PickFocusTarget()
    {
        foreach (Contact contact in enemies)
        {
            if (contact.incomingDps * focusFireWindow < contact.health.currentHealth * 1.2f)
            {
                return contact;
            }
        }
        return enemies[0];
    }

    private Contact NearestContact(Vector3 position)
    {
        Contact nearest = null;
        float best = float.MaxValue;
        foreach (Contact contact in enemies)
        {
            float distance = (contact.health.transform.position - position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = contact;
            }
        }
        return nearest;
    }

    private void AimAt(Unit unit, Contact contact)
    {
        List<HardpointHealth> alive = contact.hardpoints.hardpoints;
        if (unit.target == contact.health && unit.aimPoint != null && alive.Exists(h => h != null && h.transform == unit.aimPoint)) return;

        HardpointHealth pick = null;
        bool pickIsTurret = false;

        foreach (HardpointHealth hardpoint in alive)
        {
            if (hardpoint == null) continue;

            bool isTurret = hardpoint.GetComponent<TurretController>() != null;
            if (pick == null || (isTurret && !pickIsTurret) || (isTurret == pickIsTurret && hardpoint.currentHealth < pick.currentHealth))
            {
                pick = hardpoint;
                pickIsTurret = isTurret;
            }
        }

        if (pick == null) return;

        unit.target = contact.health;
        unit.aimPoint = pick.transform;
        unit.hardpoints.AssignTarget(pick.transform);
    }

    private void Maneuver()
    {
        Vector3 anchor = Anchor();
        bool hasDefenses = friendlyDefenses.Count > 0;

        Vector3 front = CurrentStance switch
        {
            Stance.Press => enemyCenter - toEnemy * ourRange * 0.45f,
            Stance.FallBack => anchor + toEnemy * ourRange * 0.2f,
            _ => enemyCenter - toEnemy * ourRange * 0.85f
        };

        if (!IsAttackerSide && hasDefenses && CurrentStance == Stance.Engage)
        {
            front = Vector3.Lerp(anchor, front, 0.5f);
        }

        List<Unit> ordered = new List<Unit>(units);
        ordered.Sort((a, b) => (b.ship != null ? b.ship.combatPower : 0f).CompareTo(a.ship != null ? a.ship.combatPower : 0f));
        float spacing = FieldSpacing();

        for (int i = 0; i < ordered.Count; i++)
        {
            Unit unit = ordered[i];
            Vector3 destination = front + lateral * SlotOffset(i) * spacing;

            if (CurrentStance != Stance.FallBack && unit.target != null)
            {
                float range = unit.spaceUnit != null && unit.spaceUnit.maxRange > 0f ? unit.spaceUnit.maxRange : defaultRange;
                Vector3 targetPosition = unit.target.transform.position;
                if (Vector3.Distance(destination, targetPosition) > range * 0.95f)
                {
                    Vector3 approach = (destination - targetPosition).normalized;
                    destination = targetPosition + approach * range * 0.8f;
                }
            }

            MoveTo(unit, SnapToNavMesh(destination));
        }
    }

    private void MoveTo(Unit unit, Vector3 destination)
    {
        SpaceUnit spaceUnit = unit.spaceUnit;
        if (spaceUnit == null) return;

        if (spaceUnit.moveState == null || spaceUnit.stateMachine == null)
        {
            SteerAgent(unit, destination);
            return;
        }

        bool moving = (Object)spaceUnit.stateMachine.currentState == spaceUnit.moveState;
        if (moving && Vector3.Distance(destination, unit.lastDestination) < repositionThreshold) return;
        if (!moving && Vector3.Distance(spaceUnit.transform.position, destination) < repositionThreshold) return;

        unit.lastDestination = destination;
        spaceUnit.moveState.ClearDestinations();
        spaceUnit.moveState.AddDestination(destination);
        spaceUnit.moveState.SetFacing(toEnemy);

        if (!moving)
        {
            spaceUnit.stateMachine.SetState(spaceUnit.moveState);
        }
    }

    private void SteerAgent(Unit unit, Vector3 destination)
    {
        NavMeshAgent agent = unit.spaceUnit.agent != null ? unit.spaceUnit.agent : unit.spaceUnit.GetComponent<NavMeshAgent>();
        if (agent == null || !agent.isOnNavMesh) return;

        if (Vector3.Distance(unit.spaceUnit.transform.position, destination) < repositionThreshold) return;
        if (agent.hasPath && Vector3.Distance(destination, unit.lastDestination) < repositionThreshold) return;

        unit.lastDestination = destination;
        agent.isStopped = false;
        agent.SetDestination(destination);
    }

    private void Spawn(Ship ship, Vector3 position, Quaternion rotation)
    {
        reserve.Remove(ship);
        if (GameManager.Instance == null) return;

        GameObject spawned = GameManager.Instance.SpawnShip(ship, SnapToNavMesh(position), rotation, IsAttackerSide);
        if (spawned == null) return;

        units.Add(new Unit
        {
            health = spawned.GetComponent<UnitHealthManager>(),
            spaceUnit = spawned.GetComponent<SpaceUnit>(),
            hardpoints = spawned.GetComponent<HardpointManager>(),
            ship = ship
        });
    }

    private bool HasEnemyShips()
    {
        foreach (Contact contact in enemies)
        {
            if (!contact.health.IsDefense) return true;
        }
        return false;
    }

    private Vector3 Anchor()
    {
        if (friendlyDefenses.Count == 0) return SpawnPosition();

        Vector3 sum = Vector3.zero;
        foreach (UnitHealthManager defense in friendlyDefenses)
        {
            sum += defense.transform.position;
        }
        return sum / friendlyDefenses.Count;
    }

    private float NearestEnemyDistance(Vector3 point)
    {
        float nearest = float.MaxValue;
        foreach (Contact contact in enemies)
        {
            nearest = Mathf.Min(nearest, Vector3.Distance(point, contact.health.transform.position));
        }
        return nearest;
    }

    private Vector3 SpawnPosition()
    {
        return spawnPoint != null ? spawnPoint.position : transform.position;
    }

    private static Vector3 Centroid(List<Unit> list)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Unit unit in list)
        {
            if (unit.health == null) continue;
            sum += unit.health.transform.position;
            count++;
        }
        return count > 0 ? sum / count : Vector3.zero;
    }

    private static float SlotOffset(int index)
    {
        int step = (index + 1) / 2;
        return index % 2 == 0 ? -step : step;
    }

    private static Vector3 SnapToNavMesh(Vector3 position)
    {
        return NavMesh.SamplePosition(position, out NavMeshHit hit, 100f, NavMesh.AllAreas) ? hit.position : position;
    }

    private static float HealthFraction(UnitHealthManager health)
    {
        return health != null && health.maxHealth > 0f ? Mathf.Clamp01(health.currentHealth / health.maxHealth) : 1f;
    }

    private static float BasePower(UnitHealthManager health)
    {
        if (health.isBattleStation) return Strength.BattleStationPower;
        if (health.isShipyardBonus) return Strength.ShipyardPower;
        return health.sourceShip != null ? health.sourceShip.combatPower : Strength.DefaultShipPower;
    }

    private static float Dps(HardpointManager hardpoints)
    {
        if (hardpoints == null) return 0f;

        float dps = 0f;
        foreach (TurretController turret in hardpoints.GetSpecificHardpoints<TurretController>())
        {
            dps += turret.damage * Mathf.Max(1, turret.firingPoints.Count) / Mathf.Max(0.05f, turret.fireRate);
        }
        return dps;
    }
}
