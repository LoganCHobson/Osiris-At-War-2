using System.Collections.Generic;
using UnityEngine;

public class GalacticWorld
{
    public readonly List<Planet> planets = new List<Planet>();
    public readonly List<GalacticFleet> fleets = new List<GalacticFleet>();

    private readonly Dictionary<Planet, int> indexOf = new Dictionary<Planet, int>();
    private readonly Dictionary<string, Planet> planetsByName = new Dictionary<string, Planet>();
    private readonly Dictionary<string, GalacticFleet> fleetsByName = new Dictionary<string, GalacticFleet>();

    private float[] chokeScore = new float[0];
    private bool[] articulation = new bool[0];
    private int[,] hops = new int[0, 0];

    public void EnsurePlanets()
    {
        if (PlanetsInvalid())
        {
            RebuildPlanets();
        }
    }

    public void Refresh()
    {
        EnsurePlanets();

        fleets.Clear();
        fleetsByName.Clear();
        foreach (GalacticFleet fleet in Object.FindObjectsByType<GalacticFleet>(FindObjectsSortMode.None))
        {
            if (fleet.roster.Count == 0) continue;

            fleets.Add(fleet);
            fleetsByName[fleet.gameObject.name] = fleet;
        }
    }

    public Planet FindPlanet(string planetName)
    {
        if (string.IsNullOrEmpty(planetName)) return null;
        return planetsByName.TryGetValue(planetName, out Planet planet) ? planet : null;
    }

    public GalacticFleet FindFleet(string fleetName)
    {
        if (string.IsNullOrEmpty(fleetName)) return null;
        return fleetsByName.TryGetValue(fleetName, out GalacticFleet fleet) && fleet != null ? fleet : null;
    }

    public float ChokeScore(Planet planet)
    {
        return indexOf.TryGetValue(planet, out int i) ? chokeScore[i] : 0f;
    }

    public bool IsArticulation(Planet planet)
    {
        return indexOf.TryGetValue(planet, out int i) && articulation[i];
    }

    public bool IsChokepoint(Planet planet)
    {
        return IsArticulation(planet) || ChokeScore(planet) >= 0.35f;
    }

    public int Hops(Planet from, Planet to)
    {
        if (from == null || to == null) return int.MaxValue;
        if (!indexOf.TryGetValue(from, out int a) || !indexOf.TryGetValue(to, out int b)) return int.MaxValue;
        return hops[a, b];
    }

    public float PlanetValue(Planet planet)
    {
        float value = 1f + ChokeScore(planet) * 2f + (IsArticulation(planet) ? 1f : 0f) + planet.connections.Count * 0.1f;
        if (planet.hasTaxOffice) value += 1f;
        if (planet.hasCapitalShipyard) value += 2f;
        else if (planet.canBuildCapitalShipyard) value += 1f;
        if (planet.hasBattleStation) value += 1.5f;
        return value;
    }

    private bool PlanetsInvalid()
    {
        if (planets.Count == 0) return true;

        foreach (Planet planet in planets)
        {
            if (planet == null) return true;
        }
        return false;
    }

    private void RebuildPlanets()
    {
        planets.Clear();
        indexOf.Clear();
        planetsByName.Clear();

        foreach (Planet planet in Object.FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            indexOf[planet] = planets.Count;
            planets.Add(planet);
            planetsByName[planet.planetName] = planet;
        }

        ComputeHops();
        ComputeBetweenness();
        ComputeArticulationPoints();
    }

    private void ComputeHops()
    {
        int count = planets.Count;
        hops = new int[count, count];
        Queue<int> queue = new Queue<int>();

        for (int source = 0; source < count; source++)
        {
            for (int i = 0; i < count; i++)
            {
                hops[source, i] = int.MaxValue;
            }

            hops[source, source] = 0;
            queue.Enqueue(source);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (Planet neighbor in planets[current].connections)
                {
                    if (neighbor == null || !indexOf.TryGetValue(neighbor, out int n)) continue;
                    if (hops[source, n] != int.MaxValue) continue;

                    hops[source, n] = hops[source, current] + 1;
                    queue.Enqueue(n);
                }
            }
        }
    }

    private void ComputeBetweenness()
    {
        int count = planets.Count;
        float[] centrality = new float[count];
        Stack<int> stack = new Stack<int>();
        Queue<int> queue = new Queue<int>();
        List<int>[] predecessors = new List<int>[count];
        float[] paths = new float[count];
        int[] distance = new int[count];
        float[] dependency = new float[count];

        for (int i = 0; i < count; i++)
        {
            predecessors[i] = new List<int>();
        }

        for (int source = 0; source < count; source++)
        {
            for (int i = 0; i < count; i++)
            {
                predecessors[i].Clear();
                paths[i] = 0f;
                distance[i] = -1;
                dependency[i] = 0f;
            }

            paths[source] = 1f;
            distance[source] = 0;
            queue.Enqueue(source);

            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                stack.Push(v);

                foreach (Planet neighbor in planets[v].connections)
                {
                    if (neighbor == null || !indexOf.TryGetValue(neighbor, out int w)) continue;

                    if (distance[w] < 0)
                    {
                        distance[w] = distance[v] + 1;
                        queue.Enqueue(w);
                    }

                    if (distance[w] == distance[v] + 1)
                    {
                        paths[w] += paths[v];
                        predecessors[w].Add(v);
                    }
                }
            }

            while (stack.Count > 0)
            {
                int w = stack.Pop();
                foreach (int v in predecessors[w])
                {
                    dependency[v] += paths[v] / paths[w] * (1f + dependency[w]);
                }

                if (w != source)
                {
                    centrality[w] += dependency[w];
                }
            }
        }

        float max = 0f;
        foreach (float value in centrality)
        {
            max = Mathf.Max(max, value);
        }

        chokeScore = new float[count];
        for (int i = 0; i < count; i++)
        {
            chokeScore[i] = max > 0f ? centrality[i] / max : 0f;
        }
    }

    private void ComputeArticulationPoints()
    {
        int count = planets.Count;
        articulation = new bool[count];
        int[] discovery = new int[count];
        int[] low = new int[count];
        int[] parent = new int[count];
        int timer = 0;

        for (int i = 0; i < count; i++)
        {
            discovery[i] = -1;
            parent[i] = -1;
        }

        for (int root = 0; root < count; root++)
        {
            if (discovery[root] < 0)
            {
                VisitArticulation(root, discovery, low, parent, ref timer);
            }
        }
    }

    private void VisitArticulation(int v, int[] discovery, int[] low, int[] parent, ref int timer)
    {
        discovery[v] = low[v] = timer++;
        int children = 0;

        foreach (Planet neighbor in planets[v].connections)
        {
            if (neighbor == null || !indexOf.TryGetValue(neighbor, out int w)) continue;

            if (discovery[w] < 0)
            {
                children++;
                parent[w] = v;
                VisitArticulation(w, discovery, low, parent, ref timer);
                low[v] = Mathf.Min(low[v], low[w]);

                if (parent[v] < 0 && children > 1) articulation[v] = true;
                if (parent[v] >= 0 && low[w] >= discovery[v]) articulation[v] = true;
            }
            else if (w != parent[v])
            {
                low[v] = Mathf.Min(low[v], discovery[w]);
            }
        }
    }
}
