using System.Collections.Generic;
using UnityEngine;

public class BattleContext : MonoBehaviour
{
    public static BattleContext Instance { get; private set; }

    [System.Serializable]
    public class ShipToken
    {
        public Ship ship;
        public int fleet;
        public bool claimed;
        public bool lost;
    }

    public bool hasPendingBattle;

    public Faction attackerFaction;
    public List<string> attackerFleetNames = new List<string>();
    public List<ShipToken> attackerTokens = new List<ShipToken>();
    public List<Ship> attackerRoster = new List<Ship>();

    public bool hasDefender;
    public Faction defenderFaction;
    public List<string> defenderFleetNames = new List<string>();
    public List<ShipToken> defenderTokens = new List<ShipToken>();
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

    public bool attackerLost;
    public bool defenderLost;
    public bool attackerRetreated;
    public bool defenderRetreated;

    public bool IsLocked { get; private set; }

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

    public void BeginBattle(GalacticFleet attacker, Planet planet)
    {
        Clear();
        hasPendingBattle = true;

        GalacticFleet firstDefender = attacker.FindOpposingFleet(planet);

        attackerFaction = attacker.faction;
        defenderFaction = firstDefender != null ? firstDefender.faction : planet != null ? planet.owner : null;

        AddFleet(attacker, attackerFleetNames, attackerTokens);
        foreach (GalacticFleet fleet in FleetsAt(planet))
        {
            if (fleet == attacker) continue;

            if (fleet.faction == attackerFaction)
            {
                AddFleet(fleet, attackerFleetNames, attackerTokens);
            }
            else if (firstDefender != null && fleet.faction == defenderFaction)
            {
                AddFleet(fleet, defenderFleetNames, defenderTokens);
            }
        }

        hasDefender = defenderFleetNames.Count > 0;
        attackerRoster = RosterOf(attackerTokens);
        defenderRoster = RosterOf(defenderTokens);

        destinationPlanetName = planet != null ? planet.planetName : null;

        defenderHasShipyard = planet != null && planet.hasCapitalShipyard;
        defenderHasBattleStation = planet != null && planet.hasBattleStation;

        attackerStartPower = Strength.Of(attackerRoster);
        defenderStartPower = Strength.Of(defenderRoster);

        attackerStartRoster = new List<Ship>(attackerRoster);
        defenderStartRoster = new List<Ship>(defenderRoster);
    }

    private static List<GalacticFleet> FleetsAt(Planet planet)
    {
        List<GalacticFleet> fleets = new List<GalacticFleet>();
        if (planet == null) return fleets;

        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet fleet = planet.GetFleetInSlot(slot);
            if (fleet != null && fleet.IsPresentAt(planet) && !fleets.Contains(fleet))
            {
                fleets.Add(fleet);
            }
        }
        return fleets;
    }

    private static void AddFleet(GalacticFleet fleet, List<string> names, List<ShipToken> tokens)
    {
        if (fleet == null || names.Contains(fleet.gameObject.name)) return;

        int index = names.Count;
        names.Add(fleet.gameObject.name);
        foreach (Ship ship in fleet.roster)
        {
            if (ship != null)
            {
                tokens.Add(new ShipToken { ship = ship, fleet = index });
            }
        }
    }

    private static List<Ship> RosterOf(List<ShipToken> tokens)
    {
        List<Ship> roster = new List<Ship>();
        foreach (ShipToken token in tokens)
        {
            if (!token.lost) roster.Add(token.ship);
        }
        return roster;
    }

    public int Claim(bool attackerSide, Ship ship)
    {
        List<ShipToken> tokens = attackerSide ? attackerTokens : defenderTokens;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!tokens[i].claimed && !tokens[i].lost && tokens[i].ship == ship)
            {
                tokens[i].claimed = true;
                return i;
            }
        }
        return -1;
    }

    public List<Ship> SurvivorsOf(bool attackerSide, int fleet)
    {
        List<Ship> survivors = new List<Ship>();
        foreach (ShipToken token in attackerSide ? attackerTokens : defenderTokens)
        {
            if (token.fleet == fleet && !token.lost) survivors.Add(token.ship);
        }
        return survivors;
    }

    public void Lock()
    {
        IsLocked = true;
    }

    public void ReportLoss(bool attackerSide, Ship ship, int token = -1)
    {
        if (ship == null || IsLocked) return;

        List<ShipToken> tokens = attackerSide ? attackerTokens : defenderTokens;
        if (token < 0 || token >= tokens.Count || tokens[token].lost || tokens[token].ship != ship)
        {
            token = tokens.FindIndex(t => !t.lost && t.ship == ship);
        }
        if (token < 0) return;

        tokens[token].lost = true;
        (attackerSide ? attackerRoster : defenderRoster).Remove(ship);
    }

    public void ReportShipyardDestroyed()
    {
        if (IsLocked) return;
        defenderShipyardSurvived = false;
    }

    public void ReportBattleStationDestroyed()
    {
        if (IsLocked) return;
        defenderBattleStationSurvived = false;
    }

    public void Clear()
    {
        hasPendingBattle = false;
        attackerFaction = null;
        attackerFleetNames.Clear();
        attackerTokens.Clear();
        attackerRoster.Clear();
        hasDefender = false;
        defenderFaction = null;
        defenderFleetNames.Clear();
        defenderTokens.Clear();
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
        attackerLost = false;
        defenderLost = false;
        attackerRetreated = false;
        defenderRetreated = false;
        IsLocked = false;
    }
}
