using System.Collections.Generic;

public static class GalacticVisibility
{
    public static void Compute(Faction faction, IEnumerable<Planet> planets, IEnumerable<GalacticFleet> fleets, HashSet<Planet> result)
    {
        result.Clear();
        if (faction == null) return;

        foreach (Planet planet in planets)
        {
            if (planet != null && planet.owner == faction)
            {
                AddWithNeighbors(planet, result);
            }
        }

        foreach (GalacticFleet fleet in fleets)
        {
            if (fleet == null || fleet.faction != faction || fleet.currentPlanet == null) continue;

            if (fleet.IsPresentAt(fleet.currentPlanet))
            {
                AddWithNeighbors(fleet.currentPlanet, result);
            }
        }

        // A parked spy probe sees its planet and the systems next to it, like a fleet would.
        HashSet<string> watched = new HashSet<string>(SpyProbes.WatchedPlanets(faction));
        if (watched.Count == 0) return;
        foreach (Planet planet in planets)
        {
            if (planet != null && watched.Contains(planet.planetName)) AddWithNeighbors(planet, result);
        }
    }

    public static bool CanSeeFleet(GalacticFleet fleet, Faction viewer, HashSet<Planet> visible)
    {
        if (fleet == null) return false;
        if (fleet.faction == viewer) return true;

        if (fleet.currentPlanet != null && visible.Contains(fleet.currentPlanet)) return true;

        Planet next = fleet.NextWaypoint;
        return next != null && visible.Contains(next);
    }

    private static void AddWithNeighbors(Planet planet, HashSet<Planet> result)
    {
        result.Add(planet);
        foreach (Planet neighbor in planet.connections)
        {
            if (neighbor != null)
            {
                result.Add(neighbor);
            }
        }
    }
}
