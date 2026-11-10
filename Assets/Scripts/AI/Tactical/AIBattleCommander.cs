using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public class AIBattleCommander : MonoBehaviour
{
    public enum Stance { Engage, Press, FallBack }

    public float decisionInterval = 0.75f;
    public float formationSpacing = 15f;
    public float repositionThreshold = 8f;
    public float reissueCooldown = 6f;
    public float focusFireWindow = 10f;
    public float defaultRange = 50f;

    [Tooltip("Off = use the campaign's difficulty. The difficulty simulator turns this on to pit levels against each other.")]
    public bool useDifficultyOverride;
    public DifficultyProfile difficultyOverride;
    public DifficultyProfile Difficulty { get; private set; }

    public Faction Faction { get; private set; }
    public bool IsAttackerSide { get; private set; }
    public Stance CurrentStance { get; private set; } = Stance.Engage;
    public float ForceRatio { get; private set; } = 1f;
    public int ReserveCount => reserve.Count;

    public float ReservePower
    {
        get
        {
            float power = 0f;
            foreach (ShipEntry ship in reserve) power += ship != null ? ship.combatPower : 0f;
            return power;
        }
    }
    public bool Patient { get; private set; }
    public int FieldCount => units.Count;

    private class Unit
    {
        public UnitHealthManager health;
        public SpaceUnit spaceUnit;
        public HardpointManager hardpoints;
        public Ship ship;
        public Squadron squadron;
        public float dps;
        public UnitHealthManager target;
        public Transform aimPoint;
        public Vector3 lastDestination = Vector3.positiveInfinity;
        public float lastOrderedAt = float.NegativeInfinity;
        public float lineKey = float.NaN; // Fixed place along the battle line, so ships don't swap slots.
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

    private readonly List<ShipEntry> reserve = new List<ShipEntry>();
    private ShipEntry lead;
    private readonly List<Unit> units = new List<Unit>();
    private readonly List<Contact> enemies = new List<Contact>();
    private readonly List<UnitHealthManager> friendlyDefenses = new List<UnitHealthManager>();

    private AIPersonality personality;
    private Transform spawnPoint;
    private int populationCap;
    private float nextDecisionAt;
    private float reinforceReadyAt;
    private readonly int orderGroup = SolarStudios.PlayerUnitMoveState.NextOrderGroup();

    private Vector3 ourCenter;
    private Vector3 enemyCenter;
    private Vector3 toEnemy;
    private Vector3 lateral;
    private float ourRange;

    // Strike craft launched from a hangar are part of their carrier's power, not extra on top of it:
    // count only this share of them so a few carriers don't read as a doubled enemy fleet.
    public const float LaunchedCraftPowerShare = 0.25f;
    // Being shot while (almost) none of our ships can reach anything: close the distance, don't sit there.
    public const float KitedInRangeShare = 0.35f;
    public const float KitedHoldTime = 8f;

    public bool Kited => Time.time < kitedUntil;
    private float kitedUntil = float.NegativeInfinity;
    private float lastFieldHealth = -1f;

    public void Initialize(Faction faction, bool attackerSide, List<ShipEntry> roster, Transform spawn, int population)
    {
        Faction = faction;
        IsAttackerSide = attackerSide;
        spawnPoint = spawn;
        populationCap = population;
        Difficulty = useDifficultyOverride && difficultyOverride != null ? difficultyOverride : global::Difficulty.Profile;
        personality = Difficulty.Apply(ResolvePersonality(faction));
        decisionInterval = Difficulty.decisionInterval;
        Patient = Random.value < personality.patience;

        reserve.AddRange(roster);
        lead = BattleDeployment.ChooseLead(roster);

        Vector3 forward = spawnPoint != null ? spawnPoint.forward : Vector3.forward;
        List<ShipEntry> opening = ChooseOpening();
        DefenseFormation formation = null;
        if (!attackerSide)
        {
            // Defending: pick a layout for what we're fielding - hug the station if we have one.
            BattleContext context = BattleContext.Instance;
            bool fortified = context != null && (context.defenderHasShipyard || context.defenderHasBattleStation);
            formation = DefenseFormation.ChooseForAI(opening, fortified);
        }
        foreach (BattleDeployment.Placement placement in BattleDeployment.ArrangeFormation(opening, formation, lead, SpawnPosition(), forward))
        {
            Spawn(placement.ship, placement.position, Quaternion.LookRotation(forward), false, placement.altitude);
        }
    }

    // What to field at the start. Holding reserves, it opens with its lead and enough of the fleet to outgun
    // what the other side can put on the field; the rest waits, and escapes for free if the battle goes badly.
    private List<ShipEntry> ChooseOpening()
    {
        if (!Difficulty.holdReserves) return TakeDeployable();
        return TakeUpTo(ExpectedEnemyPower() * personality.pressAdvantageRatio, lead);
    }

    // Reserve ships until the power wanted is on the field (or the population cap is reached): the given
    // ship first, then the strongest, strike craft last.
    private List<ShipEntry> TakeUpTo(float wantedPower, ShipEntry first)
    {
        List<ShipEntry> chosen = new List<ShipEntry>();
        int room = populationCap - FieldPopulation();
        float power = 0f;

        IEnumerable<ShipEntry> order = reserve.Where(s => s != null)
            .OrderByDescending(s => s == first)
            .ThenBy(s => s.CanLead ? 0 : 1)
            .ThenByDescending(s => s.combatPower);
        foreach (ShipEntry ship in order)
        {
            if (chosen.Count > 0 && power >= wantedPower) break;
            if (ship.populationCost > room) continue;
            chosen.Add(ship);
            room -= ship.populationCost;
            power += ship.combatPower;
        }
        return chosen;
    }

    // What the other side can put on the field: its best ships up to the population cap, plus planet defenses.
    private float ExpectedEnemyPower()
    {
        BattleContext context = BattleContext.Instance;
        if (context == null || !context.hasPendingBattle) return float.PositiveInfinity; // Unknown: field everything.

        List<ShipEntry> enemy = IsAttackerSide ? context.defenderRoster : context.attackerRoster;
        float power = 0f;
        int room = populationCap;
        foreach (ShipEntry ship in (enemy ?? new List<ShipEntry>()).Where(s => s != null).OrderByDescending(s => s.combatPower))
        {
            if (ship.populationCost > room) continue;
            room -= ship.populationCost;
            power += ship.combatPower;
        }

        if (IsAttackerSide)
        {
            if (context.defenderHasShipyard) power += Strength.ShipyardPower;
            if (context.defenderHasBattleStation) power += Strength.BattleStationPower;
        }
        return power;
    }

    private float FieldPower()
    {
        float power = 0f;
        foreach (Unit unit in units) power += UnitPower(unit);
        foreach (UnitHealthManager defense in friendlyDefenses) power += BasePower(defense) * HealthFraction(defense);
        return power;
    }

    private float EnemyFieldPower()
    {
        float power = 0f;
        foreach (Contact contact in enemies) power += contact.power;
        return power;
    }

    private int FieldPopulation()
    {
        int population = 0;
        foreach (Unit unit in units)
        {
            if (unit.health != null && unit.health.IsFreeUnit) continue; // Launched craft cost no population.
            population += unit.ship != null ? unit.ship.populationCost : 1;
        }
        return population;
    }

    private List<ShipEntry> TakeDeployable()
    {
        List<ShipEntry> deployable = new List<ShipEntry>();
        int room = populationCap - FieldPopulation();

        foreach (ShipEntry ship in reserve)
        {
            if (ship == null || ship.populationCost > room) continue;
            deployable.Add(ship);
            room -= ship.populationCost;
        }
        return deployable;
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

    private float ReinforcementDelay => personality.reinforcementDelay * (1f - HeroForces.Buff(IsAttackerSide, HeroBuff.Reinforcement));

    private void Update()
    {
        if (Time.time < nextDecisionAt) return;
        nextDecisionAt = Time.time + decisionInterval;

        PruneUnits();
        Scan();
        if (enemies.Count == 0) return;

        ComputeGeometry();
        EvaluateStance();

        if (Retreating)
        {
            AssignTargets();
            UseAbilities();
            return;
        }

        ConsiderRetreat();
        if (Retreating)
        {
            AssignTargets();
            return;
        }

        Reinforce();
        AssignTargets();
        if (!ambushPlanned) PlanAmbush();
        UpdateAmbush();
        if (!probePlanned) PlanProbe();
        UpdateProbe();
        Maneuver();
        UseAbilities();
    }

    public bool Retreating => RetreatManager.Instance != null && RetreatManager.Instance.IsRetreating(IsAttackerSide);
    public bool Abandoning { get; private set; }
    public float Outlook { get; private set; } = 1f;

    private bool EnemyRetreating => RetreatManager.Instance != null && RetreatManager.Instance.IsRetreating(!IsAttackerSide);

    private void ConsiderRetreat()
    {
        RetreatManager retreat = RetreatManager.Instance;
        if (retreat == null || !personality.canRetreat) return;
        if (Faction != null && Faction.isNeutral) return; // Neutrals defend their home to the death.

        float ours = 0f;
        foreach (Unit unit in units)
        {
            ours += UnitPower(unit);
        }
        foreach (ShipEntry ship in reserve)
        {
            ours += ship != null ? ship.combatPower : Strength.DefaultShipPower;
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
        Outlook = Mathf.Lerp(Outlook, ratio, 0.5f);

        float threshold = personality.retreatRatio * (friendlyDefenses.Count > 0 ? personality.defendedRetreatFactor : 1f)
            * (1f - HeroForces.Buff(IsAttackerSide, HeroBuff.Resolve)); // A hero's presence holds the line.
        if (Outlook >= threshold)
        {
            Abandoning = false;
            return;
        }

        if (retreat.CanRetreat(IsAttackerSide))
        {
            retreat.BeginRetreat(IsAttackerSide);
        }
        else if (retreat.IsBlocked(IsAttackerSide) && reserve.Count > 0)
        {
            Abandoning = true;
        }
    }

    private void PruneUnits()
    {
        int before = units.Count;
        units.RemoveAll(u => u.health == null || u.health.IsDead);

        if (units.Count < before)
        {
            reinforceReadyAt = Mathf.Max(reinforceReadyAt, Time.time + ReinforcementDelay);
        }

        AdoptLaunchedCraft();

        foreach (Unit unit in units)
        {
            unit.dps = Dps(unit.hardpoints);
        }
    }

    // Squadrons our carriers launch mid-battle aren't in the roster we deployed from, so they'd otherwise sit
    // next to their carrier with nobody giving them orders. Take them under command.
    private void AdoptLaunchedCraft()
    {
        foreach (UnitHealthManager health in UnitHealthManager.Active)
        {
            if (health == null || health.IsDead || !health.IsFreeUnit || health.isAttackerSide != IsAttackerSide) continue;
            if (units.Exists(u => u.health == health)) continue;

            units.Add(new Unit
            {
                health = health,
                spaceUnit = health.GetComponent<SpaceUnit>(),
                hardpoints = health.GetComponent<HardpointManager>(),
                ship = health.sourceShip,
                squadron = health.GetComponent<Squadron>()
            });
        }
    }

    // A unit's share of our fighting power (launched strike craft count only partly - see LaunchedCraftPowerShare).
    private static float UnitPower(Unit unit)
    {
        float power = (unit.ship != null ? unit.ship.combatPower : Strength.DefaultShipPower) * HealthFraction(unit.health);
        return unit.health != null && unit.health.IsFreeUnit ? power * LaunchedCraftPowerShare : power;
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

            if (!BattleSensors.CanSee(IsAttackerSide, health)) continue; // Can't fight what we can't see.

            HardpointManager hardpoints = health.GetComponent<HardpointManager>();
            if (hardpoints == null || hardpoints.hardpoints.Count == 0) continue;

            SpaceUnit spaceUnit = health.GetComponent<SpaceUnit>();
            enemies.Add(new Contact
            {
                health = health,
                hardpoints = hardpoints,
                dps = Dps(hardpoints),
                power = BasePower(health) * HealthFraction(health) * (health.IsFreeUnit ? LaunchedCraftPowerShare : 1f),
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

        DetectKiting();
    }

    // The enemy out-ranges us and is plinking away from beyond our reach: we're losing hull while few of our
    // warships have anything in range. The only answer is to close (or leave) - never to hold position.
    private void DetectKiting()
    {
        float fieldHealth = 0f;
        int warships = 0;
        int inRange = 0;

        foreach (Unit unit in units)
        {
            if (unit.health == null) continue;
            fieldHealth += unit.health.currentHealth;
            if (unit.ship == null || !unit.ship.CanLead) continue;

            warships++;
            float range = unit.spaceUnit != null && unit.spaceUnit.maxRange > 0f ? unit.spaceUnit.maxRange : defaultRange;
            if (NearestEnemyShipDistance(unit.health.transform.position) <= range * 1.05f) inRange++;
        }

        bool takingFire = lastFieldHealth >= 0f && fieldHealth < lastFieldHealth - 0.5f;
        lastFieldHealth = fieldHealth;

        if (warships > 0 && takingFire && inRange < warships * KitedInRangeShare)
        {
            kitedUntil = Time.time + KitedHoldTime;
        }
    }

    private float NearestEnemyShipDistance(Vector3 point)
    {
        float nearest = float.MaxValue;
        foreach (Contact contact in enemies)
        {
            if (contact.health.IsFreeUnit) continue;
            nearest = Mathf.Min(nearest, Vector3.Distance(point, contact.health.transform.position));
        }
        return nearest;
    }

    private static bool IsCarrier(Unit unit)
    {
        return unit.ship != null && unit.ship.fleetRole == FleetRole.Carrier;
    }

    private void EvaluateStance()
    {
        float ours = 0f;
        foreach (Unit unit in units)
        {
            ours += UnitPower(unit);
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
        // Falling back only means something when there's something to fall back ON - our own shipyard or
        // battle station. With nothing there, retreating to the far edge of the map just hands the enemy
        // free shots: either commit to the fight or leave it (ConsiderRetreat). Same if we're being out-ranged.
        bool canRegroup = friendlyDefenses.Count > 0 && !Kited;

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
        if (Abandoning || reserve.Count == 0 || Time.time < reinforceReadyAt) return;

        List<ShipEntry> deploying;
        if (Difficulty.holdReserves)
        {
            // Only what it takes to keep the upper hand; the rest stays safe in reserve.
            float shortfall = EnemyFieldPower() * personality.pressAdvantageRatio - FieldPower();
            if (shortfall <= 0f && units.Count > 0) return;
            deploying = TakeUpTo(Mathf.Max(shortfall, 1f), null);
        }
        else
        {
            deploying = TakeDeployable();
        }
        if (deploying.Count == 0) return;

        Vector3 drop = DropPoint(ChooseDropZone());
        Quaternion facing = Quaternion.LookRotation(toEnemy);
        foreach (BattleDeployment.Placement placement in BattleDeployment.Arrange(deploying, null, drop, toEnemy))
        {
            Spawn(placement.ship, placement.position, facing, true);
        }

        reinforceReadyAt = Time.time + ReinforcementDelay;
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
            if (BattleTerrain.Instance != null && !BattleTerrain.Instance.CanDrop(candidate, IsAttackerSide)) continue;

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
        bool enemyRetreating = EnemyRetreating;

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

            if (enemyRetreating && !contact.health.IsDefense)
            {
                contact.priority *= LiveEngine(contact.hardpoints) != null ? 4f : 0.25f;
            }
        }

        enemies.Sort((a, b) => b.priority.CompareTo(a.priority));

        // Fighters flex: as many as the interceptors can't cover go after enemy craft; the rest strike ships.
        int enemyCraft = 0;
        foreach (Contact contact in enemies) if (contact.health.GetComponent<Squadron>() != null) enemyCraft++;
        int interceptors = units.Count(u => u.squadron != null && u.squadron.CraftRole == Squadron.Role.Interceptor && u.health != null && !u.health.IsDead);
        int fightersOnAir = Mathf.Max(0, enemyCraft - interceptors);

        foreach (Unit unit in units)
        {
            if (unit.hardpoints == null) continue;

            if (unit.squadron != null)
            {
                // Strike craft are tasked by role, not thrown at the fleet's focus target.
                bool antiAir = false;
                if (unit.squadron.CraftRole == Squadron.Role.Fighter && fightersOnAir > 0) { antiAir = true; fightersOnAir--; }
                Contact craftTarget = CraftTarget(unit, antiAir);
                if (craftTarget != null) AimAt(unit, craftTarget);
                continue; // Nothing for its role: it screens the fleet (DirectCraft) and fights what it meets.
            }

            Contact chosen = personality.canFocusFire ? PickFocusTarget() : NearestContact(unit.health.transform.position);
            if (Random.value < Difficulty.targetMistakeChance) chosen = NearestContact(unit.health.transform.position); // Tunnel vision.
            if (chosen == null) continue;

            chosen.incomingDps += unit.dps;
            AimAt(unit, chosen);
        }
    }

    // Strike craft judgement (both navies). Not hard rules: each squadron scores every target by how well it
    // suits its role, how much it's worth killing, how hurt it already is and how close it is, and takes the best.
    // Role leanings: bombers favour big hulls (battleships, carriers, cruisers) and keep clear of corvettes,
    // whose point defence shreds them, and of other craft; interceptors favour enemy strike craft, bombers most;
    // fighters are multirole - they lean toward enemy craft when the interceptors can't cover them all
    // (antiAir) and toward big ships otherwise.
    private Contact CraftTarget(Unit unit, bool antiAir)
    {
        Vector3 at = unit.health.transform.position;
        Squadron.Role role = unit.squadron.CraftRole;
        return BestContact(at, c => RoleFit(role, antiAir, c) * Worth(c));
    }

    private static float RoleFit(Squadron.Role role, bool antiAir, Contact contact)
    {
        if (contact.health.TryGetComponent(out Squadron enemy))
        {
            float craft = enemy.CraftRole == Squadron.Role.Bomber ? 1f : enemy.CraftRole == Squadron.Role.Fighter ? 0.8f : 0.65f;
            return role switch
            {
                Squadron.Role.Bomber => 0f,
                Squadron.Role.Interceptor => 1.2f * craft,
                _ => (antiAir ? 1.2f : 0.45f) * craft,
            };
        }
        if (contact.health.IsDefense) return role == Squadron.Role.Interceptor ? 0.05f : 0.3f;
        float hull = ShipClass(contact) switch
        {
            ShipType.Battleship => 1f,
            ShipType.Carrier => 0.9f,
            ShipType.Cruiser => 0.85f,
            ShipType.Destroyer => 0.55f,
            ShipType.Corvette => 0.3f,
            _ => 0.5f,
        };
        return role switch
        {
            Squadron.Role.Bomber => ShipClass(contact) == ShipType.Corvette ? 0.1f : hull,
            Squadron.Role.Interceptor => 0.12f * hull,
            _ => (antiAir ? 0.5f : 0.9f) * hull,
        };
    }

    // How much a kill is worth right now: dangerous targets, and ones already badly hurt, are worth more.
    private static float Worth(Contact contact)
    {
        float healthFraction = HealthFraction(contact.health);
        return (1f + Mathf.Clamp01(contact.dps / 150f)) * (1f + (1f - healthFraction) * 0.6f);
    }

    private static ShipType ShipClass(Contact contact) =>
        contact.health.TryGetComponent(out SpaceUnit unit) ? unit.shipType : ShipType.Station;

    // The best by weight, nearer ones preferred.
    private Contact BestContact(Vector3 from, System.Func<Contact, float> weight)
    {
        Contact best = null;
        float bestScore = 0f;
        foreach (Contact contact in enemies)
        {
            if (contact.health == null || contact.health.IsDead) continue;
            float w = weight(contact);
            if (w <= 0f) continue;
            float score = w / (1f + Vector3.Distance(from, contact.health.transform.position) / 300f);
            if (score > bestScore) { bestScore = score; best = contact; }
        }
        return best;
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

    private static HardpointHealth LiveEngine(HardpointManager hardpoints)
    {
        if (hardpoints == null) return null;

        foreach (HardpointHealth hardpoint in hardpoints.hardpoints)
        {
            if (hardpoint != null && hardpoint.GetComponent<EngineHardpoint>() != null) return hardpoint;
        }
        return null;
    }

    private void AimAt(Unit unit, Contact contact)
    {
        List<HardpointHealth> alive = contact.hardpoints.hardpoints;
        HardpointHealth engine = EnemyRetreating ? LiveEngine(contact.hardpoints) : null;

        if (engine != null)
        {
            if (unit.target == contact.health && unit.aimPoint == engine.transform) return;

            unit.target = contact.health;
            unit.aimPoint = engine.transform;
            unit.hardpoints.AssignTarget(engine.transform);
            return;
        }

        // A shielded ship: knock out its shield generator first, and everything after hits the hull.
        HardpointHealth generator = ShieldGenerator.LiveGenerator(contact.health.gameObject);
        if (generator != null && alive.Contains(generator) && Difficulty.level != DifficultyLevel.Cadet)
        {
            if (unit.target == contact.health && unit.aimPoint == generator.transform) return;
            unit.target = contact.health;
            unit.aimPoint = generator.transform;
            unit.hardpoints.AssignTarget(generator.transform);
            return;
        }

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

        bool closing = Kited && CurrentStance != Stance.FallBack;

        Vector3 front = CurrentStance switch
        {
            Stance.Press => enemyCenter - toEnemy * ourRange * 0.45f,
            Stance.FallBack => anchor + toEnemy * ourRange * 0.2f,
            _ => enemyCenter - toEnemy * ourRange * (closing ? 0.55f : 0.85f)
        };

        // Sieges are fought at the objective. Defenders with a station or shipyard hold close in front of it,
        // under its guns, rather than sallying out to meet the attack mid-field. Attackers push for the
        // enemy's defences, fighting whatever stands in the way as they go, instead of stopping to brawl
        // wherever they first meet the defending fleet.
        if (!IsAttackerSide && hasDefenses && CurrentStance != Stance.FallBack)
        {
            Vector3 outward = enemyCenter - anchor;
            outward.y = 0f;
            float hold = Mathf.Min(ourRange * 0.45f, outward.magnitude * 0.4f);
            front = anchor + (outward.sqrMagnitude > 0.01f ? outward.normalized : toEnemy) * hold;
        }
        else if (IsAttackerSide && CurrentStance != Stance.FallBack && EnemyDefenseCentre(out Vector3 objective))
        {
            Vector3 approach = objective - ourCenter;
            approach.y = 0f;
            Vector3 heading = approach.sqrMagnitude > 0.01f ? approach.normalized : toEnemy;
            // Close to gun range of the defences; the line advances steadily toward that point.
            Vector3 objectiveLine = objective - heading * ourRange * 0.75f;
            front = Vector3.MoveTowards(ourCenter, objectiveLine, Mathf.Max(ourRange * 0.6f, 40f));
        }

        // Carriers are back-line ships: they hold behind the battle line and let their strike craft do the
        // work. They only come forward once there's no line left to hide behind.
        // Strike craft aren't part of the battle line: they go after their targets, and once they're in
        // a fight they're left alone - re-ordering a squadron every time its target drifts keeps it in
        // "moving" mode, which stops its fighters from attacking.
        foreach (Unit unit in units)
        {
            if (unit.squadron != null && !InAmbush(unit)) DirectCraft(unit, front);
        }

        if (probing && probers.Count > 0)
        {
            // The probe: a line of its own, at gun range of the attackers' fleet wherever it has got to.
            Vector3 from = enemyCenter - anchor;
            from.y = 0f;
            Vector3 dir = from.sqrMagnitude > 0.01f ? from.normalized : toEnemy;
            PlaceRow(probers, enemyCenter - dir * ourRange * 0.8f, FieldSpacing(), false);
        }

        List<Unit> line = units.FindAll(u => !IsCarrier(u) && u.squadron == null && !InAmbush(u) && !InProbe(u));
        List<Unit> carriers = units.FindAll(IsCarrier);
        bool screened = line.Exists(u => u.ship != null && u.ship.CanLead);
        if (!screened)
        {
            line.AddRange(carriers);
            carriers.Clear();
        }

        float spacing = FieldSpacing();
        PlaceRow(line, front, spacing, closing);
        ChooseAltitudes();

        if (carriers.Count > 0)
        {
            float standoff = Mathf.Max(ourRange * 0.6f, 80f) + spacing;
            Vector3 rear = front - toEnemy * standoff;
            if (CurrentStance == Stance.FallBack) rear = anchor - toEnemy * spacing;
            PlaceRow(carriers, rear, spacing * 1.5f, false, false);
        }
    }

    // The AI fights in 3D too: each ship flies at the height of whatever it's shooting at, staggered a
    // little up and down so a battle line becomes a wall rather than a flat row. ShipAltitude's automatic
    // angling then turns its guns onto the target. With no target it holds the enemy fleet's average height.
    private const float AltitudeStagger = 20f;
    private const float AltitudeDeadband = 10f;

    private void ChooseAltitudes()
    {
        float enemyHeight = 0f;
        int counted = 0;
        foreach (Contact contact in enemies)
        {
            if (contact.health.IsDefense || contact.health.IsFreeUnit) continue;
            enemyHeight += ShipAltitude.AltitudeOf(contact.health.transform);
            counted++;
        }
        if (counted > 0) enemyHeight /= counted;

        for (int i = 0; i < units.Count; i++)
        {
            Unit unit = units[i];
            if (unit.health == null || !unit.health.TryGetComponent(out ShipAltitude altitude)) continue;

            float wanted;
            if (unit.squadron != null)
            {
                wanted = unit.target != null ? ShipAltitude.AltitudeOf(unit.target.transform) : enemyHeight;
            }
            else
            {
                float baseHeight = unit.target != null ? ShipAltitude.AltitudeOf(unit.target.transform) : enemyHeight;
                float stagger = (i % 3 - 1) * AltitudeStagger; // -1, 0, +1 steps.
                wanted = baseHeight + BlindSide(unit.target, altitude) + stagger;
            }

            if (Mathf.Abs(altitude.TargetAltitude - wanted) > AltitudeDeadband) altitude.SetTargetAltitude(wanted);
        }
    }

    // Height offset that puts us on the side of the target with fewer guns: under a ship whose firepower is
    // mostly on top, over one that's bottom-heavy. Cadets don't think in three dimensions.
    private const float BlindSideRatio = 1.5f;
    private const float BlindSideClearance = 40f;

    private float BlindSide(UnitHealthManager target, ShipAltitude self)
    {
        if (target == null || Difficulty.level == DifficultyLevel.Cadet) return 0f;
        if (!target.TryGetComponent(out ShipAltitude theirs)) return 0f;

        float dorsal = theirs.DorsalFirepower;
        float ventral = theirs.VentralFirepower;
        float offset = BlindSideClearance + theirs.HalfHeight + self.HalfHeight;
        if (dorsal > ventral * BlindSideRatio) return -offset;
        if (ventral > dorsal * BlindSideRatio) return offset;
        return 0f;
    }

    // ---------------- Ambush ----------------
    // Now and then the AI hides its corvettes and squadrons in an asteroid field between the fleets, holding
    // fire so the rocks keep them concealed (BattleSensors), and springs the trap when enemy ships pass close
    // - or if it's spotted, takes damage, or waits too long.
    private const float AmbushTriggerDistance = 170f;
    private const float AmbushTimeout = 120f;
    private readonly List<Unit> ambushers = new List<Unit>();
    private bool ambushPlanned;
    private bool ambushSprung;
    private Vector3 ambushPoint;
    private float ambushStartedAt;

    private bool InAmbush(Unit unit) => !ambushSprung && ambushers.Contains(unit);

    private void PlanAmbush()
    {
        ambushPlanned = true;
        float chance = Difficulty.level == DifficultyLevel.Cadet ? 0f : Difficulty.level == DifficultyLevel.Captain ? 0.3f : 0.45f;
        if (Random.value >= chance || BattleTerrain.Instance == null) return;

        List<Unit> candidates = units.FindAll(u => u.health != null && !u.health.IsFreeUnit
            && (u.squadron != null || (u.spaceUnit != null && u.spaceUnit.shipType == ShipType.Corvette)));
        if (candidates.Count < 2) return;

        // The (non-dense) field closest to the midpoint between the fleets.
        Vector3 middle = Vector3.Lerp(ourCenter, enemyCenter, 0.55f);
        float best = float.MaxValue;
        bool found = false;
        foreach (AsteroidField field in BattleTerrain.Instance.Fields)
        {
            if (field == null || field.density == AsteroidDensity.Dense) continue;
            foreach (AsteroidField.Blob blob in field.blobs)
            {
                Vector3 centre = new Vector3(blob.centre.x, 0f, blob.centre.y);
                float distance = Vector3.Distance(centre, middle);
                if (distance < best)
                {
                    best = distance;
                    ambushPoint = centre;
                    found = true;
                }
            }
        }
        if (!found || best > Vector3.Distance(ourCenter, enemyCenter)) return;

        ambushers.AddRange(candidates);
        ambushStartedAt = Time.time;
        for (int i = 0; i < ambushers.Count; i++)
        {
            Unit unit = ambushers[i];
            unit.health.HoldFire = true;
            Vector3 offset = Quaternion.Euler(0f, i * 137.5f, 0f) * Vector3.forward * (8f + i * 6f);
            MoveTo(unit, SnapToNavMesh(ambushPoint + offset));
        }
    }

    private void UpdateAmbush()
    {
        if (ambushSprung || ambushers.Count == 0) return;

        ambushers.RemoveAll(u => u.health == null || u.health.IsDead);
        bool enemyClose = false;
        foreach (Contact contact in enemies)
        {
            if (contact.health.IsDefense || contact.health.IsFreeUnit) continue;
            if (Vector3.Distance(contact.health.transform.position, ambushPoint) <= AmbushTriggerDistance) { enemyClose = true; break; }
        }
        bool hurt = ambushers.Exists(u => HealthFraction(u.health) < 0.97f);
        bool spotted = ambushers.Exists(u => BattleSensors.CanSee(!IsAttackerSide, u.health));

        if (ambushers.Count == 0 || enemyClose || hurt || spotted || Time.time - ambushStartedAt > AmbushTimeout)
        {
            ambushSprung = true;
            foreach (Unit unit in ambushers) unit.health.HoldFire = false;
        }
    }

    // ---------------- Probe ----------------
    // Recording only (set by SiegeRecording; off in the game). Defending a station or shipyard, the AI sends a detachment of its faster warships (corvettes, then
    // destroyers, then a cruiser) out to meet the attack mid-field, testing it before it reaches the
    // defences. After a spell of fighting - or once the detachment is badly hurt - the survivors fall back to
    // the station and rejoin the defensive line.
    private const float ProbeShare = 0.3f;          // Of the warships on the field.
    private const float ProbeMinTime = 70f, ProbeMaxTime = 110f; // Seconds of fighting before pulling back.
    private const float ProbeBreakHealth = 0.55f;   // Pull back early once the detachment is this battered.
    public static bool ProbeEnabled; // Only the dev siege recording turns this on.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetProbe() => ProbeEnabled = false; // Never carried into a normal play session.
    private readonly List<Unit> probers = new List<Unit>();
    private bool probePlanned;
    private float nextProbeLogAt;
    private bool probing;
    private float probeEngagedAt = float.NaN;
    private float probeLength;
    private float probeStartHealth;

    private bool InProbe(Unit unit) => probing && probers.Contains(unit);

    private void PlanProbe()
    {
        if (!ProbeEnabled || IsAttackerSide) { probePlanned = true; return; }
        if (friendlyDefenses.Count == 0) return; // The station may not be on the field yet.
        List<Unit> warships = units.FindAll(u => u.squadron == null && !IsCarrier(u) && u.spaceUnit != null
            && u.health != null && !u.health.IsFreeUnit && !InAmbush(u) && (u.health.entry == null || string.IsNullOrEmpty(u.health.entry.hero)));
        if (Time.time >= nextProbeLogAt)
        {
            nextProbeLogAt = Time.time + 20f;
            Debug.Log($"[AI] Defender probe check: {warships.Count} warships ({string.Join(", ", warships.ConvertAll(u => u.spaceUnit.shipType.ToString()))}), {friendlyDefenses.Count} defences");
        }
        if (warships.Count < 6) return; // Wait until the defence is on the field.
        probePlanned = true;

        int want = Mathf.Clamp(Mathf.RoundToInt(warships.Count * ProbeShare), 3, 8);
        int cruisers = 0;
        foreach (ShipType type in new[] { ShipType.Corvette, ShipType.Destroyer, ShipType.Cruiser })
        {
            foreach (Unit unit in warships)
            {
                if (probers.Count >= want) break;
                if (unit.spaceUnit.shipType != type) continue;
                // One heavier ship to anchor it - more only if there aren't enough light ships on the field.
                if (type == ShipType.Cruiser && cruisers++ >= 1 && probers.Count >= 3) break;
                probers.Add(unit);
            }
        }
        if (probers.Count < 3) { probers.Clear(); return; }

        probing = true;
        Debug.Log($"[AI] Defender probe sent out: {probers.Count} ships ({string.Join(", ", probers.ConvertAll(u => u.spaceUnit.shipType.ToString()))})");
        probeLength = Random.Range(ProbeMinTime, ProbeMaxTime);
        probeStartHealth = 0f;
        foreach (Unit unit in probers) probeStartHealth += unit.health.currentHealth;
    }

    private void UpdateProbe()
    {
        if (!probing) return;
        probers.RemoveAll(u => u.health == null || u.health.IsDead);
        if (probers.Count == 0) { probing = false; return; }

        // The clock starts once the detachment is trading fire.
        if (float.IsNaN(probeEngagedAt) && probers.Exists(u => u.target != null
            && Vector3.Distance(u.health.transform.position, u.target.transform.position) <= ourRange * 1.1f))
            probeEngagedAt = Time.time;

        float health = 0f;
        foreach (Unit unit in probers) health += unit.health.currentHealth;
        bool battered = probeStartHealth > 0f && health / probeStartHealth < ProbeBreakHealth;
        bool done = !float.IsNaN(probeEngagedAt) && Time.time - probeEngagedAt > probeLength;
        if (battered || done)
        {
            Debug.Log($"[AI] Defender probe pulling back after {(float.IsNaN(probeEngagedAt) ? 0f : Time.time - probeEngagedAt):F0}s of fighting, {probers.Count} ships left ({(probeStartHealth > 0f ? health / probeStartHealth : 0f):P0} health){(battered ? " - battered" : "")}");
            probing = false; // Back to the station: they're part of the defensive line again.
            foreach (Unit unit in probers) unit.lineKey = float.NaN;
        }
    }

    // Fires abilities when they'd actually help. Cadets often forget to.
    private void UseAbilities()
    {
        float reliability = Difficulty.level == DifficultyLevel.Cadet ? 0.3f : Difficulty.level == DifficultyLevel.Captain ? 0.75f : 1f;

        foreach (Unit unit in units)
        {
            if (unit.health == null || !unit.health.TryGetComponent(out ShipAbility ability) || !ability.Ready) continue;
            if (!WantsAbility(unit, ability) || Random.value > reliability) continue;
            ability.TryActivate();
        }
    }

    private bool WantsAbility(Unit unit, ShipAbility ability)
    {
        float range = unit.spaceUnit != null && unit.spaceUnit.maxRange > 0f ? unit.spaceUnit.maxRange : defaultRange;
        float targetDistance = unit.target != null ? Vector3.Distance(unit.target.transform.position, unit.health.transform.position) : float.MaxValue;
        float nearest = NearestEnemyShipDistance(unit.health.transform.position);

        switch (ability.kind)
        {
            case ShipAbility.Kind.Overcharge:
            case ShipAbility.Kind.Uplink:
                return targetDistance <= range * 1.1f;
            case ShipAbility.Kind.ShieldSurge:
                return unit.health.TryGetComponent(out ShieldGenerator shield) && shield.Capacity > 0f && shield.Current / shield.Capacity < 0.35f && nearest <= range * 1.5f;
            case ShipAbility.Kind.Afterburners:
                return Retreating || Kited || (CurrentStance != Stance.FallBack && targetDistance > range * 1.3f && targetDistance < range * 4f);
            case ShipAbility.Kind.Evasive:
                return HealthFraction(unit.health) < 0.7f && nearest <= range * 1.2f;
            case ShipAbility.Kind.Scramble:
                return nearest <= range * 3f && HangarShort(unit.health);
            default:
                return false;
        }
    }

    private static bool HangarShort(UnitHealthManager carrier)
    {
        foreach (HangarHardpoint hangar in carrier.GetComponentsInChildren<HangarHardpoint>())
        {
            if (hangar.IsDestroyed) continue;
            for (int bay = 0; bay < hangar.complement.Count; bay++)
            {
                if (hangar.complement[bay] != null && hangar.ActiveCount(bay) < hangar.complement[bay].count) return true;
            }
        }
        return false;
    }

    private void DirectCraft(Unit unit, Vector3 front)
    {
        Squadron squadron = unit.squadron;
        if (squadron.CurrentTarget != null && !squadron.CurrentTarget.IsDead
            && Vector3.Distance(squadron.transform.position, squadron.CurrentTarget.transform.position) <= squadron.engageRadius)
        {
            return; // In the fight - let the fighters work.
        }

        Vector3 destination = front;
        if (unit.target != null && CurrentStance != Stance.FallBack)
        {
            Vector3 targetPosition = unit.target.transform.position;
            Vector3 approach = squadron.transform.position - targetPosition;
            approach.y = 0f;
            approach = approach.sqrMagnitude > 0.01f ? approach.normalized : -toEnemy;
            destination = targetPosition + approach * squadron.dogfightRadius * 0.5f;
        }
        MoveTo(unit, SnapToNavMesh(destination));
    }

    private void PlaceRow(List<Unit> row, Vector3 centre, float spacing, bool closing, bool approachTargets = true)
    {
        List<Unit> ordered = new List<Unit>(row);
        foreach (Unit unit in ordered)
        {
            if (float.IsNaN(unit.lineKey)) unit.lineKey = Vector3.Dot(unit.health.transform.position - centre, lateral);
        }
        ordered.Sort((a, b) => a.lineKey.CompareTo(b.lineKey)); // Each ship keeps its place; nobody crosses the line.

        List<float> slots = new List<float>();
        for (int i = 0; i < ordered.Count; i++)
        {
            slots.Add(SlotOffset(i));
        }
        slots.Sort();

        for (int i = 0; i < ordered.Count; i++)
        {
            Unit unit = ordered[i];
            Vector3 destination = centre + lateral * slots[i] * spacing;

            if (approachTargets && CurrentStance != Stance.FallBack && unit.target != null)
            {
                float range = unit.spaceUnit != null && unit.spaceUnit.maxRange > 0f ? unit.spaceUnit.maxRange : defaultRange;
                Vector3 targetPosition = unit.target.transform.position;
                float keep = closing ? 0.7f : 0.8f;
                if (Vector3.Distance(destination, targetPosition) > range * 0.95f)
                {
                    Vector3 approach = (destination - targetPosition).normalized;
                    destination = targetPosition + approach * range * keep;
                }
            }

            MoveTo(unit, SnapToNavMesh(destination));
        }
    }

    // Reinforcements can't drop inside asteroids or near an enemy shipyard/station - slide to the nearest clear spot.
    private Vector3 DropPoint(Vector3 point)
    {
        return BattleTerrain.Instance != null ? BattleTerrain.Instance.NearestDropPoint(point, IsAttackerSide) : point;
    }

    // Through an asteroid field: a priority move keeps full speed but scrapes the hull. Worth it when
    // retreating, for small hulls that barely notice, or for healthy ships pressing an attack.
    private bool WantsPriority(Unit unit, Vector3 destination)
    {
        BattleTerrain terrain = BattleTerrain.Instance;
        if (terrain == null || unit.spaceUnit == null) return false;

        if (Difficulty.priorityMoveSkill <= 0) return false;

        Vector3 from = unit.spaceUnit.transform.position;
        if (!terrain.InField(from) && !terrain.CrossesField(from, destination)) return false;
        if (Retreating) return true;
        if (Difficulty.priorityMoveSkill < 2) return false; // Only knows to rush out when escaping.
        if (BattleTerrain.SizeFactor(unit.spaceUnit.shipType) <= 0.55f) return true;
        // Capital ships go around the rocks rather than grind their hulls through them (except to escape, above) -
        // measured, a battleship pressing or closing a range gap lost most of a hull that way. Escorts still do it.
        bool capital = unit.spaceUnit.shipType == ShipType.Battleship || unit.spaceUnit.shipType == ShipType.Carrier || unit.spaceUnit.shipType == ShipType.Cruiser;
        if (capital) return false;
        if (Kited && HealthFraction(unit.health) > 0.35f) return true; // Scraping the hull beats being shot from range.
        return CurrentStance == Stance.Press && HealthFraction(unit.health) > 0.6f && personality.aggression >= 1f;
    }

    private void MoveTo(Unit unit, Vector3 destination)
    {
        SpaceUnit spaceUnit = unit.spaceUnit;
        if (spaceUnit == null) return;

        BattleTerrain.SetAIPriority(spaceUnit, WantsPriority(unit, destination));

        if (spaceUnit.moveState == null || spaceUnit.stateMachine == null)
        {
            SteerAgent(unit, destination);
            return;
        }

        bool moving = (Object)spaceUnit.stateMachine.currentState == spaceUnit.moveState;
        float threshold = RepositionThreshold(unit);
        bool sameOrder = Vector3.Distance(destination, unit.lastDestination) < threshold;
        if (moving && sameOrder) return;
        if (!moving && Vector3.Distance(spaceUnit.transform.position, destination) < threshold) return;
        if (!moving && sameOrder && Time.time - unit.lastOrderedAt < reissueCooldown) return;
        // Big ships commit to a course: a fresh order only every so often, unless the line has moved a long way.
        if (IsLineCapital(unit) && Time.time - unit.lastOrderedAt < CapitalOrderInterval
            && Vector3.Distance(destination, unit.lastDestination) < threshold * 2.5f) return;

        unit.lastDestination = destination;
        unit.lastOrderedAt = Time.time;
        spaceUnit.moveState.ClearDestinations();
        spaceUnit.moveState.AddDestination(destination);
        spaceUnit.moveState.SetFacing(CombatFacing.FightingHeading(spaceUnit.gameObject, toEnemy, destination - spaceUnit.transform.position));
        spaceUnit.moveState.SetOrderGroup(orderGroup);

        if (!moving)
        {
            spaceUnit.stateMachine.SetState(spaceUnit.moveState);
        }
    }

    // Battleships, cruisers and carriers hold a firing line: small drifts in where the line "should" be
    // don't send them weaving about. Escorts react more quickly.
    private const float CapitalOrderInterval = 12f;

    private static bool IsLineCapital(Unit unit) => unit.spaceUnit != null
        && (unit.spaceUnit.shipType == ShipType.Battleship || unit.spaceUnit.shipType == ShipType.Cruiser || unit.spaceUnit.shipType == ShipType.Carrier);

    private float RepositionThreshold(Unit unit)
    {
        if (unit.squadron != null || unit.spaceUnit == null) return repositionThreshold;
        float range = unit.spaceUnit.maxRange > 0f ? unit.spaceUnit.maxRange : defaultRange;
        return Mathf.Max(repositionThreshold, range * (IsLineCapital(unit) ? 0.3f : 0.12f));
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

    // Ships present when the battle starts are simply there; only reinforcements jump in from FTL.
    private void Spawn(ShipEntry ship, Vector3 position, Quaternion rotation, bool jumpIn = false, float altitude = 0f)
    {
        reserve.Remove(ship);
        if (GameManager.Instance == null) return;

        GameObject spawned = GameManager.Instance.SpawnShip(ship, SnapToNavMesh(position), rotation, IsAttackerSide);
        if (spawned == null) return;
        if (spawned.TryGetComponent(out ShipAltitude height)) height.SnapAltitude(altitude);
        if (jumpIn) FTLJump.Arrive(spawned);

        units.Add(new Unit
        {
            health = spawned.GetComponent<UnitHealthManager>(),
            spaceUnit = spawned.GetComponent<SpaceUnit>(),
            hardpoints = spawned.GetComponent<HardpointManager>(),
            ship = ship,
            squadron = spawned.GetComponent<Squadron>()
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

    // Where the enemy's station/shipyard are (their average position), if they have any left.
    private bool EnemyDefenseCentre(out Vector3 centre)
    {
        centre = Vector3.zero;
        int count = 0;
        foreach (Contact contact in enemies)
        {
            if (!contact.health.IsDefense || contact.health.IsDead) continue;
            centre += contact.health.transform.position;
            count++;
        }
        if (count == 0) return false;
        centre /= count;
        return true;
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
            if (turret.targetFilter == TurretController.TargetFilter.FightersOnly) continue;
            dps += turret.damage * Mathf.Max(1, turret.firingPoints.Count) / Mathf.Max(0.05f, turret.fireRate);
        }

        if (hardpoints.TryGetComponent(out Squadron squadron))
        {
            dps += squadron.Dps();
        }
        return dps;
    }
}
