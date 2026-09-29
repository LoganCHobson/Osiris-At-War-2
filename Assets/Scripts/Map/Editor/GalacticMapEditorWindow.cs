using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class GalacticMapEditorWindow : EditorWindow
{
    private enum ToolMode { Select, ShipyardSites, Lanes, PaintOwner, AddPlanet }

    private static readonly string[] ToolModeLabels = { "Select", "Shipyard Sites", "Lanes", "Paint Owner", "Add Planet" };
    private static readonly Color SiteColor = new Color(1f, 0.6f, 0f, 1f);
    private static readonly Color LaneStartColor = new Color(0f, 1f, 1f, 1f);

    private ToolMode toolMode = ToolMode.Select;
    private bool showLabels = true;
    private float handleRadius = 1.5f;

    private int siteTarget = 6;
    private bool sitePerFaction = true;
    private int siteSeed;

    private Faction paintFaction;
    private Planet laneStart;
    private Planet planetTemplate;

    private string filter = "";
    private bool onlySites;
    private Vector2 scroll;

    private List<Planet> planets = new List<Planet>();
    private List<Faction> factions = new List<Faction>();
    private List<string> issues;

    private GUIStyle labelStyle;

    [MenuItem("Osiris/Galactic Map Editor")]
    public static void Open()
    {
        GetWindow<GalacticMapEditorWindow>("Galaxy Map Editor");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.hierarchyChanged += Refresh;
        Undo.undoRedoPerformed += OnUndoRedo;
        Refresh();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.hierarchyChanged -= Refresh;
        Undo.undoRedoPerformed -= OnUndoRedo;
        SceneView.RepaintAll();
    }

    private void OnFocus()
    {
        Refresh();
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnUndoRedo()
    {
        Refresh();
        foreach (Planet planet in planets)
        {
            planet.UpdateOwnershipVisual();
        }
        SceneView.RepaintAll();
    }

    private void Refresh()
    {
        planets = FindObjectsByType<Planet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .OrderBy(p => p.planetName)
            .ToList();

        factions = AssetDatabase.FindAssets("t:Faction")
            .Select(guid => AssetDatabase.LoadAssetAtPath<Faction>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(f => f != null)
            .OrderBy(f => f.factionName)
            .ToList();

        if (planetTemplate == null && planets.Count > 0)
        {
            planetTemplate = planets[0];
        }

        Repaint();
    }

    private void OnGUI()
    {
        planets.RemoveAll(p => p == null);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        if (EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("You're in Play Mode - edits made now are lost when you stop.", MessageType.Warning);
        }

        DrawToolSection();
        EditorGUILayout.Space();
        DrawSummary();
        EditorGUILayout.Space();
        DrawShipyardSection();
        EditorGUILayout.Space();
        DrawMapActions();
        EditorGUILayout.Space();
        DrawValidation();
        EditorGUILayout.Space();
        DrawPlanetList();

        EditorGUILayout.EndScrollView();
    }

    private void DrawToolSection()
    {
        EditorGUILayout.LabelField("Scene Tool", EditorStyles.boldLabel);

        ToolMode newMode = (ToolMode)GUILayout.Toolbar((int)toolMode, ToolModeLabels);
        if (newMode != toolMode)
        {
            toolMode = newMode;
            laneStart = null;
            SceneView.RepaintAll();
        }

        EditorGUILayout.HelpBox(ModeHint(), MessageType.None);

        if (toolMode == ToolMode.PaintOwner)
        {
            DrawFactionPalette();
        }
        else if (toolMode == ToolMode.AddPlanet)
        {
            planetTemplate = (Planet)EditorGUILayout.ObjectField("Template Planet", planetTemplate, typeof(Planet), true);
        }

        EditorGUI.BeginChangeCheck();
        showLabels = EditorGUILayout.Toggle("Show Names In Scene", showLabels);
        handleRadius = EditorGUILayout.Slider("Handle Radius", handleRadius, 0.3f, 6f);
        if (EditorGUI.EndChangeCheck())
        {
            SceneView.RepaintAll();
        }
    }

    private string ModeHint()
    {
        return toolMode switch
        {
            ToolMode.ShipyardSites => "Click a system in the Scene view to toggle whether a Capital Shipyard can be built there. Orange rings = shipyard sites, filled = shipyard already built at start.",
            ToolMode.Lanes => "Click a system, then another, to add or remove a hyperspace lane between them. Keeps chaining from the last system clicked. Esc to stop chaining.",
            ToolMode.PaintOwner => "Pick a faction below, then click systems in the Scene view to assign them.",
            ToolMode.AddPlanet => "Click empty space in the Scene view to place a new system (cloned from the template, with no lanes or buildings).",
            _ => "Normal Unity selection. Move systems with the Move tool - lanes follow automatically."
        };
    }

    private void DrawFactionPalette()
    {
        EditorGUILayout.BeginHorizontal();

        Color previous = GUI.backgroundColor;
        if (GUILayout.Toggle(paintFaction == null, "Unowned", "Button"))
        {
            paintFaction = null;
        }

        foreach (Faction faction in factions)
        {
            GUI.backgroundColor = faction.color;
            if (GUILayout.Toggle(paintFaction == faction, faction.factionName, "Button"))
            {
                paintFaction = faction;
            }
        }

        GUI.backgroundColor = previous;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSummary()
    {
        EditorGUILayout.LabelField("Overview", EditorStyles.boldLabel);

        int siteCount = planets.Count(p => p.IsShipyardSite);
        int builtCount = planets.Count(p => p.hasCapitalShipyard);
        EditorGUILayout.LabelField($"{planets.Count} systems   |   {siteCount} shipyard sites   |   {builtCount} shipyards built at start");

        foreach (IGrouping<Faction, Planet> group in planets.GroupBy(p => p.owner).OrderBy(g => g.Key != null ? g.Key.factionName : "~"))
        {
            string factionName = group.Key != null ? group.Key.factionName : "Unowned";
            int sites = group.Count(p => p.IsShipyardSite);
            EditorGUILayout.LabelField($"   {factionName}", $"{group.Count()} systems, {sites} shipyard sites");
        }
    }

    private void DrawShipyardSection()
    {
        EditorGUILayout.LabelField("Capital Shipyard Sites", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Only shipyard sites can build a Capital Shipyard (player and AI). Distribute spreads sites evenly across the map; systems that start with a shipyard always stay sites. 'One per faction' gives each warring faction a site near the heart of its territory.", MessageType.Info);

        siteTarget = EditorGUILayout.IntSlider("Total Sites", siteTarget, 0, Mathf.Max(1, planets.Count));
        sitePerFaction = EditorGUILayout.Toggle("Guarantee One Per Faction", sitePerFaction);
        siteSeed = EditorGUILayout.IntField("Seed (0 = random)", siteSeed);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Distribute Sites"))
        {
            RunGrouped("Distribute Shipyard Sites", () => ShipyardSitePlanner.Distribute(planets, siteTarget, sitePerFaction, siteSeed));
        }
        if (GUILayout.Button("Allow All"))
        {
            RunGrouped("Allow All Shipyard Sites", () => planets.ForEach(p => ShipyardSitePlanner.SetSite(p, true, "Allow All Shipyard Sites")));
        }
        if (GUILayout.Button("Clear All"))
        {
            RunGrouped("Clear Shipyard Sites", () => planets.ForEach(p => ShipyardSitePlanner.SetSite(p, false, "Clear Shipyard Sites")));
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMapActions()
    {
        EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);

        List<Planet> selected = SelectedPlanets();
        EditorGUILayout.LabelField($"{selected.Count} systems selected");

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(selected.Count == 0))
        {
            if (GUILayout.Button("Make Sites"))
            {
                RunGrouped("Make Shipyard Sites", () => selected.ForEach(p => ShipyardSitePlanner.SetSite(p, true, "Make Shipyard Sites")));
            }
            if (GUILayout.Button("Remove Sites"))
            {
                RunGrouped("Remove Shipyard Sites", () => selected.ForEach(p => ShipyardSitePlanner.SetSite(p, false, "Remove Shipyard Sites")));
            }
            if (GUILayout.Button("Delete Systems"))
            {
                DeletePlanets(selected);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawValidation()
    {
        EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Check Map"))
        {
            issues = Validate();
        }
        if (GUILayout.Button("Repair Lanes"))
        {
            RunGrouped("Repair Hyperspace Lanes", RepairLanes);
            issues = Validate();
        }
        EditorGUILayout.EndHorizontal();

        if (issues == null) return;

        if (issues.Count == 0)
        {
            EditorGUILayout.HelpBox("No problems found.", MessageType.Info);
            return;
        }

        foreach (string issue in issues)
        {
            EditorGUILayout.HelpBox(issue, MessageType.Warning);
        }
    }

    private void DrawPlanetList()
    {
        EditorGUILayout.LabelField("Systems", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        filter = EditorGUILayout.TextField("Filter", filter);
        onlySites = GUILayout.Toggle(onlySites, "Sites Only", "Button", GUILayout.Width(80));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(26);
        GUILayout.Label("Name", EditorStyles.miniBoldLabel, GUILayout.MinWidth(100));
        GUILayout.Label("Owner", EditorStyles.miniBoldLabel, GUILayout.Width(120));
        GUILayout.Label("Site", EditorStyles.miniBoldLabel, GUILayout.Width(32));
        GUILayout.Label("Tax", EditorStyles.miniBoldLabel, GUILayout.Width(32));
        GUILayout.Label("Yard", EditorStyles.miniBoldLabel, GUILayout.Width(32));
        GUILayout.Label("Stn", EditorStyles.miniBoldLabel, GUILayout.Width(32));
        GUILayout.Label("Lanes", EditorStyles.miniBoldLabel, GUILayout.Width(40));
        EditorGUILayout.EndHorizontal();

        HashSet<GameObject> selection = new HashSet<GameObject>(Selection.gameObjects);
        Color previous = GUI.backgroundColor;

        foreach (Planet planet in planets)
        {
            if (onlySites && !planet.IsShipyardSite) continue;
            if (!string.IsNullOrEmpty(filter) && !DisplayName(planet).ToLowerInvariant().Contains(filter.ToLowerInvariant())) continue;

            GUI.backgroundColor = selection.Contains(planet.gameObject) ? new Color(0.6f, 0.8f, 1f) : previous;
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.backgroundColor = previous;

            if (GUILayout.Button(">", GUILayout.Width(22)))
            {
                Selection.activeGameObject = planet.gameObject;
                if (SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.FrameSelected();
                }
            }

            string newName = EditorGUILayout.DelayedTextField(planet.planetName, GUILayout.MinWidth(100));
            Faction newOwner = (Faction)EditorGUILayout.ObjectField(planet.owner, typeof(Faction), false, GUILayout.Width(120));
            bool newSite = EditorGUILayout.Toggle(planet.canBuildCapitalShipyard, GUILayout.Width(32));
            bool newTax = EditorGUILayout.Toggle(planet.hasTaxOffice, GUILayout.Width(32));
            bool newYard = EditorGUILayout.Toggle(planet.hasCapitalShipyard, GUILayout.Width(32));
            bool newStation = EditorGUILayout.Toggle(planet.hasBattleStation, GUILayout.Width(32));
            GUILayout.Label(planet.connections.Count(c => c != null).ToString(), GUILayout.Width(40));

            EditorGUILayout.EndHorizontal();

            if (newName != planet.planetName)
            {
                RenamePlanet(planet, newName);
            }

            if (newOwner != planet.owner || newSite != planet.canBuildCapitalShipyard || newTax != planet.hasTaxOffice
                || newYard != planet.hasCapitalShipyard || newStation != planet.hasBattleStation)
            {
                Undo.RecordObject(planet, "Edit System");
                planet.canBuildCapitalShipyard = newSite || (newYard && !planet.hasCapitalShipyard);
                planet.hasTaxOffice = newTax;
                planet.hasCapitalShipyard = newYard && planet.canBuildCapitalShipyard;
                planet.hasBattleStation = newStation;
                planet.SetOwnership(newOwner);
                MarkDirty(planet);
            }
        }

        GUI.backgroundColor = previous;
    }

    private void OnSceneGUI(SceneView view)
    {
        Event e = Event.current;

        if (toolMode != ToolMode.Select)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (laneStart != null)
                {
                    laneStart = null;
                }
                else
                {
                    toolMode = ToolMode.Select;
                    Repaint();
                }
                e.Use();
            }
        }

        Quaternion flat = Quaternion.LookRotation(Vector3.up);
        bool clickable = toolMode == ToolMode.ShipyardSites || toolMode == ToolMode.Lanes || toolMode == ToolMode.PaintOwner;
        Planet clicked = null;

        foreach (Planet planet in planets)
        {
            if (planet == null) continue;

            Vector3 position = planet.transform.position;
            DrawPlanetOverlay(planet, position);

            if (!clickable) continue;

            Handles.color = new Color(1f, 1f, 1f, 0.35f);
            if (Handles.Button(position, flat, handleRadius, handleRadius, Handles.CircleHandleCap))
            {
                clicked = planet;
            }
        }

        if (clicked != null)
        {
            OnPlanetClicked(clicked);
        }

        if (toolMode == ToolMode.Lanes && laneStart != null && TryGetMousePoint(out Vector3 mousePoint))
        {
            Handles.color = LaneStartColor;
            Handles.DrawDottedLine(laneStart.transform.position, mousePoint, 4f);
            if (e.type == EventType.MouseMove)
            {
                view.Repaint();
            }
        }

        if (toolMode == ToolMode.AddPlanet && e.type == EventType.MouseDown && e.button == 0 && !e.alt && TryGetMousePoint(out Vector3 placePoint))
        {
            AddPlanet(placePoint);
            e.Use();
        }
    }

    private void DrawPlanetOverlay(Planet planet, Vector3 position)
    {
        if (planet.IsShipyardSite)
        {
            Handles.color = SiteColor;
            Handles.DrawWireDisc(position, Vector3.up, handleRadius * 1.3f, 3f);

            if (planet.hasCapitalShipyard)
            {
                Handles.color = new Color(SiteColor.r, SiteColor.g, SiteColor.b, 0.3f);
                Handles.DrawSolidDisc(position, Vector3.up, handleRadius * 1.3f);
            }
        }

        if (planet == laneStart)
        {
            Handles.color = LaneStartColor;
            Handles.DrawWireDisc(position, Vector3.up, handleRadius * 1.6f, 3f);
        }

        if (!showLabels) return;

        labelStyle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
        labelStyle.normal.textColor = planet.owner != null ? planet.owner.color : Color.white;

        string text = planet.IsShipyardSite ? $"{DisplayName(planet)} [Yard]" : DisplayName(planet);
        Handles.Label(position + Vector3.back * (handleRadius * 1.6f), text, labelStyle);
    }

    private void OnPlanetClicked(Planet planet)
    {
        switch (toolMode)
        {
            case ToolMode.ShipyardSites:
                Undo.RecordObject(planet, "Toggle Shipyard Site");
                if (planet.IsShipyardSite)
                {
                    planet.canBuildCapitalShipyard = false;
                    planet.hasCapitalShipyard = false;
                }
                else
                {
                    planet.canBuildCapitalShipyard = true;
                }
                MarkDirty(planet);
                break;

            case ToolMode.PaintOwner:
                if (planet.owner == paintFaction) break;
                Undo.RecordObject(planet, "Paint System Owner");
                planet.SetOwnership(paintFaction);
                MarkDirty(planet);
                break;

            case ToolMode.Lanes:
                if (laneStart == null)
                {
                    laneStart = planet;
                }
                else if (laneStart == planet)
                {
                    laneStart = null;
                }
                else
                {
                    ToggleLane(laneStart, planet);
                    laneStart = planet;
                }
                break;
        }

        Repaint();
        SceneView.RepaintAll();
    }

    private void ToggleLane(Planet a, Planet b)
    {
        Undo.RecordObjects(new Object[] { a, b }, "Toggle Hyperspace Lane");

        if (a.connections.Contains(b) || b.connections.Contains(a))
        {
            a.connections.RemoveAll(c => c == b);
            b.connections.RemoveAll(c => c == a);
        }
        else
        {
            a.connections.Add(b);
            b.connections.Add(a);
        }

        MarkDirty(a);
        MarkDirty(b);
    }

    private void AddPlanet(Vector3 position)
    {
        if (planetTemplate == null)
        {
            Debug.LogWarning("Galaxy Map Editor: set a Template Planet before adding systems.");
            return;
        }

        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(planetTemplate.gameObject);
        GameObject go = source != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(source, planetTemplate.gameObject.scene)
            : Instantiate(planetTemplate.gameObject);

        go.transform.SetParent(planetTemplate.transform.parent, false);
        go.transform.position = new Vector3(position.x, planetTemplate.transform.position.y, position.z);
        go.transform.rotation = planetTemplate.transform.rotation;
        go.transform.localScale = planetTemplate.transform.localScale;
        go.layer = planetTemplate.gameObject.layer;

        Planet planet = go.GetComponent<Planet>();
        string planetName = UniqueName("New System");
        go.name = $"Planet_{planetName}";
        planet.planetName = planetName;
        planet.connections = new List<Planet>();
        planet.shipBuildQueue = new List<ShipBuildOrder>();
        planet.canBuildCapitalShipyard = false;
        planet.hasTaxOffice = false;
        planet.hasCapitalShipyard = false;
        planet.hasBattleStation = false;
        planet.SetOwnership(null);

        Undo.RegisterCreatedObjectUndo(go, "Add System");
        MarkDirty(planet);
        Refresh();
    }

    private void DeletePlanets(List<Planet> doomed)
    {
        if (!EditorUtility.DisplayDialog("Delete Systems", $"Delete {doomed.Count} system(s) and all their hyperspace lanes?", "Delete", "Cancel")) return;

        RunGrouped("Delete Systems", () =>
        {
            HashSet<Planet> doomedSet = new HashSet<Planet>(doomed);
            foreach (Planet planet in planets)
            {
                if (doomedSet.Contains(planet) || !planet.connections.Any(doomedSet.Contains)) continue;

                Undo.RecordObject(planet, "Delete Systems");
                planet.connections.RemoveAll(doomedSet.Contains);
                MarkDirty(planet);
            }

            foreach (Planet planet in doomed)
            {
                Undo.DestroyObjectImmediate(planet.gameObject);
            }
        });

        laneStart = null;
        Refresh();
    }

    private void RenamePlanet(Planet planet, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return;

        Undo.RecordObjects(new Object[] { planet, planet.gameObject }, "Rename System");
        planet.planetName = newName;
        planet.gameObject.name = $"Planet_{newName}";
        MarkDirty(planet);
    }

    private List<string> Validate()
    {
        List<string> found = new List<string>();

        foreach (Planet planet in planets)
        {
            string label = DisplayName(planet);
            List<Planet> lanes = planet.connections.Where(c => c != null && c != planet).ToList();

            if (string.IsNullOrWhiteSpace(planet.planetName)) found.Add($"'{planet.name}' has no planet name.");
            if (lanes.Count == 0) found.Add($"{label} has no hyperspace lanes.");
            if (lanes.Count != planet.connections.Count) found.Add($"{label} has empty or self-referencing lanes.");
            if (lanes.Distinct().Count() != lanes.Count) found.Add($"{label} has duplicate lanes.");

            foreach (Planet other in lanes.Distinct())
            {
                if (!other.connections.Contains(planet)) found.Add($"Lane {label} -> {DisplayName(other)} is one-way.");
            }
        }

        foreach (IGrouping<string, Planet> duplicate in planets.GroupBy(p => p.planetName).Where(g => g.Count() > 1))
        {
            found.Add($"{duplicate.Count()} systems are named '{duplicate.Key}' - save/load matches systems by name, so names must be unique.");
        }

        int regions = CountRegions();
        if (regions > 1) found.Add($"The map is split into {regions} regions that can't reach each other.");

        foreach (IGrouping<Faction, Planet> territory in planets.Where(p => p.owner != null && !p.owner.isNeutral).GroupBy(p => p.owner))
        {
            if (!territory.Any(p => p.IsShipyardSite)) found.Add($"{territory.Key.factionName} owns no shipyard sites - it can't build capital ships until it captures one.");
        }

        return found;
    }

    private int CountRegions()
    {
        HashSet<Planet> visited = new HashSet<Planet>();
        int regions = 0;

        foreach (Planet start in planets)
        {
            if (!visited.Add(start)) continue;
            regions++;

            Queue<Planet> frontier = new Queue<Planet>();
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                Planet current = frontier.Dequeue();
                foreach (Planet next in current.connections)
                {
                    if (next != null && visited.Add(next)) frontier.Enqueue(next);
                }
                foreach (Planet other in planets)
                {
                    if (other.connections.Contains(current) && visited.Add(other)) frontier.Enqueue(other);
                }
            }
        }

        return regions;
    }

    private void RepairLanes()
    {
        foreach (Planet planet in planets)
        {
            List<Planet> clean = planet.connections.Where(c => c != null && c != planet).Distinct().ToList();
            if (clean.Count != planet.connections.Count)
            {
                Undo.RecordObject(planet, "Repair Hyperspace Lanes");
                planet.connections = clean;
                MarkDirty(planet);
            }
        }

        foreach (Planet planet in planets)
        {
            foreach (Planet other in planet.connections)
            {
                if (other.connections.Contains(planet)) continue;

                Undo.RecordObject(other, "Repair Hyperspace Lanes");
                other.connections.Add(planet);
                MarkDirty(other);
            }
        }
    }

    private void RunGrouped(string undoName, System.Action action)
    {
        Undo.SetCurrentGroupName(undoName);
        int group = Undo.GetCurrentGroup();
        action();
        Undo.CollapseUndoOperations(group);
        SceneView.RepaintAll();
        Repaint();
    }

    private void MarkDirty(Planet planet)
    {
        EditorUtility.SetDirty(planet);
        if (!EditorApplication.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
        }
    }

    private bool TryGetMousePoint(out Vector3 point)
    {
        float height = planets.Count > 0 && planets[0] != null ? planets[0].transform.position.y : 0f;
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Plane plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));

        if (plane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = default;
        return false;
    }

    private string UniqueName(string baseName)
    {
        HashSet<string> taken = new HashSet<string>(planets.Select(p => p.planetName));
        int index = 1;
        while (taken.Contains($"{baseName} {index}"))
        {
            index++;
        }
        return $"{baseName} {index}";
    }

    private static List<Planet> SelectedPlanets()
    {
        return Selection.gameObjects
            .Select(g => g.GetComponent<Planet>())
            .Where(p => p != null)
            .ToList();
    }

    private static string DisplayName(Planet planet)
    {
        return string.IsNullOrEmpty(planet.planetName) ? planet.name : planet.planetName;
    }
}
