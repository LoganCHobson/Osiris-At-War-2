using System.Collections.Generic;
using UnityEngine;

public static class AutoResolver
{
    public const float RollVariance = 0.25f;
    public const float WinnerLossScale = 0.6f;
    public const float MinWinnerCasualtyChance = 0.25f;

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
            report.attackerLosses = ApplyWinnerLosses(attacker.roster, attackerPower, attackerRoll, defenderRoll);
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
            report.defenderLosses = defender != null ? ApplyWinnerLosses(defender.roster, defenderFleetPower, defenderRoll, attackerRoll) : 0f;
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

    private static float ApplyWinnerLosses(List<Ship> roster, float winnerPower, float winnerRoll, float loserRoll)
    {
        float closeness = Mathf.Clamp01(loserRoll / Mathf.Max(winnerRoll, 1f));
        float lost = ApplyLosses(roster, winnerPower * closeness * WinnerLossScale);

        if (lost <= 0f && roster.Count > 1 && Random.value < Mathf.Lerp(MinWinnerCasualtyChance, 1f, closeness))
        {
            int index = Random.Range(0, roster.Count);
            lost += roster[index] != null ? roster[index].combatPower : 0f;
            roster.RemoveAt(index);
        }

        return lost;
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
