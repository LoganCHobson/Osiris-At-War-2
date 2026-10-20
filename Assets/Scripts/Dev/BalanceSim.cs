using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

// Dev balance suite: cost-matched Human vs Avanti fights (balanced, line-heavy, escort-heavy and carrier
// fleets, class-vs-class duels, strike craft against ships), each run with the factions swapping sides so
// neither gets a positional edge. Ships close to gun range on their nearest enemy. Asteroids are cleared and
// wrecks swept between fights. Writes one line per
// fight and a summary per matchup (Human share of the surviving value, 50% = even) to -balanceOut.
// Run with -executeMethod BalanceSimRunner.Run (or BuildAndRun to rebuild the ships first).
public class BalanceSim : MonoBehaviour
{
    public float timeScale = 6f;
    public float timeout = 240f;
    public int repeats = 4;

    private class Matchup
    {
        public string name;
        public string human;
        public string avanti;
        public string expect; // What a healthy result looks like, for the report.
    }

    // Fleets as "Class*count,..." - costs are matched as closely as the price lists allow.
    private static readonly Matchup[] Matchups =
    {
        new Matchup { name = "Balanced fleets", human = "Battleship*2,Cruiser*2,Destroyer*3,Corvette*4", avanti = "Capital*1,Cruiser*2,Destroyer*2,Corvette*2", expect = "even" },
        new Matchup { name = "Balanced fleets B", human = "Battleship*1,Cruiser*3,Destroyer*3,Corvette*3", avanti = "Capital*1,Cruiser*1,Destroyer*3,Corvette*1", expect = "even" },
        new Matchup { name = "Line-heavy fleets", human = "Battleship*4,Cruiser*4", avanti = "Capital*2,Cruiser*3", expect = "even" },
        new Matchup { name = "Escort-heavy fleets", human = "Destroyer*7,Corvette*7", avanti = "Destroyer*4,Corvette*5", expect = "even" },
        new Matchup { name = "Carrier groups", human = "Carrier*2,Destroyer*3,Corvette*2", avanti = "Carrier*1,Destroyer*2,Corvette*2", expect = "even" },
        new Matchup { name = "Corvette duel", human = "Corvette*5", avanti = "Corvette*3", expect = "even" },
        new Matchup { name = "Destroyer duel", human = "Destroyer*5", avanti = "Destroyer*3", expect = "even" },
        new Matchup { name = "Cruiser duel", human = "Cruiser*6", avanti = "Cruiser*4", expect = "even" },
        new Matchup { name = "Battleship vs Capital", human = "Battleship*5", avanti = "Capital*3", expect = "even" },
        new Matchup { name = "Human bombers vs Avanti line", human = "BomberSquadron*6", avanti = "Capital*1,Cruiser*2", expect = "bombers win" },
        new Matchup { name = "Avanti bombers vs Human line", human = "Battleship*2,Cruiser*4", avanti = "BomberSquadron*5", expect = "bombers win" },
        new Matchup { name = "Human bombers vs Avanti escorts", human = "BomberSquadron*6", avanti = "Corvette*6", expect = "escorts win" },
        new Matchup { name = "Avanti bombers vs Human escorts", human = "Corvette*11", avanti = "BomberSquadron*5", expect = "escorts win" },
    };

    private readonly StringBuilder report = new StringBuilder();
    private string outPath;

    private IEnumerator Start()
    {
        outPath = Argument("-balanceOut", Path.Combine(Application.dataPath, "..", "Logs", "balance.txt"));
        string only = Argument("-balanceOnly", "");
        repeats = int.Parse(Argument("-balanceRepeats", repeats.ToString()));

        yield return null;
        ClearTerrain();
        Time.timeScale = timeScale;

        Vector3 a = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 d = GameManager.Instance.defenderShipSpawnPoint.position;
        Vector3 centre = (a + d) / 2f;
        Vector3 axis = (d - a).normalized;

        foreach (Matchup m in Matchups.Where(m => only == "" || m.name.Contains(only)))
        {
            report.AppendLine($"== {m.name}  (Human ${Cost("Human", m.human)} vs Avanti ${Cost("Avanti", m.avanti)}, expect {m.expect})");
            List<float> shares = new List<float>();
            int humanWins = 0, avantiWins = 0;
            for (int r = 0; r < repeats; r++)
            {
                bool humanAttacks = r % 2 == 0;
                List<UnitHealthManager> human = SpawnFleet("Human", m.human, centre, humanAttacks ? axis : -axis, humanAttacks);
                List<UnitHealthManager> avanti = SpawnFleet("Avanti", m.avanti, centre, humanAttacks ? -axis : axis, !humanAttacks);
                float humanValue = Value(human), avantiValue = Value(avanti);

                float start = Time.time;
                yield return new WaitForSeconds(0.5f);
                float nextOrders = 0f;
                Dictionary<UnitHealthManager, float> diedAt = new Dictionary<UnitHealthManager, float>();
                int[] gunsBearing = new int[2], gunsWithTarget = new int[2], gunsTotal = new int[2]; // [Human, Avanti]
                while (Time.time - start < timeout && human.Any(Alive) && avanti.Any(Alive))
                {
                    foreach (UnitHealthManager unit in human.Concat(avanti))
                    {
                        if (!Alive(unit) && !diedAt.ContainsKey(unit)) diedAt[unit] = Time.time - start;
                    }
                    if (Time.time >= nextOrders)
                    {
                        nextOrders = Time.time + 2f;
                        SampleGuns(human, 0, gunsBearing, gunsWithTarget, gunsTotal);
                        SampleGuns(avanti, 1, gunsBearing, gunsWithTarget, gunsTotal);
                        Close(human, avanti);
                        Close(avanti, human);
                    }
                    yield return new WaitForSeconds(0.5f);
                }

                float hLeft = Remaining(human) / Mathf.Max(1f, humanValue);
                float aLeft = Remaining(avanti) / Mathf.Max(1f, avantiValue);
                float share = hLeft + aLeft > 0f ? hLeft / (hLeft + aLeft) : 0.5f;
                shares.Add(share);
                string winner = !avanti.Any(Alive) && human.Any(Alive) ? "Human" : !human.Any(Alive) && avanti.Any(Alive) ? "Avanti" : "draw";
                if (winner == "Human") humanWins++;
                if (winner == "Avanti") avantiWins++;
                report.AppendLine($"   {(humanAttacks ? "H atk" : "A atk")}  t={Time.time - start,5:F0}s  Human {hLeft,4:P0} left  Avanti {aLeft,4:P0} left  -> {winner}");
                if (r == 0) // Casualty order for the first fight, to see how it played out.
                {
                    string Name(UnitHealthManager u) => (u.sourceShip != null ? u.sourceShip.name : "?").Replace("Squadron", "Sq");
                    report.AppendLine("      deaths: " + string.Join(", ", diedAt.OrderBy(d => d.Value).Select(d => $"{Name(d.Key)}@{d.Value:F0}s")));
                    string Share(int side) => gunsTotal[side] == 0 ? "-" : $"{(float)gunsWithTarget[side] / gunsTotal[side]:P0} have a target, {(float)gunsBearing[side] / gunsTotal[side]:P0} can fire on it";
                    report.AppendLine($"      main guns: Human {Share(0)}; Avanti {Share(1)}");
                }

                yield return Cleanup();
            }
            report.AppendLine($"   RESULT Human share {shares.Average():P0}  (Human wins {humanWins}, Avanti wins {avantiWins}, draws {repeats - humanWins - avantiWins})");
            File.WriteAllText(outPath, report.ToString());
        }

        File.WriteAllText(outPath, report.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    // Surviving value: each live unit's cost times its remaining health.
    private static float Remaining(List<UnitHealthManager> fleet) =>
        fleet.Where(Alive).Sum(u => (u.sourceShip != null ? u.sourceShip.cost : 100f) * Mathf.Clamp01(u.currentHealth / Mathf.Max(1f, u.maxHealth)));

    private static float Value(List<UnitHealthManager> fleet) => fleet.Sum(u => u.sourceShip != null ? u.sourceShip.cost : 100f);

    private static bool Alive(UnitHealthManager u) => u != null && !u.IsDead;

    private static int Cost(string faction, string spec) =>
        Parse(spec).Sum(p => { Ship ship = Load(faction, p.key); return ship != null ? ship.cost * p.count : 0; });

    private static IEnumerable<(string key, int count)> Parse(string spec) =>
        spec.Split(',').Select(part => part.Split('*')).Select(bits => (bits[0].Trim(), bits.Length > 1 ? int.Parse(bits[1]) : 1));

    private static Ship Load(string faction, string key)
    {
#if UNITY_EDITOR
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{faction}/{faction}_{key}.prefab");
        return prefab != null ? prefab.GetComponent<Ship>() : null;
#else
        return null;
#endif
    }

    private List<UnitHealthManager> SpawnFleet(string faction, string spec, Vector3 centre, Vector3 forward, bool attacker)
    {
        List<Ship> ships = new List<Ship>();
        foreach ((string key, int count) in Parse(spec))
        {
            Ship ship = Load(faction, key);
            if (ship == null) { report.AppendLine($"   missing {faction}_{key}"); continue; }
            for (int i = 0; i < count; i++) ships.Add(ship);
        }

        // Fronts 110 apart, like two fleets that have just closed to fighting range.
        Vector3 front = centre - forward * 55f;
        List<UnitHealthManager> units = new List<UnitHealthManager>();
        foreach (BattleDeployment.Placement placement in BattleDeployment.Arrange(ships, BattleDeployment.ChooseLead(ships), front, forward))
        {
            GameObject go = GameManager.Instance.SpawnShip(placement.ship, placement.position, Quaternion.LookRotation(forward), attacker, false);
            if (go == null) continue;
            if (go.TryGetComponent(out NavMeshAgent agent) && agent.isOnNavMesh) agent.ResetPath();
            units.Add(go.GetComponent<UnitHealthManager>());
        }
        return units;
    }

    // Each ship closes on its nearest enemy until it's inside its own gun range, as ships do in a real
    // battle - otherwise lines left out of each other's reach just sit there.
    private static void Close(List<UnitHealthManager> fleet, List<UnitHealthManager> enemy)
    {
        foreach (UnitHealthManager unit in fleet.Where(Alive))
        {
            if (!unit.TryGetComponent(out NavMeshAgent agent) || !agent.isOnNavMesh || unit.TryGetComponent(out Squadron _)) continue;

            UnitHealthManager target = enemy.Where(Alive).OrderBy(e => (e.transform.position - unit.transform.position).sqrMagnitude).FirstOrDefault();
            if (target == null) continue;

            float range = unit.TryGetComponent(out SpaceUnit space) && space.maxRange > 0f ? space.maxRange : 120f;
            Vector3 offset = unit.transform.position - target.transform.position;
            offset.y = 0f;
            if (offset.magnitude > range * 0.85f) agent.SetDestination(target.transform.position + offset.normalized * range * 0.75f);
            else if (agent.hasPath) agent.ResetPath();
        }
    }

    // How many main guns have a target, and how many of those are turned so they can fire on it.
    private static void SampleGuns(List<UnitHealthManager> fleet, int side, int[] bearing, int[] withTarget, int[] total)
    {
        foreach (UnitHealthManager unit in fleet.Where(Alive))
        {
            foreach (TurretController gun in unit.GetComponentsInChildren<TurretController>())
            {
                if (gun.targetFilter == TurretController.TargetFilter.FightersOnly) continue;
                total[side]++;
                if (gun.target == null) continue;
                withTarget[side]++;
                if (gun.InArc(gun.target.position)) bearing[side]++;
            }
        }
    }

    private IEnumerator Cleanup()
    {
        foreach (UnitHealthManager unit in UnitHealthManager.Active.ToList()) if (unit != null) Destroy(unit.gameObject);
        foreach (WreckChunk wreck in FindObjectsByType<WreckChunk>(FindObjectsSortMode.None)) Destroy(wreck.gameObject);
        yield return new WaitForSeconds(10f); // Let stray missiles burn out before the next fight.
    }

    private static void ClearTerrain()
    {
        foreach (AsteroidField field in FindObjectsByType<AsteroidField>(FindObjectsSortMode.None)) Destroy(field.gameObject);
    }

    private static string Argument(string name, string fallback)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
