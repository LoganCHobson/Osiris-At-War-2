using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public List<SpaceUnit> allFriendlyUnits = new List<SpaceUnit>();

    public GameObject UnitsListUI;
    public Transform attackerShipSpawnPoint;
    public Transform defenderShipSpawnPoint;
    public float defenderSpawnSpacing = 15f;

    [Header("Planet Defenses")]
    public GameObject shipyardDefenderPrefab;
    public GameObject battleStationPrefab;
    public Vector3 battleStationLocalOffset = new Vector3(100f, 0f, -120f);
    public Vector3 shipyardLocalOffset = new Vector3(-100f, 0f, -260f);

    [Header("Allegiance")]
    public int playerLayer = 7;
    public int enemyLayer = 8;
    [FormerlySerializedAs("playerProjectilePrefab")] public GameObject defaultProjectilePrefab;
    public Material[] teamMaterials;

    public Fleet fleet;

    [Header("Battle Rules")]
    public int maxShipsPerSide = 5;
    public float battleEndDelay = 3f;

    public bool PlayerIsAttacker { get; private set; } = true;

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

        bool hasBattleContext = BattleContext.Instance != null && BattleContext.Instance.hasPendingBattle;

        if (!hasBattleContext)
        {
            DeployPlayerSide(fleet != null ? fleet.ships : new List<Ship>(), true);
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
            SpawnPlanetDefense(shipyardDefenderPrefab, shipyardLocalOffset, isShipyardBonus: true, isBattleStation: false);
        }

        if (context.defenderHasBattleStation)
        {
            SpawnPlanetDefense(battleStationPrefab, battleStationLocalOffset, isShipyardBonus: false, isBattleStation: true);
        }
    }

    private void Update()
    {
        if (battleEnded) return;
        if (BattleContext.Instance == null || !BattleContext.Instance.hasPendingBattle) return;

        bool attackerDefeated = BattleContext.Instance.attackerRoster.Count == 0;

        bool defenderRosterCleared = !BattleContext.Instance.hasDefender || BattleContext.Instance.defenderRoster.Count == 0;
        bool shipyardCleared = !BattleContext.Instance.defenderHasShipyard || !BattleContext.Instance.defenderShipyardSurvived;
        bool battleStationCleared = !BattleContext.Instance.defenderHasBattleStation || !BattleContext.Instance.defenderBattleStationSurvived;
        bool defenderDefeated = defenderRosterCleared && shipyardCleared && battleStationCleared;

        if (attackerDefeated || defenderDefeated)
        {
            bool attackerWon = !attackerDefeated;
            StartCoroutine(EndBattle(PlayerIsAttacker == attackerWon));
        }
    }

    private IEnumerator EndBattle(bool playerWon)
    {
        battleEnded = true;
        BattleEndBanner.Instance?.Show(playerWon);

        yield return new WaitForSecondsRealtime(battleEndDelay);

        GameSpeed.Reset();
        SceneManager.LoadScene("GalacticMap");
    }

    private void DeployPlayerSide(List<Ship> roster, bool attackerSide)
    {
        if (attackerSide)
        {
            if (roster.Count > 0)
            {
                Vector3 position = attackerShipSpawnPoint != null ? attackerShipSpawnPoint.position : Vector3.zero;
                Quaternion rotation = attackerShipSpawnPoint != null ? attackerShipSpawnPoint.rotation : Quaternion.identity;
                SpawnShip(roster[0], position, rotation, true);
            }

            for (int i = 1; i < roster.Count; i++)
            {
                AddReinforcement(roster[i]);
            }
            return;
        }

        Vector3 basePosition = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.position : Vector3.zero;
        Quaternion lineRotation = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.rotation : Quaternion.identity;
        Vector3 back = lineRotation * Vector3.back;
        int deployed = Mathf.Min(roster.Count, maxShipsPerSide);

        float spacing = defenderSpawnSpacing;
        for (int i = 0; i < deployed; i++)
        {
            if (roster[i] != null)
            {
                spacing = Mathf.Max(spacing, FleetFormation.Spacing(roster[i].prefab));
            }
        }

        for (int i = 0; i < roster.Count; i++)
        {
            if (i >= deployed)
            {
                AddReinforcement(roster[i]);
                continue;
            }

            Vector3 sideOffset = (lineRotation * Vector3.right) * (i * spacing);
            Vector3 depthJitter = back * Random.Range(0f, defenderSpawnSpacing);
            SpawnShip(roster[i], basePosition + sideOffset + depthJitter, lineRotation, false);
        }
    }

    private void DeployAISide(Faction faction, List<Ship> roster, bool attackerSide)
    {
        AIBattleCommander commander = gameObject.AddComponent<AIBattleCommander>();
        commander.Initialize(faction, attackerSide, roster, attackerSide ? attackerShipSpawnPoint : defenderShipSpawnPoint, maxShipsPerSide);
    }

    public GameObject SpawnShip(Ship ship, Vector3 position, Quaternion rotation, bool attackerSide)
    {
        if (ship == null || ship.prefab == null) return null;

        GameObject spawned = Instantiate(ship.prefab, position, rotation);
        TagShip(spawned, ship, attackerSide);
        return spawned;
    }

    public bool CanDeployPlayerShip()
    {
        return CountLiveShips(PlayerIsAttacker) < maxShipsPerSide;
    }

    public static int CountLiveShips(bool attackerSide)
    {
        int count = 0;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (!unit.IsDefense && !unit.IsDead && unit.isAttackerSide == attackerSide)
            {
                count++;
            }
        }
        return count;
    }

    private void SpawnPlanetDefense(GameObject prefab, Vector3 localOffset, bool isShipyardBonus, bool isBattleStation)
    {
        if (prefab == null)
        {
            Debug.LogWarning("Planet has a defense building but no matching prefab is assigned on GameManager - skipping its battle spawn.");
            return;
        }

        Vector3 basePosition = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.position : Vector3.zero;
        Quaternion rotation = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.rotation : Quaternion.identity;
        Vector3 offset = rotation * localOffset;

        GameObject spawned = Instantiate(prefab, basePosition + offset, rotation);
        UnitHealthManager health = spawned.GetComponent<UnitHealthManager>();
        health?.ConfigureSpecial(isShipyardBonus, isBattleStation);

        ApplyAllegiance(spawned, false);
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

        if (faction != null && faction.shipMaterial != null)
        {
            Repaint(spawned, faction.shipMaterial);
        }

        if (!playerSide)
        {
            StripPlayerOnly(spawned);
        }
    }

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

    private void AddReinforcement(Ship ship)
    {
        if (ship == null || ship.icon == null) return;

        GameObject temp = Instantiate(ship.icon, UnitsListUI.transform);

        IconShipRef iconRef = temp.GetComponent<IconShipRef>();
        if (iconRef == null)
        {
            Debug.LogWarning($"Ship icon prefab for '{ship.name}' has no IconShipRef component - skipping reinforcement entry.");
            Destroy(temp);
            return;
        }

        iconRef.ship = ship;
    }

    public static void TagShip(GameObject spawned, Ship ship, bool isAttackerSide)
    {
        UnitHealthManager health = spawned.GetComponent<UnitHealthManager>();
        if (health != null)
        {
            health.Configure(ship, isAttackerSide);
        }

        if (Instance != null)
        {
            Instance.ApplyAllegiance(spawned, isAttackerSide);
        }
    }
}
