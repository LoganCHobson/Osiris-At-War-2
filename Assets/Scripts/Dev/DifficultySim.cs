using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Pits AI difficulty levels against each other in real battles to check that harder really is harder.
// Every matchup is played four times - each level gets each faction once, on each side once - with the
// same asteroid layout for both halves of a side-swap, so faction and map differences cancel out.
// Driven by DifficultySimRunner (Osiris > Run Difficulty Sim); writes a report and quits.
public class DifficultySim : MonoBehaviour
{
    public Faction factionA;
    public Faction factionB;
    public float simSpeed = 6f;
    public float battleTimeLimit = 420f;
    public string reportPath;
    [Tooltip("Grand Admiral self-play: only Grand Admiral vs Admiral, exploring, with records and win rates sent to the server.")]
    public bool selfPlay;
    public int selfPlayRounds = 10;
    [Tooltip("Only Grand Admiral vs Admiral (selfPlayRounds x 4 battles), offline: no model check, nothing reported.")]
    public bool grandAdmiralOnly;

    private static readonly (DifficultyLevel stronger, DifficultyLevel weaker)[] Matchups =
    {
        (DifficultyLevel.GrandAdmiral, DifficultyLevel.Admiral),
        (DifficultyLevel.Admiral, DifficultyLevel.Cadet),
        (DifficultyLevel.Admiral, DifficultyLevel.Captain),
        (DifficultyLevel.Captain, DifficultyLevel.Cadet)
    };

    private readonly StringBuilder report = new StringBuilder();
    private readonly List<AIBattleCommander> commanders = new List<AIBattleCommander>();

    private IEnumerator Start()
    {
        yield return null;
        if (selfPlay) yield return GrandAdmiralService.SelfPlayModel(); // The trial being tested, else the newest release.
        GameSpeed.SetSpeed(simSpeed);
        report.AppendLine($"DIFFICULTY SIM  {factionA?.name} vs {factionB?.name}  speed x{simSpeed}  limit {battleTimeLimit}s");

        Vector3 attackerStart = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 defenderStart = GameManager.Instance.defenderShipSpawnPoint.position;
        int battle = 0;
        // Self-play needs fresh maps every run, or the AI learns a handful of asteroid layouts by heart.
        // The difficulty comparison keeps its fixed seeds so runs stay comparable.
        int seedBase = selfPlay ? Random.Range(0, 1000000) : 1000;
        int gaGames = 0, gaWins = 0, gaDraws = 0;
        var matchups = new List<(DifficultyLevel stronger, DifficultyLevel weaker)>();
        if (selfPlay || grandAdmiralOnly)
        {
            for (int i = 0; i < selfPlayRounds; i++) matchups.Add((DifficultyLevel.GrandAdmiral, DifficultyLevel.Admiral));
        }
        else
        {
            matchups.AddRange(Matchups);
        }

        foreach ((DifficultyLevel stronger, DifficultyLevel weaker) in matchups)
        {
            int strongerWins = 0, weakerWins = 0, draws = 0;
            float marginTotal = 0f;

            for (int variant = 0; variant < 4; variant++)
            {
                bool strongerAttacks = variant % 2 == 0;
                bool strongerGetsA = variant < 2;
                int terrainSeed = seedBase + battle / 2 * 7;
                battle++;
                float wallStarted = Time.realtimeSinceStartup;
                Heartbeat = wallStarted;

                Clear();
                if (BattleTerrain.Instance != null) BattleTerrain.Instance.Regenerate(attackerStart, defenderStart, terrainSeed, true);
                yield return null;

                Faction strongFaction = strongerGetsA ? factionA : factionB;
                Faction weakFaction = strongerGetsA ? factionB : factionA;
                if (stronger == DifficultyLevel.GrandAdmiral || weaker == DifficultyLevel.GrandAdmiral)
                {
                    // Grand Admiral only ever plays Avanti: its records don't carry the faction, so a battle
                    // fought as Human would poison training. Sides still alternate attacker and defender.
                    Faction avanti = factionA != null && factionA.CanBeGrandAdmiral ? factionA : factionB != null && factionB.CanBeGrandAdmiral ? factionB : null;
                    if (avanti == null)
                    {
                        Debug.LogWarning("DIFFSIM skipping Grand Admiral: neither faction is Avanti");
                        continue;
                    }
                    Faction other = avanti == factionA ? factionB : factionA;
                    bool gaIsStronger = stronger == DifficultyLevel.GrandAdmiral;
                    strongFaction = gaIsStronger ? avanti : other;
                    weakFaction = gaIsStronger ? other : avanti;
                }
                if (variant % 2 == 0) battleBudget = Random.Range(MinBudget, MaxBudget + 1); // Side-swapped pairs share one.
                AIBattleCommander strong = Deploy(stronger, strongFaction, strongerAttacks);
                AIBattleCommander weak = Deploy(weaker, weakFaction, !strongerAttacks);

                yield return new WaitForSeconds(3f); // Let ships finish setting up their health first.
                float started = Time.time;
                float strongStart = Power(strongerAttacks, strong);
                float weakStart = Power(!strongerAttacks, weak);
                while (Time.time - started < battleTimeLimit)
                {
                    if (!SideAlive(strongerAttacks, strong) || !SideAlive(!strongerAttacks, weak)) break;
                    Heartbeat = Time.realtimeSinceStartup;
                    yield return new WaitForSeconds(1f);
                }

                float strongLeft = Power(strongerAttacks, strong) / Mathf.Max(1f, strongStart);
                float weakLeft = Power(!strongerAttacks, weak) / Mathf.Max(1f, weakStart);
                string outcome;
                if (strongLeft > weakLeft + 0.05f) { strongerWins++; outcome = stronger.ToString(); }
                else if (weakLeft > strongLeft + 0.05f) { weakerWins++; outcome = weaker.ToString(); }
                else { draws++; outcome = "draw"; }
                marginTotal += strongLeft - weakLeft;

                bool strongWon = outcome == stronger.ToString(), weakWon = outcome == weaker.ToString();
                strong.NotifyBattleEnded(strongWon, strong.Retreating, weak.Retreating);
                weak.NotifyBattleEnded(weakWon, weak.Retreating, strong.Retreating);
                if (stronger == DifficultyLevel.GrandAdmiral && weaker == DifficultyLevel.Admiral && strong.GrandAdmiralActive)
                {
                    gaGames++;
                    if (strongWon) gaWins++;
                    else if (!weakWon) gaDraws++;
                    // One machine-readable line per battle for the self-play dashboard (Loki parses it as JSON).
                    Debug.Log("GASTAT " + JsonUtility.ToJson(new BattleStat
                    {
                        model = GrandAdmiral.Model != null ? GrandAdmiral.Model.Version : 0,
                        result = strongWon ? "win" : weakWon ? "loss" : "draw",
                        side = strongerAttacks ? "attacker" : "defender",
                        game_seconds = Time.time - started,
                        wall_seconds = Time.realtimeSinceStartup - wallStarted,
                        ga_left = strongLeft,
                        admiral_left = weakLeft,
                        decisions = strong.GrandAdmiralRecord != null ? strong.GrandAdmiralRecord.Decisions.Count : 0,
                        terrain = terrainSeed,
                        budget = selfPlay ? battleBudget : 0,
                        self_play = selfPlay,
                    }));
                }

                report.AppendLine($"  {stronger,-8}({strongFaction?.name}, {(strongerAttacks ? "attacker" : "defender")}) vs {weaker,-8}({weakFaction?.name})  "
                    + $"left {strongLeft:P0} vs {weakLeft:P0}  {Time.time - started:F0}s  terrain {terrainSeed}  winner={outcome}");
                Debug.Log($"DIFFSIM {report.ToString().Split('\n')[^2]}");
            }

            report.AppendLine($"== {stronger} vs {weaker}: {strongerWins} - {weakerWins} ({draws} draws), average margin {marginTotal / 4f:+0%;-0%}");
            report.AppendLine();
        }

        Clear();
        GameSpeed.Reset();
        if (gaGames > 0 && GrandAdmiral.Model != null)
        {
            report.AppendLine($"GRAND ADMIRAL v{GrandAdmiral.Model.Version} vs Admiral: {gaWins} wins, {gaDraws} draws, {gaGames} games");
            if (selfPlay) yield return GrandAdmiralService.ReportArena(GrandAdmiral.Model.Version, "admiral", gaGames, gaWins, gaDraws);
        }
        if (selfPlay) yield return GrandAdmiralService.WaitForUploads();
        string text = report.ToString();
        Debug.Log("DIFFSIM REPORT\n" + text);
        if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, text);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

    [System.Serializable]
    private class BattleStat
    {
        public int model;
        public string result, side;
        public float game_seconds, wall_seconds, ga_left, admiral_left;
        public int decisions, terrain, budget;
        public bool self_play;
    }

    // ---- command line (shared by the editor runner and the standalone self-play player) ----

    // Last time a battle made progress (real seconds). The watchdog ends a stuck process so whatever
    // runs it (a Kubernetes pod, the home script) can start a fresh one.
    public static float Heartbeat;
    private const float StallSeconds = 900f;

    public void Configure()
    {
        string speed = Argument("-simSpeed", ""), limit = Argument("-simLimit", ""), rounds = Argument("-selfPlayRounds", "");
        if (float.TryParse(speed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s)) simSpeed = s;
        if (float.TryParse(limit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float l)) battleTimeLimit = l;
        if (int.TryParse(rounds, out int r)) selfPlayRounds = Mathf.Clamp(r, 1, 250);
        selfPlay = HasFlag("-selfPlay");
        grandAdmiralOnly = HasFlag("-grandAdmiralOnly");
        string report = Argument("-simReport", "");
        if (!string.IsNullOrEmpty(report)) reportPath = report;
    }

    public static bool HasFlag(string name) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;

    public static string Argument(string name, string fallback)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return fallback;
    }

    // In a standalone player (the self-play farm), -selfPlay or -grandAdmiralOnly turns the battle scene
    // into the sim. The factions come from the asset SelfPlayBuild fills in (no AssetDatabase in a player).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootFromCommandLine()
    {
        if (Application.isEditor || (!HasFlag("-selfPlay") && !HasFlag("-grandAdmiralOnly"))) return;
        SiegeRecordingAssets assets = Resources.Load<SiegeRecordingAssets>("Dev/SiegeRecordingAssets");
        if (assets == null || assets.titans == null || assets.avanti == null || GameManager.Instance == null)
        {
            Debug.LogError("DIFFSIM can't start: this build has no faction assets or no battle scene");
            Application.Quit(2);
            return;
        }
        Application.runInBackground = true;
        Application.targetFrameRate = -1;
        // The farm's logs go to the cluster's Loki: plain lines without a stack trace each (errors keep theirs).
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
        DifficultySim sim = new GameObject("DifficultySim").AddComponent<DifficultySim>();
        sim.factionA = assets.titans;
        sim.factionB = assets.avanti;
        sim.Configure();

        Heartbeat = Time.realtimeSinceStartup;
        System.Threading.Thread watchdog = new System.Threading.Thread(() =>
        {
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            float offset = Heartbeat;
            while (true)
            {
                System.Threading.Thread.Sleep(30000);
                float now = offset + (float)clock.Elapsed.TotalSeconds;
                if (now - Heartbeat > StallSeconds)
                {
                    System.Console.Error.WriteLine($"DIFFSIM watchdog: no progress for {StallSeconds:F0}s, exiting");
                    System.Environment.Exit(3);
                }
            }
        }) { IsBackground = true, Name = "DifficultySim watchdog" };
        watchdog.Start();
    }

    private AIBattleCommander Deploy(DifficultyLevel level, Faction faction, bool attackerSide)
    {
        GameObject go = new GameObject($"Sim_{level}_{(attackerSide ? "Attacker" : "Defender")}");
        AIBattleCommander commander = go.AddComponent<AIBattleCommander>();
        if (level == DifficultyLevel.GrandAdmiral && (faction == null || !faction.CanBeGrandAdmiral)) Debug.LogError($"DIFFSIM Grand Admiral deployed as {faction?.name}; it must be Avanti");
        commander.useDifficultyOverride = true;
        commander.difficultyOverride = DifficultyProfile.For(level);
        commander.SelfPlay = selfPlay && level == DifficultyLevel.GrandAdmiral;

        Transform spawn = attackerSide ? GameManager.Instance.attackerShipSpawnPoint : GameManager.Instance.defenderShipSpawnPoint;
        List<Ship> fleet = selfPlay ? EqualCostFleet(faction, battleBudget) : Fleet(faction);
        int population = selfPlay ? 999 : GameManager.Instance.populationCap; // Equal cost, not equal population.
        commander.Initialize(faction, attackerSide, fleet.ConvertAll(ship => ShipEntry.New(ship, faction)), spawn, population);
        commanders.Add(commander);
        return commander;
    }

    // The same recipe for every faction: a balanced battle group plus a reserve for reinforcements.
    // Self-play fights at equal cost: both fleets are bought with the same budget (a fresh one every battle),
    // each a random mix of its own roster in the usual role proportions. Fixed per-role counts would hand
    // the pricier Avanti ships an edge so large that every model wins and self-play can't tell them apart.
    private int battleBudget;
    private const int MinBudget = 1500, MaxBudget = 3500;

    private static List<Ship> EqualCostFleet(Faction faction, int budget)
    {
        List<Ship> fleet = new List<Ship>();
        if (faction == null) return fleet;
        List<(Ship ship, float weight)> options = new List<(Ship, float)>();
        foreach (Ship ship in faction.roster)
        {
            if (ship == null || ship.cost <= 0) continue;
            float weight = ship.fleetRole switch { FleetRole.Line => 3f, FleetRole.Screen => 4f, FleetRole.Carrier => 1f, _ => 2f };
            options.Add((ship, weight));
        }
        if (options.Count == 0) return Fleet(faction);
        int spent = 0, cheapest = options.Min(o => o.ship.cost);
        while (budget - spent >= cheapest)
        {
            List<(Ship ship, float weight)> affordable = options.FindAll(o => o.ship.cost <= budget - spent);
            float roll = Random.value * affordable.Sum(o => o.weight);
            foreach ((Ship ship, float weight) in affordable)
            {
                roll -= weight;
                if (roll > 0f) continue;
                fleet.Add(ship);
                spent += ship.cost;
                break;
            }
        }
        return fleet;
    }

    private static List<Ship> Fleet(Faction faction)
    {
        List<Ship> fleet = new List<Ship>();
        if (faction == null) return fleet;

        foreach (Ship ship in faction.roster)
        {
            if (ship == null) continue;
            int count = ship.fleetRole switch
            {
                FleetRole.Line => 3,
                FleetRole.Screen => 4,
                FleetRole.Carrier => 1,
                _ => 2
            };
            for (int i = 0; i < count; i++) fleet.Add(ship);
        }
        return fleet;
    }

    private static bool SideAlive(bool attackerSide, AIBattleCommander commander)
    {
        if (commander != null && commander.ReserveCount > 0) return true;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit != null && !unit.IsDead && !unit.IsDefense && unit.isAttackerSide == attackerSide) return true;
        }
        return false;
    }

    // Combat power still standing on a side: live ships weighted by health, plus the unspent reserve.
    private static float Power(bool attackerSide, AIBattleCommander commander)
    {
        float power = 0f;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead || unit.IsDefense || unit.isAttackerSide != attackerSide || unit.sourceShip == null) continue;
            float health = unit.maxHealth > 0f ? Mathf.Clamp01(unit.currentHealth / unit.maxHealth) : 1f;
            power += unit.sourceShip.combatPower * health;
        }
        if (commander != null) power += commander.ReservePower;
        return power;
    }

    private void Clear()
    {
        foreach (AIBattleCommander commander in commanders)
        {
            if (commander != null) Destroy(commander.gameObject);
        }
        commanders.Clear();

        foreach (UnitHealthManager unit in new List<UnitHealthManager>(UnitHealthManager.Active))
        {
            if (unit != null && !unit.IsDefense) Destroy(unit.gameObject);
        }
        foreach (WreckChunk wreck in FindObjectsByType<WreckChunk>(FindObjectsSortMode.None))
        {
            if (wreck != null) Destroy(wreck.gameObject);
        }
    }
}
