using System.Collections.Generic;
using UnityEngine;

public static class AutoResolver
{
    public const float RollVariance = 0.25f;
    public const float WinnerLossScale = 0.6f;

    public static bool ResolveAll(GalacticFleet attacker, Planet planet)
    {
        bool defensesStanding = planet.owner != attacker.faction && Strength.Defenses(planet) > 0f;

        for (int round = 0; round <= Planet.FleetSlotCount; round++)
        {
            GalacticFleet defender = attacker.FindOpposingFleet(planet);
            if (defender == null && !defensesStanding) break;

            bool includeDefenses = defensesStanding && (defender == null || defender.faction == planet.owner);
            if (!Resolve(attacker, defender, planet, includeDefenses)) return false;

            if (includeDefenses)
            {
                defensesStanding = false;
            }
        }

        planet.Capture(attacker.faction);
        return true;
    }

    private static bool Resolve(GalacticFleet attacker, GalacticFleet defender, Planet planet, bool includeDefenses)
    {
        float attackerPower = Strength.Of(attacker);
        float defenderFleetPower = Strength.Of(defender);
        float defenderPower = defenderFleetPower + (includeDefenses ? Strength.Defenses(planet) : 0f);

        float attackerRoll = attackerPower * Roll();
        float defenderRoll = defenderPower * Roll();
        bool attackerWon = attackerRoll > defenderRoll;

        BattleReport report = new BattleReport
        {
            planet = planet,
            attacker = attacker.faction,
            defender = defender != null ? defender.faction : planet.owner,
            attackerWon = attackerWon,
            autoResolved = true
        };

        if (attackerWon)
        {
            report.attackerLosses = ApplyLosses(attacker.roster, attackerPower * LossFraction(defenderRoll, attackerRoll));
            report.defenderLosses = defenderPower;

            if (defender != null)
            {
                Eliminate(defender);
            }

            if (includeDefenses)
            {
                planet.hasCapitalShipyard = false;
                planet.hasBattleStation = false;
            }
        }
        else
        {
            report.defenderLosses = defender != null ? ApplyLosses(defender.roster, defenderFleetPower * LossFraction(attackerRoll, defenderRoll)) : 0f;
            report.attackerLosses = attackerPower;

            Eliminate(attacker);
            defender?.HoldAfterBattle();
        }

        GalacticEvents.RaiseBattleResolved(report);
        return attackerWon;
    }

    private static float Roll()
    {
        return Random.Range(1f - RollVariance, 1f + RollVariance);
    }

    private static float LossFraction(float loserRoll, float winnerRoll)
    {
        return Mathf.Clamp01(loserRoll / Mathf.Max(winnerRoll, 1f)) * WinnerLossScale;
    }

    private static float ApplyLosses(List<Ship> roster, float lossPower)
    {
        float lost = 0f;

        while (roster.Count > 1)
        {
            float remaining = lossPower - lost;
            if (remaining <= 0f) break;

            int index = Random.Range(0, roster.Count);
            float power = roster[index] != null ? roster[index].combatPower : 0f;

            if (remaining < power && Random.value > remaining / power) break;

            roster.RemoveAt(index);
            lost += power;
        }

        return lost;
    }

    private static void Eliminate(GalacticFleet fleet)
    {
        fleet.roster.Clear();
        fleet.Destination?.ReleaseSlot(fleet);
        fleet.currentPlanet?.ReleaseSlot(fleet);
        Object.Destroy(fleet.gameObject);
    }
}
