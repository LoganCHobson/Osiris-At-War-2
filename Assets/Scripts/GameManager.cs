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

    [Header("Battle Rules")]
    public int maxShipsPerSide = 5;

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
            EndBattle();
        }
    }

    private void EndBattle()
    {
        battleEnded = true;
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

        for (int i = 0; i < roster.Count; i++)
        {
            if (i >= deployed)
            {
                AddReinforcement(roster[i]);
                continue;
            }

            Vector3 sideOffset = (lineRotation * Vector3.right) * (i * defenderSpawnSpacing);
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
