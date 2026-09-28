using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    public int playerLayer = 7;
    public int enemyLayer = 8;
    public GameObject playerProjectilePrefab;
    public GameObject enemyProjectilePrefab;

    public Fleet fleet;

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
        }

        bool hasBattleContext = BattleContext.Instance != null && BattleContext.Instance.hasPendingBattle;

        List<Ship> attackerRoster = hasBattleContext ? BattleContext.Instance.attackerRoster : (fleet != null ? fleet.ships : new List<Ship>());
        List<Ship> defenderRoster = hasBattleContext ? BattleContext.Instance.defenderRoster : new List<Ship>();

        if (attackerRoster.Count > 0)
        {
            SpawnStartingShip(attackerRoster[0], attackerShipSpawnPoint);
        }

        for (int i = 1; i < attackerRoster.Count; i++)
        {
            AddReinforcement(attackerRoster[i]);
        }

        SpawnDefendingFleet(defenderRoster);

        if (hasBattleContext && BattleContext.Instance.defenderHasShipyard)
        {
            SpawnPlanetDefense(shipyardDefenderPrefab, shipyardLocalOffset, isShipyardBonus: true, isBattleStation: false);
        }

        if (hasBattleContext && BattleContext.Instance.defenderHasBattleStation)
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
            EndBattle();
        }
    }

    private void EndBattle()
    {
        battleEnded = true;
        SceneManager.LoadScene("GalacticMap");
    }

    private void SpawnStartingShip(Ship ship, Transform spawnPoint)
    {
        if (ship == null || ship.prefab == null) return;

        Vector3 position = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        GameObject spawned = Instantiate(ship.prefab, position, rotation);
        TagShip(spawned, ship, true);
    }

    private void SpawnDefendingFleet(List<Ship> defenderRoster)
    {
        Vector3 basePosition = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.position : Vector3.zero;
        Quaternion rotation = defenderShipSpawnPoint != null ? defenderShipSpawnPoint.rotation : Quaternion.identity;
        Vector3 back = rotation * Vector3.back;

        for (int i = 0; i < defenderRoster.Count; i++)
        {
            Ship ship = defenderRoster[i];
            if (ship == null || ship.prefab == null) continue;

            Vector3 sideOffset = (rotation * Vector3.right) * (i * defenderSpawnSpacing);
            Vector3 depthJitter = back * Random.Range(0f, defenderSpawnSpacing);
            GameObject spawned = Instantiate(ship.prefab, basePosition + sideOffset + depthJitter, rotation);
            TagShip(spawned, ship, false);
        }
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

        bool defenderIsPlayer = BattleContext.Instance != null
            && BattleContext.Instance.defenderFaction != null
            && BattleContext.Instance.defenderFaction.isPlayerFaction;

        int ownLayer = defenderIsPlayer ? playerLayer : enemyLayer;
        int hostileLayer = defenderIsPlayer ? enemyLayer : playerLayer;
        GameObject friendlyProjectile = defenderIsPlayer ? playerProjectilePrefab : enemyProjectilePrefab;

        SetLayerRecursively(spawned, ownLayer);

        foreach (TurretController turret in spawned.GetComponentsInChildren<TurretController>())
        {
            turret.targetLayer = 1 << hostileLayer;

            if (friendlyProjectile != null)
            {
                turret.projectilePrefab = friendlyProjectile;
            }
        }
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
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
    }
}
