using System;
using System.Collections.Generic;
using UnityEngine;

public partial class FactionBrain
{
    private class EconomyOption
    {
        public string label;
        public int cost;
        public float score;
        public Func<bool> execute;
    }

    public const int MaxEconomyActionsPerThink = 3;

    public Ship BuildShip { get; private set; }
    public string EconomyIntent { get; private set; } = "";

    private readonly List<EconomyOption> economyOptions = new List<EconomyOption>();

    private void PlanEconomy()
    {
        if (GalacticState.Instance == null) return;

        IncomePerMinute = (owned.Count * Planet.BaseIncome + CountTaxOffices() * Planet.TaxOfficeIncome) * 60f / TaxInterval();

        economyOptions.Clear();
        AddShipOptions();
        AddTaxOfficeOption();
        AddShipyardOption();
        AddBattleStationOption();
        AddStationDemolishOption();
        economyOptions.Sort((a, b) => b.score.CompareTo(a.score));

        int currency = GalacticState.Instance.GetCurrency(faction);
        float incomePerSecond = IncomePerMinute / 60f;
        int actions = 0;
        EconomyIntent = "Idle";

        foreach (EconomyOption option in economyOptions)
        {
            if (actions >= MaxEconomyActionsPerThink || option.score <= 0.1f) break;

            if (option.cost <= currency)
            {
                if (!GalacticState.Instance.TrySpend(faction, option.cost)) continue;

                if (option.execute())
                {
                    currency -= option.cost;
                    actions++;
                    EconomyIntent = option.label;
                }
                else
                {
                    GalacticState.Instance.AddCurrency(faction, option.cost);
                }
                continue;
            }

            float wait = incomePerSecond > 0f ? (option.cost - currency) / incomePerSecond : float.MaxValue;
            if (wait <= personality.saveUpWindow)
            {
                EconomyIntent = $"Saving for {option.label} ({wait:0}s)";
                break;
            }
        }
    }

    private Ship ChooseShip()
    {
        List<Ship> candidates = new List<Ship>();

        foreach (Ship ship in faction.roster)
        {
            if (ship != null && !candidates.Contains(ship)) candidates.Add(ship);
        }

        if (candidates.Count == 0)
        {
            foreach (GalacticFleet fleet in myFleets)
            {
                foreach (Ship ship in fleet.roster)
                {
                    if (ship != null && !candidates.Contains(ship)) candidates.Add(ship);
                }
            }
        }

        if (candidates.Count == 0) return BuildShip;

        Dictionary<FleetRole, float> desired = DesiredComposition();
        Dictionary<FleetRole, float> current = OwnComposition(out float ownTotal);

        List<FleetRole> byNeed = new List<FleetRole>(desired.Keys);
        byNeed.Sort((a, b) => Shortfall(b, desired, current, ownTotal).CompareTo(Shortfall(a, desired, current, ownTotal)));

        foreach (FleetRole role in byNeed)
        {
            Ship pick = BestInRole(candidates, role);
            if (pick != null) return pick;
        }

        return candidates[0];
    }

    private static float Shortfall(FleetRole role, Dictionary<FleetRole, float> desired, Dictionary<FleetRole, float> current, float ownTotal)
    {
        float share = ownTotal > 0f && current.TryGetValue(role, out float power) ? power / ownTotal : 0f;
        return desired[role] - share;
    }

    private static Ship BestInRole(List<Ship> candidates, FleetRole role)
    {
        Ship best = null;
        float bestValue = float.MinValue;
        foreach (Ship ship in candidates)
        {
            if (ship.fleetRole != role) continue;

            float value = ship.combatPower / Mathf.Max(1, ship.cost);
            if (value > bestValue)
            {
                bestValue = value;
                best = ship;
            }
        }
        return best;
    }

    public Dictionary<FleetRole, float> DesiredComposition()
    {
        Dictionary<FleetRole, float> mix = new Dictionary<FleetRole, float>
        {
            { FleetRole.Line, 0.6f },
            { FleetRole.Screen, 0.1f },
            { FleetRole.Carrier, 0.1f },
            { FleetRole.Fighter, 0.1f },
            { FleetRole.Interceptor, 0.05f },
            { FleetRole.Bomber, 0.05f }
        };

        float seen = 0f;
        foreach (float power in enemyComposition.Values) seen += power;

        if (seen > 0f)
        {
            float strike = (Seen(FleetRole.Fighter) + Seen(FleetRole.Interceptor) + Seen(FleetRole.Bomber)) / seen;
            float bombers = Seen(FleetRole.Bomber) / seen;
            float capital = (Seen(FleetRole.Line) + Seen(FleetRole.Carrier)) / seen;

            mix[FleetRole.Screen] += 0.35f * strike + 0.3f * bombers;
            mix[FleetRole.Interceptor] += 0.25f * strike;
            mix[FleetRole.Carrier] += 0.15f * capital;
            mix[FleetRole.Bomber] += 0.2f * capital;
            mix[FleetRole.Fighter] += 0.1f * capital;
        }

        Normalize(mix);

        float squadrons = mix[FleetRole.Fighter] + mix[FleetRole.Interceptor] + mix[FleetRole.Bomber];
        if (squadrons > personality.maxFighterShare && squadrons > 0f)
        {
            float scale = personality.maxFighterShare / squadrons;
            float freed = squadrons - personality.maxFighterShare;
            mix[FleetRole.Fighter] *= scale;
            mix[FleetRole.Interceptor] *= scale;
            mix[FleetRole.Bomber] *= scale;
            mix[FleetRole.Line] += freed;
        }

        if (mix[FleetRole.Line] < personality.minLineShare)
        {
            float others = 1f - mix[FleetRole.Line];
            float scale = others > 0f ? (1f - personality.minLineShare) / others : 0f;
            List<FleetRole> roles = new List<FleetRole>(mix.Keys);
            foreach (FleetRole role in roles)
            {
                if (role != FleetRole.Line) mix[role] *= scale;
            }
            mix[FleetRole.Line] = personality.minLineShare;
        }

        return mix;
    }

    private float Seen(FleetRole role)
    {
        return enemyComposition.TryGetValue(role, out float power) ? power : 0f;
    }

    private static void Normalize(Dictionary<FleetRole, float> mix)
    {
        float total = 0f;
        foreach (float share in mix.Values) total += share;
        if (total <= 0f) return;

        List<FleetRole> roles = new List<FleetRole>(mix.Keys);
        foreach (FleetRole role in roles)
        {
            mix[role] /= total;
        }
    }

    private Dictionary<FleetRole, float> OwnComposition(out float total)
    {
        Dictionary<FleetRole, float> composition = new Dictionary<FleetRole, float>();
        total = 0f;

        foreach (GalacticFleet fleet in myFleets)
        {
            foreach (Ship ship in fleet.roster)
            {
                AddPower(composition, ship, ref total);
            }
        }

        foreach (Planet planet in owned)
        {
            foreach (ShipBuildOrder order in planet.shipBuildQueue)
            {
                AddPower(composition, order.ship, ref total);
            }
        }

        return composition;
    }

    private static void AddPower(Dictionary<FleetRole, float> composition, Ship ship, ref float total)
    {
        if (ship == null) return;

        composition.TryGetValue(ship.fleetRole, out float power);
        composition[ship.fleetRole] = power + ship.combatPower;
        total += ship.combatPower;
    }

    private int CountTaxOffices()
    {
        int count = 0;
        foreach (Planet planet in owned)
        {
            if (planet.hasTaxOffice) count++;
        }
        return count;
    }

    private static float TaxInterval()
    {
        return EconomyManager.Instance != null ? Mathf.Max(1f, EconomyManager.Instance.taxTickInterval) : 20f;
    }

    private void AddShipOptions()
    {
        if (BuildShip == null) return;

        float queuedPower = 0f;
        int capacity = 0;
        foreach (Planet planet in owned)
        {
            if (!planet.hasCapitalShipyard) continue;

            foreach (ShipBuildOrder order in planet.shipBuildQueue)
            {
                if (order.ship != null) queuedPower += order.ship.combatPower;
            }
            capacity += Mathf.Max(0, personality.shipsQueuedPerShipyard - planet.shipBuildQueue.Count);
        }

        if (capacity == 0) return;

        float reserve = ShipPower * personality.reserveShips * personality.aggression;
        float deficit = BuildTarget() + reserve - (TotalPower + queuedPower);
        bool nearCap = GalacticState.Instance.GetCurrency(faction) > GalacticState.Instance.currencyCap * 0.6f;

        int wanted = deficit > 0f ? Mathf.CeilToInt(deficit / ShipPower) : (nearCap ? 1 : 0);
        wanted = Mathf.Min(wanted, capacity);

        for (int i = 0; i < wanted; i++)
        {
            float score = deficit > 0f ? 2f + Mathf.Min(4f, deficit / ShipPower * 0.5f) - i * 0.3f : 1f;
            economyOptions.Add(new EconomyOption
            {
                label = $"Ship: {BuildShip.name}",
                cost = BuildShip.cost,
                score = score,
                execute = QueueShip
            });
        }
    }

    private float BuildTarget()
    {
        float rival = TotalThreat;
        foreach (Faction other in targetScores.Keys)
        {
            rival = Mathf.Max(rival, EstimatedFactionPower(other));
        }

        float ceiling = rival * personality.attackPowerMargin + ShipPower * personality.strikeMinShips;
        return Mathf.Min(RequiredPower, ceiling);
    }

    private bool QueueShip()
    {
        Planet best = null;
        float bestScore = float.MinValue;

        foreach (Planet planet in owned)
        {
            if (!planet.hasCapitalShipyard || planet.shipBuildQueue.Count >= personality.shipsQueuedPerShipyard) continue;
            if (planet.FindFreeSlot() < 0 && !HasOwnFleetInSlot(planet)) continue;

            int depth = HostileDepth(planet);
            float score = -planet.shipBuildQueue.Count * 2f - (depth == int.MaxValue ? 0f : Mathf.Abs(depth - 1)) - (ThreatAt(planet) > OwnPowerAt(planet) + Strength.Defenses(planet) ? 5f : 0f);
            if (score > bestScore)
            {
                bestScore = score;
                best = planet;
            }
        }

        if (best == null) return false;

        Ship ship = ChooseShip();
        if (ship == null) ship = BuildShip;

        int difference = ship.cost - BuildShip.cost;
        if (difference > 0 && !GalacticState.Instance.TrySpend(faction, difference))
        {
            ship = BuildShip;
        }
        else if (difference < 0)
        {
            GalacticState.Instance.AddCurrency(faction, -difference);
        }

        best.shipBuildQueue.Add(new ShipBuildOrder { ship = ship, remainingTime = faction.BuildTime(ship) });
        return true;
    }

    private bool HasOwnFleetInSlot(Planet planet)
    {
        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet fleet = planet.GetFleetInSlot(slot);
            if (fleet != null && fleet.faction == faction && !fleet.IsTraveling && fleet.currentPlanet == planet) return true;
        }
        return false;
    }

    private void AddTaxOfficeOption()
    {
        Planet best = null;
        float bestSafety = 0f;

        foreach (Planet planet in owned)
        {
            if (planet.hasTaxOffice) continue;

            float safety = HostileDepth(planet) >= 2 ? 1f : (ThreatAt(planet) > 0f ? 0.25f : 0.6f);
            if (safety > bestSafety)
            {
                bestSafety = safety;
                best = planet;
            }
        }

        if (best == null) return;

        Planet target = best;
        economyOptions.Add(new EconomyOption
        {
            label = $"Tax Office @ {target.planetName}",
            cost = Planet.TaxOfficeCost,
            score = 3f * personality.economy * bestSafety,
            execute = () => Build(target, "Tax Office", p => p.hasTaxOffice = true, p => !p.hasTaxOffice)
        });
    }

    private void AddShipyardOption()
    {
        int shipyards = 0;
        foreach (Planet planet in owned)
        {
            if (planet.hasCapitalShipyard) shipyards++;
        }

        bool wanted = shipyards == 0 || owned.Count / Mathf.Max(1f, personality.planetsPerShipyard) > shipyards;
        if (!wanted) return;

        Planet best = null;
        float bestScore = float.MinValue;

        foreach (Planet planet in owned)
        {
            if (planet.hasCapitalShipyard || !planet.canBuildCapitalShipyard) continue;

            int depth = HostileDepth(planet);
            float placement = depth == int.MaxValue ? 0.5f : 1f / (1f + Mathf.Abs(depth - 2));
            float score = placement * (ThreatAt(planet) > 0f ? 0.3f : 1f) * world.PlanetValue(planet);
            if (score > bestScore)
            {
                bestScore = score;
                best = planet;
            }
        }

        if (best == null) return;

        Planet target = best;
        economyOptions.Add(new EconomyOption
        {
            label = $"Shipyard @ {target.planetName}",
            cost = Planet.CapitalShipyardCost,
            score = shipyards == 0 ? 8f : 2.5f,
            execute = () => Build(target, "Capital Shipyard", p => p.hasCapitalShipyard = true, p => !p.hasCapitalShipyard)
        });
    }

    private readonly Dictionary<Planet, float> stationBuiltAt = new Dictionary<Planet, float>();

    private void AddBattleStationOption()
    {
        if (!personality.canBuildBattleStations || IncomePerMinute <= 0f) return;

        int stations = CountOwnStations();
        int desired = DesiredStations();
        if (stations >= desired || stations >= faction.maxBattleStations) return;
        if (Planet.BattleStationCost > IncomePerMinute * personality.stationPaybackMinutes) return;

        Planet best = BestStationSite(out float bestScore);
        if (best == null) return;

        Planet target = best;
        float urgency = 1f - 0.5f * stations / Mathf.Max(1f, desired);
        economyOptions.Add(new EconomyOption
        {
            label = $"Battle Station @ {target.planetName}",
            cost = Planet.BattleStationCost,
            score = bestScore * personality.defensiveness * urgency,
            execute = () =>
            {
                if (Planet.BattleStationLimitReached(faction)) return false;
                if (!Build(target, "Battle Station", p => p.hasBattleStation = true, p => !p.hasBattleStation)) return false;
                stationBuiltAt[target] = now;
                return true;
            }
        });
    }

    private void AddStationDemolishOption()
    {
        int stations = CountOwnStations();
        if (stations == 0) return;

        Planet worst = null;
        float worstScore = float.MaxValue;
        foreach (Planet planet in owned)
        {
            if (!planet.hasBattleStation) continue;
            if (stationBuiltAt.TryGetValue(planet, out float builtAt) && now - builtAt < personality.stationRelocateDelay) continue;

            float score = StationSiteScore(planet, planet);
            if (score < worstScore)
            {
                worstScore = score;
                worst = planet;
            }
        }

        if (worst == null) return;

        Planet candidate = BestStationSite(out float candidateScore);
        bool obsolete = worstScore < 0.3f;
        bool betterSiteWaiting = candidate != null && candidateScore >= worstScore + 1f;
        bool overBudget = stations > DesiredStations();
        bool atLimit = stations >= faction.maxBattleStations;

        if (!(obsolete && (overBudget || betterSiteWaiting)) && !(atLimit && betterSiteWaiting)) return;

        Planet target = worst;
        economyOptions.Add(new EconomyOption
        {
            label = $"Demolish Station @ {target.planetName}",
            cost = 0,
            score = 2.5f,
            execute = () =>
            {
                if (target.owner != faction || !target.DemolishBattleStation()) return false;
                stationBuiltAt.Remove(target);
                Log($"Demolished Battle Station at {target.planetName} to relocate");
                return true;
            }
        });
    }

    private int CountOwnStations()
    {
        int count = 0;
        foreach (Planet planet in owned)
        {
            if (planet.hasBattleStation) count++;
        }
        return count;
    }

    private int DesiredStations()
    {
        int wanted = Mathf.CeilToInt(owned.Count * personality.battleStationShare * personality.defensiveness);
        return Mathf.Clamp(wanted, 1, Mathf.Max(1, faction.maxBattleStations));
    }

    private Planet BestStationSite(out float bestScore)
    {
        Planet best = null;
        bestScore = 0f;

        foreach (Planet planet in owned)
        {
            if (planet.hasBattleStation) continue;

            float score = StationSiteScore(planet, null);
            if (score >= personality.minStationSiteScore && score > bestScore)
            {
                bestScore = score;
                best = planet;
            }
        }

        return best;
    }

    private float StationSiteScore(Planet planet, Planet ignoredStation)
    {
        int depth = HostileDepth(planet);
        float score = depth == 1 ? 1f : depth == 2 ? 0.4f : 0f;

        if (world.IsChokepoint(planet) && depth <= 2) score += 1f;
        if (planet.hasCapitalShipyard && depth <= 3) score += depth <= 2 ? 1f : 0.5f;
        if (ThreatAt(planet) > OwnPowerAt(planet)) score += 0.5f;

        float value = world.PlanetValue(planet) - (planet.hasBattleStation ? 1.5f : 0f);
        score += Mathf.Clamp01((value - 1f) * 0.15f);

        foreach (Planet neighbor in planet.connections)
        {
            if (neighbor != null && neighbor != ignoredStation && neighbor.owner == faction && neighbor.hasBattleStation)
            {
                score *= 0.4f;
                break;
            }
        }

        return score;
    }

    private bool Build(Planet planet, string building, Action<Planet> apply, Func<Planet, bool> stillValid)
    {
        if (planet == null || planet.owner != faction || !stillValid(planet)) return false;

        apply(planet);
        Log($"Built {building} at {planet.planetName}");
        return true;
    }
}
