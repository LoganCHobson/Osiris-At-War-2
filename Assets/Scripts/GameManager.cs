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
    }

    private void Update()
    {
        if (battleEnded) return;
        if (BattleContext.Instance == null || !BattleContext.Instance.hasPendingBattle) return;

        bool attackerDefeated = BattleContext.Instance.attackerRoster.Count == 0;
        bool defenderDefeated = BattleContext.Instance.hasDefender && BattleContext.Instance.defenderRoster.Count == 0;

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

        for (int i = 0; i < defenderRoster.Count; i++)
        {
            Ship ship = defenderRoster[i];
            if (ship == null || ship.prefab == null) continue;

            Vector3 offset = (rotation * Vector3.right) * (i * defenderSpawnSpacing);
            GameObject spawned = Instantiate(ship.prefab, basePosition + offset, rotation);
            TagShip(spawned, ship, false);
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
