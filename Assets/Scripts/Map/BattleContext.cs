using System.Collections.Generic;
using UnityEngine;

public class BattleContext : MonoBehaviour
{
    public static BattleContext Instance { get; private set; }

    public bool hasPendingBattle;

    public string attackerFleetName;
    public bool attackerIsPlayerFleet;
    public List<Ship> attackerRoster = new List<Ship>();

    public bool hasDefender;
    public string defenderFleetName;
    public List<Ship> defenderRoster = new List<Ship>();

    public string destinationPlanetName;

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

    public void BeginBattle(GalacticFleet attacker, GalacticFleet defender)
    {
        hasPendingBattle = true;

        attackerFleetName = attacker.gameObject.name;
        attackerIsPlayerFleet = attacker.isPlayerFleet;
        attackerRoster = new List<Ship>(attacker.roster);

        hasDefender = defender != null;
        defenderFleetName = hasDefender ? defender.gameObject.name : null;
        defenderRoster = hasDefender ? new List<Ship>(defender.roster) : new List<Ship>();

        destinationPlanetName = attacker.currentPlanet != null ? attacker.currentPlanet.planetName : null;
    }

    public void ReportLoss(bool attackerSide, Ship ship)
    {
        if (ship == null) return;

        if (attackerSide)
        {
            attackerRoster.Remove(ship);
        }
        else
        {
            defenderRoster.Remove(ship);
        }
    }

    public void Clear()
    {
        hasPendingBattle = false;
        attackerFleetName = null;
        attackerRoster.Clear();
        hasDefender = false;
        defenderFleetName = null;
        defenderRoster.Clear();
        destinationPlanetName = null;
    }
}
