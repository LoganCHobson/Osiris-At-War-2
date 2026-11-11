using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A named fleet composition the player designs once and orders as often as they like.
[System.Serializable]
public class FleetDesign
{
    [System.Serializable]
    public class Entry
    {
        public string ship; // SaveRegistry ship id.
        public int count;
    }

    public string name = "New Fleet";
    public List<Entry> entries = new List<Entry>();

    public int Count(Ship ship)
    {
        string id = SaveRegistry.Id(ship);
        Entry entry = entries.Find(e => e.ship == id);
        return entry != null ? entry.count : 0;
    }

    public void SetCount(Ship ship, int count)
    {
        string id = SaveRegistry.Id(ship);
        Entry entry = entries.Find(e => e.ship == id);
        if (count <= 0)
        {
            if (entry != null) entries.Remove(entry);
            return;
        }
        if (entry == null) entries.Add(entry = new Entry { ship = id });
        entry.count = count;
    }

    public IEnumerable<(Ship ship, int count)> Ships()
    {
        foreach (Entry entry in entries)
        {
            Ship ship = SaveRegistry.FindShip(entry.ship);
            if (ship != null && entry.count > 0) yield return (ship, entry.count);
        }
    }

    public int ShipCount => Ships().Sum(s => s.count);
    public int Cost => Ships().Sum(s => s.ship.cost * s.count);
    public int Manpower => Ships().Sum(s => s.ship.populationCost * s.count);

    public FleetDesign Clone()
    {
        return new FleetDesign { name = name, entries = entries.Select(e => new Entry { ship = e.ship, count = e.count }).ToList() };
    }
}

// The player's saved fleet designs, kept per faction across campaigns (they're a preference, not campaign state).
public static class FleetDesigns
{
    [System.Serializable]
    private class Store
    {
        public List<FleetDesign> designs = new List<FleetDesign>();
    }

    private static string Key(Faction faction) => $"osiris.fleetDesigns.{SaveRegistry.Id(faction)}";

    public static List<FleetDesign> Load(Faction faction)
    {
        if (faction == null) return new List<FleetDesign>();
        string json = PlayerPrefs.GetString(Key(faction), "");
        Store store = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Store>(json);
        return store != null ? store.designs : new List<FleetDesign>();
    }

    public static void Save(Faction faction, List<FleetDesign> designs)
    {
        if (faction == null) return;
        PlayerPrefs.SetString(Key(faction), JsonUtility.ToJson(new Store { designs = designs }));
        PlayerPrefs.Save();
    }
}

// Ordering ships across the empire: a fleet design (or any list of ships) is spread over the chosen
// shipyards - each ship goes to whichever yard would finish it soonest - and every finished ship is sent
// on to a destination planet. Ships finishing at the same yard gather there and leave together once that
// yard's share of the order is done, so a big order arrives as a few fleets rather than a stream of
// single ships.
public static class ProductionOrders
{
    public struct Result
    {
        public int queued;
        public int requested;
        public string stoppedBecause;
    }

    public static List<Planet> Shipyards(Faction faction)
    {
        return Planet.All.Where(p => p != null && p.owner == faction && p.hasCapitalShipyard).OrderBy(p => p.planetName).ToList();
    }

    public static float QueueTime(Planet yard)
    {
        return yard.shipBuildQueue.Sum(o => o.remainingTime);
    }

    // Queues every ship of the design. yards: where it may be built (null = all of the faction's shipyards).
    public static Result Order(Faction faction, FleetDesign design, Planet destination, IList<Planet> yards = null)
    {
        List<Ship> ships = new List<Ship>();
        foreach ((Ship ship, int count) in design.Ships())
        {
            for (int i = 0; i < count; i++) ships.Add(ship);
        }
        // Big hulls first so they get the emptiest yards.
        ships.Sort((a, b) => faction.BuildTime(b).CompareTo(faction.BuildTime(a)));
        return Order(faction, ships, destination, yards);
    }

    public static Result Order(Faction faction, List<Ship> ships, Planet destination, IList<Planet> yards = null)
    {
        Result result = new Result { requested = ships.Count };
        List<Planet> candidates = (yards ?? Shipyards(faction)).Where(y => y != null && y.owner == faction && y.hasCapitalShipyard).ToList();
        if (candidates.Count == 0)
        {
            result.stoppedBecause = "No shipyards available";
            return result;
        }

        Dictionary<Planet, float> load = candidates.ToDictionary(y => y, QueueTime);

        foreach (Ship ship in ships)
        {
            if (GalacticState.Instance == null || GalacticState.Instance.GetCurrency(faction) < ship.cost)
            {
                result.stoppedBecause = "Not enough credits";
                break;
            }
            if (ship.populationCost > global::Manpower.Free(faction))
            {
                result.stoppedBecause = "Manpower limit reached";
                break;
            }

            Planet yard = candidates.OrderBy(y => load[y]).First();
            if (!GalacticState.Instance.TrySpend(faction, ship.cost))
            {
                result.stoppedBecause = "Not enough credits";
                break;
            }

            float time = faction.BuildTime(ship);
            string deliverTo = destination != null && destination != yard ? destination.planetName : null;
            yard.shipBuildQueue.Add(new ShipBuildOrder { ship = ship, remainingTime = time, deliverTo = deliverTo });
            load[yard] += time;
            result.queued++;
        }
        return result;
    }

    // Called when a ship finishes. Returns true if it was a delivery and has been taken care of.
    public static bool TryDeliver(Planet yard, ShipBuildOrder order)
    {
        if (order == null || order.ship == null || string.IsNullOrEmpty(order.deliverTo)) return false;
        if (order.deliverTo == yard.planetName) return false;

        GalacticFleet convoy = FindConvoy(yard, order.deliverTo);
        if (convoy == null)
        {
            int slot = yard.FindFreeSlot();
            if (slot < 0 || FleetPanel.Instance == null) return false; // Nowhere to gather - it stays at the yard.

            convoy = FleetPanel.Instance.CreateFleetInSlot(yard, slot);
            if (convoy == null) return false;
            convoy.faction = yard.owner;
            convoy.pendingDelivery = order.deliverTo;
        }

        convoy.ships.Add(ShipEntry.New(order.ship, yard.owner));
        convoy.EnsureValidLead();

        bool moreComing = yard.shipBuildQueue.Exists(o => o.deliverTo == order.deliverTo);
        if (!moreComing) Dispatch(convoy);
        return true;
    }

    private static GalacticFleet FindConvoy(Planet yard, string destination)
    {
        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet fleet = yard.GetFleetInSlot(slot);
            if (fleet != null && fleet.faction == yard.owner && !fleet.IsTraveling && fleet.currentPlanet == yard && fleet.pendingDelivery == destination)
            {
                return fleet;
            }
        }
        return null;
    }

    private static void Dispatch(GalacticFleet convoy)
    {
        Planet destination = Planet.All.Find(p => p != null && p.planetName == convoy.pendingDelivery);
        convoy.pendingDelivery = null;
        if (destination == null) return;

        // New ships are no escort: route around any system where they'd have to fight (what the player can
        // see, for the player's convoys). With no safe way through they wait at the yard instead.
        bool player = convoy.faction != null && convoy.faction.isPlayerFaction;
        System.Func<Planet, bool> dangerous = p => convoy.HasBattleAt(p) && (!player || FogOfWar.CanSee(p));
        if (convoy.TrySetDestination(destination, -1, dangerous))
        {
            if (player)
            {
                Toast.Show($"{convoy.ships.Count} new ship{(convoy.ships.Count == 1 ? "" : "s")} leaving {convoy.currentPlanet.planetName} for {destination.planetName}");
            }
        }
        else if (player)
        {
            List<Planet> blocked = convoy.PlanRoute(destination);
            Planet blocker = blocked.Count >= 2 ? blocked.Find(p => p != convoy.currentPlanet && p != destination && dangerous(p)) : null;
            Toast.Show(blocker != null
                ? $"Enemy at {blocker.planetName} blocks the way to {destination.planetName} - new ships are waiting at {convoy.currentPlanet.planetName}"
                : $"No route from {convoy.currentPlanet.planetName} to {destination.planetName} - new ships are waiting at the shipyard");
        }
    }

    // Cancels every queued, not-yet-started delivery to a planet (full refund). The ship currently on the
    // slipway at each yard is left to finish.
    public static int CancelDeliveries(Faction faction, string destination)
    {
        int cancelled = 0;
        foreach (Planet yard in Shipyards(faction))
        {
            for (int i = yard.shipBuildQueue.Count - 1; i >= 1; i--)
            {
                ShipBuildOrder order = yard.shipBuildQueue[i];
                if (order.deliverTo != destination) continue;
                yard.shipBuildQueue.RemoveAt(i);
                if (order.ship != null) GalacticState.Instance?.AddCurrency(faction, order.ship.cost);
                cancelled++;
            }
        }
        return cancelled;
    }
}
