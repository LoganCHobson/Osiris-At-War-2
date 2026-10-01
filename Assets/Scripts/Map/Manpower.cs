public static class Manpower
{
    public static int PerPlanet = 3;
    public static int PerCapitalShipyard = 25;

    public static int Cap(Faction faction)
    {
        if (faction == null) return 0;
        int cap = 0;
        foreach (Planet planet in Planet.All)
        {
            if (planet.owner != faction) continue;
            cap += PerPlanet;
            if (planet.hasCapitalShipyard) cap += PerCapitalShipyard;
        }
        return cap;
    }

    public static int Used(Faction faction)
    {
        if (faction == null) return 0;
        int used = 0;
        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (fleet == null || fleet.faction != faction) continue;
            foreach (Ship ship in fleet.roster)
            {
                if (ship != null) used += ship.populationCost;
            }
        }
        foreach (Planet planet in Planet.All)
        {
            if (planet.owner != faction) continue;
            foreach (ShipBuildOrder order in planet.shipBuildQueue)
            {
                if (order.ship != null) used += order.ship.populationCost;
            }
        }
        return used;
    }

    public static int Free(Faction faction) => Cap(faction) - Used(faction);

    public static bool CanAfford(Faction faction, Ship ship)
    {
        return ship != null && ship.populationCost <= Free(faction);
    }
}
