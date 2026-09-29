using System.Collections.Generic;
using UnityEngine;

public class Fleet : MonoBehaviour
{
    public List<Ship> ships = new List<Ship>();

    public GameObject tempPrefab;

    private void Start()
    {
        if (tempPrefab == null) return;

        Ship ship = tempPrefab.GetComponent<Ship>();
        if (ship != null)
        {
            ships.Add(ship);
        }
    }
}
