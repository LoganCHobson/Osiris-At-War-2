using System.Collections.Generic;

public static class Strength
{
    public const float ShipyardPower = 150f;
    public const float BattleStationPower = 500f;
    public const float DefaultShipPower = 100f;

    public static float Of(List<Ship> roster)
    {
        float total = 0f;
        if (roster == null) return total;

        foreach (Ship ship in roster)
        {
            if (ship != null)
            {
                total += ship.combatPower;
            }
        }
        return total;
    }

    public static float Of(GalacticFleet fleet)
    {
        return fleet != null ? Of(fleet.roster) : 0f;
    }

    public static float Defenses(Planet planet)
    {
        if (planet == null) return 0f;
        return (planet.hasCapitalShipyard ? ShipyardPower : 0f) + (planet.hasBattleStation ? BattleStationPower : 0f);
    }

    public static bool IsPlayer(Faction faction)
    {
        return faction != null && faction.isPlayerFaction;
    }
}
