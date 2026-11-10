using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public List<SpaceUnit> allFriendlyUnits = new List<SpaceUnit>();

    public GameObject UnitsListUI;
    public Transform attackerShipSpawnPoint;
    public Transform defenderShipSpawnPoint;

    [Header("Planet Defenses")]
    public GameObject shipyardDefenderPrefab;
    public GameObject battleStationPrefab;
    public Vector3 battleStationLocalOffset = new Vector3(100f, 0f, -120f);
    public Vector3 shipyardLocalOffset = new Vector3(-100f, 0f, -260f);
    public float defenseLineClearance = 60f;
    public float defenseSideClearance = 25f;

    [Header("Allegiance")]
    public int playerLayer = 7;
    public int enemyLayer = 8;
    [FormerlySerializedAs("playerProjectilePrefab")] public GameObject defaultProjectilePrefab;
    public Material[] teamMaterials;

    public Fleet fleet;

    [Header("Battle Rules")]
    public int populationCap = 30;

    // The cap for one side right now.
    public int PopulationCapFor(bool attackerSide) => populationCap;
    public float battleEndDelay = 3f;

    public bool PlayerIsAttacker { get; private set; } = true;

    public static bool CombatOver => Instance != null && Instance.battleEnded;

    private bool battleEnded;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        SkyPlanet.Create(); // The planet being fought over, below the fleets.
        CreateTerrain();

        bool hasBattleContext = BattleContext.Instance != null && BattleContext.Instance.hasPendingBattle;

        if (!hasBattleContext)
        {
            DeployPlayerSide(fleet != null ? fleet.ships.ConvertAll(ship => ShipEntry.New(ship)) : new List<ShipEntry>(), true);
            return;
        }

        BattleContext context = BattleContext.Instance;
        bool attackerIsPlayer = Strength.IsPlayer(context.attackerFaction);
        bool defenderIsPlayer = Strength.IsPlayer(context.defenderFaction);
        PlayerIsAttacker = attackerIsPlayer || !defenderIsPlayer;

        if (attackerIsPlayer)
        {
            DeployPlayerSide(context.attackerRoster, true);
        }
        else
        {
            DeployAISide(context.attackerFaction, context.attackerRoster, true);
        }

        if (defenderIsPlayer)
        {
            DeployPlayerSide(context.defenderRoster, false);
        }
        else
        {
            DeployAISide(context.defenderFaction, context.defenderRoster, false);
        }

        if (context.defenderHasShipyard)
        {
            SpawnPlanetDefense(DefensePrefab(context.defenderFaction, shipyard: true), shipyardLocalOffset, isShipyardBonus: true, isBattleStation: false);
        }

        if (context.defenderHasBattleStation)
        {
            SpawnPlanetDefense(DefensePrefab(context.defenderFaction, shipyard: false), battleStationLocalOffset, isShipyardBonus: false, isBattleStation: true);
        }
    }

    private void Update()
    {
        if (battleEnded) return;
        if (BattleContext.Instance == null || !BattleContext.Instance.hasPendingBattle) return;
        if (RetreatManager.Instance != null && RetreatManager.Instance.JumpingOut) return; // The retreat ends the battle itself.

        bool attackerInPlay = CountInPlay(true) > 0;
        bool defenderInPlay = CountInPlay(false) > 0;

        if (!attackerInPlay || !defenderInPlay)
        {
            ConcludeBattle(!attackerInPlay, false);
        }
    }

    public static int CountInPlay(bool attackerSide)
    {
        int count = 0;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit != null && !unit.IsDead && unit.isAttackerSide == attackerSide)
            {
                count++;
            }
        }
        return count;
    }

    // Asteroids for this battle. The same planet always gets the same layout.
    private void CreateTerrain()
    {
        Vector3 attackerStart = attackerShipSpawnPoint != null ? attackerShipSpawnPoint.position : Vector3.zero;
        Vector3 defenderStart = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.position : Vector3.zero;

        string planet = BattleContext.Instance != null ? BattleContext.Instance.destinationPlanetName : null;
        int seed = string.IsNullOrEmpty(planet) ? Random.Range(1, int.MaxValue) : PlanetArchetypes.StableHash(planet) + 17;
        BattleTerrain.Create(attackerStart, defenderStart, seed);
    }

    public Transform StartPointFor(bool attackerSide)
    {
        return attackerSide ? attackerShipSpawnPoint : defenderShipSpawnPoint;
    }

    public void ConcludeBattle(bool attackerLost, bool byRetreat)
    {
        if (battleEnded) return;
        battleEnded = true;
        RetreatManager.Instance?.Finish();

        BattleContext context = BattleContext.Instance;
        if (context != null)
        {
            context.attackerLost = attackerLost;
            context.defenderLost = !attackerLost;

            if (byRetreat)
            {
                ResolveRetreat(context, attackerLost);
                if (attackerLost) context.attackerRetreated = true;
                else context.defenderRetreated = true;
            }

            if (!attackerLost)
            {
                if (context.defenderHasShipyard) context.ReportShipyardDestroyed();
                if (context.defenderHasBattleStation) context.ReportBattleStationDestroyed();
            }

            context.Lock();
        }

        bool playerLost = attackerLost == PlayerIsAttacker;
        string subtitle = !byRetreat ? null : playerLost ? "Your fleet has retreated" : "The enemy fleet has retreated";
        StartCoroutine(EndBattle(!playerLost, subtitle));
    }

    private static void ResolveRetreat(BattleContext context, bool attackerSide)
    {
        foreach (UnitHealthManager unit in new List<UnitHealthManager>(UnitHealthManager.Active))
        {
            if (unit == null || unit.IsDead || unit.IsDefense || unit.isAttackerSide != attackerSide) continue;

            if (RetreatManager.ShipCanRetreat(unit))
            {
                RetreatManager.Depart(unit);
            }
            else
            {
                context.ReportLoss(attackerSide, unit.entry, unit.battleToken);
            }
        }
    }

    private IEnumerator EndBattle(bool playerWon, string subtitle)
    {
        BattleEndBanner.Instance?.Show(playerWon, subtitle);

        yield return new WaitForSecondsRealtime(battleEndDelay);

        GameSpeed.Reset();
        SceneManager.LoadScene("GalacticMap");
    }

    private void DeployPlayerSide(List<ShipEntry> roster, bool attackerSide)
    {
        if (attackerSide)
        {
            if (roster.Count > 0)
            {
                Vector3 position = attackerShipSpawnPoint != null ? attackerShipSpawnPoint.position : Vector3.zero;
                Quaternion rotation = attackerShipSpawnPoint != null ? attackerShipSpawnPoint.rotation : Quaternion.identity;
                BattleDeployment.Placement lead = BattleDeployment.Arrange(new[] { roster[0] }, roster[0], position, rotation * Vector3.forward)[0];
                SpawnShip(roster[0], lead.position, rotation, true);
            }

            for (int i = 1; i < roster.Count; i++)
            {
                AddReinforcement(roster[i]);
            }
            return;
        }

        Vector3 basePosition = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.position : Vector3.zero;
        Quaternion lineRotation = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.rotation : Quaternion.identity;

        List<ShipEntry> deploying = new List<ShipEntry>();
        int population = 0;
        foreach (ShipEntry ship in roster)
        {
            if (ship == null || ship.type == null) continue;

            if (population + ship.populationCost <= populationCap)
            {
                deploying.Add(ship);
                population += ship.populationCost;
            }
            else
            {
                AddReinforcement(ship);
            }
        }

        // The fleet's lead (first in its roster) at the centre of the battle line, screen ahead, carriers behind.
        // Laid out on the planet's chosen defense formation if it has one (see DefensePlanner).
        ShipEntry fleetLead = deploying.Count > 0 ? deploying[0] : null;
        BattleContext context = BattleContext.Instance;
        DefenseFormation formation = context != null ? DefensePlans.For(context.defenderFaction, context.destinationPlanetName) : null;
        foreach (BattleDeployment.Placement placement in BattleDeployment.ArrangeFormation(deploying, formation, fleetLead, basePosition, lineRotation * Vector3.forward))
        {
            GameObject spawned = SpawnShip(placement.ship, placement.position, lineRotation, false);
            if (spawned != null && spawned.TryGetComponent(out ShipAltitude altitude)) altitude.SnapAltitude(placement.altitude);
        }
    }

    private void DeployAISide(Faction faction, List<ShipEntry> roster, bool attackerSide)
    {
        AIBattleCommander commander = gameObject.AddComponent<AIBattleCommander>();
        commander.Initialize(faction, attackerSide, roster, attackerSide ? attackerShipSpawnPoint : defenderShipSpawnPoint, populationCap);
    }

    // An individual ship from a fleet: it carries its name/hero into battle and is claimed from the roster.
    public GameObject SpawnShip(ShipEntry ship, Vector3 position, Quaternion rotation, bool attackerSide)
    {
        if (ship == null || ship.prefab == null) return null;

        GameObject spawned = Instantiate(ship.prefab, position, rotation);
        TagShip(spawned, ship, attackerSide);
        return spawned;
    }

    public GameObject SpawnShip(Ship ship, Vector3 position, Quaternion rotation, bool attackerSide, bool fromRoster = true)
    {
        if (ship == null || ship.prefab == null) return null;

        GameObject spawned = Instantiate(ship.prefab, position, rotation);
        TagShip(spawned, ship, attackerSide, fromRoster);
        return spawned;
    }

    public bool CanDeployPlayerShip(Ship ship)
    {
        int cost = ship != null ? ship.populationCost : 0;
        return PopulationInPlay(PlayerIsAttacker) + cost <= PopulationCapFor(PlayerIsAttacker);
    }

    public static int PopulationInPlay(bool attackerSide)
    {
        int population = 0;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit.IsDefense || unit.IsDead || unit.IsFreeUnit || unit.isAttackerSide != attackerSide) continue;
            population += unit.sourceShip != null ? unit.sourceShip.populationCost : 1;
        }
        return population;
    }

    private static float DefenseRadius(GameObject prefab)
    {
        float scale = prefab.transform.localScale.x;
        if (prefab.TryGetComponent(out NavMeshObstacle obstacle))
        {
            return Mathf.Max(obstacle.size.x, obstacle.size.z) * 0.5f * scale;
        }
        return 30f;
    }

    private GameObject DefensePrefab(Faction faction, bool shipyard)
    {
        GameObject own = faction == null ? null : shipyard ? faction.capitalShipyardPrefab : faction.battleStationPrefab;
        if (own != null) return own;
        return shipyard ? shipyardDefenderPrefab : battleStationPrefab;
    }

    private void SpawnPlanetDefense(GameObject prefab, Vector3 localOffset, bool isShipyardBonus, bool isBattleStation)
    {
        if (prefab == null)
        {
            Debug.LogWarning("Planet has a defense building but no prefab is assigned on its Faction or on GameManager - skipping its battle spawn.");
            return;
        }

        Vector3 basePosition = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.position : Vector3.zero;
        Quaternion rotation = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.rotation : Quaternion.identity;

        float radius = DefenseRadius(prefab);
        Vector3 local = localOffset;
        local.z = Mathf.Min(local.z, -(radius + defenseLineClearance));
        if (Mathf.Abs(local.x) < radius + defenseSideClearance)
        {
            local.x = (isBattleStation ? 1f : -1f) * (radius + defenseSideClearance);
        }

        GameObject spawned = Instantiate(prefab, basePosition + rotation * local, rotation);
        UnitHealthManager health = spawned.GetComponent<UnitHealthManager>();
        health?.ConfigureSpecial(isShipyardBonus, isBattleStation);

        ApplyAllegiance(spawned, false);
        BattleTerrain.Instance?.AddDefenseZone(health, false); // Attackers can't drop reinforcements near it.
        if (isShipyardBonus) SpawnShipyardGuard(spawned.transform.position, rotation, radius);
    }

    // A capital shipyard lends the battle its guard squadron - one battleship, two destroyers and a corvette of
    // the defender's own classes, free: they don't count against the population cap, and they belong to the
    // yard, not the fleet, so win or lose (or retreat) they don't join the roster afterwards.
    private static readonly ShipType[] ShipyardGuard = { ShipType.Battleship, ShipType.Destroyer, ShipType.Destroyer, ShipType.Corvette };

    private void SpawnShipyardGuard(Vector3 yard, Quaternion facing, float yardRadius)
    {
        Faction faction = BattleContext.Instance != null ? BattleContext.Instance.defenderFaction : null;
        if (faction == null || faction.roster == null) return;
        Vector3 forward = facing * Vector3.forward, right = facing * Vector3.right;
        for (int i = 0; i < ShipyardGuard.Length; i++)
        {
            Ship ship = faction.roster.Find(s => s != null && s.prefab != null && s.prefab.TryGetComponent(out SpaceUnit u) && u.shipType == ShipyardGuard[i]);
            if (ship == null) continue;
            float side = (i - (ShipyardGuard.Length - 1) / 2f) * 32f;
            Vector3 position = yard + forward * (yardRadius + 35f + (i == 0 ? 15f : 0f)) + right * side;
            if (UnityEngine.AI.NavMesh.SamplePosition(position, out UnityEngine.AI.NavMeshHit hit, 40f, UnityEngine.AI.NavMesh.AllAreas)) position = hit.position;
            SpawnShip(ship, position, facing, false, fromRoster: false); // Free unit: no population, no roster.
        }
    }


    public void ApplyAllegiance(GameObject spawned, bool attackerSide)
    {
        bool playerSide = attackerSide == PlayerIsAttacker;
        int ownLayer = playerSide ? playerLayer : enemyLayer;
        int hostileLayer = playerSide ? enemyLayer : playerLayer;

        Faction faction = SideFaction(attackerSide);
        GameObject projectile = faction != null && faction.projectilePrefab != null ? faction.projectilePrefab : defaultProjectilePrefab;

        SetSideLayer(spawned.transform, ownLayer);

        foreach (TurretController turret in spawned.GetComponentsInChildren<TurretController>(true))
        {
            turret.targetLayer = 1 << hostileLayer;

            if (projectile != null)
            {
                turret.projectilePrefab = projectile;
            }
        }

        foreach (Squadron squadron in spawned.GetComponentsInChildren<Squadron>(true))
        {
            squadron.SetAllegiance(1 << hostileLayer, projectile, playerSide);
        }

        MissileLauncher.Fit(spawned, 1 << hostileLayer); // Destroyers and corvettes.
        HardpointWreck.Fit(spawned); // Destroyed turrets are wrecked in place, not just switched off.
        HullScarring.Fit(spawned);   // Sustained fire scores, scorches, cracks and finally breaches the hull.
        FleetDoctrine.Apply(spawned, IsAvanti(spawned, faction), spawned.TryGetComponent(out UnitHealthManager unitHealth) && unitHealth.isShipyardBonus);

        // Avanti: shields, point-defence beams, and they turn bow-on to their target to bring every gun to bear.
        // Humans: CIWS gatlings, and they turn broadside on, which brings the most guns to bear.
        bool avantiTech = spawned.GetComponent<ShieldGenerator>() != null;
        bool warship = spawned.TryGetComponent(out SpaceUnit spaceUnit) && spaceUnit.shipType != ShipType.Station && spawned.GetComponent<Squadron>() == null;
        foreach (TurretController turret in spawned.GetComponentsInChildren<TurretController>(true))
        {
            if (turret.targetFilter == TurretController.TargetFilter.FightersOnly)
            {
                if (avantiTech) turret.MakeBeam();
                else turret.MakeGatling();
            }
            else
            {
                turret.ApplyBatteryCadence(avantiTech); // Heavy guns fire slowly (same damage per second).
            }
        }
        if (warship) CombatFacing.Fit(spawned, avantiTech, 1 << hostileLayer);
        ShipAltitude.Fit(spawned, playerSide); // Height, pitch/roll and 3D spacing (warships and squadrons).
        if (warship) ShipAbility.Fit(spawned);  // One active ability per class.
        if (warship && playerSide) HardpointApproach.Fit(spawned); // Repositions to expose a targeted hidden hardpoint.
        if (warship && playerSide) ShipStance.Fit(spawned);        // Hold / aggressive / skirmish between orders.

        if (faction != null && faction.shipMaterial != null)
        {
            Repaint(spawned, faction.shipMaterial);
        }

        if (!playerSide)
        {
            StripPlayerOnly(spawned);
        }
    }

    private static bool IsAvanti(GameObject spawned, Faction faction) =>
        spawned.GetComponent<ShieldGenerator>() != null
        || (faction != null && faction.factionName != null && faction.factionName.IndexOf("Avanti", System.StringComparison.OrdinalIgnoreCase) >= 0)
        || spawned.name.IndexOf("Avanti", System.StringComparison.OrdinalIgnoreCase) >= 0;

    private void Repaint(GameObject spawned, Material material)
    {
        if (teamMaterials == null || teamMaterials.Length == 0) return;

        foreach (Renderer renderer in spawned.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i] != material && System.Array.IndexOf(teamMaterials, materials[i]) >= 0)
                {
                    materials[i] = material;
                    changed = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    private static void StripPlayerOnly(GameObject spawned)
    {
        foreach (PlayerSideOnly playerOnly in spawned.GetComponentsInChildren<PlayerSideOnly>(true))
        {
            playerOnly.Strip();
        }

        UnitHealthManager health = spawned.GetComponent<UnitHealthManager>();
        if (health != null && health.healthSlider != null)
        {
            health.healthSlider.gameObject.SetActive(false);
        }
    }

    private static Faction SideFaction(bool attackerSide)
    {
        BattleContext context = BattleContext.Instance;
        if (context == null || !context.hasPendingBattle) return null;
        return attackerSide ? context.attackerFaction : context.defenderFaction;
    }

    private void SetSideLayer(Transform target, int layer)
    {
        if (target.gameObject.layer == playerLayer || target.gameObject.layer == enemyLayer)
        {
            target.gameObject.layer = layer;
        }

        foreach (Transform child in target)
        {
            SetSideLayer(child, layer);
        }
    }

    private void AddReinforcement(ShipEntry ship)
    {
        if (ship == null || ship.icon == null) return;

        // One icon per ship class, with a count.
        foreach (Transform child in UnitsListUI.transform)
        {
            IconShipRef existing = child.GetComponent<IconShipRef>();
            if (existing != null && existing.Accepts(ship))
            {
                existing.Add(ship);
                return;
            }
        }

        GameObject temp = Instantiate(ship.icon, UnitsListUI.transform);

        IconShipRef iconRef = temp.GetComponent<IconShipRef>();
        if (iconRef == null)
        {
            Debug.LogWarning($"Ship icon prefab for '{ship.type.name}' has no IconShipRef component - skipping reinforcement entry.");
            Destroy(temp);
            return;
        }

        iconRef.ship = ship.type;
        iconRef.Add(ship);
    }

    public static void TagShip(GameObject spawned, ShipEntry ship, bool isAttackerSide)
    {
        UnitHealthManager health = spawned.GetComponent<UnitHealthManager>();
        if (health != null)
        {
            health.Configure(ship.type, isAttackerSide);
            health.entry = ship;

            BattleContext context = BattleContext.Instance;
            if (context != null && context.hasPendingBattle)
            {
                health.battleToken = context.Claim(isAttackerSide, ship);
            }
        }

        if (Instance != null) Instance.ApplyAllegiance(spawned, isAttackerSide);
        BattleTerrain.Instance?.RestrictShip(spawned);
        HeroForces.Register(spawned, ship, isAttackerSide);
    }

    // A unit that isn't one of the fleet's ships (launched strike craft, test spawns).
    public static void TagShip(GameObject spawned, Ship ship, bool isAttackerSide, bool fromRoster = true)
    {
        UnitHealthManager health = spawned.GetComponent<UnitHealthManager>();
        if (health != null)
        {
            health.Configure(ship, isAttackerSide);

            BattleContext context = BattleContext.Instance;
            if (!fromRoster)
            {
                health.battleToken = UnitHealthManager.FreeUnitToken;
            }
        }

        if (Instance != null)
        {
            Instance.ApplyAllegiance(spawned, isAttackerSide);
        }

        BattleTerrain.Instance?.RestrictShip(spawned);
    }
}
