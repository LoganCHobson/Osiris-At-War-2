using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Builds a fleet for a credit budget from a faction's roster: a weighted random pick of whatever it can
// still afford, so two fleets of equal budget are equal in cost. Weighted toward screens and line
// ships, with the odd carrier. Shared by skirmish (Scripts/UI/SkirmishSetup.cs) and the self-play farm
// (Scripts/Dev/DifficultySim.cs), so a skirmish fleet looks like the fleets the Grand Admiral trains on.
public static class FleetBuilder
{
    public static List<Ship> EqualCost(Faction faction, int budget)
    {
        List<Ship> fleet = new List<Ship>();
        if (faction == null) return fleet;
        List<(Ship ship, float weight)> options = new List<(Ship, float)>();
        foreach (Ship ship in faction.roster)
        {
            if (ship == null || ship.cost <= 0) continue;
            options.Add((ship, Weight(ship)));
        }
        if (options.Count == 0) return Standard(faction);
        int spent = 0, cheapest = options.Min(o => o.ship.cost);
        while (budget - spent >= cheapest)
        {
            List<(Ship ship, float weight)> affordable = options.FindAll(o => o.ship.cost <= budget - spent);
            float roll = Random.value * affordable.Sum(o => o.weight);
            foreach ((Ship ship, float weight) in affordable)
            {
                roll -= weight;
                if (roll > 0f) continue;
                fleet.Add(ship);
                spent += ship.cost;
                break;
            }
        }
        return fleet;
    }

    // A fixed mix of every ship in the roster, for factions whose ships have no cost set.
    public static List<Ship> Standard(Faction faction)
    {
        List<Ship> fleet = new List<Ship>();
        if (faction == null) return fleet;
        foreach (Ship ship in faction.roster)
        {
            if (ship == null) continue;
            int count = ship.fleetRole switch
            {
                FleetRole.Line => 3,
                FleetRole.Screen => 4,
                FleetRole.Carrier => 1,
                _ => 2
            };
            for (int i = 0; i < count; i++) fleet.Add(ship);
        }
        return fleet;
    }

    public static int Cost(IEnumerable<Ship> fleet) => fleet.Where(s => s != null).Sum(s => s.cost);

    private static float Weight(Ship ship) => ship.fleetRole switch
    {
        FleetRole.Line => 3f,
        FleetRole.Screen => 4f,
        FleetRole.Carrier => 1f,
        _ => 2f
    };
}
