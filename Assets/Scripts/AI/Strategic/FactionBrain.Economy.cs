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

        float seenTotal = 0f;
        foreach (float power in enemyComposition.Values)
        {
            seenTotal += power;
        }

        Ship best = null;
        float bestScore = float.MinValue;

        foreach (Ship ship in candidates)
        {
            float countered = 0f;
            if (seenTotal > 0f)
            {
                foreach (ShipType type in ship.strongAgainst)
                {
                    if (enemyComposition.TryGetValue(type, out float power)) countered += power;
                }
                countered /= seenTotal;
            }

            float score = ship.combatPower / Mathf.Max(1, ship.cost) * (1f + personality.counterWeight * countered);
            if (score > bestScore)
            {
                bestScore = score;
                best = ship;
            }
        }
        return best;
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
        float deficit = RequiredPower + reserve - (TotalPower + queuedPower);
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

        best.shipBuildQueue.Add(new ShipBuildOrder { ship = BuildShip, remainingTime = faction.BuildTime(BuildShip) });
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

    private void AddBattleStationOption()
    {
        if (!personality.canBuildBattleStations || IncomePerMinute <= 0f) return;

        Planet best = null;
        float bestNeed = 0f;

        foreach (Planet planet in owned)
        {
            if (planet.hasBattleStation) continue;

            int depth = HostileDepth(planet);
            float need = (world.IsChokepoint(planet) && depth <= 1 ? 1f : 0f)
                + (planet.hasCapitalShipyard && depth <= 2 ? 1f : 0f)
                + (ThreatAt(planet) > 0f ? 0.5f : 0f);

            if (need > bestNeed)
            {
                bestNeed = need;
                best = planet;
            }
        }

        if (best == null) return;

        Planet target = best;
        economyOptions.Add(new EconomyOption
        {
            label = $"Battle Station @ {target.planetName}",
            cost = Planet.BattleStationCost,
            score = bestNeed * personality.defensiveness * 1.2f,
            execute = () => Build(target, "Battle Station", p => p.hasBattleStation = true, p => !p.hasBattleStation)
        });
    }

    private bool Build(Planet planet, string building, Action<Planet> apply, Func<Planet, bool> stillValid)
    {
        if (planet == null || planet.owner != faction || !stillValid(planet)) return false;

        apply(planet);
        Log($"Built {building} at {planet.planetName}");
        return true;
    }
}
