using System.Collections.Generic;
using UnityEngine;

public static class GalacticPathfinder
{
    private const float FriendlyCostMultiplier = 0.5f;
    private const float UnfriendlyCostMultiplier = 1.5f;

    public static List<Planet> FindPath(Planet start, Planet goal, Faction travelingFaction)
    {
        List<Planet> path = new List<Planet>();
        if (start == null || goal == null) return path;

        if (start == goal)
        {
            path.Add(start);
            return path;
        }

        Dictionary<Planet, float> bestCost = new Dictionary<Planet, float> { { start, 0f } };
        Dictionary<Planet, Planet> cameFrom = new Dictionary<Planet, Planet>();
        List<Planet> open = new List<Planet> { start };
        HashSet<Planet> closed = new HashSet<Planet>();

        while (open.Count > 0)
        {
            Planet current = null;
            float currentCost = float.MaxValue;
            foreach (Planet candidate in open)
            {
                if (bestCost[candidate] < currentCost)
                {
                    currentCost = bestCost[candidate];
                    current = candidate;
                }
            }

            open.Remove(current);
            closed.Add(current);

            if (current == goal)
            {
                break;
            }

            foreach (Planet neighbor in current.connections)
            {
                if (neighbor == null || closed.Contains(neighbor)) continue;

                float edgeCost = Vector3.Distance(current.transform.position, neighbor.transform.position);
                if (travelingFaction != null)
                {
                    edgeCost *= neighbor.owner == travelingFaction ? FriendlyCostMultiplier : UnfriendlyCostMultiplier;
                }

                float newCost = bestCost[current] + edgeCost;

                if (!bestCost.TryGetValue(neighbor, out float existingCost) || newCost < existingCost)
                {
                    bestCost[neighbor] = newCost;
                    cameFrom[neighbor] = current;

                    if (!open.Contains(neighbor))
                    {
                        open.Add(neighbor);
                    }
                }
            }
        }

        if (!cameFrom.ContainsKey(goal))
        {
            return new List<Planet>();
        }

        path.Add(goal);
        Planet step = goal;
        while (step != start)
        {
            step = cameFrom[step];
            path.Insert(0, step);
        }

        return path;
    }
}
