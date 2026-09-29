using System.Collections.Generic;
using UnityEngine;

public partial class FactionBrain
{
    public const float GrievanceScale = 500f;
    public const float PlanetCaptureWorth = 150f;
    public const float BattleInsult = 50f;
    public const float BorderPressureRate = 0.25f;
    public const float MinimumGrudge = 25f;
    public const int LogCapacity = 10;

    public enum OrderKind { Defend, Garrison, Stage, Attack, Expand, Rally }

    public class FleetOrder
    {
        public string fleetName;
        public OrderKind kind;
        public string planetName;
    }

    public class PlanetIntel
    {
        public float power;
        public Faction faction;
        public float seenAt;
    }

    public class Campaign
    {
        public Faction target;
        public string objectivePlanet;
        public string stagingPlanet;
        public float startedAt;
        public float powerLost;
        public float enemyPowerDestroyed;
        public int planetsTaken;
        public float score;
        public float requiredPower;
        public float stagedPower;
        public bool suspended;

        public float Success => (enemyPowerDestroyed + planetsTaken * PlanetCaptureWorth + 100f) / (powerLost + 100f);
    }

    public readonly Faction faction;
    public readonly AIPersonality personality;

    public readonly Dictionary<Faction, float> grievance = new Dictionary<Faction, float>();
    public readonly Dictionary<string, PlanetIntel> intel = new Dictionary<string, PlanetIntel>();
    public readonly List<Campaign> campaigns = new List<Campaign>();
    public readonly Dictionary<string, FleetOrder> orders = new Dictionary<string, FleetOrder>();
    public readonly List<string> log = new List<string>();

    public readonly List<Planet> owned = new List<Planet>();
    public readonly HashSet<Planet> ownedSet = new HashSet<Planet>();
    public readonly HashSet<Planet> visible = new HashSet<Planet>();
    public readonly List<GalacticFleet> myFleets = new List<GalacticFleet>();
    public readonly Dictionary<Planet, float> threat = new Dictionary<Planet, float>();
    public readonly Dictionary<Planet, int> hostileDepth = new Dictionary<Planet, int>();
    public readonly Dictionary<ShipType, float> enemyComposition = new Dictionary<ShipType, float>();

    public float nextThinkAt;
    public float TotalPower { get; private set; }
    public float TotalThreat { get; private set; }
    public float MaxBorderThreat { get; private set; }
    public float IncomePerMinute { get; private set; }
    public Faction PrimaryThreat { get; private set; }
    public bool IsEliminated => owned.Count == 0 && myFleets.Count == 0;

    private readonly Dictionary<Faction, float> borderPresence = new Dictionary<Faction, float>();
    private readonly Dictionary<Faction, float> slotPower = new Dictionary<Faction, float>();
    private readonly HashSet<Planet> borderPlanets = new HashSet<Planet>();
    private readonly Queue<Planet> bfsQueue = new Queue<Planet>();

    private GalacticWorld world;
    private float now;
    private float lastThinkAt = -1f;

    public FactionBrain(Faction faction, AIPersonality personality)
    {
        this.faction = faction;
        this.personality = personality;
    }

    public void Think(GalacticWorld world, float now)
    {
        this.world = world;
        float elapsed = lastThinkAt < 0f ? 0f : now - lastThinkAt;
        this.now = now;
        lastThinkAt = now;

        Perceive();
        UpdateRelations(elapsed);

        if (IsEliminated) return;

        BuildShip = ChooseShip();
        UpdateCampaigns();
        PlanMilitary();
        PlanEconomy();
    }

    public float Grievance(Faction other)
    {
        return other != null && grievance.TryGetValue(other, out float value) ? value : 0f;
    }

    public float EstimatedFleetPower(Planet planet)
    {
        return EstimatedFleetPower(planet, out _);
    }

    public float EstimatedFleetPower(Planet planet, out Faction dominant)
    {
        dominant = null;
        if (planet == null || !intel.TryGetValue(planet.planetName, out PlanetIntel entry)) return 0f;

        dominant = entry.faction;
        float age = now - entry.seenAt;
        return age <= personality.intelMemory ? entry.power : entry.power * 0.5f;
    }

    public float EstimatedFactionPower(Faction other)
    {
        float total = 0f;
        foreach (PlanetIntel entry in intel.Values)
        {
            if (entry.faction == other)
            {
                total += entry.power;
            }
        }
        return total;
    }

    public int HostileDepth(Planet planet)
    {
        return planet != null && hostileDepth.TryGetValue(planet, out int depth) ? depth : int.MaxValue;
    }

    public float ThreatAt(Planet planet)
    {
        return planet != null && threat.TryGetValue(planet, out float value) ? value : 0f;
    }

    private void Perceive()
    {
        owned.Clear();
        ownedSet.Clear();
        visible.Clear();
        myFleets.Clear();
        threat.Clear();
        borderPresence.Clear();
        borderPlanets.Clear();

        foreach (Planet planet in world.planets)
        {
            if (planet.owner == faction)
            {
                owned.Add(planet);
                ownedSet.Add(planet);
            }
        }

        float totalPower = 0f;
        foreach (GalacticFleet fleet in world.fleets)
        {
            if (fleet.faction != faction) continue;

            myFleets.Add(fleet);
            totalPower += Strength.Of(fleet);
        }
        TotalPower = totalPower;

        GalacticVisibility.Compute(faction, world.planets, world.fleets, visible);

        foreach (Planet planet in owned)
        {
            foreach (Planet neighbor in planet.connections)
            {
                if (neighbor == null) continue;

                if (!ownedSet.Contains(neighbor))
                {
                    borderPlanets.Add(neighbor);
                }
            }
        }

        foreach (Planet planet in visible)
        {
            ScoutPlanet(planet);
        }

        ComputeHostileDepth();
        ComputeThreat();
    }

    private void ScoutPlanet(Planet planet)
    {
        slotPower.Clear();
        float power = 0f;
        Faction dominant = null;
        float dominantPower = 0f;

        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet fleet = planet.GetFleetInSlot(slot);
            if (fleet == null || fleet.faction == faction || fleet.IsTraveling || fleet.roster.Count == 0) continue;

            float fleetPower = Strength.Of(fleet);
            power += fleetPower;
            RecordComposition(fleet);

            slotPower.TryGetValue(fleet.faction, out float factionPower);
            factionPower += fleetPower;
            slotPower[fleet.faction] = factionPower;

            if (factionPower > dominantPower)
            {
                dominantPower = factionPower;
                dominant = fleet.faction;
            }
        }

        if (borderPlanets.Contains(planet))
        {
            foreach (KeyValuePair<Faction, float> entry in slotPower)
            {
                if (entry.Key == null) continue;

                borderPresence.TryGetValue(entry.Key, out float presence);
                borderPresence[entry.Key] = presence + entry.Value;
            }
        }

        if (!intel.TryGetValue(planet.planetName, out PlanetIntel record))
        {
            record = new PlanetIntel();
            intel[planet.planetName] = record;
        }

        record.power = power;
        record.faction = dominant != null ? dominant : planet.owner;
        record.seenAt = now;
    }

    private void RecordComposition(GalacticFleet fleet)
    {
        foreach (Ship ship in fleet.roster)
        {
            if (ship == null) continue;

            ShipType type = TypeOf(ship);
            enemyComposition.TryGetValue(type, out float seen);
            enemyComposition[type] = seen + ship.combatPower;
        }
    }

    private static readonly Dictionary<Ship, ShipType> shipTypes = new Dictionary<Ship, ShipType>();

    public static ShipType TypeOf(Ship ship)
    {
        if (!shipTypes.TryGetValue(ship, out ShipType type))
        {
            SpaceUnit unit = ship.prefab != null ? ship.prefab.GetComponent<SpaceUnit>() : null;
            type = unit != null ? unit.shipType : ShipType.Cruiser;
            shipTypes[ship] = type;
        }
        return type;
    }

    private void ComputeHostileDepth()
    {
        hostileDepth.Clear();
        bfsQueue.Clear();

        foreach (Planet planet in world.planets)
        {
            if (planet.owner != null && planet.owner != faction)
            {
                hostileDepth[planet] = 0;
                bfsQueue.Enqueue(planet);
            }
        }

        while (bfsQueue.Count > 0)
        {
            Planet current = bfsQueue.Dequeue();
            int depth = hostileDepth[current];

            foreach (Planet neighbor in current.connections)
            {
                if (neighbor == null || hostileDepth.ContainsKey(neighbor)) continue;

                hostileDepth[neighbor] = depth + 1;
                bfsQueue.Enqueue(neighbor);
            }
        }
    }

    private void ComputeThreat()
    {
        float totalThreat = 0f;

        foreach (Planet border in borderPlanets)
        {
            float power = EstimatedFleetPower(border, out Faction source);
            totalThreat += power * ThreatWeight(source);
        }

        foreach (Planet planet in owned)
        {
            float power = EstimatedFleetPower(planet, out Faction occupier) * ThreatWeight(occupier);

            foreach (Planet neighbor in planet.connections)
            {
                if (neighbor == null || ownedSet.Contains(neighbor)) continue;

                power += EstimatedFleetPower(neighbor, out Faction source) * ThreatWeight(source);
            }

            threat[planet] = power;
            totalThreat += EstimatedFleetPower(planet);
        }

        TotalThreat = totalThreat;
    }

    private float ThreatWeight(Faction source)
    {
        return 1f + 0.5f * Mathf.Clamp01(Grievance(source) / GrievanceScale);
    }

    private void UpdateRelations(float elapsed)
    {
        if (elapsed > 0f)
        {
            float decay = Mathf.Pow(0.5f, elapsed / Mathf.Max(1f, personality.grudgeHalfLife));
            List<Faction> keys = new List<Faction>(grievance.Keys);
            foreach (Faction key in keys)
            {
                grievance[key] *= decay;
            }

            float intelDecay = Mathf.Pow(0.5f, elapsed / Mathf.Max(1f, personality.intelMemory));
            List<ShipType> types = new List<ShipType>(enemyComposition.Keys);
            foreach (ShipType type in types)
            {
                enemyComposition[type] *= intelDecay;
            }

            foreach (KeyValuePair<Faction, float> entry in borderPresence)
            {
                AddGrievance(entry.Key, entry.Value * BorderPressureRate * elapsed / 60f);
            }
        }

        float maxBorderThreat = 0f;
        foreach (KeyValuePair<Faction, float> entry in borderPresence)
        {
            maxBorderThreat = Mathf.Max(maxBorderThreat, entry.Value * ThreatWeight(entry.Key));
        }
        MaxBorderThreat = maxBorderThreat;

        PrimaryThreat = null;
        float worst = MinimumGrudge;
        foreach (KeyValuePair<Faction, float> entry in grievance)
        {
            if (entry.Value > worst)
            {
                worst = entry.Value;
                PrimaryThreat = entry.Key;
            }
        }
    }

    private void AddGrievance(Faction other, float amount)
    {
        if (other == null || other == faction || other.isNeutral || amount <= 0f) return;

        grievance.TryGetValue(other, out float current);
        grievance[other] = current + amount;
    }

    public void OnPlanetCaptured(Planet planet, Faction previousOwner, Faction newOwner)
    {
        if (planet == null) return;

        float worth = world != null ? world.PlanetValue(planet) * PlanetCaptureWorth : PlanetCaptureWorth;

        if (previousOwner == faction && newOwner != null && newOwner != faction)
        {
            AddGrievance(newOwner, worth);
            Log($"Lost {planet.planetName} to {Name(newOwner)}");
        }

        if (newOwner == faction && previousOwner != null)
        {
            foreach (Campaign campaign in campaigns)
            {
                if (campaign.target == previousOwner)
                {
                    campaign.planetsTaken++;
                }
            }
            Log($"Took {planet.planetName} from {Name(previousOwner)}");
        }
    }

    public void OnBattleResolved(BattleReport report)
    {
        if (report.defender == faction)
        {
            AddGrievance(report.attacker, report.defenderLosses + BattleInsult);
        }

        foreach (Campaign campaign in campaigns)
        {
            if (report.attacker == faction && report.defender == campaign.target)
            {
                campaign.powerLost += report.attackerLosses;
                campaign.enemyPowerDestroyed += report.defenderLosses;
            }
            else if (report.defender == faction && report.attacker == campaign.target)
            {
                campaign.powerLost += report.defenderLosses;
                campaign.enemyPowerDestroyed += report.attackerLosses;
            }
        }

        if (report.attacker == faction || report.defender == faction)
        {
            bool won = (report.attacker == faction) == report.attackerWon;
            Faction other = report.attacker == faction ? report.defender : report.attacker;
            string where = report.planet != null ? report.planet.planetName : "?";
            Log($"{(won ? "Won" : "Lost")} battle vs {Name(other)} at {where}");
        }
    }

    private void Log(string message)
    {
        log.Insert(0, $"[{now:0}s] {message}");
        if (log.Count > LogCapacity)
        {
            log.RemoveAt(log.Count - 1);
        }
    }

    public static string Name(Faction other)
    {
        return other != null ? other.factionName : "Unclaimed";
    }
}
