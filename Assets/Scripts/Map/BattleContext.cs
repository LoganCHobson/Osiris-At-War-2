using System.Collections.Generic;
using UnityEngine;

public class BattleContext : MonoBehaviour
{
    public static BattleContext Instance { get; private set; }

    public List<Ship> incomingRoster = new List<Ship>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetIncomingRoster(List<Ship> roster)
    {
        incomingRoster = new List<Ship>(roster);
    }
}
