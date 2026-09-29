using System.Collections.Generic;
using UnityEngine;

public class BattleContext : MonoBehaviour
{
    public static BattleContext Instance { get; private set; }

    public bool hasPendingBattle;

    public string attackerFleetName;
    public Faction attackerFaction;
    public List<Ship> attackerRoster = new List<Ship>();

    public bool hasDefender;
    public string defenderFleetName;
    public Faction defenderFaction;
    public List<Ship> defenderRoster = new List<Ship>();

    public string destinationPlanetName;

    public bool defenderHasShipyard;
    public bool defenderShipyardSurvived = true;
    public bool defenderHasBattleStation;
    public bool defenderBattleStationSurvived = true;

    public float attackerStartPower;
    public float defenderStartPower;

    public List<Ship> attackerStartRoster = new List<Ship>();
    public List<Ship> defenderStartRoster = new List<Ship>();

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
        attackerFaction = attacker.faction;
        attackerRoster = new List<Ship>(attacker.roster);

        hasDefender = defender != null;
        defenderFleetName = hasDefender ? defender.gameObject.name : null;
        defenderFaction = hasDefender ? defender.faction : attacker.currentPlanet?.owner;
        defenderRoster = hasDefender ? new List<Ship>(defender.roster) : new List<Ship>();

        destinationPlanetName = attacker.currentPlanet != null ? attacker.currentPlanet.planetName : null;

        defenderHasShipyard = attacker.currentPlanet != null && attacker.currentPlanet.hasCapitalShipyard;
        defenderShipyardSurvived = true;
        defenderHasBattleStation = attacker.currentPlanet != null && attacker.currentPlanet.hasBattleStation;
        defenderBattleStationSurvived = true;

        attackerStartPower = Strength.Of(attackerRoster);
        defenderStartPower = Strength.Of(defenderRoster);

        attackerStartRoster = new List<Ship>(attackerRoster);
        defenderStartRoster = new List<Ship>(defenderRoster);
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

    public void ReportShipyardDestroyed()
    {
        defenderShipyardSurvived = false;
    }

    public void ReportBattleStationDestroyed()
    {
        defenderBattleStationSurvived = false;
    }

    public void Clear()
    {
        hasPendingBattle = false;
        attackerFleetName = null;
        attackerFaction = null;
        attackerRoster.Clear();
        hasDefender = false;
        defenderFleetName = null;
        defenderFaction = null;
        defenderRoster.Clear();
        destinationPlanetName = null;
        defenderHasShipyard = false;
        defenderShipyardSurvived = true;
        defenderHasBattleStation = false;
        defenderBattleStationSurvived = true;
        attackerStartPower = 0f;
        defenderStartPower = 0f;
        attackerStartRoster.Clear();
        defenderStartRoster.Clear();
    }
}
