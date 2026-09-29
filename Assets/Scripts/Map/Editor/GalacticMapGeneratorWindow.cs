using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class GalacticMapGeneratorWindow : EditorWindow
{
    private enum DistributionMode { Annulus, Clustered, Spiral }

    private const string ParentName = "Planets";

    private static readonly string[] FallbackNames =
    {
        "Coruscant", "Kamino", "Geonosis", "Mustafar", "Hoth", "Bespin", "Endor", "Kashyyyk",
        "Mygeeto", "Felucia", "Utapau", "Dagobah", "Tatooine", "Naboo", "Alderaan", "Yavin",
        "Sullust", "Corellia", "Dantooine", "Ryloth"
    };

    private static readonly string[] NameSuffixes =
    {
        "", " Prime", " Minor", " Major", " Outpost", " Reach", " Expanse", " Nadir", " Anchorage", " Terminus"
    };

    private int planetCount = 60;
    private int seed = 0;
    private float mapRadius = 60f;
    private float coreRadius = 12f;
    private float minPlanetDistance = 6f;

    private DistributionMode distributionMode = DistributionMode.Clustered;
    private int clusterCount = 6;
    private float clusterSpread = 12f;
    private int spiralArms = 3;
    private float spiralTwist = 3.5f;
    private float spiralArmWidth = 6f;

    private float extraLaneChance = 0.15f;
    private float extraLaneDistanceFactor = 1.5f;
    private int maxConnectionsPerPlanet = 4;

    private bool spawnCoreMarker = true;
    private GameObject coreMarkerPrefab;

    private string planetLayerName = "Planet";
    private GameObject planetPrefab;
    private string planetNamesRaw = "";

    [System.Serializable]
    private class FactionTerritoryConfig
    {
        public Faction faction;
        public int capitalCount = 1;
        public float territoryWeight = 1f;
        public int maxPlanets = 0;
    }

    private List<FactionTerritoryConfig> factionTerritories = new List<FactionTerritoryConfig>();

    private int shipyardSiteCount = 6;
    private bool shipyardSitePerFaction = true;

    [MenuItem("Osiris/Galactic Map Generator")]
    public static void Open()
    {
        GetWindow<GalacticMapGeneratorWindow>("Galaxy Map Generator");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
        planetCount = EditorGUILayout.IntSlider("Planet Count", planetCount, 2, 400);
        seed = EditorGUILayout.IntField("Seed (0 = random)", seed);
        mapRadius = EditorGUILayout.FloatField("Map Radius", mapRadius);
        coreRadius = EditorGUILayout.FloatField("Core Radius (black hole)", coreRadius);
        minPlanetDistance = EditorGUILayout.FloatField("Min Planet Spacing", minPlanetDistance);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Distribution", EditorStyles.boldLabel);
        distributionMode = (DistributionMode)EditorGUILayout.EnumPopup("Shape", distributionMode);

        if (distributionMode == DistributionMode.Clustered)
        {
            clusterCount = EditorGUILayout.IntSlider("Cluster Count", clusterCount, 1, 24);
            clusterSpread = EditorGUILayout.FloatField("Cluster Spread", clusterSpread);
        }
        else if (distributionMode == DistributionMode.Spiral)
        {
            spiralArms = EditorGUILayout.IntSlider("Spiral Arms", spiralArms, 1, 8);
            spiralTwist = EditorGUILayout.FloatField("Spiral Twist", spiralTwist);
            spiralArmWidth = EditorGUILayout.FloatField("Arm Width (jitter)", spiralArmWidth);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Hyperspace Lanes", EditorStyles.boldLabel);
        extraLaneChance = EditorGUILayout.Slider("Extra Lane Chance", extraLaneChance, 0f, 1f);
        extraLaneDistanceFactor = EditorGUILayout.FloatField("Extra Lane Distance Factor", extraLaneDistanceFactor);
        maxConnectionsPerPlanet = EditorGUILayout.IntSlider("Max Lanes Per Planet (0 = unlimited)", maxConnectionsPerPlanet, 0, 12);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Faction Territories", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Each warring faction claims planets nearest to its capitals (weight = how far its reach extends relative to the others). Neutral factions instead get a mix of small clustered pockets and lone scattered holdings. Max Planets caps how many total planets a faction can end up owning (0 = unlimited). Leave empty to leave every planet unowned.", MessageType.Info);

        int removeIndex = -1;
        for (int i = 0; i < factionTerritories.Count; i++)
        {
            FactionTerritoryConfig config = factionTerritories[i];
            bool isNeutral = config.faction != null && config.faction.isNeutral;

            EditorGUILayout.BeginHorizontal();
            config.faction = (Faction)EditorGUILayout.ObjectField(config.faction, typeof(Faction), false, GUILayout.MinWidth(100));

            GUILayout.Label(isNeutral ? "Holdings" : "Capitals", GUILayout.Width(55));
            config.capitalCount = EditorGUILayout.IntField(config.capitalCount, GUILayout.Width(35));

            GUILayout.Label("Weight", GUILayout.Width(45));
            using (new EditorGUI.DisabledScope(isNeutral))
            {
                config.territoryWeight = EditorGUILayout.FloatField(config.territoryWeight, GUILayout.Width(40));
            }

            GUILayout.Label("Max", GUILayout.Width(30));
            config.maxPlanets = EditorGUILayout.IntField(config.maxPlanets, GUILayout.Width(40));

            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                removeIndex = i;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (removeIndex >= 0)
        {
            factionTerritories.RemoveAt(removeIndex);
        }

        if (GUILayout.Button("Add Faction"))
        {
            factionTerritories.Add(new FactionTerritoryConfig());
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Capital Shipyard Sites", EditorStyles.boldLabel);
        shipyardSiteCount = EditorGUILayout.IntField("Site Count (0 = every planet)", shipyardSiteCount);
        shipyardSitePerFaction = EditorGUILayout.Toggle("Guarantee One Per Faction", shipyardSitePerFaction);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Core Marker", EditorStyles.boldLabel);
        spawnCoreMarker = EditorGUILayout.Toggle("Spawn Black Hole Marker", spawnCoreMarker);
        if (spawnCoreMarker)
        {
            coreMarkerPrefab = (GameObject)EditorGUILayout.ObjectField("Marker Prefab (optional)", coreMarkerPrefab, typeof(GameObject), false);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Planet Object", EditorStyles.boldLabel);
        planetPrefab = (GameObject)EditorGUILayout.ObjectField("Planet Prefab (optional)", planetPrefab, typeof(GameObject), false);
        planetLayerName = EditorGUILayout.TextField("Planet Layer", planetLayerName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Names (optional, one per line)", EditorStyles.boldLabel);
        planetNamesRaw = EditorGUILayout.TextArea(planetNamesRaw, GUILayout.Height(80));

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Galaxy Map"))
        {
            Generate();
        }

        if (GUILayout.Button("Clear Generated Map"))
        {
            Clear();
        }
    }

    private void Generate()
    {
        if (LayerMask.NameToLayer(planetLayerName) < 0)
        {
            Debug.LogError($"Layer '{planetLayerName}' doesn't exist. Add it in Project Settings > Tags and Layers first.");
            return;
        }

        if (coreRadius >= mapRadius)
        {
            Debug.LogError("Core Radius must be smaller than Map Radius.");
            return;
        }

        Clear();

        Undo.SetCurrentGroupName("Generate Galactic Map");
        int undoGroup = Undo.GetCurrentGroup();

        GameObject parent = new GameObject(ParentName);
        Undo.RegisterCreatedObjectUndo(parent, "Generate Galactic Map");

        if (seed != 0)
        {
            Random.InitState(seed);
        }

        if (spawnCoreMarker)
        {
            SpawnCoreMarker(parent.transform, coreRadius);
        }

        List<Vector3> points = ScatterPoints(planetCount, coreRadius, mapRadius, minPlanetDistance);
        List<string> names = BuildNameList(points.Count);

        Planet[] planets = new Planet[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            planets[i] = CreatePlanet(points[i], names[i], parent.transform);
        }

        ConnectPlanets(planets);
        AssignFactionTerritories(planets);

        if (shipyardSiteCount > 0)
        {
            ShipyardSitePlanner.Distribute(planets, shipyardSiteCount, shipyardSitePerFaction, seed);
        }

        foreach (Planet planet in planets)
        {
            EditorUtility.SetDirty(planet);
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = parent;
    }

    private void Clear()
    {
        GameObject existing = GameObject.Find(ParentName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }

    private void AssignFactionTerritories(Planet[] planets)
    {
        List<FactionTerritoryConfig> configs = new List<FactionTerritoryConfig>();
        HashSet<Faction> seenFactions = new HashSet<Faction>();
        foreach (FactionTerritoryConfig entry in factionTerritories)
        {
            if (entry.faction == null) continue;

            if (!seenFactions.Add(entry.faction))
            {
                Debug.LogWarning($"'{entry.faction.factionName}' is listed more than once in Faction Territories - only the first entry is used.");
                continue;
            }

            configs.Add(entry);
        }

        if (configs.Count == 0 || planets.Length == 0) return;

        List<int> remainingIndices = Enumerable.Range(0, planets.Length).ToList();
        Dictionary<Faction, int> claimedCounts = configs.ToDictionary(c => c.faction, c => 0);
        Dictionary<Faction, int> maxPlanetsByFaction = configs.ToDictionary(c => c.faction, c => c.maxPlanets);

        bool CanClaim(Faction faction)
        {
            int max = maxPlanetsByFaction[faction];
            return max <= 0 || claimedCounts[faction] < max;
        }

        void Claim(int planetIndex, Faction faction)
        {
            planets[planetIndex].SetOwnership(faction);
            claimedCounts[faction]++;
        }

        foreach (FactionTerritoryConfig config in configs.Where(c => c.faction.isNeutral))
        {
            AssignNeutralHoldings(planets, remainingIndices, config, CanClaim, Claim);
        }

        List<FactionTerritoryConfig> warringConfigs = configs.Where(c => !c.faction.isNeutral).ToList();
        if (warringConfigs.Count == 0) return;

        List<(Faction faction, Vector3 position, float weight)> capitals = new List<(Faction, Vector3, float)>();
        foreach (FactionTerritoryConfig config in warringConfigs)
        {
            for (int c = 0; c < Mathf.Max(1, config.capitalCount) && remainingIndices.Count > 0 && CanClaim(config.faction); c++)
            {
                int pickAt = Random.Range(0, remainingIndices.Count);
                int planetIndex = remainingIndices[pickAt];
                remainingIndices.RemoveAt(pickAt);

                Claim(planetIndex, config.faction);
                capitals.Add((config.faction, planets[planetIndex].transform.position, Mathf.Max(0.01f, config.territoryWeight)));
            }
        }

        if (capitals.Count == 0) return;

        foreach (int index in remainingIndices)
        {
            Vector3 position = planets[index].transform.position;
            Faction closest = null;
            float bestScore = float.MaxValue;

            foreach ((Faction faction, Vector3 capitalPos, float weight) in capitals)
            {
                if (!CanClaim(faction)) continue;

                float score = Vector3.Distance(position, capitalPos) / weight;
                if (score < bestScore)
                {
                    bestScore = score;
                    closest = faction;
                }
            }

            if (closest != null)
            {
                Claim(index, closest);
            }
        }
    }

    private void AssignNeutralHoldings(Planet[] planets, List<int> remainingIndices, FactionTerritoryConfig config, System.Func<Faction, bool> canClaim, System.Action<int, Faction> claim)
    {
        int target = config.maxPlanets > 0 ? Mathf.Min(config.capitalCount, config.maxPlanets) : config.capitalCount;
        int placed = 0;

        while (placed < target && remainingIndices.Count > 0 && canClaim(config.faction))
        {
            int pickAt = Random.Range(0, remainingIndices.Count);
            int seedIndex = remainingIndices[pickAt];
            remainingIndices.RemoveAt(pickAt);

            claim(seedIndex, config.faction);
            placed++;

            bool formCluster = placed < target && Random.value < 0.5f;
            if (formCluster)
            {
                int clusterSize = Random.Range(1, 3); // Pull in 1-2 nearby neighbors to form a small pocket.
                Vector3 seedPosition = planets[seedIndex].transform.position;

                List<int> nearestNeighbors = remainingIndices
                    .OrderBy(i => Vector3.Distance(planets[i].transform.position, seedPosition))
                    .Take(clusterSize)
                    .ToList();

                foreach (int neighborIndex in nearestNeighbors)
                {
                    if (placed >= target || !canClaim(config.faction)) break;

                    claim(neighborIndex, config.faction);
                    remainingIndices.Remove(neighborIndex);
                    placed++;
                }
            }
        }
    }

    private void SpawnCoreMarker(Transform parent, float radius)
    {
        GameObject marker = coreMarkerPrefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(coreMarkerPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        if (coreMarkerPrefab == null)
        {
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            Renderer markerRenderer = marker.GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                markerRenderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.black };
            }
        }

        marker.transform.SetParent(parent, false);
        marker.transform.position = Vector3.zero;
        marker.transform.localScale = Vector3.one * radius * 2f;
        marker.name = "BlackHoleCore";

        Undo.RegisterCreatedObjectUndo(marker, "Generate Galactic Map");
    }

    private List<Vector3> ScatterPoints(int count, float innerRadius, float outerRadius, float minDistance)
    {
        List<Vector3> points = new List<Vector3>();
        SpatialGrid grid = new SpatialGrid(minDistance);
        int maxAttempts = count * 80;
        int attempts = 0;

        List<Vector2> clusterCenters = null;
        if (distributionMode == DistributionMode.Clustered)
        {
            clusterCenters = new List<Vector2>();
            for (int i = 0; i < Mathf.Max(1, clusterCount); i++)
            {
                clusterCenters.Add(RandomPointInAnnulus(innerRadius, outerRadius));
            }
        }

        while (points.Count < count && attempts < maxAttempts)
        {
            attempts++;

            Vector2 candidate2D = distributionMode switch
            {
                DistributionMode.Clustered => RandomPointNearCluster(clusterCenters, innerRadius, outerRadius),
                DistributionMode.Spiral => RandomPointOnSpiral(innerRadius, outerRadius),
                _ => RandomPointInAnnulus(innerRadius, outerRadius)
            };

            Vector3 candidate = new Vector3(candidate2D.x, 0f, candidate2D.y);

            if (grid.IsFarEnough(candidate, minDistance))
            {
                points.Add(candidate);
                grid.Add(candidate);
            }
        }

        if (points.Count < count)
        {
            Debug.LogWarning($"Only placed {points.Count}/{count} planets - reduce Min Planet Spacing, shrink Core Radius, or increase Map Radius to fit them all.");
        }

        return points;
    }

    private Vector2 RandomPointInAnnulus(float innerRadius, float outerRadius)
    {
        float angle = Random.value * Mathf.PI * 2f;
        float r = Mathf.Sqrt(Random.Range(innerRadius * innerRadius, outerRadius * outerRadius));
        return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
    }

    private Vector2 RandomPointNearCluster(List<Vector2> centers, float innerRadius, float outerRadius)
    {
        Vector2 center = centers[Random.Range(0, centers.Count)];

        for (int i = 0; i < 10; i++)
        {
            Vector2 candidate = center + Random.insideUnitCircle * clusterSpread;
            float r = candidate.magnitude;
            if (r >= innerRadius && r <= outerRadius)
            {
                return candidate;
            }
        }

        return RandomPointInAnnulus(innerRadius, outerRadius);
    }

    private Vector2 RandomPointOnSpiral(float innerRadius, float outerRadius)
    {
        int armCount = Mathf.Max(1, spiralArms);
        int arm = Random.Range(0, armCount);
        float t = Random.value;
        float r = Mathf.Lerp(innerRadius, outerRadius, t);
        float baseAngle = (Mathf.PI * 2f / armCount) * arm;
        float angle = baseAngle + t * spiralTwist;

        Vector2 armPoint = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        Vector2 candidate = armPoint + Random.insideUnitCircle * spiralArmWidth;

        float finalRadius = candidate.magnitude;
        if (finalRadius < innerRadius || finalRadius > outerRadius)
        {
            finalRadius = Mathf.Clamp(finalRadius, innerRadius, outerRadius);
            candidate = candidate.normalized * finalRadius;
        }

        return candidate;
    }

    private List<string> BuildNameList(int count)
    {
        List<string> custom = planetNamesRaw
            .Split('\n')
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .ToList();

        List<string> names = new List<string>();
        for (int i = 0; i < count; i++)
        {
            if (i < custom.Count)
            {
                names.Add(custom[i]);
                continue;
            }

            int index = i - custom.Count;
            string baseName = FallbackNames[index % FallbackNames.Length];
            int cycle = index / FallbackNames.Length;
            string suffix = NameSuffixes[cycle % NameSuffixes.Length];
            string name = baseName + suffix;

            if (cycle >= NameSuffixes.Length)
            {
                name += $" {cycle / NameSuffixes.Length + 1}";
            }

            names.Add(name);
        }

        return names;
    }

    private Planet CreatePlanet(Vector3 position, string name, Transform parent)
    {
        GameObject go = planetPrefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(planetPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.name = $"Planet_{name}";
        go.layer = LayerMask.NameToLayer(planetLayerName);

        Planet planet = go.GetComponent<Planet>();
        if (planet == null)
        {
            planet = go.AddComponent<Planet>();
        }

        planet.planetName = name;

        Undo.RegisterCreatedObjectUndo(go, "Generate Galactic Map");
        return planet;
    }

    private void ConnectPlanets(Planet[] planets)
    {
        int n = planets.Length;
        if (n < 2) return;

        float[,] dist = new float[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                dist[i, j] = Vector3.Distance(planets[i].transform.position, planets[j].transform.position);
            }
        }

        bool[] inTree = new bool[n];
        inTree[0] = true;
        List<(int a, int b)> mstEdges = new List<(int, int)>();

        for (int edgeCount = 0; edgeCount < n - 1; edgeCount++)
        {
            float best = float.MaxValue;
            int bestA = -1, bestB = -1;

            for (int i = 0; i < n; i++)
            {
                if (!inTree[i]) continue;

                for (int j = 0; j < n; j++)
                {
                    if (inTree[j]) continue;

                    if (dist[i, j] < best)
                    {
                        best = dist[i, j];
                        bestA = i;
                        bestB = j;
                    }
                }
            }

            if (bestB == -1) break;

            inTree[bestB] = true;
            mstEdges.Add((bestA, bestB));
        }

        int[] connectionCounts = new int[n];
        HashSet<(int, int)> connected = new HashSet<(int, int)>();
        foreach ((int a, int b) in mstEdges)
        {
            Connect(planets, a, b, connected, connectionCounts);
        }

        float avgMstLength = mstEdges.Count > 0 ? mstEdges.Average(e => dist[e.a, e.b]) : 0f;
        float extraLaneMaxDistance = avgMstLength * extraLaneDistanceFactor;

        List<(int a, int b, float d)> candidates = new List<(int, int, float)>();
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (connected.Contains((i, j)) || connected.Contains((j, i))) continue;
                if (dist[i, j] > extraLaneMaxDistance) continue;

                candidates.Add((i, j, dist[i, j]));
            }
        }

        candidates.Sort((x, y) => x.d.CompareTo(y.d));

        foreach ((int a, int b, float d) in candidates)
        {
            if (maxConnectionsPerPlanet > 0 && (connectionCounts[a] >= maxConnectionsPerPlanet || connectionCounts[b] >= maxConnectionsPerPlanet))
            {
                continue;
            }

            if (Random.value < extraLaneChance)
            {
                Connect(planets, a, b, connected, connectionCounts);
            }
        }
    }

    private void Connect(Planet[] planets, int a, int b, HashSet<(int, int)> connected, int[] connectionCounts)
    {
        planets[a].connections.Add(planets[b]);
        planets[b].connections.Add(planets[a]);
        connected.Add((a, b));
        connectionCounts[a]++;
        connectionCounts[b]++;
    }

    private class SpatialGrid
    {
        private readonly float cellSize;
        private readonly Dictionary<(int, int), List<Vector3>> cells = new Dictionary<(int, int), List<Vector3>>();

        public SpatialGrid(float cellSize)
        {
            this.cellSize = Mathf.Max(cellSize, 0.01f);
        }

        private (int, int) CellOf(Vector3 point)
        {
            return (Mathf.FloorToInt(point.x / cellSize), Mathf.FloorToInt(point.z / cellSize));
        }

        public void Add(Vector3 point)
        {
            (int, int) cell = CellOf(point);
            if (!cells.TryGetValue(cell, out List<Vector3> list))
            {
                list = new List<Vector3>();
                cells[cell] = list;
            }
            list.Add(point);
        }

        public bool IsFarEnough(Vector3 point, float minDistance)
        {
            (int cx, int cz) = CellOf(point);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!cells.TryGetValue((cx + dx, cz + dz), out List<Vector3> list)) continue;

                    foreach (Vector3 existing in list)
                    {
                        if (Vector3.Distance(existing, point) < minDistance) return false;
                    }
                }
            }

            return true;
        }
    }
}
