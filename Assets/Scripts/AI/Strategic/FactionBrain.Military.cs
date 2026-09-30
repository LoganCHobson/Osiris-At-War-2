using System.Collections.Generic;
using UnityEngine;

public partial class FactionBrain
{
    public class MilitaryTask
    {
        public string key;
        public OrderKind kind;
        public string planetName;
        public float required;
        public float priority;
        public float assigned;
        public float lastNeededAt;
        public bool critical;
    }

    public class Assignment
    {
        public string taskKey;
        public OrderKind kind;
        public string planetName;
        public float since;
    }

    public const string RallyKey = "Rally";

    public readonly Dictionary<Faction, float> targetScores = new Dictionary<Faction, float>();
    public readonly Dictionary<string, MilitaryTask> taskBook = new Dictionary<string, MilitaryTask>();
    public readonly Dictionary<string, Assignment> assignments = new Dictionary<string, Assignment>();
    public readonly List<MilitaryTask> activeTasks = new List<MilitaryTask>();
    public float RequiredPower { get; private set; }
    public string RallyPlanet { get; private set; }
    public bool CampaignsSuspended { get; private set; }

    private readonly Dictionary<Faction, Planet> bestObjectives = new Dictionary<Faction, Planet>();
    private readonly Dictionary<Faction, float> bestObjectiveScores = new Dictionary<Faction, float>();
    private readonly HashSet<string> neededThisThink = new HashSet<string>();
    private readonly HashSet<string> liveFleetNames = new HashSet<string>();
    private readonly List<GalacticFleet> idle = new List<GalacticFleet>();
    private readonly List<string> expiredKeys = new List<string>();

    private float ShipPower
    {
        get
        {
            float best = 0f;
            foreach (Ship ship in faction.roster)
            {
                if (ship != null && ship.fleetRole == FleetRole.Line) best = Mathf.Max(best, ship.combatPower);
            }
            return best > 0f ? best : Strength.DefaultShipPower;
        }
    }

    private void UpdateCampaigns()
    {
        ScoreTargets();

        float pressure = MaxBorderThreat * personality.defensiveness / Mathf.Max(1f, TotalPower * (0.8f + personality.aggression * 0.5f));
        bool wasSuspended = CampaignsSuspended;
        CampaignsSuspended = wasSuspended ? pressure > 0.7f : pressure > 1f;

        if (CampaignsSuspended != wasSuspended && campaigns.Count > 0)
        {
            Log(CampaignsSuspended ? "Home front threatened - offensives paused" : "Home front stable - offensives resumed");
        }

        for (int i = campaigns.Count - 1; i >= 0; i--)
        {
            Campaign campaign = campaigns[i];

            if (!targetScores.TryGetValue(campaign.target, out float score))
            {
                EndCampaign(i, $"No front left with {Name(campaign.target)}");
                continue;
            }

            float age = now - campaign.startedAt;
            bool matured = age >= personality.minCampaignDuration;
            campaign.score = score * 1.25f * (matured ? Mathf.Clamp(campaign.Success, 0.25f, 2f) : 1f);

            if (matured && campaign.powerLost >= ShipPower * 2f && campaign.Success < 1f / personality.campaignGiveUpRatio)
            {
                EndCampaign(i, $"Giving up on {Name(campaign.target)} (lost {campaign.powerLost:0}, took {campaign.planetsTaken})");
            }
        }

        if (personality.canSwitchCampaigns)
        {
            for (int i = campaigns.Count - 1; i >= 0; i--)
            {
                if (now - campaigns[i].startedAt < personality.minCampaignDuration) continue;

                Faction alternative = BestUntargetedFaction(out float alternativeScore);
                if (alternative == null) break;

                if (alternativeScore > campaigns[i].score * (1f + personality.campaignSwitchMargin))
                {
                    Faction previousTarget = campaigns[i].target;
                    EndCampaign(i, $"Dropping {Name(previousTarget)} - {Name(alternative)} looks more fruitful");
                    StartCampaign(alternative);
                }
            }
        }

        float surplus = Mathf.Max(0f, TotalPower - RequiredPower) / Mathf.Max(1f, ShipPower * personality.strikeMinShips);
        float startThreshold = personality.campaignStartThreshold / (1f + surplus);
        int campaignLimit = personality.maxCampaigns + (surplus >= 3f ? 1 : 0);

        while (campaigns.Count < campaignLimit && !CampaignsSuspended)
        {
            Faction candidate = BestUntargetedFaction(out float candidateScore);
            if (candidate == null || candidateScore < startThreshold) break;

            StartCampaign(candidate);
        }

        foreach (Campaign campaign in campaigns)
        {
            campaign.suspended = CampaignsSuspended;
            RefreshCampaignObjective(campaign);
        }
    }

    private void ScoreTargets()
    {
        targetScores.Clear();
        bestObjectives.Clear();
        bestObjectiveScores.Clear();

        foreach (Planet planet in owned)
        {
            foreach (Planet neighbor in planet.connections)
            {
                if (neighbor == null || neighbor.owner == null || neighbor.owner == faction) continue;

                float score = ScoreObjective(neighbor);
                if (!bestObjectiveScores.TryGetValue(neighbor.owner, out float best) || score > best)
                {
                    bestObjectiveScores[neighbor.owner] = score;
                    bestObjectives[neighbor.owner] = neighbor;
                }
            }
        }

        foreach (KeyValuePair<Faction, float> entry in bestObjectiveScores)
        {
            Faction target = entry.Key;
            float grudge = Mathf.Clamp(Grievance(target) / GrievanceScale, 0f, 2f);
            float weakness = Mathf.Clamp(TotalPower / (EstimatedFactionPower(target) + ShipPower), 0.25f, 3f);
            targetScores[target] = entry.Value * personality.aggression * (1f + personality.vengefulness * grudge) * Mathf.Sqrt(weakness);
        }
    }

    private float ScoreObjective(Planet target)
    {
        float required = RequiredToTake(target);
        float feasibility = Mathf.Clamp01(TotalPower / required);
        return world.PlanetValue(target) * feasibility / (1f + required / (ShipPower * 5f));
    }

    private float RequiredToTake(Planet target)
    {
        float defense = EstimatedFleetPower(target) + Strength.Defenses(target);
        return Mathf.Max(ShipPower * personality.strikeMinShips, defense * personality.attackPowerMargin);
    }

    private Faction BestUntargetedFaction(out float bestScore)
    {
        Faction best = null;
        bestScore = 0f;

        foreach (KeyValuePair<Faction, float> entry in targetScores)
        {
            if (IsCampaignTarget(entry.Key)) continue;

            if (entry.Value > bestScore)
            {
                bestScore = entry.Value;
                best = entry.Key;
            }
        }
        return best;
    }

    private bool IsCampaignTarget(Faction target)
    {
        foreach (Campaign campaign in campaigns)
        {
            if (campaign.target == target) return true;
        }
        return false;
    }

    private void StartCampaign(Faction target)
    {
        Campaign campaign = new Campaign { target = target, startedAt = now };
        campaigns.Add(campaign);
        RefreshCampaignObjective(campaign);
        Log($"Campaign started against {Name(target)}");
    }

    private void EndCampaign(int index, string reason)
    {
        campaigns.RemoveAt(index);
        Log(reason);
    }

    private void RefreshCampaignObjective(Campaign campaign)
    {
        if (!bestObjectives.TryGetValue(campaign.target, out Planet objective)) return;

        Planet current = world.FindPlanet(campaign.objectivePlanet);
        bool currentValid = current != null && current.owner == campaign.target && BordersOwned(current);
        if (currentValid && current != objective && ScoreObjective(objective) < ScoreObjective(current) * (1f + personality.objectiveSwitchMargin))
        {
            objective = current;
        }

        Planet staging = world.FindPlanet(campaign.stagingPlanet);
        if (staging == null || !ownedSet.Contains(staging) || !staging.IsConnectedTo(objective))
        {
            staging = ChooseStaging(objective);
        }

        if (staging == null) return;

        if (campaign.objectivePlanet != objective.planetName)
        {
            campaign.objectivePlanet = objective.planetName;
            Log($"Next objective vs {Name(campaign.target)}: {objective.planetName} (staging {staging.planetName})");
        }

        campaign.stagingPlanet = staging.planetName;
        campaign.requiredPower = RequiredToTake(objective);
    }

    private Planet ChooseStaging(Planet objective)
    {
        Planet staging = null;
        float bestScore = float.MinValue;

        foreach (Planet neighbor in objective.connections)
        {
            if (neighbor == null || !ownedSet.Contains(neighbor)) continue;

            float score = OwnPowerAt(neighbor) - (ThreatAt(neighbor) - EstimatedFleetPower(objective)) * 0.5f + (neighbor.hasCapitalShipyard ? ShipPower : 0f);
            if (score > bestScore)
            {
                bestScore = score;
                staging = neighbor;
            }
        }
        return staging;
    }

    private bool BordersOwned(Planet planet)
    {
        foreach (Planet neighbor in planet.connections)
        {
            if (neighbor != null && ownedSet.Contains(neighbor)) return true;
        }
        return false;
    }

    private float OwnPowerAt(Planet planet)
    {
        float power = 0f;
        foreach (GalacticFleet fleet in myFleets)
        {
            if (!fleet.IsTraveling && fleet.currentPlanet == planet)
            {
                power += Strength.Of(fleet);
            }
        }
        return power;
    }

    private void PlanMilitary()
    {
        RefreshTasks();
        CollectFleets();
        ReleaseSurplus();

        foreach (MilitaryTask task in activeTasks)
        {
            FillTask(task);
        }

        HandleEmergencies();
        LaunchAttacks();
        SendIdleToRally();
        MergeIdleFleets();
        PublishOrders();
    }

    private void RefreshTasks()
    {
        neededThisThink.Clear();
        int detachment = Mathf.Max(1, personality.minDetachmentShips);

        foreach (Campaign campaign in campaigns)
        {
            if (campaign.suspended) continue;

            Planet staging = world.FindPlanet(campaign.stagingPlanet);
            if (staging == null || !ownedSet.Contains(staging)) continue;

            float defenseNeed = ThreatAt(staging) * personality.defensePowerMargin - Strength.Defenses(staging);
            NeedTask(OrderKind.Stage, staging, Mathf.Max(campaign.requiredPower, defenseNeed), 60f * personality.aggression, false);
        }

        foreach (Planet planet in owned)
        {
            if (neededThisThink.Contains(Key(OrderKind.Stage, planet))) continue;

            float threatHere = ThreatAt(planet);
            if (threatHere <= 0f) continue;

            float need = threatHere * personality.defensePowerMargin - Strength.Defenses(planet);
            if (need <= 0f) continue;

            float value = world.PlanetValue(planet);
            bool critical = planet.hasCapitalShipyard || value >= personality.criticalPlanetValue;
            if (!critical && need > TotalPower * 1.5f) continue;

            if (!critical)
            {
                need = Mathf.Min(need, Mathf.Max(ShipPower, TotalPower * personality.maxDefenseShare));
            }

            need = Mathf.Max(need, ShipPower * detachment);
            NeedTask(OrderKind.Defend, planet, need, 100f * value * personality.defensiveness, critical);
        }

        NeedExpansionTasks();

        if (personality.canGarrisonChokepoints)
        {
            foreach (Planet planet in owned)
            {
                if (HostileDepth(planet) > 1) continue;
                if (!world.IsChokepoint(planet) && !planet.hasCapitalShipyard) continue;
                if (neededThisThink.Contains(Key(OrderKind.Stage, planet)) || neededThisThink.Contains(Key(OrderKind.Defend, planet))) continue;

                NeedTask(OrderKind.Garrison, planet, ShipPower * detachment * personality.defensiveness, 20f * world.PlanetValue(planet) * personality.defensiveness, false);
            }
        }

        expiredKeys.Clear();
        foreach (MilitaryTask task in taskBook.Values)
        {
            if (neededThisThink.Contains(task.key)) continue;

            Planet planet = world.FindPlanet(task.planetName);
            bool lingers = (task.kind == OrderKind.Defend || task.kind == OrderKind.Garrison)
                && planet != null && ownedSet.Contains(planet)
                && now - task.lastNeededAt < personality.taskMemory;

            if (!lingers)
            {
                expiredKeys.Add(task.key);
            }
        }

        foreach (string key in expiredKeys)
        {
            taskBook.Remove(key);
        }

        activeTasks.Clear();
        activeTasks.AddRange(taskBook.Values);
        activeTasks.Sort((a, b) => b.priority.CompareTo(a.priority));

        float required = 0f;
        foreach (MilitaryTask task in activeTasks)
        {
            task.assigned = 0f;
            required += task.required;
        }
        RequiredPower = required;
    }

    private void NeedExpansionTasks()
    {
        int inFlight = 0;
        foreach (Assignment assignment in assignments.Values)
        {
            if (assignment.kind == OrderKind.Expand) inFlight++;
        }

        List<(Planet planet, float priority)> candidates = new List<(Planet, float)>();
        foreach (Planet planet in owned)
        {
            foreach (Planet neighbor in planet.connections)
            {
                if (neighbor == null || ownedSet.Contains(neighbor)) continue;
                if (EstimatedFleetPower(neighbor) > 0f || Strength.Defenses(neighbor) > 0f) continue;
                if (candidates.Exists(c => c.planet == neighbor)) continue;

                float weight = neighbor.owner == null ? 50f * personality.expansion : 45f * personality.aggression;
                candidates.Add((neighbor, weight * world.PlanetValue(neighbor)));
            }
        }

        candidates.Sort((a, b) => b.priority.CompareTo(a.priority));

        int allowed = personality.maxConcurrentExpansions;
        for (int i = 0; i < candidates.Count && i < allowed; i++)
        {
            string key = Key(OrderKind.Expand, candidates[i].planet);
            if (!taskBook.ContainsKey(key) && inFlight >= allowed) continue;

            NeedTask(OrderKind.Expand, candidates[i].planet, ShipPower * 0.5f, candidates[i].priority, false);
        }
    }

    private void NeedTask(OrderKind kind, Planet planet, float required, float priority, bool critical)
    {
        string key = Key(kind, planet);
        if (!taskBook.TryGetValue(key, out MilitaryTask task))
        {
            task = new MilitaryTask { key = key, kind = kind, planetName = planet.planetName };
            taskBook[key] = task;
        }

        task.required = required;
        task.priority = priority;
        task.critical = critical;
        task.lastNeededAt = now;
        neededThisThink.Add(key);
    }

    private static string Key(OrderKind kind, Planet planet)
    {
        return $"{kind}:{planet.planetName}";
    }

    private void CollectFleets()
    {
        idle.Clear();
        liveFleetNames.Clear();

        foreach (GalacticFleet fleet in myFleets)
        {
            if (fleet.roster.Count == 0) continue;

            string fleetName = fleet.gameObject.name;
            liveFleetNames.Add(fleetName);
            bool busy = fleet.IsTraveling || fleet.IsHolding;

            if (assignments.TryGetValue(fleetName, out Assignment assignment))
            {
                if (assignment.kind == OrderKind.Attack)
                {
                    if (busy) continue;
                    assignments.Remove(fleetName);
                }
                else if (assignment.taskKey != RallyKey)
                {
                    if (taskBook.TryGetValue(assignment.taskKey, out MilitaryTask task) && (busy || ReachOrStay(fleet, assignment)))
                    {
                        task.assigned += Strength.Of(fleet);
                        continue;
                    }
                    assignments.Remove(fleetName);
                }
            }

            if (!busy)
            {
                idle.Add(fleet);
            }
        }

        expiredKeys.Clear();
        foreach (string fleetName in assignments.Keys)
        {
            if (!liveFleetNames.Contains(fleetName)) expiredKeys.Add(fleetName);
        }
        foreach (string fleetName in expiredKeys)
        {
            assignments.Remove(fleetName);
        }
    }

    private bool ReachOrStay(GalacticFleet fleet, Assignment assignment)
    {
        if (fleet.currentPlanet != null && fleet.currentPlanet.planetName == assignment.planetName) return true;

        Planet target = world.FindPlanet(assignment.planetName);
        return target != null && IsRouteSafe(fleet.currentPlanet, target) && fleet.TrySetDestination(target);
    }

    private void ReleaseSurplus()
    {
        foreach (MilitaryTask task in activeTasks)
        {
            if (task.kind == OrderKind.Stage || task.assigned <= task.required * 1.75f) continue;

            foreach (GalacticFleet fleet in myFleets)
            {
                if (task.assigned - task.required <= 0f) break;
                if (fleet.roster.Count == 0 || fleet.IsTraveling || fleet.IsHolding) continue;
                if (!assignments.TryGetValue(fleet.gameObject.name, out Assignment assignment) || assignment.taskKey != task.key) continue;
                if (now - assignment.since < personality.commitmentTime) continue;

                float power = Strength.Of(fleet);
                if (task.assigned - power < task.required) continue;

                task.assigned -= power;
                assignments.Remove(fleet.gameObject.name);
                idle.Add(fleet);
            }
        }
    }

    private void FillTask(MilitaryTask task)
    {
        if (task.assigned >= task.required) return;

        Planet planet = world.FindPlanet(task.planetName);
        if (planet == null) return;

        List<GalacticFleet> present = idle.FindAll(f => f.currentPlanet == planet);
        present.Sort((a, b) => Strength.Of(a).CompareTo(Strength.Of(b)));
        foreach (GalacticFleet fleet in present)
        {
            if (task.assigned >= task.required) break;
            Take(task, fleet, planet);
        }

        while (task.assigned < task.required)
        {
            GalacticFleet fleet = BestCandidate(task, planet, idle);
            if (fleet == null) break;

            Take(task, fleet, planet);
        }
    }

    private GalacticFleet BestCandidate(MilitaryTask task, Planet planet, List<GalacticFleet> pool)
    {
        float need = task.required - task.assigned;
        int detachment = task.kind == OrderKind.Expand ? 1 : Mathf.Max(1, personality.minDetachmentShips);
        GalacticFleet best = null;
        int bestHops = int.MaxValue;

        foreach (GalacticFleet fleet in pool)
        {
            int hops = world.Hops(fleet.currentPlanet, planet);
            if (hops == int.MaxValue || hops >= bestHops) continue;
            if (hops > personality.responseRadiusHops && !task.critical && task.kind != OrderKind.Stage) continue;

            bool bigEnough = fleet.roster.Count >= detachment || Strength.Of(fleet) >= need || hops <= 1;
            if (!bigEnough) continue;
            if (!IsRouteSafe(fleet.currentPlanet, planet)) continue;

            best = fleet;
            bestHops = hops;
        }
        return best;
    }

    private void Take(MilitaryTask task, GalacticFleet fleet, Planet planet)
    {
        float need = task.required - task.assigned;
        GalacticFleet sent = fleet;
        int detachment = task.kind == OrderKind.Expand ? 1 : Mathf.Max(1, personality.minDetachmentShips);

        if (personality.canSplitFleets && Strength.Of(fleet) > need * 1.5f)
        {
            float perShip = Strength.Of(fleet) / fleet.roster.Count;
            int count = Mathf.Max(detachment, Mathf.CeilToInt(need * 1.1f / perShip));
            if (fleet.roster.Count - count >= Mathf.Max(1, personality.minDetachmentShips))
            {
                GalacticFleet split = SplitOff(fleet, count);
                if (split != null) sent = split;
            }
        }

        if (sent == fleet)
        {
            idle.Remove(fleet);
        }

        if (sent.currentPlanet != planet && !sent.TrySetDestination(planet))
        {
            if (sent == fleet) idle.Add(fleet);
            return;
        }

        Assign(sent, task.key, task.kind, planet);
        task.assigned += Strength.Of(sent);
    }

    private void HandleEmergencies()
    {
        foreach (MilitaryTask task in activeTasks)
        {
            if (task.kind != OrderKind.Defend || task.assigned >= task.required * 0.8f) continue;

            Planet planet = world.FindPlanet(task.planetName);
            if (planet == null) continue;

            foreach (GalacticFleet fleet in myFleets)
            {
                if (task.assigned >= task.required) break;
                if (fleet.roster.Count == 0 || fleet.IsTraveling || fleet.IsHolding) continue;
                if (!assignments.TryGetValue(fleet.gameObject.name, out Assignment assignment)) continue;
                if (!taskBook.TryGetValue(assignment.taskKey, out MilitaryTask current) || current == task) continue;
                if (current.priority * personality.emergencyPriorityRatio > task.priority) continue;

                int hops = world.Hops(fleet.currentPlanet, planet);
                if (hops > personality.responseRadiusHops && !task.critical) continue;
                if (!IsRouteSafe(fleet.currentPlanet, planet) || !fleet.TrySetDestination(planet)) continue;

                current.assigned -= Strength.Of(fleet);
                Assign(fleet, task.key, task.kind, planet);
                task.assigned += Strength.Of(fleet);
                Log($"Emergency: pulled {fleet.gameObject.name} from {current.kind} {current.planetName} to defend {planet.planetName}");
            }
        }
    }

    private bool IsRouteSafe(Planet from, Planet to)
    {
        List<Planet> path = GalacticPathfinder.FindPath(from, to, faction);
        if (path.Count < 2) return from == to;

        for (int i = 1; i < path.Count - 1; i++)
        {
            Planet waypoint = path[i];
            if (ownedSet.Contains(waypoint)) continue;
            if (EstimatedFleetPower(waypoint) > 0f || Strength.Defenses(waypoint) > 0f) return false;
        }
        return true;
    }

    private void Assign(GalacticFleet fleet, string taskKey, OrderKind kind, Planet planet)
    {
        string fleetName = fleet.gameObject.name;
        if (assignments.TryGetValue(fleetName, out Assignment existing) && existing.taskKey == taskKey && existing.planetName == planet.planetName)
        {
            return;
        }

        assignments[fleetName] = new Assignment { taskKey = taskKey, kind = kind, planetName = planet.planetName, since = now };
    }

    private GalacticFleet SplitOff(GalacticFleet source, int count)
    {
        Planet planet = source.currentPlanet;
        if (planet == null || FleetPanel.Instance == null) return null;

        int slot = planet.FindFreeSlot();
        if (slot < 0) return null;

        GalacticFleet split = FleetPanel.Instance.CreateFleetInSlot(planet, slot);
        if (split == null) return null;

        split.faction = faction;
        for (int i = 0; i < count && source.roster.Count > 1; i++)
        {
            source.TransferShipTo(source.roster[source.roster.Count - 1], split);
        }
        return split;
    }

    private void Merge(GalacticFleet from, GalacticFleet into)
    {
        for (int i = from.roster.Count - 1; i >= 0; i--)
        {
            from.TransferShipTo(from.roster[i], into);
        }
        assignments.Remove(from.gameObject.name);
    }

    private void LaunchAttacks()
    {
        foreach (Campaign campaign in campaigns)
        {
            campaign.stagedPower = 0f;
            if (campaign.suspended) continue;

            Planet staging = world.FindPlanet(campaign.stagingPlanet);
            Planet objective = world.FindPlanet(campaign.objectivePlanet);
            if (staging == null || objective == null || objective.owner == faction) continue;

            string stageKey = Key(OrderKind.Stage, staging);
            List<GalacticFleet> staged = new List<GalacticFleet>();
            foreach (GalacticFleet fleet in myFleets)
            {
                if (fleet.roster.Count == 0 || fleet.IsTraveling || fleet.IsHolding || fleet.currentPlanet != staging) continue;

                bool stagedHere = assignments.TryGetValue(fleet.gameObject.name, out Assignment assignment)
                    && (assignment.taskKey == stageKey || assignment.taskKey == RallyKey);
                if (!stagedHere) continue;

                staged.Add(fleet);
                campaign.stagedPower += Strength.Of(fleet);
            }

            if (staged.Count == 0 || campaign.stagedPower < campaign.requiredPower) continue;

            staged.Sort((a, b) => Strength.Of(b).CompareTo(Strength.Of(a)));
            GalacticFleet strike = staged[0];
            for (int i = 1; i < staged.Count; i++)
            {
                Merge(staged[i], strike);
            }

            if (strike.TrySetDestination(objective))
            {
                idle.Remove(strike);
                Assign(strike, $"{OrderKind.Attack}:{objective.planetName}", OrderKind.Attack, objective);
                Log($"Attacking {objective.planetName} with {Strength.Of(strike):0} vs est. {EstimatedFleetPower(objective) + Strength.Defenses(objective):0}");
            }
        }
    }

    private void SendIdleToRally()
    {
        Planet rally = ChooseRally();
        if (rally == null) return;

        foreach (GalacticFleet fleet in idle)
        {
            if (fleet.roster.Count == 0) continue;

            if (fleet.currentPlanet != rally)
            {
                if (!IsRouteSafe(fleet.currentPlanet, rally) || !fleet.TrySetDestination(rally)) continue;
            }

            Assign(fleet, RallyKey, OrderKind.Rally, rally);
        }
        idle.Clear();
    }

    private Planet ChooseRally()
    {
        foreach (Campaign campaign in campaigns)
        {
            if (campaign.suspended) continue;

            Planet staging = world.FindPlanet(campaign.stagingPlanet);
            if (staging != null && ownedSet.Contains(staging))
            {
                RallyPlanet = staging.planetName;
                return staging;
            }
        }

        Planet current = world.FindPlanet(RallyPlanet);
        Planet best = null;
        float bestScore = float.MinValue;

        foreach (Planet planet in owned)
        {
            float score = RallyScore(planet);
            if (score > bestScore)
            {
                bestScore = score;
                best = planet;
            }
        }

        if (current != null && ownedSet.Contains(current) && best != null && bestScore < RallyScore(current) + 3f)
        {
            best = current;
        }

        RallyPlanet = best != null ? best.planetName : null;
        return best;
    }

    private float RallyScore(Planet planet)
    {
        int depth = HostileDepth(planet);
        float score = world.PlanetValue(planet);
        if (planet.hasCapitalShipyard) score += 5f;
        if (depth == 1) score -= 2f;
        if (depth != int.MaxValue) score -= Mathf.Abs(depth - 2) * 0.5f;
        return score;
    }

    private void MergeIdleFleets()
    {
        for (int i = 0; i < myFleets.Count; i++)
        {
            GalacticFleet into = myFleets[i];
            if (!CanMerge(into) || !assignments.TryGetValue(into.gameObject.name, out Assignment a)) continue;

            for (int j = i + 1; j < myFleets.Count; j++)
            {
                GalacticFleet from = myFleets[j];
                if (!CanMerge(from) || from.currentPlanet != into.currentPlanet) continue;
                if (!assignments.TryGetValue(from.gameObject.name, out Assignment b)) continue;
                if (a.taskKey != b.taskKey || a.planetName != b.planetName) continue;

                if (from.roster.Count > into.roster.Count)
                {
                    Merge(into, from);
                    break;
                }

                Merge(from, into);
            }
        }
    }

    private static bool CanMerge(GalacticFleet fleet)
    {
        return fleet != null && fleet.roster.Count > 0 && !fleet.IsTraveling && !fleet.IsHolding && fleet.currentPlanet != null;
    }

    private void PublishOrders()
    {
        orders.Clear();
        foreach (KeyValuePair<string, Assignment> entry in assignments)
        {
            orders[entry.Key] = new FleetOrder { fleetName = entry.Key, kind = entry.Value.kind, planetName = entry.Value.planetName };
        }
    }
}
