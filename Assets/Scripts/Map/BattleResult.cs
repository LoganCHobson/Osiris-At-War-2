using System.Collections.Generic;

public class BattleResult
{
    public string planetName;
    public Faction enemy;
    public bool playerWon;
    public bool planetCaptured;
    public bool planetLost;
    public bool autoResolved;
    public bool playerRetreated;
    public bool enemyRetreated;
    public string retreatedTo;

    public List<Ship> playerLosses = new List<Ship>();
    public List<Ship> enemyLosses = new List<Ship>();
    public List<string> playerStructuresLost = new List<string>();
    public List<string> enemyStructuresLost = new List<string>();

    public const string ShipyardName = "Capital Shipyard";
    public const string BattleStationName = "Battle Station";

    public static List<Ship> Missing(List<Ship> before, List<Ship> after)
    {
        List<Ship> missing = new List<Ship>(before);
        foreach (Ship ship in after)
        {
            missing.Remove(ship);
        }
        missing.RemoveAll(ship => ship == null);
        return missing;
    }

    public void AddStructureLoss(bool playerSide, string structure)
    {
        (playerSide ? playerStructuresLost : enemyStructuresLost).Add(structure);
    }
}
