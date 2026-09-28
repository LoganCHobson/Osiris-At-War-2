using System;

public class BattleReport
{
    public Planet planet;
    public Faction attacker;
    public Faction defender;
    public float attackerLosses;
    public float defenderLosses;
    public bool attackerWon;
    public bool autoResolved;
}

public static class GalacticEvents
{
    public static event Action<Planet, Faction, Faction> PlanetCaptured;
    public static event Action<BattleReport> BattleResolved;

    public static void RaisePlanetCaptured(Planet planet, Faction previousOwner, Faction newOwner)
    {
        PlanetCaptured?.Invoke(planet, previousOwner, newOwner);
    }

    public static void RaiseBattleResolved(BattleReport report)
    {
        BattleResolved?.Invoke(report);
    }
}
