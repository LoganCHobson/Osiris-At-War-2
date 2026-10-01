using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public static class OsirisPrefabBuilder
{
    const string ModelRoot = "Assets/Art/Models/Osiris";
    const string OutRoot = "Assets/Prefabs/Osiris";
    const int ShipLayer = 7;
    public static bool Verbose;

    public static void BuildAllVerbose()
    {
        Verbose = true;
        BuildAll();
    }

    class ShipSpec
    {
        public string name, model, template, icon;
        public ShipType type;
        public FleetRole role;
        public int cost, buildTime, combatPower, population;
        public float speed, health, dps, range;
    }

    const float AvantiPdDps = 1.2f, GlobalDps = 1.4f, AvantiSpeed = 1.1f, AvantiFireRate = 0.35f, AvantiPdFireRate = 0.3f;
    const float PdDamage = 0.5f, SystemHardpointWeight = 3f, EmitterHitRadius = 1.1f;
    static readonly Dictionary<ShipType, (int avantiPdEvery, float pdFireRate, float pdDps, float gunFighterAccuracy, float gunFighterDamage, bool gunsHuntFighters)> AntiFighter =
        new Dictionary<ShipType, (int, float, float, float, float, bool)>
    {
        [ShipType.Corvette] = (3, 0.06f, 12f, 1.5f, 0.8f, true),
        [ShipType.Destroyer] = (4, 0.14f, 8f, 2.5f, 0.5f, true),
        [ShipType.Cruiser] = (6, 0.25f, 5f, 4f, 0.4f, false),
        [ShipType.Carrier] = (6, 0.25f, 6f, 4f, 0.4f, false),
        [ShipType.Battleship] = (12, 0.7f, 2.5f, 8f, 0.2f, false),
        [ShipType.Station] = (4, 0.2f, 10f, 4f, 0.4f, false),
    };

    static readonly Dictionary<string, (float health, float speed, float shipDamage, float fighterDamage, float fireRate, float fireRange, int cost, int power)> CraftStats =
        new Dictionary<string, (float, float, float, float, float, float, int, int)>
    {
        ["Human_FighterSquadron"] = (10f, 32f, 2.2f, 1.2f, 0.45f, 60f, 70, 28),
        ["Human_InterceptorSquadron"] = (8f, 44f, 0.4f, 1.6f, 0.3f, 65f, 75, 28),
        ["Human_BomberSquadron"] = (22f, 24f, 15f, 0.3f, 0.8f, 50f, 95, 40),
        ["Avanti_FighterSquadron"] = (14f, 35f, 2.75f, 1.6f, 0.45f, 60f, 95, 38),
        ["Avanti_InterceptorSquadron"] = (10f, 48f, 0.5f, 2.0f, 0.3f, 65f, 100, 38),
        ["Avanti_BomberSquadron"] = (28f, 26f, 18f, 0.4f, 0.8f, 50f, 130, 55),
    };

    static readonly Dictionary<string, ((int fighters, int interceptors, int bombers) port, (int fighters, int interceptors, int bombers) starboard, float interval)> HangarComplement =
        new Dictionary<string, ((int, int, int), (int, int, int), float)>
    {
        ["Human"] = ((2, 1, 1), (1, 1, 2), 10f),
        ["Avanti"] = ((1, 1, 1), (1, 0, 1), 14f),
    };

    static readonly Dictionary<string, float> ShipDpsTuning = new Dictionary<string, float>
    {
        ["Human_Battleship"] = 0.95f,
        ["Human_Cruiser"] = 1.2f,
        ["Human_Destroyer"] = 1.25f,
        ["Human_Corvette"] = 0.95f,
        ["Human_Carrier"] = 0.95f,
    };

    static readonly Dictionary<ShipType, (float acceleration, float turn)> Handling = new Dictionary<ShipType, (float, float)>
    {
        [ShipType.Battleship] = (3f, 40f),
        [ShipType.Carrier] = (2.5f, 35f),
        [ShipType.Cruiser] = (4f, 55f),
        [ShipType.Destroyer] = (6f, 75f),
        [ShipType.Corvette] = (8f, 100f),
    };

    static readonly Dictionary<string, (float health, float dps, float range)> StructureStats = new Dictionary<string, (float, float, float)>
    {
        ["Human_Shipyard"] = (4500f, 120f, 190f),
        ["Human_Station"] = (7500f, 240f, 200f),
        ["Avanti_Shipyard"] = (5200f, 140f, 200f),
        ["Avanti_Station"] = (8500f, 290f, 210f),
    };

    class CraftSpec
    {
        public string name, model, template;
    }

    static readonly string[] Factions = { "Human", "Avanti" };

    static ShipSpec[] Ships(string f)
    {
        bool h = f == "Human";
        string dir(string n) => h ? $"{ModelRoot}/Human/Human_{n}/Human_{n}.fbx" : $"{ModelRoot}/Avanti/{n}/Avanti_{n}.fbx";
        ShipSpec spec(string n, string template, string icon, ShipType type, FleetRole role, float speed, float range,
                      (int cost, int build, int pop, int power, float health, float dps) human,
                      (int cost, int build, int pop, int power, float health, float dps) avanti)
        {
            var s = h ? human : avanti;
            return new ShipSpec { name = $"{f}_{n}", model = dir(n), template = template, icon = icon, type = type, role = role,
                cost = s.cost, buildTime = s.build, population = s.pop, combatPower = s.power, health = s.health, dps = s.dps,
                speed = speed, range = range };
        }
        return new[]
        {
            spec(h ? "Battleship" : "Capital", "ToOShipA", "ShipA Unit Icon", ShipType.Battleship, FleetRole.Line, 5, 170,
                 (150, 120, 2, 100, 2000, 112), (205, 170, 3, 130, 3000, 165)),
            spec("Carrier", "Carrier", "Carrier Unit Icon", ShipType.Carrier, FleetRole.Carrier, 4.5f, 150,
                 (260, 140, 3, 140, 2200, 30), (360, 200, 4, 170, 2860, 39)),
            spec("Cruiser", "ToOShipA", "ShipA Unit Icon", ShipType.Cruiser, FleetRole.Line, 6, 160,
                 (120, 90, 2, 69, 1400, 75), (160, 125, 2, 90, 1950, 105)),
            spec("Destroyer", "Picket", "Picket Unit Icon", ShipType.Destroyer, FleetRole.Screen, 7, 145,
                 (90, 65, 1, 46, 950, 56), (125, 90, 2, 60, 1150, 68)),
            spec("Corvette", "Picket", "Picket Unit Icon", ShipType.Corvette, FleetRole.Screen, 8, 135,
                 (70, 45, 1, 29, 600, 40), (95, 65, 1, 38, 700, 44)),
        };
    }

    static CraftSpec[] Craft(string f) => new[]
    {
        new CraftSpec { name = $"{f}_FighterSquadron", model = $"{ModelRoot}/StrikeCraft/{f}_Fighter/{f}_Fighter.fbx", template = "StarfighterSquadron" },
        new CraftSpec { name = $"{f}_InterceptorSquadron", model = $"{ModelRoot}/StrikeCraft/{f}_Interceptor/{f}_Interceptor.fbx", template = "InterceptorSquadron" },
        new CraftSpec { name = $"{f}_BomberSquadron", model = $"{ModelRoot}/StrikeCraft/{f}_Bomber/{f}_Bomber.fbx", template = "BomberSquadron" },
    };

    [MenuItem("Osiris/Build Ship Prefabs")]
    public static void BuildAll()
    {
        OsirisShipSetup.SetUpAll();
        foreach (string f in Factions)
        {
            string dir = $"{OutRoot}/{f}";
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();

            var squadrons = new Dictionary<string, Ship>();
            foreach (CraftSpec c in Craft(f))
            {
                GameObject prefab = BuildSquadron(c, dir);
                if (prefab != null) squadrons[c.template] = prefab.GetComponent<Ship>();
            }

            var roster = new List<Ship>();
            foreach (ShipSpec s in Ships(f))
            {
                GameObject prefab = BuildShip(s, dir, squadrons);
                if (prefab != null) roster.Add(prefab.GetComponent<Ship>());
            }

            var ordered = new List<Ship> { roster[0], squadrons.GetValueOrDefault("StarfighterSquadron"), roster[4], roster[1],
                squadrons.GetValueOrDefault("InterceptorSquadron"), squadrons.GetValueOrDefault("BomberSquadron"), roster[2], roster[3] };
            string factionName = f == "Human" ? "Faction_TitansOfOsiris" : "Faction_AvantiEmpire";
            SetRoster(factionName, ordered.Where(x => x != null).ToList());

            GameObject yard = BuildStructure($"{f}_Shipyard", "CapitalShipYard", dir, squadrons);
            GameObject station = BuildStructure($"{f}_Station", "BattleStation", dir, squadrons);
            structures[f] = (yard, station);

            swaps[factionName] = new Dictionary<string, Ship>
            {
                ["ToOShipA"] = roster[0], ["Carrier"] = roster[1], ["Picket"] = roster[4],
                ["StarfighterSquadron"] = squadrons.GetValueOrDefault("StarfighterSquadron"),
                ["InterceptorSquadron"] = squadrons.GetValueOrDefault("InterceptorSquadron"),
                ["BomberSquadron"] = squadrons.GetValueOrDefault("BomberSquadron"),
            };
        }
        AssignStructures();
        AssetDatabase.SaveAssets();
        SwapStartingFleets("Assets/Scenes/GalacticMap.unity", swaps);
        Debug.Log("[Osiris] Prefab build finished.");
    }

    static readonly Dictionary<string, Dictionary<string, Ship>> swaps = new Dictionary<string, Dictionary<string, Ship>>();
    static readonly Dictionary<string, (GameObject yard, GameObject station)> structures = new Dictionary<string, (GameObject, GameObject)>();

    static void AssignStructures()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Faction", new[] { "Assets/Data/Factions" }))
        {
            var faction = AssetDatabase.LoadAssetAtPath<Faction>(AssetDatabase.GUIDToAssetPath(guid));
            if (faction == null || !structures.TryGetValue(faction.name == "Faction_TitansOfOsiris" ? "Human" : "Avanti", out var set)) continue;
            faction.capitalShipyardPrefab = set.yard;
            faction.battleStationPrefab = set.station;
            EditorUtility.SetDirty(faction);
            Debug.Log($"[Osiris] {faction.name}: shipyard {(set.yard ? set.yard.name : "none")}, battle station {(set.station ? set.station.name : "none")}");
        }
    }

    static GameObject BuildStructure(string name, string template, string dir, Dictionary<string, Ship> squadrons)
    {
        string modelPath = $"{ModelRoot}/Structures/{name}/{name}.fbx";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) { Debug.LogError($"[Osiris] Missing model {modelPath}"); return null; }

        GameObject root = PrefabUtility.LoadPrefabContents($"Assets/Prefabs/{template}.prefab");
        try
        {
            root.name = name;
            int layer = root.layer;
            const float scale = 0.5f;
            float k = root.transform.localScale.x / scale;
            foreach (Transform c in root.transform)
            {
                c.localPosition *= k;
                c.localScale *= k;
            }
            root.transform.localScale = Vector3.one * scale;

            GameObject turretAsset = TurretAsset();
            HangarHardpoint hangar = root.GetComponentInChildren<HangarHardpoint>(true);
            foreach (TurretController t in root.GetComponentsInChildren<TurretController>(true))
                Object.DestroyImmediate(t.gameObject);
            foreach (Transform c in root.transform.Cast<Transform>().ToList())
                if (c.name.StartsWith("Cube")) Object.DestroyImmediate(c.gameObject);

            var gfx = new GameObject("GFX").transform;
            gfx.SetParent(root.transform, false);
            gfx.gameObject.layer = layer;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, gfx);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localScale = Vector3.one;
            SetLayer(inst.transform, layer);
            AddHullColliders(inst.transform);
            Bounds b = LocalBounds(root.transform, inst.GetComponentsInChildren<MeshFilter>());

            int turrets = 0;
            foreach (Transform pivot in inst.transform.Cast<Transform>().Where(t => IsTurretPivot(t.name)).ToList())
            {
                BuildModelTurret(turretAsset, gfx, pivot, !name.StartsWith("Human"));
                turrets++;
            }

            if (hangar != null)
            {
                string faction = name.StartsWith("Human") ? "Human" : "Avanti";
                var bays = inst.transform.Cast<Transform>().Where(t => t.name.StartsWith("HP_Hangar")).ToList();
                hangar.transform.SetParent(gfx, true);
                SetComplement(hangar, faction, squadrons, portSide: true);
                var starboard = Object.Instantiate(hangar.gameObject, gfx).GetComponent<HangarHardpoint>();
                starboard.name = "HangarHardpoint Starboard";
                hangar.name = "HangarHardpoint Port";
                SetComplement(starboard, faction, squadrons, portSide: false);
                PlaceHangar(hangar, bays.Where(t => gfx.InverseTransformPoint(t.position).x < 0).ToList(), gfx, -1, b);
                PlaceHangar(starboard, bays.Where(t => gfx.InverseTransformPoint(t.position).x >= 0).ToList(), gfx, 1, b);
            }

            Transform healthBar = root.transform.Cast<Transform>()
                .FirstOrDefault(c => string.Equals(c.name, "HealthBar", System.StringComparison.OrdinalIgnoreCase));
            if (healthBar != null) healthBar.localPosition = new Vector3(0, b.max.y + 6f, 0);

            var obstacle = root.GetComponent<NavMeshObstacle>();
            if (obstacle != null)
            {
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = b.center;
                obstacle.size = b.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
            }
            root.GetComponent<SpaceUnit>().shipType = ShipType.Station;
            var st = StructureStats[name];
            bool human = name.StartsWith("Human");
            ApplyAntiFighter(root, ShipType.Station, !name.StartsWith("Human"));
            string stats = Balance(root, st.health, st.dps * GlobalDps, st.range);

            string path = $"{dir}/{name}.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[Osiris] {name}: {turrets} turrets, {stats} -> {path}");
            return saved;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void SwapStartingFleets(string scenePath, Dictionary<string, Dictionary<string, Ship>> byFaction)
    {
        if (!File.Exists(scenePath)) return;
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        int changed = 0;
        foreach (GameObject rootGo in scene.GetRootGameObjects())
        foreach (GalacticFleet fleet in rootGo.GetComponentsInChildren<GalacticFleet>(true))
        {
            if (fleet.faction == null || !byFaction.TryGetValue(fleet.faction.name, out var map)) continue;
            for (int i = 0; i < fleet.roster.Count; i++)
            {
                Ship old = fleet.roster[i];
                if (old != null && map.TryGetValue(old.name, out Ship replacement) && replacement != null)
                {
                    fleet.roster[i] = replacement;
                    changed++;
                }
            }
            EditorUtility.SetDirty(fleet);
        }
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Osiris] {scenePath}: swapped {changed} starting-fleet ships to the new prefabs.");
        if (!string.IsNullOrEmpty(previous) && previous != scenePath)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previous, UnityEditor.SceneManagement.OpenSceneMode.Single);
    }

    static GameObject BuildShip(ShipSpec spec, string dir, Dictionary<string, Ship> squadrons)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.model);
        if (model == null) { Debug.LogError($"[Osiris] Missing model {spec.model}"); return null; }

        string templatePath = $"Assets/Prefabs/{spec.template}.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(templatePath);
        try
        {
            root.name = spec.name;
            Transform gfx = root.transform.Find("GFX");
            Transform hull = gfx.Find("Hull");

            Bounds oldBounds = LocalBounds(root.transform, hull.GetComponentsInChildren<MeshFilter>());

            GameObject turretAsset = TurretAsset();
            EngineHardpoint engine = gfx.GetComponentInChildren<EngineHardpoint>(true);
            HangarHardpoint hangar = gfx.GetComponentInChildren<HangarHardpoint>(true);
            Transform enginesGroup = gfx.Find("Engines");
            ParticleSystem flameTemplate = enginesGroup != null ? enginesGroup.GetComponentInChildren<ParticleSystem>(true) : null;
            GameObject flameCopy = flameTemplate != null ? Object.Instantiate(flameTemplate.gameObject, root.transform) : null;
            if (flameCopy != null) flameCopy.SetActive(false);

            foreach (TurretController t in gfx.GetComponentsInChildren<TurretController>(true))
                Object.DestroyImmediate(t.gameObject);
            foreach (Component c in hull.GetComponents<Component>())
                if (c is MeshFilter || c is MeshRenderer || c is Collider) Object.DestroyImmediate(c);
            if (enginesGroup != null)
                for (int i = enginesGroup.childCount - 1; i >= 0; i--) Object.DestroyImmediate(enginesGroup.GetChild(i).gameObject);

            gfx.localScale = Vector3.one;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, hull);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localScale = Vector3.one;
            SetLayer(inst.transform, ShipLayer);
            AddHullColliders(inst.transform);

            Bounds newBounds = LocalBounds(root.transform, inst.GetComponentsInChildren<MeshFilter>());
            float ratio = newBounds.size.z / Mathf.Max(0.01f, oldBounds.size.z);

            bool human = spec.name.StartsWith("Human");
            int turrets = 0;
            var pivots = inst.transform.Cast<Transform>().Where(t => IsTurretPivot(t.name)).ToList();
            foreach (Transform pivot in pivots)
            {
                BuildModelTurret(turretAsset, gfx, pivot, !human);
                turrets++;
            }
            int emitter = 0;
            foreach (Transform hp in inst.transform.Cast<Transform>().Where(t => t.name.StartsWith("HP_Weapon")).ToList())
            {
                BuildHiddenTurret(turretAsset, gfx, hp, pointDefense: emitter++ % AntiFighter[spec.type].avantiPdEvery == AntiFighter[spec.type].avantiPdEvery - 1);
                Object.DestroyImmediate(hp.gameObject);
                turrets++;
            }
            var engineHps = inst.transform.Cast<Transform>().Where(t => t.name.StartsWith("HP_Engine")).ToList();
            if (engine != null && engineHps.Count > 0)
            {
                Vector3 centre = Vector3.zero;
                foreach (Transform hp in engineHps) centre += hp.position;
                engine.transform.position = centre / engineHps.Count;
                var flames = new List<ParticleSystem>();
                if (flameCopy != null)
                {
                    float flameScale = Mathf.Clamp(3.5f * ratio, 1.5f, 6f);
                    foreach (Transform hp in engineHps)
                    {
                        var flame = Object.Instantiate(flameCopy, enginesGroup);
                        flame.name = "Flame_" + hp.name.Substring("HP_Engine_".Length);
                        flame.SetActive(true);
                        flame.transform.position = hp.position;
                        flame.transform.rotation = Quaternion.LookRotation(Aft(hp, root.transform));
                        flame.transform.localScale = Vector3.one * flameScale;
                        flames.Add(flame.GetComponent<ParticleSystem>());
                    }
                }
                engine.thrusterFlames = flames.ToArray();
            }
            else if (engine != null)
            {
                engine.transform.localPosition = new Vector3(0, newBounds.center.y, newBounds.min.z + 2f);
                engine.thrusterFlames = new ParticleSystem[0];
            }
            if (flameCopy != null) Object.DestroyImmediate(flameCopy);

            if (hangar != null)
            {
                var hangarHps = inst.transform.Cast<Transform>().Where(t => t.name.StartsWith("HP_Hangar")).ToList();
                SetComplement(hangar, spec.name.StartsWith("Human") ? "Human" : "Avanti", squadrons, portSide: true);
                var starboard = Object.Instantiate(hangar.gameObject, hangar.transform.parent).GetComponent<HangarHardpoint>();
                starboard.name = "HangarHardpoint Starboard";
                hangar.name = "HangarHardpoint Port";
                SetComplement(starboard, spec.name.StartsWith("Human") ? "Human" : "Avanti", squadrons, portSide: false);
                PlaceHangar(hangar, hangarHps.Where(t => gfx.InverseTransformPoint(t.position).x < 0).ToList(), gfx, -1, newBounds);
                PlaceHangar(starboard, hangarHps.Where(t => gfx.InverseTransformPoint(t.position).x >= 0).ToList(), gfx, 1, newBounds);
            }

            Transform healthBar = gfx.Find("HealthBar");
            if (healthBar != null) healthBar.localPosition = new Vector3(0, newBounds.max.y + 5f, 0);
            Transform selection = root.transform.Find("SelectionGFX");
            if (selection != null) selection.localScale *= ratio;
            Transform deathFx = root.transform.Find("WFX_ExplosiveSmoke Big Alt");
            if (deathFx != null) deathFx.localScale *= Mathf.Clamp(ratio, 0.5f, 1.5f);

            var agent = root.GetComponent<NavMeshAgent>();
            agent.radius = Mathf.Max(newBounds.extents.x, newBounds.extents.z) * 0.46f;
            if (spec.speed > 0) agent.speed = spec.speed * (human ? 1f : AvantiSpeed);
            else if (!human) agent.speed *= AvantiSpeed;
            agent.acceleration = Handling[spec.type].acceleration * (human ? 1f : AvantiSpeed);
            agent.angularSpeed = Handling[spec.type].turn * (human ? 1f : AvantiSpeed);

            root.GetComponent<SpaceUnit>().shipType = spec.type;
            var ship = root.GetComponent<Ship>();
            ship.fleetRole = spec.role;
            if (spec.cost > 0)
            {
                ship.cost = spec.cost;
                ship.buildTime = spec.buildTime;
                ship.combatPower = spec.combatPower;
                ship.populationCost = spec.population;
            }
            var icon = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Ui/{spec.icon}.prefab");
            if (icon != null) ship.icon = UnitIcon(spec.name, icon, spec.name);
            float tune = ShipDpsTuning.GetValueOrDefault(spec.name, 1f);
            ApplyAntiFighter(root, spec.type, !human);
            string stats = Balance(root, spec.health, spec.dps * GlobalDps * tune, spec.range);

            string path = $"{dir}/{spec.name}.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[Osiris] {spec.name}: {turrets} turrets, {engineHps.Count} engine flames, {stats} -> {path}");
            return saved;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static GameObject UnitIcon(string unitName, GameObject template, string iconFile)
    {
        string png = $"Assets/Art/UI/OsirisIcons/{iconFile}.png";
        var importer = AssetImporter.GetAtPath(png) as TextureImporter;
        if (importer == null) { Debug.LogWarning($"[Osiris] No icon at {png}"); return template; }
        if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(png);

        string dir = "Assets/Prefabs/Ui/Osiris";
        Directory.CreateDirectory(dir);
        string templatePath = AssetDatabase.GetAssetPath(template);
        string outPath = $"{dir}/{unitName} Icon.prefab";
        if (templatePath == outPath)
        {
            var existing = PrefabUtility.LoadPrefabContents(outPath);
            try { SetIconSprite(existing, sprite); PrefabUtility.SaveAsPrefabAsset(existing, outPath); }
            finally { PrefabUtility.UnloadPrefabContents(existing); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
        }
        var root = PrefabUtility.LoadPrefabContents(templatePath);
        try
        {
            root.name = $"{unitName} Icon";
            SetIconSprite(root, sprite);
            return PrefabUtility.SaveAsPrefabAsset(root, outPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void SetIconSprite(GameObject root, Sprite sprite)
    {
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = Color.white;
        }
    }

    static void ApplyAntiFighter(GameObject root, ShipType type, bool avanti)
    {
        var profile = AntiFighter[type];
        var pd = root.GetComponentsInChildren<TurretController>(true).Where(t => t.targetFilter == TurretController.TargetFilter.FightersOnly).ToList();
        float pdShots = pd.Sum(t => t.firingPoints.Count / profile.pdFireRate);
        float pdDamage = pdShots > 0f ? profile.pdDps * (avanti ? AvantiPdDps : 1f) / pdShots : 0f;
        foreach (TurretController t in root.GetComponentsInChildren<TurretController>(true))
        {
            if (t.targetFilter == TurretController.TargetFilter.FightersOnly)
            {
                t.fireRate = profile.pdFireRate;
                t.damage = pdDamage;
                t.projectileSpeed = 80f;
                t.rotationSpeed = 25f;
                t.elevationSpeed = 25f;
                t.minElevationAngle = -60f;
                t.maxElevationAngle = 60f;
            }
            else
            {
                t.fighterAccuracyMultiplier = profile.gunFighterAccuracy;
                t.fighterDamageMultiplier = profile.gunFighterDamage;
                t.preferCapitalShips = !profile.gunsHuntFighters;
                t.leadFighters = profile.gunsHuntFighters;
            }
        }
    }

    static string Balance(GameObject root, float health, float dps, float range)
    {
        var hardpoints = root.GetComponentsInChildren<HardpointHealth>(true);
        float HpWeight(HardpointHealth h) =>
            h.GetComponent<EngineHardpoint>() != null || h.GetComponent<HangarHardpoint>() != null ? SystemHardpointWeight : 1f;
        float totalWeight = hardpoints.Sum(HpWeight);
        foreach (HardpointHealth h in hardpoints)
            h.maxHealth = Mathf.Round(health * HpWeight(h) / totalWeight);

        var guns = root.GetComponentsInChildren<TurretController>(true);
        float Weight(TurretController t) => t.name.StartsWith("VentralBattery") ? 0.75f : 1f;
        var shipGuns = guns.Where(t => t.targetFilter != TurretController.TargetFilter.FightersOnly).ToList();
        float shotsPerSecond = shipGuns.Sum(t => Weight(t) * t.firingPoints.Count / Mathf.Max(0.05f, t.fireRate));
        float damage = shotsPerSecond > 0 ? dps / shotsPerSecond : 0f;
        foreach (TurretController t in shipGuns)
        {
            t.damage = Mathf.Round(damage * Weight(t) * 100f) / 100f;
            t.range = range;
        }
        foreach (TurretController t in guns.Where(t => t.targetFilter == TurretController.TargetFilter.FightersOnly))
            t.range = range * 0.7f;
        return $"health {health:F0} over {hardpoints.Length} hardpoints, {dps:F0} dps over {shipGuns.Count} guns ({damage:F2} per shot), range {range:F0}";
    }

    static bool IsTurretPivot(string n) =>
        n.StartsWith("Turret_") || n.StartsWith("VentralBattery_") || n.StartsWith("PointDefense_");

    static Vector3 Aim(Transform t) => -t.up;
    static Vector3 Top(Transform t) => t.forward;

    static GameObject TurretAsset() => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Turret.prefab");

    static void BuildModelTurret(GameObject template, Transform gfx, Transform pivot, bool avanti)
    {
        var lods = pivot.GetComponentsInChildren<MeshFilter>(true);
        MeshFilter lod0 = lods.FirstOrDefault(m => m.name.EndsWith("_LOD0")) ?? lods.FirstOrDefault();
        Bounds b = lod0 != null ? lod0.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(template, gfx);
        go.SetActive(true);
        go.name = pivot.name;
        go.transform.SetPositionAndRotation(pivot.position, Quaternion.LookRotation(Aim(pivot), Top(pivot)));
        float size = Mathf.Max(b.extents.x, b.extents.y);
        go.transform.localScale = Vector3.one * Mathf.Clamp(size / 1.75f, 0.2f, 1.2f);

        var tc = go.GetComponent<TurretController>();
        HideTemplateMeshes(tc);

        foreach (MeshFilter mf in lods) mf.transform.SetParent(tc.turretBase, true);

        List<Vector3> tips = lod0 != null ? BarrelTips(lod0) : new List<Vector3>();
        if (tips.Count == 0) tips.Add(pivot.position + Aim(pivot) * size);
        SetFiringPoints(tc, tips);
        if (Verbose)
            Debug.Log($"[Osiris]   {pivot.name}: aim {gfx.InverseTransformDirection(Aim(pivot))} top {gfx.InverseTransformDirection(Top(pivot))} barrels {tips.Count} size {size:F2}");

        if (pivot.name.StartsWith("PointDefense"))
        {
            MakePointDefense(tc, avanti ? AvantiPdFireRate : tc.fireRate * 0.5f);
        }
        else if (avanti)
        {
            tc.fireRate = AvantiFireRate;
        }
        Object.DestroyImmediate(pivot.gameObject);
    }

    static void MakePointDefense(TurretController tc, float fireRate)
    {
        tc.targetFilter = TurretController.TargetFilter.FightersOnly;
        tc.leadFighters = true;
        tc.fireRate = fireRate;
        tc.damage = PdDamage;
        tc.fighterDamageMultiplier = 1f;
        tc.fighterAccuracyMultiplier = 2f;
    }

    static void BuildHiddenTurret(GameObject template, Transform gfx, Transform hp, bool pointDefense)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(template, gfx);
        go.SetActive(true);
        go.name = hp.name.Replace(".001", "");
        go.transform.position = hp.position;
        go.transform.rotation = gfx.rotation * Quaternion.LookRotation(Flat(gfx.InverseTransformDirection(Aim(hp))));
        go.transform.localScale = Vector3.one * 0.4f;

        var tc = go.GetComponent<TurretController>();
        HideTemplateMeshes(tc);
        SetFiringPoints(tc, new List<Vector3> { hp.position + Aim(hp) * 0.3f });
        var hitbox = go.AddComponent<SphereCollider>();
        hitbox.radius = EmitterHitRadius / go.transform.lossyScale.x * gfx.lossyScale.x;
        tc.fireRate = AvantiFireRate;
        if (pointDefense) MakePointDefense(tc, AvantiPdFireRate);
    }

    static void HideTemplateMeshes(TurretController tc)
    {
        foreach (MeshRenderer r in tc.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
        foreach (MeshCollider c in tc.turretBarrel.GetComponentsInChildren<MeshCollider>(true)) c.enabled = false;
        Transform neck = tc.turretBase.parent != null ? tc.turretBase.parent.Find("Neck") : null;
        if (neck != null && neck.TryGetComponent(out MeshCollider nc)) nc.enabled = false;
    }

    static void SetFiringPoints(TurretController tc, List<Vector3> tips)
    {
        var points = tc.firingPoints.Where(p => p != null).ToList();
        if (points.Count == 0) return;
        if (tips.Count > points.Count)
            tips = Enumerable.Range(0, points.Count).Select(i => tips[i * tips.Count / points.Count]).ToList();

        var used = new List<Transform>();
        for (int i = 0; i < points.Count; i++)
        {
            Transform fp = points[i];
            if (i >= tips.Count)
            {
                fp.parent.gameObject.SetActive(false);
                continue;
            }
            fp.parent.position += tips[i] - fp.position;
            used.Add(fp);
        }
        tc.firingPoints = used;
    }

    static List<Vector3> BarrelTips(MeshFilter mf)
    {
        Vector3[] v = mf.sharedMesh.vertices;
        if (v.Length == 0) return new List<Vector3>();
        float front = v.Min(p => p.y), back = v.Max(p => p.y);
        float width = v.Max(p => p.x) - v.Min(p => p.x);
        float band = Mathf.Max(0.02f, 0.04f * (back - front));
        float radius = Mathf.Max(0.04f, 0.12f * width);

        var clusters = new List<List<Vector3>>();
        foreach (Vector3 p in v.Where(p => p.y < front + band))
        {
            var home = clusters.FirstOrDefault(c => Vector2.Distance(new Vector2(c[0].x, c[0].z), new Vector2(p.x, p.z)) < radius);
            if (home == null) clusters.Add(new List<Vector3> { p });
            else home.Add(p);
        }
        return clusters
            .Select(c => new Vector3(c.Average(p => p.x), front, c.Average(p => p.z)))
            .OrderBy(p => p.x)
            .Select(p => mf.transform.TransformPoint(p))
            .ToList();
    }

    static void SetComplement(HangarHardpoint hangar, string faction, Dictionary<string, Ship> squadrons, bool portSide)
    {
        Ship get(string key) => squadrons.GetValueOrDefault(key);
        var plan = HangarComplement[faction];
        var side = portSide ? plan.port : plan.starboard;
        hangar.complement = new List<HangarHardpoint.Bay>
        {
            new HangarHardpoint.Bay { squadron = get("StarfighterSquadron"), count = side.fighters },
            new HangarHardpoint.Bay { squadron = get("InterceptorSquadron"), count = side.interceptors },
            new HangarHardpoint.Bay { squadron = get("BomberSquadron"), count = side.bombers },
        };
        hangar.firstLaunchDelay = portSide ? 3f : 3f + plan.interval / 2f;
        hangar.launchInterval = plan.interval;
    }

    static void PlaceHangar(HangarHardpoint hangar, List<Transform> bays, Transform gfx, int side, Bounds shipBounds)
    {
        Vector3 local = bays.Count > 0
            ? bays.Aggregate(Vector3.zero, (a, t) => a + gfx.InverseTransformPoint(t.position)) / bays.Count
            : new Vector3(side * shipBounds.extents.x, 0, 0);
        hangar.transform.localPosition = local;
        hangar.transform.localRotation = Quaternion.identity;
        hangar.transform.localScale = Vector3.one * 0.5f;
        if (hangar.launchPoint != null)
        {
            hangar.launchPoint.localPosition = new Vector3(side * 16f, 0, 4f);
            hangar.launchPoint.localRotation = Quaternion.identity;
        }
    }

    static GameObject BuildSquadron(CraftSpec spec, string dir)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.model);
        if (model == null) { Debug.LogError($"[Osiris] Missing model {spec.model}"); return null; }

        GameObject root = PrefabUtility.LoadPrefabContents($"Assets/Prefabs/{spec.template}.prefab");
        try
        {
            root.name = spec.name;
            int n = 0;
            foreach (Fighter fighter in root.GetComponentsInChildren<Fighter>(true))
            {
                Transform t = fighter.transform;
                t.localScale = Vector3.one;
                if (t.TryGetComponent(out MeshRenderer mr)) Object.DestroyImmediate(mr);
                if (t.TryGetComponent(out MeshFilter mf)) Object.DestroyImmediate(mf);

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, t);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localScale = Vector3.one;
                SetLayer(inst.transform, t.gameObject.layer);

                Bounds b = LocalBounds(t, inst.GetComponentsInChildren<MeshFilter>());
                if (t.TryGetComponent(out BoxCollider box)) { box.center = b.center; box.size = b.size; }

                var cs = CraftStats[spec.name];
                bool avanti = spec.name.StartsWith("Avanti");
                fighter.speed = cs.speed;
                fighter.shipDamage = cs.shipDamage;
                fighter.fighterDamage = cs.fighterDamage;
                fighter.fireRate = cs.fireRate;
                fighter.fireRange = cs.fireRange;
                if (t.TryGetComponent(out HardpointHealth hh)) hh.maxHealth = cs.health;
                n++;
            }
            var stats = CraftStats[spec.name];
            var ship = root.GetComponent<Ship>();
            ship.cost = stats.cost;
            ship.combatPower = stats.power;
            if (ship.icon != null) ship.icon = UnitIcon(spec.name, ship.icon, spec.name.Replace("Squadron", ""));
            string path = $"{dir}/{spec.name}.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[Osiris] {spec.name}: {n} craft, {stats.health} hp, {stats.shipDamage / stats.fireRate:F1} anti-ship / {stats.fighterDamage / stats.fireRate:F1} anti-fighter dps each -> {path}");
            return saved;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void SetRoster(string faction, List<Ship> roster)
    {
        var asset = AssetDatabase.LoadAssetAtPath<Faction>($"Assets/Data/Factions/{faction}.asset");
        if (asset == null) { Debug.LogWarning($"[Osiris] Faction {faction} not found"); return; }
        asset.roster = roster;
        EditorUtility.SetDirty(asset);
        Debug.Log($"[Osiris] {faction} roster: {string.Join(", ", roster.Select(s => s.name))}");
    }

    static void AddHullColliders(Transform model)
    {
        foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.name.EndsWith("_LOD0") || mf.transform.parent != model) continue;
            string baseName = mf.name.Substring(0, mf.name.Length - "_LOD0".Length);
            Transform low = model.Find(baseName + "_LOD2") ?? model.Find(baseName + "_LOD1");
            var col = mf.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = low != null ? low.GetComponent<MeshFilter>().sharedMesh : mf.sharedMesh;
        }
    }

    static Bounds LocalBounds(Transform space, IEnumerable<MeshFilter> filters)
    {
        bool any = false;
        Bounds result = new Bounds();
        foreach (MeshFilter mf in filters)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        return result;
    }

    static Vector3 Aft(Transform hp, Transform root)
    {
        Vector3 d = Aim(hp);
        return Vector3.Dot(d, -root.forward) > 0.3f ? d : -root.forward;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0;
        return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.forward;
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform c in t) SetLayer(c, layer);
    }
}