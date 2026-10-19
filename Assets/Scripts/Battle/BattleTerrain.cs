using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// The battle's terrain rules: asteroid fields, dense asteroid clusters and reinforcement no-drop zones.
//
// Asteroid field: normal move = slowed, no damage. Priority move = full speed, hull damage scaled by ship
//   size (fighters never). Ships inside shoot less accurately. Big rocks stop lasers.
// Dense cluster: capital ships can't path in or be ordered in. Fighters can, but take damage while moving.
// No-drop: reinforcements can't be dropped inside any asteroids, or near an enemy shipyard/battle station.
public class BattleTerrain : MonoBehaviour
{
    public static BattleTerrain Instance { get; private set; }

    private const string RockMaterialPath = "Battle/Asteroid";
    private const string DenseAreaName = "Dense Asteroids";
    private const string AsteroidLayerName = "Asteroid";

    [Header("Layout")]
    public BattleTerrainLayout.Settings layout = new BattleTerrainLayout.Settings();

    [Header("Asteroid field effects")]
    [Range(0.1f, 1f)] public float fieldSlowdown = 0.5f;
    [Tooltip("Share of a ship's max health lost per second on a priority move through a field, before size scaling.")]
    public float priorityDamagePerSecond = 0.006f;
    [Tooltip("How much wider shots spread when the shooter is inside asteroids.")]
    public float accuracyPenalty = 2.5f;

    [Header("Dense cluster effects")]
    [Tooltip("Share of a fighter's health lost per second while moving inside a dense cluster.")]
    public float fighterDensePerSecond = 0.08f;

    [Header("No-drop zones")]
    [Tooltip("Radius around an enemy shipyard or battle station where reinforcements can't be dropped.")]
    public float defenseNoDropRadius = 260f;
    [Tooltip("Extra margin kept between a dropped ship and any asteroids.")]
    public float asteroidDropMargin = 30f;

    private const float TickInterval = 0.2f;

    private struct DefenseZone
    {
        public UnitHealthManager defense;
        public Vector3 centre;
        public float radius;
        public bool ownerIsAttacker;
    }

    private readonly List<AsteroidField> fields = new List<AsteroidField>();
    private readonly List<DefenseZone> defenseZones = new List<DefenseZone>();
    private readonly HashSet<NavMeshAgent> restrictedAgents = new HashSet<NavMeshAgent>();
    private static readonly HashSet<SpaceUnit> AIPriority = new HashSet<SpaceUnit>();
    private int denseArea = -1;
    private float nextTick;

    public Rect Map { get; private set; }
    public IReadOnlyList<AsteroidField> Fields => fields;
    // Rocks live on the "Asteroid" layer; without it they borrow the Wreck layer, which also stops lasers.
    public static int AsteroidLayer
    {
        get
        {
            int layer = LayerMask.NameToLayer(AsteroidLayerName);
            return layer >= 0 ? layer : WreckChunk.WreckLayer;
        }
    }
    public static int BlockingMask => AsteroidLayer >= 0 ? 1 << AsteroidLayer : 0;

    // ---------------- Setup ----------------

    public static BattleTerrain Create(Vector3 attackerStart, Vector3 defenderStart, int seed)
    {
        if (Instance != null) return Instance;

        BattleTerrain terrain = new GameObject("BattleTerrain").AddComponent<BattleTerrain>();
        terrain.Generate(attackerStart, defenderStart, seed);
        return terrain;
    }

    private void Awake()
    {
        Instance = this;
        AIPriority.Clear();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Generate(Vector3 attackerStart, Vector3 defenderStart, int seed)
    {
        Map = FindMapBounds();
        Vector2 attacker = new Vector2(attackerStart.x, attackerStart.z);
        Vector2 defender = new Vector2(defenderStart.x, defenderStart.z);

        List<BattleTerrainLayout.FieldSpec> specs = BattleTerrainLayout.Generate(seed, Map, attacker, defender, layout);

        Material rockMaterial = Resources.Load<Material>(RockMaterialPath);
        int rockLayer = AsteroidLayer >= 0 ? AsteroidLayer : 0;
        if (AsteroidLayer < 0) Debug.LogWarning("BattleTerrain: no 'Asteroid' or 'Wreck' layer - asteroids won't block lasers.");

        for (int i = 0; i < specs.Count; i++)
        {
            AsteroidField field = new GameObject(specs[i].density == AsteroidDensity.Dense ? "DenseAsteroids" : "AsteroidField").AddComponent<AsteroidField>();
            field.transform.SetParent(transform, false);
            field.density = specs[i].density;
            field.blobs.AddRange(specs[i].blobs);
            field.Build(seed * 31 + i, rockMaterial, rockLayer);
            fields.Add(field);
        }

        RebuildNavMesh();
    }

    private static Rect FindMapBounds()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        foreach (MeshRenderer meshRenderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (meshRenderer.gameObject.layer != groundLayer) continue;
            Bounds bounds = meshRenderer.bounds;
            return new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z);
        }
        return new Rect(-500f, -500f, 1000f, 1000f);
    }

    // Dense clusters become their own NavMesh area: capital ships are barred from it, fighters aren't.
    private void RebuildNavMesh()
    {
        denseArea = NavMesh.GetAreaFromName(DenseAreaName);
        bool anyDense = fields.Exists(field => field.density == AsteroidDensity.Dense);
        if (!anyDense) return;

        if (denseArea < 0)
        {
            Debug.LogWarning($"BattleTerrain: no '{DenseAreaName}' NavMesh area - dense clusters won't block ships.");
            return;
        }

        NavMeshSurface surface = FindFirstObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            Debug.LogWarning("BattleTerrain: no NavMeshSurface in the scene - dense clusters won't block ships.");
            return;
        }

        int groundLayer = LayerMask.NameToLayer("Ground");
        List<GameObject> volumes = new List<GameObject>();
        foreach (AsteroidField field in fields)
        {
            if (field.density != AsteroidDensity.Dense) continue;
            foreach (AsteroidField.Blob blob in field.blobs)
            {
                GameObject volumeObject = new GameObject("DenseVolume");
                volumeObject.layer = groundLayer;
                volumeObject.transform.position = new Vector3(blob.centre.x, 0f, blob.centre.y);

                NavMeshModifierVolume volume = volumeObject.AddComponent<NavMeshModifierVolume>();
                float span = BattleTerrainLayout.DenseHalfExtent(blob.radius) * 2f; // Keeps hulls off the rocks.
                volume.size = new Vector3(span, 60f, span);
                volume.center = Vector3.zero;
                volume.area = denseArea;
                volumes.Add(volumeObject);
            }
        }

        surface.layerMask = 1 << groundLayer; // Only the battle plane - not ships, rocks or effects.
        surface.BuildNavMesh();

        foreach (GameObject volumeObject in volumes) Destroy(volumeObject);
    }

    // Capital ships never path into dense clusters (fighter squadrons may).
    public void RestrictShip(GameObject ship)
    {
        if (ship == null || ship.GetComponent<Squadron>() != null) return;
        RestrictAgent(ship.GetComponent<NavMeshAgent>());
    }

    private void RestrictAgent(NavMeshAgent agent)
    {
        if (agent == null || denseArea < 0 || !restrictedAgents.Add(agent)) return;
        agent.areaMask &= ~(1 << denseArea);
    }

    public void AddDefenseZone(UnitHealthManager defense, bool ownerIsAttacker)
    {
        if (defense == null) return;
        defenseZones.Add(new DefenseZone
        {
            defense = defense,
            centre = defense.transform.position,
            radius = defenseNoDropRadius,
            ownerIsAttacker = ownerIsAttacker
        });
    }

    // ---------------- Queries ----------------

    public bool InField(Vector3 point) => FieldAt(point, AsteroidDensity.Field) != null;
    public bool InDense(Vector3 point) => FieldAt(point, AsteroidDensity.Dense) != null;
    public bool InAsteroids(Vector3 point) => FieldAt(point, null) != null;

    private AsteroidField FieldAt(Vector3 point, AsteroidDensity? density)
    {
        foreach (AsteroidField field in fields)
        {
            if (density.HasValue && field.density != density.Value) continue;
            if (field.Contains(point)) return field;
        }
        return null;
    }

    public bool CrossesField(Vector3 from, Vector3 to)
    {
        foreach (AsteroidField field in fields)
        {
            if (field.density == AsteroidDensity.Field && field.Crosses(from, to)) return true;
        }
        return false;
    }

    // Can a ship of the given side be dropped here as a reinforcement?
    public bool CanDrop(Vector3 point, bool attackerSide)
    {
        foreach (AsteroidField field in fields)
        {
            if (field.Contains(point, asteroidDropMargin)) return false;
        }

        foreach (DefenseZone zone in defenseZones)
        {
            if (zone.ownerIsAttacker == attackerSide || zone.defense == null || zone.defense.IsDead) continue;
            Vector3 offset = point - zone.centre;
            offset.y = 0f;
            if (offset.sqrMagnitude <= zone.radius * zone.radius) return false;
        }
        return true;
    }

    // The nearest spot to 'point' where a reinforcement may drop, searching outward in rings.
    public Vector3 NearestDropPoint(Vector3 point, bool attackerSide)
    {
        if (CanDrop(point, attackerSide)) return point;

        for (float radius = 40f; radius <= 600f; radius += 40f)
        {
            int steps = Mathf.CeilToInt(2f * Mathf.PI * radius / 40f);
            for (int i = 0; i < steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                Vector3 candidate = point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (Map.Contains(new Vector2(candidate.x, candidate.z)) && CanDrop(candidate, attackerSide)) return candidate;
            }
        }
        return point;
    }

    // Fills a grid (rows of z, columns of x across the map) with 1 where 'side' may not drop reinforcements.
    public void FillNoDropMask(float[] mask, int resolution, bool attackerSide)
    {
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            Vector3 point = new Vector3(Map.xMin + (x + 0.5f) / resolution * Map.width, 0f, Map.yMin + (y + 0.5f) / resolution * Map.height);
            mask[y * resolution + x] = CanDrop(point, attackerSide) ? 0f : 1f;
        }
    }

    // Wider spread for shots fired from inside asteroids.
    public static float AccuracyMultiplier(Vector3 shooter)
    {
        return Instance != null && Instance.InAsteroids(shooter) ? Instance.accuracyPenalty : 1f;
    }

    // AI ships flagged here count as priority-moving through asteroid fields.
    public static void SetAIPriority(SpaceUnit unit, bool priority)
    {
        if (unit == null) return;
        if (priority) AIPriority.Add(unit);
        else AIPriority.Remove(unit);
    }

    // Bigger hulls grind against more rock.
    public static float SizeFactor(ShipType type)
    {
        switch (type)
        {
            case ShipType.Corvette: return 0.3f;
            case ShipType.Destroyer: return 0.55f;
            case ShipType.Cruiser: return 1f;
            case ShipType.Carrier: return 1.25f;
            case ShipType.Battleship: return 1.6f;
            default: return 0f; // Fighters and stations.
        }
    }

    // ---------------- Hazards ----------------

    private void Update()
    {
        if (fields.Count == 0 || Time.time < nextTick) return;
        nextTick = Time.time + TickInterval;

        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead || unit.IsDefense) continue;

            SpaceUnit spaceUnit = unit.GetComponent<SpaceUnit>();
            if (spaceUnit == null) continue;

            if (unit.TryGetComponent(out Squadron squadron))
            {
                GrindFighters(squadron);
                continue;
            }

            NavMeshAgent agent = spaceUnit.agent != null ? spaceUnit.agent : unit.GetComponent<NavMeshAgent>();
            if (agent == null) continue;

            RestrictAgent(agent);

            bool inField = InField(unit.transform.position);
            bool moving = agent.velocity.sqrMagnitude > 1f;
            bool priority = (spaceUnit.moveState != null && spaceUnit.moveState.IsPriorityMove) || AIPriority.Contains(spaceUnit);

            ShipSpeed.For(agent).SetTerrainFactor(inField && !priority ? fieldSlowdown : 1f);

            if (inField && priority && moving)
            {
                float damage = unit.maxHealth * priorityDamagePerSecond * SizeFactor(spaceUnit.shipType) * TickInterval;
                if (damage > 0f) unit.DealRandomDamage(damage);
            }
        }
    }

    private void GrindFighters(Squadron squadron)
    {
        foreach (Fighter fighter in squadron.fighters)
        {
            if (fighter == null || !fighter.Alive || fighter.IsHolding) continue; // Sitting still is safe.
            if (fighter.Velocity.sqrMagnitude < 1f || !InDense(fighter.transform.position)) continue;

            HardpointHealth health = fighter.GetComponent<HardpointHealth>();
            if (health != null) health.DealDamage(health.maxHealth * fighterDensePerSecond * TickInterval);
        }
    }
}
