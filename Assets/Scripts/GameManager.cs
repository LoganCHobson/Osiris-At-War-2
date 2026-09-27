using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public List<SpaceUnit> allFriendlyUnits = new List<SpaceUnit>();

    public GameObject UnitsListUI;
    public Transform startingShipSpawnPoint;

    public Fleet fleet;
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

        Debug.Log($"[GameManager] BattleContext.Instance is {(BattleContext.Instance == null ? "NULL" : "present")}, incomingRoster.Count = {(BattleContext.Instance == null ? -1 : BattleContext.Instance.incomingRoster.Count)}");

        List<Ship> roster = fleet != null ? fleet.ships : new List<Ship>();
        if (BattleContext.Instance != null && BattleContext.Instance.incomingRoster.Count > 0)
        {
            roster = BattleContext.Instance.incomingRoster;
        }

        Debug.Log($"[GameManager] Using roster with {roster.Count} entries.");
        for (int i = 0; i < roster.Count; i++)
        {
            Ship s = roster[i];
            Debug.Log($"[GameManager] roster[{i}] = {(s == null ? "NULL" : $"{s.name} (prefab={(s.prefab == null ? "NULL" : s.prefab.name)}, icon={(s.icon == null ? "NULL" : s.icon.name)})")}");
        }

        if (roster.Count > 0)
        {
            SpawnStartingShip(roster[0]);
        }

        for (int i = 1; i < roster.Count; i++)
        {
            AddReinforcement(i, roster[i]);
        }

        Debug.Log($"[GameManager] UnitsListUI is {(UnitsListUI == null ? "NULL" : UnitsListUI.name)}, childCount after populating = {(UnitsListUI == null ? -1 : UnitsListUI.transform.childCount)}");
    }

    private void SpawnStartingShip(Ship ship)
    {
        if (ship == null || ship.prefab == null) return;

        Vector3 position = startingShipSpawnPoint != null ? startingShipSpawnPoint.position : Vector3.zero;
        Quaternion rotation = startingShipSpawnPoint != null ? startingShipSpawnPoint.rotation : Quaternion.identity;

        Instantiate(ship.prefab, position, rotation);
    }

    private void AddReinforcement(int index, Ship ship)
    {
        Debug.Log($"[AddReinforcement #{index}] called for {(ship == null ? "NULL" : ship.name)}, UnitsListUI={(UnitsListUI == null ? "NULL" : UnitsListUI.name)}");

        if (ship == null || ship.icon == null) return;

        GameObject temp = Instantiate(ship.icon, UnitsListUI.transform);
        Debug.Log($"[AddReinforcement #{index}] instantiated instanceID={(temp == null ? -1 : temp.GetInstanceID())}, activeInHierarchy={(temp != null && temp.activeInHierarchy)}, parent={(temp != null && temp.transform.parent != null ? temp.transform.parent.name : "NULL")}, siblingIndex={(temp == null ? -1 : temp.transform.GetSiblingIndex())}");

        IconShipRef iconRef = temp.GetComponent<IconShipRef>();
        if (iconRef == null)
        {
            Debug.LogWarning($"[AddReinforcement #{index}] Ship icon prefab for '{ship.name}' has no IconShipRef component - skipping reinforcement entry.");
            Destroy(temp);
            return;
        }

        iconRef.ship = ship;
        Debug.Log($"[AddReinforcement #{index}] success for {ship.name}, instanceID={temp.GetInstanceID()}");
    }
}
