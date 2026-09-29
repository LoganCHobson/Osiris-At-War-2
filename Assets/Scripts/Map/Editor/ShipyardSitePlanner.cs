using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ShipyardSitePlanner
{
    public static int Distribute(IList<Planet> planets, int targetCount, bool onePerWarringFaction, int seed)
    {
        System.Random rng = seed != 0 ? new System.Random(seed) : new System.Random();
        List<Planet> sites = planets.Where(p => p.hasCapitalShipyard).ToList();

        if (onePerWarringFaction)
        {
            foreach (IGrouping<Faction, Planet> territory in planets.Where(p => p.owner != null && !p.owner.isNeutral).GroupBy(p => p.owner))
            {
                if (sites.Any(s => s.owner == territory.Key)) continue;

                Vector3 centroid = Vector3.zero;
                foreach (Planet planet in territory)
                {
                    centroid += planet.transform.position;
                }
                centroid /= territory.Count();

                sites.Add(territory.OrderBy(p => (p.transform.position - centroid).sqrMagnitude).First());
            }
        }

        List<Planet> candidates = planets.Where(p => !sites.Contains(p)).ToList();
        while (sites.Count < targetCount && candidates.Count > 0)
        {
            Planet pick = null;

            if (sites.Count == 0)
            {
                pick = candidates[rng.Next(candidates.Count)];
            }
            else
            {
                float bestScore = float.MinValue;
                foreach (Planet candidate in candidates)
                {
                    float score = MinDistance(candidate, sites) * (0.8f + 0.4f * (float)rng.NextDouble());
                    if (score > bestScore)
                    {
                        bestScore = score;
                        pick = candidate;
                    }
                }
            }

            sites.Add(pick);
            candidates.Remove(pick);
        }

        foreach (Planet planet in planets)
        {
            SetSite(planet, sites.Contains(planet), "Distribute Shipyard Sites");
        }

        return sites.Count;
    }

    public static void SetSite(Planet planet, bool isSite, string undoName)
    {
        if (planet.canBuildCapitalShipyard == isSite) return;

        Undo.RecordObject(planet, undoName);
        planet.canBuildCapitalShipyard = isSite;
        EditorUtility.SetDirty(planet);
    }

    private static float MinDistance(Planet planet, List<Planet> others)
    {
        float min = float.MaxValue;
        Vector3 position = planet.transform.position;
        foreach (Planet other in others)
        {
            min = Mathf.Min(min, Vector3.Distance(position, other.transform.position));
        }
        return min;
    }
}
