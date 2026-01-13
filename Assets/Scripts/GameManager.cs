using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public List<SpaceUnit> allFriendlyUnits = new List<SpaceUnit>();

    public GameObject UnitsListUI;

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

        foreach (Ship ship in fleet.ships)
        {
          GameObject temp = Instantiate(ship.icon, UnitsListUI.transform);

          temp.GetComponent<IconShipRef>().ship = ship;
        }
    }
}
