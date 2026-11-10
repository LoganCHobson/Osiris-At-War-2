using System.Collections.Generic;
using UnityEngine;

// Standing orders: "keep this planet topped up to this fleet design". Every few seconds each order counts
// what's parked at the planet, flying to it and already queued for it, and orders whatever is missing from
// the player's shipyards (credits and manpower permitting - anything it can't afford is retried later).
// Orders lapse if the planet is lost. Saved with the game.
public static class StandingOrders
{
    [System.Serializable]
    public class Order
    {
        public string planet;
        public FleetDesign design;
    }

    public const float CheckInterval = 5f;
    public static readonly Dictionary<string, FleetDesign> orders = new Dictionary<string, FleetDesign>();
    private static float nextCheckAt;

    public static void Set(Planet planet, FleetDesign design)
    {
        if (planet == null || design == null) return;
        orders[planet.planetName] = design.Clone();
        nextCheckAt = 0f;
    }

    public static void Clear(Planet planet)
    {
        if (planet != null) orders.Remove(planet.planetName);
    }

    public static FleetDesign For(Planet planet)
    {
        return planet != null && orders.TryGetValue(planet.planetName, out FleetDesign design) ? design : null;
    }

    public static void Tick(float now)
    {
        if (now < nextCheckAt || orders.Count == 0) return;
        nextCheckAt = now + CheckInterval;

        List<string> stale = null;
        foreach (KeyValuePair<string, FleetDesign> entry in orders)
        {
            Planet planet = Planet.All.Find(p => p != null && p.planetName == entry.Key);
            if (planet == null || planet.owner == null || !planet.owner.isPlayerFaction)
            {
                (stale ??= new List<string>()).Add(entry.Key); // Lost the planet - the order lapses.
                continue;
            }
            TopUp(planet, entry.Value);
        }

        if (stale == null) return;
        foreach (string key in stale) orders.Remove(key);
    }

    private static void TopUp(Planet planet, FleetDesign design)
    {
        Faction faction = planet.owner;
        Dictionary<GameObject, int> have = new Dictionary<GameObject, int>();
        void Count(Ship ship)
        {
            if (ship == null) return;
            have[ship.prefab] = have.TryGetValue(ship.prefab, out int n) ? n + 1 : 1;
        }

        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (fleet == null || fleet.faction != faction) continue;
            bool here = !fleet.IsTraveling && fleet.currentPlanet == planet && string.IsNullOrEmpty(fleet.pendingDelivery);
            bool coming = (fleet.IsTraveling && fleet.Destination == planet) || fleet.pendingDelivery == planet.planetName;
            if (!here && !coming) continue;
            foreach (Ship ship in fleet.ships) Count(ship);
        }

        foreach (Planet yard in ProductionOrders.Shipyards(faction))
        {
            foreach (ShipBuildOrder order in yard.shipBuildQueue)
            {
                bool forHere = order.deliverTo == planet.planetName || (yard == planet && string.IsNullOrEmpty(order.deliverTo));
                if (forHere) Count(order.ship);
            }
        }

        List<Ship> missing = new List<Ship>();
        foreach ((Ship ship, int count) in design.Ships())
        {
            have.TryGetValue(ship.prefab, out int present);
            for (int i = present; i < count; i++) missing.Add(ship);
        }
        if (missing.Count == 0) return;

        missing.Sort((a, b) => faction.BuildTime(b).CompareTo(faction.BuildTime(a)));
        ProductionOrders.Result result = ProductionOrders.Order(faction, missing, planet);
        if (result.queued > 0)
        {
            Toast.Show($"Standing order: {result.queued} ship{(result.queued == 1 ? "" : "s")} ordered for {planet.planetName}");
        }
    }

    public static List<Order> Capture()
    {
        List<Order> saved = new List<Order>();
        foreach (KeyValuePair<string, FleetDesign> entry in orders) saved.Add(new Order { planet = entry.Key, design = entry.Value.Clone() });
        return saved;
    }

    public static void Restore(List<Order> saved)
    {
        orders.Clear();
        if (saved == null) return;
        foreach (Order order in saved)
        {
            if (!string.IsNullOrEmpty(order.planet) && order.design != null) orders[order.planet] = order.design;
        }
    }
}
