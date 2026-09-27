using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class GalacticMapGeneratorWindow : EditorWindow
{
    private const string ParentName = "Planets";

    private static readonly string[] FallbackNames =
    {
        "Coruscant", "Kamino", "Geonosis", "Mustafar", "Hoth", "Bespin", "Endor", "Kashyyyk",
        "Mygeeto", "Felucia", "Utapau", "Dagobah", "Tatooine", "Naboo", "Alderaan", "Yavin",
        "Sullust", "Corellia", "Dantooine", "Ryloth"
    };

    private int planetCount = 12;
    private int seed = 0;
    private float mapRadius = 40f;
    private float minPlanetDistance = 8f;
    private float extraLaneChance = 0.15f;
    private float extraLaneDistanceFactor = 1.5f;
    private string planetLayerName = "Planet";
    private GameObject planetPrefab;
    private string planetNamesRaw = "";

    [MenuItem("Osiris/Galactic Map Generator")]
    public static void Open()
    {
        GetWindow<GalacticMapGeneratorWindow>("Galaxy Map Generator");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
        planetCount = EditorGUILayout.IntSlider("Planet Count", planetCount, 2, 80);
        seed = EditorGUILayout.IntField("Seed (0 = random)", seed);
        mapRadius = EditorGUILayout.FloatField("Map Radius", mapRadius);
        minPlanetDistance = EditorGUILayout.FloatField("Min Planet Spacing", minPlanetDistance);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Hyperspace Lanes", EditorStyles.boldLabel);
        extraLaneChance = EditorGUILayout.Slider("Extra Lane Chance", extraLaneChance, 0f, 1f);
        extraLaneDistanceFactor = EditorGUILayout.FloatField("Extra Lane Distance Factor", extraLaneDistanceFactor);

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

        Clear();

        Undo.SetCurrentGroupName("Generate Galactic Map");
        int undoGroup = Undo.GetCurrentGroup();

        GameObject parent = new GameObject(ParentName);
        Undo.RegisterCreatedObjectUndo(parent, "Generate Galactic Map");

        if (seed != 0)
        {
            Random.InitState(seed);
        }

        List<Vector3> points = ScatterPoints(planetCount, mapRadius, minPlanetDistance);
        List<string> names = BuildNameList(points.Count);

        Planet[] planets = new Planet[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            planets[i] = CreatePlanet(points[i], names[i], parent.transform);
        }

        ConnectPlanets(planets);

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

    private List<Vector3> ScatterPoints(int count, float radius, float minDistance)
    {
        List<Vector3> points = new List<Vector3>();
        int maxAttempts = count * 50;
        int attempts = 0;

        while (points.Count < count && attempts < maxAttempts)
        {
            attempts++;
            Vector2 circlePoint = Random.insideUnitCircle * radius;
            Vector3 candidate = new Vector3(circlePoint.x, 0f, circlePoint.y);

            bool farEnough = true;
            foreach (Vector3 existing in points)
            {
                if (Vector3.Distance(existing, candidate) < minDistance)
                {
                    farEnough = false;
                    break;
                }
            }

            if (farEnough)
            {
                points.Add(candidate);
            }
        }

        if (points.Count < count)
        {
            Debug.LogWarning($"Only placed {points.Count}/{count} planets - reduce Min Planet Spacing or increase Map Radius to fit them all.");
        }

        return points;
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
            }
            else
            {
                int lap = i / FallbackNames.Length;
                string baseName = FallbackNames[i % FallbackNames.Length];
                names.Add(lap > 0 ? $"{baseName} {lap + 1}" : baseName);
            }
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

        float avgMstLength = mstEdges.Count > 0 ? mstEdges.Average(e => dist[e.a, e.b]) : 0f;
        float extraLaneMaxDistance = avgMstLength * extraLaneDistanceFactor;

        HashSet<(int, int)> connected = new HashSet<(int, int)>();
        foreach ((int a, int b) in mstEdges)
        {
            Connect(planets, a, b, connected);
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (connected.Contains((i, j)) || connected.Contains((j, i))) continue;
                if (dist[i, j] > extraLaneMaxDistance) continue;

                if (Random.value < extraLaneChance)
                {
                    Connect(planets, i, j, connected);
                }
            }
        }
    }

    private void Connect(Planet[] planets, int a, int b, HashSet<(int, int)> connected)
    {
        planets[a].connections.Add(planets[b]);
        planets[b].connections.Add(planets[a]);
        connected.Add((a, b));
    }
}
