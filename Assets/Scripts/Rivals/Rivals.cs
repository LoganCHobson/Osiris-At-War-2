using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// What each warlord admiral (RivalAdmiral.cs) remembers about the player: their record against each
// other and how the player fights - which ships they bring, whether they attack, whether they retreat.
// Kept per campaign and saved with it. The admirals' lines before and after battles come from here
// (shown by TransmissionPanel), and the rivals dossier (RivalsPanel) shows it to the player.
public static class Rivals
{
    public enum Tendency { Carriers, HeavyLine, Screens, Retreats, Aggressive, Outnumbers }

    [System.Serializable]
    public class Dossier
    {
        public string faction;        // Faction.factionName
        public int battles;           // every battle against the player, autoresolved ones included
        public int theirWins, yourWins;
        public int youRetreated, theyRetreated;
        public int youAttacked;
        // Fought battles only (autoresolve shows nothing of how the player fights): sums to average.
        public int observed;
        public float carrierShare, lineShare, screenShare; // the player's fleet, by combat power
        public float powerRatio;                            // player's power / the admiral's
        public string lastLine;
    }

    private static readonly Dictionary<string, Dossier> dossiers = new Dictionary<string, Dossier>();

    public static Dossier Of(RivalAdmiral rival)
    {
        if (rival == null) return null;
        if (!dossiers.TryGetValue(rival.faction, out Dossier dossier))
        {
            dossiers[rival.faction] = dossier = new Dossier { faction = rival.faction };
        }
        return dossier;
    }

    // ---- campaign lifetime ----

    public static void Clear() => dossiers.Clear();

    public static List<Dossier> Capture() => dossiers.Values.Select(d => JsonUtility.FromJson<Dossier>(JsonUtility.ToJson(d))).ToList();

    public static void Restore(List<Dossier> saved)
    {
        dossiers.Clear();
        if (saved == null) return;
        foreach (Dossier dossier in saved)
        {
            if (dossier != null && !string.IsNullOrEmpty(dossier.faction)) dossiers[dossier.faction] = dossier;
        }
    }

    // ---- what they have noticed ----

    // The player's habits this admiral has seen, strongest first. Needs a couple of battles to say anything.
    public static List<Tendency> Tendencies(Dossier d)
    {
        List<(Tendency tendency, float strength)> found = new List<(Tendency, float)>();
        if (d == null) return new List<Tendency>();
        if (d.observed >= 2)
        {
            float n = d.observed;
            Add(found, Tendency.Carriers, d.carrierShare / n, 0.25f);
            Add(found, Tendency.HeavyLine, d.lineShare / n, 0.55f);
            Add(found, Tendency.Screens, d.screenShare / n, 0.45f);
            Add(found, Tendency.Outnumbers, d.powerRatio / n, 1.3f);
        }
        if (d.battles >= 2) Add(found, Tendency.Retreats, d.youRetreated / (float)d.battles, 0.3f);
        if (d.battles >= 3) Add(found, Tendency.Aggressive, d.youAttacked / (float)d.battles, 0.75f);
        return found.OrderByDescending(f => f.strength).Select(f => f.tendency).ToList();
    }

    private static void Add(List<(Tendency, float)> found, Tendency tendency, float value, float threshold)
    {
        if (value >= threshold) found.Add((tendency, value / threshold));
    }

    public static string Describe(Tendency tendency) => tendency switch
    {
        Tendency.Carriers => "You field carrier-heavy fleets",
        Tendency.HeavyLine => "You rely on a heavy battle line",
        Tendency.Screens => "You lead with light, fast ships",
        Tendency.Retreats => "You pull out when a battle turns",
        Tendency.Aggressive => "You are nearly always the attacker",
        Tendency.Outnumbers => "You bring bigger fleets than theirs",
        _ => tendency.ToString()
    };

    // "2 wins, 1 loss against you" from the admiral's side.
    public static string Record(Dossier d)
    {
        if (d == null || d.battles == 0) return "Has not faced you yet";
        return $"{d.theirWins} {(d.theirWins == 1 ? "win" : "wins")}, {d.yourWins} {(d.yourWins == 1 ? "loss" : "losses")} against you";
    }

    // ---- lines ----

    // What they say as a battle opens: a first greeting, something they've noticed, or the record so far.
    public static string OpeningLine(RivalAdmiral rival)
    {
        Dossier d = Of(rival);
        if (d.battles == 0) return Pick(rival.firstMeeting);
        List<Tendency> noticed = Tendencies(d);
        if (noticed.Count > 0 && Random.value < 0.6f)
        {
            string line = Pick(rival.OnTendency(noticed[Random.Range(0, Mathf.Min(2, noticed.Count))]));
            if (!string.IsNullOrEmpty(line)) return line;
        }
        if (d.theirWins > d.yourWins) return Pick(rival.leading);
        if (d.yourWins > d.theirWins) return Pick(rival.trailing);
        return Pick(rival.even);
    }

    private static string Pick(string[] lines) => lines == null || lines.Length == 0 ? "" : lines[Random.Range(0, lines.Length)];

    // ---- recording battles ----

    // A fought campaign battle, from the context the battle scene filled in. Returns the admiral and
    // their parting line, or (null, null) if the player didn't fight one of the warlords.
    public static (RivalAdmiral rival, string line) RecordBattle(BattleContext context)
    {
        if (context == null || !context.hasPendingBattle || context.isSkirmish) return (null, null);
        bool playerAttacker = Strength.IsPlayer(context.attackerFaction);
        bool playerDefender = Strength.IsPlayer(context.defenderFaction);
        if (playerAttacker == playerDefender) return (null, null); // Not the player's battle.

        RivalAdmiral rival = RivalAdmiral.For(playerAttacker ? context.defenderFaction : context.attackerFaction);
        if (rival == null) return (null, null);
        Dossier d = Of(rival);

        bool playerWon = playerAttacker ? !context.attackerLost : !context.defenderLost;
        bool playerFled = playerAttacker ? context.attackerRetreated : context.defenderRetreated;
        bool theyFled = playerAttacker ? context.defenderRetreated : context.attackerRetreated;
        Count(d, playerWon, playerFled, theyFled, playerAttacker);

        List<ShipEntry> fleet = playerAttacker ? context.attackerStartRoster : context.defenderStartRoster;
        float total = fleet.Where(s => s != null).Sum(s => s.combatPower);
        if (total > 0f)
        {
            d.observed++;
            d.carrierShare += Share(fleet, FleetRole.Carrier, total);
            d.lineShare += Share(fleet, FleetRole.Line, total);
            d.screenShare += Share(fleet, FleetRole.Screen, total);
            float theirs = playerAttacker ? context.defenderStartPower : context.attackerStartPower;
            float ours = playerAttacker ? context.attackerStartPower : context.defenderStartPower;
            d.powerRatio += theirs > 0f ? Mathf.Min(ours / theirs, 5f) : 1f;
        }

        string line = playerFled ? Pick(rival.playerFled) : theyFled ? Pick(rival.theyFled)
                    : playerWon ? Pick(rival.lostBattle) : Pick(rival.wonBattle);
        d.lastLine = line;
        return (rival, line);
    }

    // An autoresolved battle: only the result is known.
    public static (RivalAdmiral rival, string line) RecordAutoresolve(Faction enemy, bool playerWon, bool playerAttacked)
    {
        RivalAdmiral rival = RivalAdmiral.For(enemy);
        if (rival == null) return (null, null);
        Dossier d = Of(rival);
        Count(d, playerWon, false, false, playerAttacked);
        string line = playerWon ? Pick(rival.lostBattle) : Pick(rival.wonBattle);
        d.lastLine = line;
        return (rival, line);
    }

    private static void Count(Dossier d, bool playerWon, bool playerFled, bool theyFled, bool playerAttacked)
    {
        d.battles++;
        if (playerWon) d.yourWins++; else d.theirWins++;
        if (playerFled) d.youRetreated++;
        if (theyFled) d.theyRetreated++;
        if (playerAttacked) d.youAttacked++;
    }

    private static float Share(List<ShipEntry> fleet, FleetRole role, float total) =>
        fleet.Where(s => s != null && s.fleetRole == role).Sum(s => s.combatPower) / total;
}
