using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

public class ShieldBalanceSim : MonoBehaviour
{
    public float timeScale = 6f;
    public float timeout = 240f;
    public int repeats = 2;

    static readonly (string human, string avanti, float gap)[] Matchups =
    {
        ("Human/Human_Destroyer", "Avanti/Avanti_Destroyer", 110f),
        ("Human/Human_Cruiser", "Avanti/Avanti_Cruiser", 120f),
        ("Human/Human_Battleship", "Avanti/Avanti_Capital", 140f),
        ("Human/Human_Corvette*4", "Avanti/Avanti_Corvette*3", 105f),
        ("Human/Human_Cruiser+Human/Human_Destroyer*2+Human/Human_Corvette*2", "Avanti/Avanti_Cruiser+Avanti/Avanti_Destroyer*2+Avanti/Avanti_Corvette*2", 130f),
    };

    private readonly StringBuilder report = new StringBuilder();

    IEnumerator Start()
    {
        string output = Argument("-captureDir", Application.temporaryCachePath);
        if (float.TryParse(Argument("-capacity", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float capacity)) capacityOverride = capacity;
        if (float.TryParse(Argument("-hull", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hull)) hullOverride = hull;

        yield return null;
        Time.timeScale = timeScale;

        Vector3 a = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 d = GameManager.Instance.defenderShipSpawnPoint.position;
        Vector3 centre = (a + d) / 2f;
        Vector3 axis = (d - a).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, axis);

        report.AppendLine($"capacityOverride={capacityOverride} hullOverride={hullOverride}");

        foreach ((string human, string avanti, float gap) in Matchups)
        {
            foreach (bool shields in new[] { false, true })
            {
                for (int r = 0; r < repeats; r++)
                {
                    List<UnitHealthManager> left = SpawnGroup(human, centre - axis * gap / 2f, across, axis, true, false);
                    List<UnitHealthManager> right = SpawnGroup(avanti, centre + axis * gap / 2f, across, -axis, false, shields);
                    yield return new WaitForSeconds(0.5f);

                    Order(left, centre - axis * 20f);
                    Order(right, centre + axis * 20f);

                    float leftMax = left.Sum(u => u.maxHealth);
                    float rightMax = right.Sum(u => u.maxHealth);
                    float start = Time.time;
                    while (Time.time - start < timeout && left.Any(Alive) && right.Any(Alive)) yield return new WaitForSeconds(0.5f);

                    float elapsed = Time.time - start;
                    float leftLeft = left.Where(Alive).Sum(u => u.currentHealth) / Mathf.Max(1f, leftMax);
                    float rightLeft = right.Where(Alive).Sum(u => u.currentHealth) / Mathf.Max(1f, rightMax);
                    string winner = !left.Any(Alive) && right.Any(Alive) ? "AVANTI" : !right.Any(Alive) && left.Any(Alive) ? "HUMAN" : "draw";
                    report.AppendLine($"{Short(human),-40} vs {Short(avanti),-40} shields={(shields ? "ON " : "off")} t={elapsed,6:F1}s human={leftLeft,4:P0} avanti={rightLeft,4:P0} winner={winner}");

                    foreach (UnitHealthManager unit in UnitHealthManager.Active.ToList()) if (unit != null) Destroy(unit.gameObject);
                    foreach (WreckChunk chunk in FindObjectsByType<WreckChunk>(FindObjectsSortMode.None)) Destroy(chunk.gameObject);
                    yield return new WaitForSeconds(1.5f);
                }
            }
        }

        File.WriteAllText(Path.Combine(output, "balance.txt"), report.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    private float capacityOverride = -1f;
    private float hullOverride = -1f;

    List<UnitHealthManager> SpawnGroup(string spec, Vector3 centre, Vector3 across, Vector3 facing, bool attacker, bool shields)
    {
        List<string> keys = new List<string>();
        foreach (string part in spec.Split('+'))
        {
            int star = part.IndexOf('*');
            string key = star > 0 ? part.Substring(0, star) : part;
            int count = star > 0 ? int.Parse(part.Substring(star + 1)) : 1;
            for (int i = 0; i < count; i++) keys.Add(key);
        }

        List<UnitHealthManager> units = new List<UnitHealthManager>();
        for (int i = 0; i < keys.Count; i++)
        {
            Vector3 position = centre + across * (i - (keys.Count - 1) * 0.5f) * 38f;
            GameObject spawned = Spawn(keys[i], position, Quaternion.LookRotation(facing), attacker, shields);
            if (spawned != null) units.Add(spawned.GetComponent<UnitHealthManager>());
        }
        return units;
    }

    GameObject Spawn(string key, Vector3 position, Quaternion rotation, bool attacker, bool shields)
    {
        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{key}.prefab");
#endif
        Ship ship = prefab.GetComponent<Ship>();
        GameObject spawned = GameManager.Instance.SpawnShip(ship, position, rotation, attacker, false);

        ShieldGenerator shield = spawned.GetComponent<ShieldGenerator>();
        if (shield != null)
        {
            float applied = shield.hullHealthMultiplier;
            float wanted = shields ? (hullOverride > 0f ? hullOverride : applied) : 1f;
            foreach (HardpointHealth hardpoint in spawned.GetComponentsInChildren<HardpointHealth>(true)) hardpoint.maxHealth *= wanted / applied;

            if (shields)
            {
                if (capacityOverride >= 0f) shield.capacityFraction = capacityOverride;
            }
            else
            {
                DestroyImmediate(shield);
            }
        }
        return spawned;
    }

    static void Order(List<UnitHealthManager> units, Vector3 destination)
    {
        foreach (UnitHealthManager unit in units)
        {
            if (unit != null && unit.TryGetComponent(out NavMeshAgent agent) && agent.isOnNavMesh) agent.SetDestination(destination);
        }
    }

    static bool Alive(UnitHealthManager unit) => unit != null && !unit.IsDead;

    static string Short(string spec) => string.Join("+", spec.Split('+').Select(p => p.Substring(p.IndexOf('/') + 1)));

    static string Argument(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
