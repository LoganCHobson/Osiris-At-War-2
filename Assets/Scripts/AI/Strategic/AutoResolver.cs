using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Settles a battle on the galactic map without fighting it, weighing fleet power AND matchup. Each ship's
// power is spread across what the enemy actually fields and scaled by how well it counters each of those
// (Counter table below): bombers wreck capital ships but die to escort screens, interceptors hunt strike
// craft, the battle line beats escorts but can't track fighters, and so on. The side with more effective
// power (after a modest roll) wins and the loser is wiped out. The winner's losses follow a square law
// (loser strength squared over winner strength), so a token force does next to nothing and an even fight
// is bloody, and they fall first on the ships the enemy counters best.
public static class AutoResolver
{
    public const float RollVariance = 0.25f;
    // Winner's losses = this x loser strength squared / winner strength.
    public const float WinnerLossScale = 0.45f;
    // A ship only dies if the damage left over is at least this share of its worth.
    public const float KillThreshold = 0.5f;

    public enum Role { Line, Screen, Carrier, Fighter, Interceptor, Bomber, Defense }

    // How effective a unit of the row role is against the column role.
    //                                         Line  Screen Carrier Fighter Intercept Bomber Defense
    private static readonly float[,] Counter =
    {
        /* Line        */                    { 1.0f, 1.3f,  1.3f,   0.35f,  0.3f,     0.5f,  1.2f },
        /* Screen      */                    { 0.6f, 1.0f,  1.0f,   1.8f,   1.6f,     2.2f,  0.6f },
        /* Carrier     */                    { 0.9f, 0.9f,  1.0f,   1.0f,   1.0f,     1.0f,  0.9f },
        /* Fighter     */                    { 0.6f, 0.5f,  1.0f,   1.0f,   0.8f,     1.6f,  0.5f },
        /* Interceptor */                    { 0.3f, 0.3f,  0.5f,   1.6f,   1.0f,     2.0f,  0.2f },
        /* Bomber      */                    { 1.7f, 0.4f,  1.7f,   0.4f,   0.3f,     0.5f,  1.7f },
        /* Defense     */                    { 1.0f, 1.0f,  1.0f,   1.5f,   1.5f,     1.5f,  1.0f },
    };

    private struct Force
    {
        public Role role;
        public float power;
    }

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
        List<Ship> defenderRoster = defender != null ? defender.roster : new List<Ship>();
        float defenses = includeDefenses ? Strength.Defenses(planet) : 0f;

        List<Force> attackers = Forces(attacker.roster, 0f);
        List<Force> defenders = Forces(defenderRoster, defenses);

        float attackerRoll = Effective(attackers, defenders) * Roll();
        float defenderRoll = Effective(defenders, attackers) * Roll();
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
            report.attackerLosses = ApplyWinnerLosses(attacker.roster, defenders, attackerRoll, defenderRoll);
            report.defenderLosses = Strength.Of(defenderRoster) + defenses;

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
            report.defenderLosses = defender != null ? ApplyWinnerLosses(defender.roster, attackers, defenderRoll, attackerRoll) : 0f;
            report.attackerLosses = Strength.Of(attacker.roster);

            Eliminate(attacker);
            defender?.HoldAfterBattle();
        }

        GalacticEvents.RaiseBattleResolved(report);
        return attackerWon;
    }

    // A fleet's fighting power against this particular enemy: each ship's power spread over the enemy's
    // composition (by power share) and scaled by how well it counters each part of it.
    public static float Effective(List<Ship> roster, List<Ship> enemy, float enemyDefenses = 0f)
    {
        return Effective(Forces(roster, 0f), Forces(enemy, enemyDefenses));
    }

    private static float Effective(List<Force> side, List<Force> enemy)
    {
        float enemyTotal = enemy.Sum(f => f.power);
        if (enemyTotal <= 0f) return side.Sum(f => f.power);

        float effective = 0f;
        foreach (Force unit in side)
        {
            float factor = 0f;
            foreach (Force target in enemy) factor += target.power / enemyTotal * Counter[(int)unit.role, (int)target.role];
            effective += unit.power * factor;
        }
        return effective;
    }

    // How exposed a role is to this enemy: how hard the enemy's mix counters it.
    private static float Vulnerability(Role role, List<Force> enemy)
    {
        float enemyTotal = enemy.Sum(f => f.power);
        if (enemyTotal <= 0f) return 1f;

        float threat = 0f;
        foreach (Force attacker in enemy) threat += attacker.power / enemyTotal * Counter[(int)attacker.role, (int)role];
        return threat;
    }

    private static List<Force> Forces(List<Ship> roster, float defenses)
    {
        List<Force> forces = new List<Force>();
        if (roster != null)
        {
            foreach (Ship ship in roster)
            {
                if (ship != null) forces.Add(new Force { role = RoleOf(ship), power = ship.combatPower });
            }
        }
        if (defenses > 0f) forces.Add(new Force { role = Role.Defense, power = defenses });
        return forces;
    }

    public static Role RoleOf(Ship ship)
    {
        switch (ship.fleetRole)
        {
            case FleetRole.Screen: return Role.Screen;
            case FleetRole.Carrier: return Role.Carrier;
            case FleetRole.Fighter: return Role.Fighter;
            case FleetRole.Interceptor: return Role.Interceptor;
            case FleetRole.Bomber: return Role.Bomber;
            default: return Role.Line;
        }
    }

    private static float Roll()
    {
        return Random.Range(1f - RollVariance, 1f + RollVariance);
    }

    private static float ApplyWinnerLosses(List<Ship> roster, List<Force> enemy, float winnerRoll, float loserRoll)
    {
        float damage = WinnerLossScale * loserRoll * loserRoll / Mathf.Max(winnerRoll, 1f);
        return ApplyLosses(roster, enemy, damage);
    }

    // The ships the enemy counters best go first (smallest first among equals). A ship is lost outright if
    // the damage covers it; leftover damage short of a whole ship only kills it if it's at least
    // KillThreshold of its worth, and then only by chance. The fleet always keeps at least one ship.
    private static float ApplyLosses(List<Ship> roster, List<Force> enemy, float damage)
    {
        float lost = 0f;
        List<Ship> exposure = roster.Where(s => s != null)
            .OrderByDescending(s => Vulnerability(RoleOf(s), enemy))
            .ThenBy(s => s.combatPower)
            .ToList();

        foreach (Ship ship in exposure)
        {
            if (damage <= 0f || roster.Count <= 1) break;

            float worth = Mathf.Max(1f, ship.combatPower);
            bool killed = damage >= worth || (damage >= worth * KillThreshold && Random.value < damage / worth);
            if (!killed) break; // Not enough left to take down even the most exposed ship.

            roster.Remove(ship);
            lost += ship.combatPower;
            damage -= worth;
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
