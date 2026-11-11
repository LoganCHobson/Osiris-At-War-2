using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Settles a battle on the galactic map without fighting it, weighing fleet power AND matchup. Each ship's
// power is spread across what the enemy actually fields and scaled by how well it counters each of those
// (Counter table below): bombers wreck capital ships but die to escort screens, interceptors hunt strike
// craft, the battle line beats escorts but can't track fighters, and so on. The side with more effective
// power (after a modest roll) wins and the loser is wiped out. The winner's losses follow a square law
// (loser strength squared over winner strength), so a token force does next to nothing and an even fight
// is bloody. Those losses are shared across the fleet by how hard the enemy counters each ship type and
// how exposed it is - screened carriers sit at the back and rarely die.
public static class AutoResolver
{
    public const float RollVariance = 0.25f;
    // Winner's losses = this x loser strength squared / winner strength.
    public const float WinnerLossScale = 0.45f;

    // Retreats. The beaten side pulls out to its nearest friendly world instead of being wiped out, unless
    // it was routed (beaten by RoutRatio or more), has nowhere to go, or holds to the death (neutrals).
    // Pulling out mid-battle is costly: it loses RetreatLossShare of its strength at even odds, scaling with
    // how badly it lost (clamped to MinRetreatLoss..MaxRetreatLoss). The Avanti are slow and their commanders
    // stay in too long, so they lose AvantiRetreatPenalty times as much. A winner whose enemy escaped fought
    // a shorter battle: RetreatWinnerShare of the usual losses.
    public const float RoutRatio = 4f;
    public const float RetreatLossShare = 0.35f;
    public const float MinRetreatLoss = 0.2f, MaxRetreatLoss = 0.9f;
    public const float AvantiRetreatPenalty = 1.5f;
    public const float RetreatWinnerShare = 0.5f;

    // Dev comparison switch (GalacticSim -legacy): off = the old fight-to-the-death resolution. Always on in play.
    public static bool RetreatsEnabled = true;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSwitches() => RetreatsEnabled = true;
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
        List<ShipEntry> defenderRoster = defender != null ? defender.ships : new List<ShipEntry>();
        float defenses = includeDefenses ? Strength.Defenses(planet) : 0f;

        List<Force> attackers = Forces(attacker.ships, 0f);
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
            // Planet defences stand and fall; a defending fleet may pull out instead of dying with them.
            bool escapes = defender != null && CanEscape(defender, attackerRoll, defenderRoll);
            report.attackerLosses = ApplyWinnerLosses(attacker.ships, defenders, attackerRoll, defenderRoll,
                escapes ? RetreatWinnerShare : 1f);

            if (escapes)
            {
                report.defenderLosses = ApplyRetreatLosses(defender.ships, attackers, attackerRoll, defenderRoll, IsAvanti(defender.faction));
                report.loserRetreated = defender.RetreatFrom(planet) != null;
            }
            else
            {
                report.defenderLosses = Strength.Of(defenderRoster) + defenses;
                if (defender != null)
                {
                    Eliminate(defender);
                }
            }

            if (includeDefenses)
            {
                planet.hasCapitalShipyard = false;
                planet.hasBattleStation = false;
            }
        }
        else
        {
            bool escapes = CanEscape(attacker, defenderRoll, attackerRoll);
            report.defenderLosses = defender != null
                ? ApplyWinnerLosses(defender.ships, attackers, defenderRoll, attackerRoll, escapes ? RetreatWinnerShare : 1f) : 0f;

            if (escapes)
            {
                report.attackerLosses = ApplyRetreatLosses(attacker.ships, defenders, defenderRoll, attackerRoll, IsAvanti(attacker.faction));
                report.loserRetreated = attacker.RetreatFrom(planet) != null;
            }
            else
            {
                report.attackerLosses = Strength.Of(attacker.ships);
                Eliminate(attacker);
            }
            defender?.HoldAfterBattle();
        }

        GalacticEvents.RaiseBattleResolved(report);
        return attackerWon;
    }

    // A fleet's fighting power against this particular enemy: each ship's power spread over the enemy's
    // composition (by power share) and scaled by how well it counters each part of it.
    public static float Effective(List<ShipEntry> roster, List<ShipEntry> enemy, float enemyDefenses = 0f)
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

    private static List<Force> Forces(List<ShipEntry> roster, float defenses)
    {
        List<Force> forces = new List<Force>();
        if (roster != null)
        {
            foreach (ShipEntry ship in roster)
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

    private static float ApplyWinnerLosses(List<ShipEntry> roster, List<Force> enemy, float winnerRoll, float loserRoll,
        float share = 1f)
    {
        float damage = share * WinnerLossScale * loserRoll * loserRoll / Mathf.Max(winnerRoll, 1f);
        return ApplyLosses(roster, enemy, damage);
    }

    // ---------------- Retreats ----------------

    // A beaten fleet gets away unless it was routed or never retreats (neutrals hold to the death). Whether it
    // has somewhere to go is settled by GalacticFleet.RetreatFrom: with no route home it's destroyed there.
    private static bool CanEscape(GalacticFleet loser, float winnerRoll, float loserRoll)
    {
        return RetreatsEnabled && loser != null && loser.CanRetreat && winnerRoll < loserRoll * RoutRatio;
    }

    // Share of a retreating fleet's strength lost pulling out: RetreatLossShare at even odds, more the worse
    // the defeat, AvantiRetreatPenalty times as much for the Avanti.
    public static float RetreatLossFraction(float winnerRoll, float loserRoll, bool avanti)
    {
        float margin = winnerRoll / Mathf.Max(loserRoll, 1f);
        float fraction = Mathf.Clamp(RetreatLossShare * margin, MinRetreatLoss, MaxRetreatLoss);
        if (avanti) fraction = Mathf.Min(MaxRetreatLoss, fraction * AvantiRetreatPenalty);
        return fraction;
    }

    private static float ApplyRetreatLosses(List<ShipEntry> roster, List<Force> enemy, float winnerRoll, float loserRoll, bool avanti)
    {
        float damage = Strength.Of(roster) * RetreatLossFraction(winnerRoll, loserRoll, avanti);
        return ApplyLosses(roster, enemy, damage);
    }

    private static bool IsAvanti(Faction faction) => faction != null && faction.IsAvanti;

    // Whether a list of ships is an Avanti fleet (shielded hulls), for forecasts that have no fleet object.
    private static bool IsAvantiRoster(List<ShipEntry> roster)
    {
        foreach (ShipEntry ship in roster)
        {
            if (ship != null && ship.type != null) return ship.type.GetComponent<ShieldGenerator>() != null;
        }
        return false;
    }

    // How much of the fighting each kind of ship actually stands in. Screens are out front, the line takes
    // the brunt, strike craft fly into the guns; carriers hang back behind the rest of the fleet and are only
    // really exposed once nothing is screening them.
    private static float Exposure(Role role, bool screened)
    {
        switch (role)
        {
            case Role.Screen: return 1.2f;
            case Role.Carrier: return screened ? 0.3f : 1f;
            case Role.Fighter:
            case Role.Interceptor:
            case Role.Bomber: return 1.1f;
            default: return 1f;
        }
    }

    // The damage is shared out across the fleet's ship types in proportion to how much of each is present,
    // how hard this particular enemy counters it, and how exposed it is (Exposure). Within a type, ships die
    // in random order. A ship is lost outright if its type's share covers it; leftover damage short of a
    // whole ship only kills it if it's at least KillThreshold of its worth, and then only by chance. The
    // fleet always keeps at least one ship.
    private static float ApplyLosses(List<ShipEntry> roster, List<Force> enemy, float damage)
    {
        float lost = 0f;
        if (damage <= 0f) return lost;

        bool screened = roster.Exists(s => s != null && (RoleOf(s) == Role.Line || RoleOf(s) == Role.Screen));
        Dictionary<Role, List<ShipEntry>> byRole = new Dictionary<Role, List<ShipEntry>>();
        Dictionary<Role, float> weight = new Dictionary<Role, float>();
        float totalWeight = 0f;

        foreach (ShipEntry ship in roster)
        {
            if (ship == null) continue;
            Role role = RoleOf(ship);
            if (!byRole.TryGetValue(role, out List<ShipEntry> list))
            {
                byRole[role] = list = new List<ShipEntry>();
                weight[role] = 0f;
            }
            list.Add(ship);
        }

        foreach (KeyValuePair<Role, List<ShipEntry>> entry in byRole)
        {
            float w = entry.Value.Sum(s => Mathf.Max(1f, s.combatPower)) * Vulnerability(entry.Key, enemy) * Exposure(entry.Key, screened);
            weight[entry.Key] = w;
            totalWeight += w;
        }
        if (totalWeight <= 0f) return lost;

        // Most-hit types first, so if the "keep one ship" floor kicks in it's the sheltered ones that survive.
        foreach (Role role in byRole.Keys.OrderByDescending(r => weight[r]).ToList())
        {
            float share = damage * weight[role] / totalWeight;
            List<ShipEntry> ships = byRole[role].OrderBy(_ => Random.value).ToList();

            foreach (ShipEntry ship in ships)
            {
                if (share <= 0f || roster.Count <= 1) break;

                float worth = Mathf.Max(1f, ship.combatPower);
                bool killed = share >= worth || (share >= worth * KillThreshold && Random.value < share / worth);
                if (!killed) break;

                roster.Remove(ship);
                lost += ship.combatPower;
                share -= worth;
            }
        }

        return lost;
    }

    // ---------------- Forecast ----------------

    public class Forecast
    {
        public float attackerWinChance;
        // Average ships lost per class (by display name), for each side.
        public readonly Dictionary<string, float> attackerLosses = new Dictionary<string, float>();
        public readonly Dictionary<string, float> defenderLosses = new Dictionary<string, float>();
    }

    // What autoresolving is likely to do, by running the same maths many times on copies of the fleets
    // (nothing is changed). All the defending fleets and the planet's defenses are treated as one force.
    // A beaten side that can retreat (not routed, not holding to the death) loses its retreat share, not
    // everything; the forecast can't know whether it has a route home, so it assumes it does.
    public static Forecast Predict(List<ShipEntry> attacker, List<ShipEntry> defender, float defenses, int samples = 80,
        bool attackerCanRetreat = true, bool defenderCanRetreat = true)
    {
        Forecast forecast = new Forecast();
        List<Force> attackers = Forces(attacker, 0f);
        List<Force> defenders = Forces(defender, defenses);
        float attackEffective = Effective(attackers, defenders);
        float defendEffective = Effective(defenders, attackers);
        bool attackerAvanti = IsAvantiRoster(attacker), defenderAvanti = IsAvantiRoster(defender);
        attackerCanRetreat &= RetreatsEnabled;
        defenderCanRetreat &= RetreatsEnabled;

        int wins = 0;
        for (int i = 0; i < samples; i++)
        {
            float attackerRoll = attackEffective * Roll();
            float defenderRoll = defendEffective * Roll();
            bool attackerWon = attackerRoll > defenderRoll;
            if (attackerWon) wins++;

            List<ShipEntry> attackerCopy = attacker.Where(s => s != null).ToList();
            List<ShipEntry> defenderCopy = defender.Where(s => s != null).ToList();
            if (attackerWon)
            {
                bool escapes = defenderCanRetreat && defenderCopy.Count > 0 && attackerRoll < defenderRoll * RoutRatio;
                ApplyWinnerLosses(attackerCopy, defenders, attackerRoll, defenderRoll, escapes ? RetreatWinnerShare : 1f);
                if (escapes) ApplyRetreatLosses(defenderCopy, attackers, attackerRoll, defenderRoll, defenderAvanti);
                Tally(forecast.attackerLosses, attacker, attackerCopy);
                Tally(forecast.defenderLosses, defender, escapes ? defenderCopy : new List<ShipEntry>());
            }
            else
            {
                bool escapes = attackerCanRetreat && defenderRoll < attackerRoll * RoutRatio;
                ApplyWinnerLosses(defenderCopy, attackers, defenderRoll, attackerRoll, escapes ? RetreatWinnerShare : 1f);
                if (escapes) ApplyRetreatLosses(attackerCopy, defenders, defenderRoll, attackerRoll, attackerAvanti);
                Tally(forecast.defenderLosses, defender, defenderCopy);
                Tally(forecast.attackerLosses, attacker, escapes ? attackerCopy : new List<ShipEntry>());
            }
        }

        forecast.attackerWinChance = wins / (float)samples;
        foreach (string key in forecast.attackerLosses.Keys.ToList()) forecast.attackerLosses[key] /= samples;
        foreach (string key in forecast.defenderLosses.Keys.ToList()) forecast.defenderLosses[key] /= samples;
        return forecast;
    }

    private static void Tally(Dictionary<string, float> losses, List<ShipEntry> before, List<ShipEntry> after)
    {
        HashSet<ShipEntry> survivors = new HashSet<ShipEntry>(after);
        foreach (ShipEntry ship in before)
        {
            if (ship == null || ship.type == null || survivors.Contains(ship)) continue;
            string name = ship.IsHero ? ship.shipName : ShipInfo.Name(ship.type);
            losses[name] = losses.TryGetValue(name, out float lost) ? lost + 1f : 1f;
        }
    }

    private static void Eliminate(GalacticFleet fleet)
    {
        fleet.ships.Clear();
        fleet.Destination?.ReleaseSlot(fleet);
        fleet.currentPlanet?.ReleaseSlot(fleet);
        Object.Destroy(fleet.gameObject);
    }
}
