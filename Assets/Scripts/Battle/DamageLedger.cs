using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// Dev balance instrumentation: who dealt how much damage to whom, with what, and when - and who got the kills.
// Off unless a dev run turns it on (SiegeRecording -balance). Damage sources set the attacker around the call
// that applies damage (Begin/End); HardpointHealth and ShieldGenerator report what actually landed.
public static class DamageLedger
{
    public enum Weapon { None, Battery, PointDefence, Gatling, Beam, Missile, Torpedo, CraftGuns, MiniMissile, Bomb, Breakup, Asteroids, IonStorm }

    public static bool Enabled;

    // What each anti-ship battery is doing, sampled twice a second. OutRanged: no target, and the nearest enemy
    // warship is beyond the gun's range. IdleEnemyInRange: an enemy is within range but the gun has none (all
    // out of its arc or masked). Firing: laid on a target with a clear shot (firing or reloading).
    public enum GunState { NoEnemy, OutRanged, IdleEnemyInRange, OutOfArc, Blocked, Swinging, Firing }
    private static readonly Dictionary<string, int[]> gunStates = new Dictionary<string, int[]>();
    private static readonly Dictionary<string, List<float>> outRangedBy = new Dictionary<string, List<float>>();

    private static readonly Dictionary<string, Dictionary<string, int>> blockers = new Dictionary<string, Dictionary<string, int>>();

    public static void Blocker(UnitHealthManager unit, string what)
    {
        string name = Name(unit);
        if (!blockers.TryGetValue(name, out var d)) blockers[name] = d = new Dictionary<string, int>();
        d[what] = (d.TryGetValue(what, out int n) ? n : 0) + 1;
    }

    private static readonly Dictionary<string, int> missilesDown = new Dictionary<string, int>();

    // A missile, torpedo, mini-missile or bomb shot down: by whom (the current damage source) and what it was.
    public static void MissileDown(Weapon what)
    {
        if (!Enabled) return;
        string key = $"{(sourceSet && source != null ? Name(source) : "(unknown)")} / {(sourceSet ? weapon : Weapon.None)} downed a {what}";
        missilesDown[key] = (missilesDown.TryGetValue(key, out int n) ? n : 0) + 1;
    }

    public static void GunSample(UnitHealthManager unit, GunState state, float nearestRatio)
    {
        string name = Name(unit);
        if (!gunStates.TryGetValue(name, out int[] counts)) gunStates[name] = counts = new int[7];
        counts[(int)state]++;
        if (state == GunState.OutRanged)
        {
            if (!outRangedBy.TryGetValue(name, out List<float> list)) outRangedBy[name] = list = new List<float>();
            list.Add(nearestRatio);
        }
    }

    private static UnitHealthManager source;
    private static Weapon weapon;
    private static bool sourceSet;

    private class Line { public double hull, shield; public int hits, kills, craftKills; }
    private static readonly Dictionary<(string from, Weapon weapon, string to), Line> lines = new Dictionary<(string, Weapon, string), Line>();
    private static readonly Dictionary<(string from, Weapon weapon, int minute), double> timeline = new Dictionary<(string, Weapon, int), double>();
    private static readonly Dictionary<(string from, Weapon weapon), int> fired = new Dictionary<(string, Weapon), int>();
    private static readonly Dictionary<UnitHealthManager, (string from, Weapon weapon)> lastHit = new Dictionary<UnitHealthManager, (string, Weapon)>();
    private static readonly List<string> kills = new List<string>();
    public static float StartedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Enabled = false;
        lines.Clear(); timeline.Clear(); fired.Clear(); lastHit.Clear(); kills.Clear(); gunStates.Clear(); outRangedBy.Clear(); blockers.Clear(); missilesDown.Clear();
        sourceSet = false;
    }

    public static void Begin(UnitHealthManager from, Weapon kind)
    {
        source = from; weapon = kind; sourceSet = true;
    }

    public static void End() { sourceSet = false; source = null; weapon = Weapon.None; }

    public static void Fired(UnitHealthManager from, Weapon kind, int count = 1)
    {
        if (!Enabled) return;
        var key = (Name(from), kind);
        fired[key] = (fired.TryGetValue(key, out int n) ? n : 0) + count;
    }

    // "Human Battleship", "Avanti Fighter squadron", "Avanti Station", "Human Destroyer (yard guard)" ...
    public static string Name(UnitHealthManager unit)
    {
        if (unit == null) return "(environment)";
        bool avanti = unit.GetComponent<ShieldGenerator>() != null || unit.name.IndexOf("Avanti", System.StringComparison.OrdinalIgnoreCase) >= 0
            || (unit.sourceShip != null && unit.sourceShip.name.IndexOf("Avanti", System.StringComparison.OrdinalIgnoreCase) >= 0);
        string side = avanti ? "Avanti" : "Human";
        if (unit.TryGetComponent(out Squadron squadron)) return $"{side} {squadron.CraftRole}";
        if (unit.isShipyardBonus) return $"{side} Shipyard";
        if (unit.isBattleStation) return $"{side} Station";
        string type = unit.TryGetComponent(out SpaceUnit su) ? su.shipType.ToString() : "?";
        return unit.IsFreeUnit ? $"{side} {type} (yard guard)" : $"{side} {type}";
    }

    private static Line LineFor(UnitHealthManager target)
    {
        string from = sourceSet && source != null ? Name(source) : "(environment)";
        Weapon kind = sourceSet ? weapon : Weapon.None;
        var key = (from, kind, Name(target));
        if (!lines.TryGetValue(key, out Line line)) lines[key] = line = new Line();
        int minute = Mathf.FloorToInt((Time.time - StartedAt) / 60f);
        var tkey = (from, kind, minute);
        if (target != null) lastHit[target] = (from, kind);
        return line;
    }

    private static void Timeline(double amount)
    {
        string from = sourceSet && source != null ? Name(source) : "(environment)";
        var key = (from, sourceSet ? weapon : Weapon.None, Mathf.FloorToInt((Time.time - StartedAt) / 60f));
        timeline[key] = (timeline.TryGetValue(key, out double v) ? v : 0) + amount;
    }

    public static void Hull(UnitHealthManager target, float amount, bool destroyedHardpoint)
    {
        if (!Enabled || amount <= 0f || (sourceSet && weapon == Weapon.Breakup)) return;
        Line line = LineFor(target);
        line.hull += amount; line.hits++;
        if (destroyedHardpoint && target != null && target.GetComponent<Squadron>() != null) line.craftKills++;
        Timeline(amount);
    }

    public static void Shield(UnitHealthManager target, float amount)
    {
        if (!Enabled || amount <= 0f) return;
        Line line = LineFor(target);
        line.shield += amount; line.hits++;
        Timeline(amount);
    }

    // A warship, station or squadron is gone: the kill goes to whoever hit it last.
    public static void Died(UnitHealthManager victim)
    {
        if (!Enabled || victim == null) return;
        (string from, Weapon kind) = lastHit.TryGetValue(victim, out var who) ? who : ("(unknown)", Weapon.None);
        var key = (from, kind, Name(victim));
        if (!lines.TryGetValue(key, out Line line)) lines[key] = line = new Line();
        line.kills++;
        if (victim.GetComponent<Squadron>() == null) kills.Add($"{Time.time - StartedAt,6:F0}s  {Name(victim)} killed by {from} ({kind})");
    }

    public static string Report()
    {
        StringBuilder sb = new StringBuilder();
        foreach (string side in new[] { "Human", "Avanti", "(environment)" })
        {
            var mine = lines.Where(p => p.Key.from.StartsWith(side)).ToList();
            double total = mine.Sum(p => p.Value.hull + p.Value.shield);
            sb.AppendLine($"--- DAMAGE DEALT BY {side.ToUpper().Trim('(', ')')} ({total:F0} total: hull {mine.Sum(p => p.Value.hull):F0}, shields {mine.Sum(p => p.Value.shield):F0})");
            sb.AppendLine("   source / weapon                         hull      shield   share  hits  fired  kills craft");
            foreach (var g in mine.GroupBy(p => (p.Key.from, p.Key.weapon)).OrderByDescending(g => g.Sum(p => p.Value.hull + p.Value.shield)))
            {
                double d = g.Sum(p => p.Value.hull + p.Value.shield);
                int f = fired.TryGetValue(g.Key, out int n) ? n : 0;
                sb.AppendLine($"   {g.Key.from + " / " + g.Key.weapon,-38} {g.Sum(p => p.Value.hull),8:F0} {g.Sum(p => p.Value.shield),9:F0} {100 * d / System.Math.Max(1, total),6:F1}% {g.Sum(p => p.Value.hits),6} {(f > 0 ? f.ToString() : "-"),6} {g.Sum(p => p.Value.kills),5} {g.Sum(p => p.Value.craftKills),5}");
                foreach (var p in g.OrderByDescending(p => p.Value.hull + p.Value.shield).Take(4))
                    sb.AppendLine($"        -> {p.Key.to,-32} {p.Value.hull,8:F0} {p.Value.shield,9:F0}  kills {p.Value.kills}");
            }
        }
        sb.AppendLine("--- ANTI-SHIP GUN STATES (share of samples while enemy warships were on the field)");
        sb.AppendLine("   class                              out-ranged  idle-in-range  out-of-arc  blocked  swinging  firing   | out-ranged: nearest enemy at x range (median)");
        foreach (var p in gunStates.OrderBy(p => p.Key))
        {
            int[] c = p.Value;
            float total = Mathf.Max(1, c.Sum() - c[0]);
            string Pct(GunState s) => $"{100f * c[(int)s] / total,6:F0}%";
            string median = outRangedBy.TryGetValue(p.Key, out List<float> l) && l.Count > 0 ? $"{l.OrderBy(x => x).ElementAt(l.Count / 2):F2}x" : "-";
            sb.AppendLine($"   {p.Key,-34} {Pct(GunState.OutRanged),10} {Pct(GunState.IdleEnemyInRange),13} {Pct(GunState.OutOfArc),11} {Pct(GunState.Blocked),8} {Pct(GunState.Swinging),9} {Pct(GunState.Firing),7}   | {median}");
        }
        sb.AppendLine("--- MISSILES SHOT DOWN");
        foreach (var p in missilesDown.OrderByDescending(p => p.Value)) sb.AppendLine($"   {p.Value,5}  {p.Key}");
        sb.AppendLine("--- WHAT BLOCKS LINE OF SIGHT (share of blocked samples)");
        foreach (var p in blockers.OrderBy(p => p.Key))
        {
            int total = p.Value.Values.Sum();
            sb.AppendLine($"   {p.Key,-34} " + string.Join(", ", p.Value.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} {100f * x.Value / total:F0}%")));
        }
        sb.AppendLine("--- DAMAGE PER MINUTE (by source class, all weapons)");
        int minutes = timeline.Count > 0 ? timeline.Keys.Max(k => k.minute) + 1 : 0;
        foreach (var g in timeline.GroupBy(p => p.Key.from).OrderBy(g => g.Key))
        {
            sb.Append($"   {g.Key,-34}");
            for (int m = 0; m < minutes; m++) sb.Append($"{g.Where(p => p.Key.minute == m).Sum(p => p.Value),7:F0}");
            sb.AppendLine();
        }
        sb.AppendLine("--- KILLS (warships and stations)");
        foreach (string k in kills) sb.AppendLine("   " + k);
        return sb.ToString();
    }
}
