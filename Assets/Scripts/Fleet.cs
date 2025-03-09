using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class Fleet : MonoBehaviour
{
    public List<Ship> ships = new List<Ship>();

    public GameObject tempIcon;
    public GameObject tempPrefab;

    private void Start()
    {
        Ship ship = new Ship
        {
            icon = tempIcon,
            prefab = tempPrefab,
            cost = 10,
        
        };

        ships.Add(ship);
    }
}
