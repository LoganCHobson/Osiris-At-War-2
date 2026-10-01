using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class OsirisBattleSim : MonoBehaviour
{
    const string OutPath = @"C:\Users\logan\AppData\Local\Temp\claude\C--Users-logan\0d82d420-765c-41d7-9f58-025e8984458e\scratchpad\battle_sim.txt";
    public float timeScale = 6f;
    public float timeout = 240f;

    static readonly string[][] Matchups =
    {
        new[] { "Human/Human_FighterSquadron", "Avanti/Avanti_Corvette", "80" },
        new[] { "Human/Human_FighterSquadron", "Avanti/Avanti_Destroyer", "80" },
        new[] { "Human/Human_FighterSquadron", "Avanti/Avanti_Cruiser", "80" },
        new[] { "Human/Human_FighterSquadron", "Avanti/Avanti_Capital", "80" },
        new[] { "Avanti/Avanti_FighterSquadron", "Human/Human_Corvette", "80" },
        new[] { "Avanti/Avanti_FighterSquadron", "Human/Human_Destroyer", "80" },
        new[] { "Avanti/Avanti_FighterSquadron", "Human/Human_Cruiser", "80" },
        new[] { "Avanti/Avanti_FighterSquadron", "Human/Human_Battleship", "80" },
        new[] { "Human/Human_BomberSquadron", "Avanti/Avanti_Corvette", "80" },
        new[] { "Avanti/Avanti_BomberSquadron", "Human/Human_Corvette", "80" },
        new[] { "Avanti/Avanti_BomberSquadron", "Human/Human_Battleship", "80" },
        new[] { "Human/Human_Corvette", "Avanti/Avanti_Corvette", "100" },
        new[] { "Human/Human_Destroyer", "Avanti/Avanti_Destroyer", "110" },
        new[] { "Human/Human_Corvette*4", "Avanti/Avanti_Corvette*3", "105" },
    };

    IEnumerator Start()
    {
        yield return null;
        Time.timeScale = timeScale;
        var lines = new List<string>();
        Vector3 a = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 d = GameManager.Instance.defenderShipSpawnPoint.position;
        Vector3 centre = (a + d) / 2f;
        Vector3 axis = (d - a).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, axis);

        foreach (string[] m in Matchups)
        {
            float gap = float.Parse(m[2]);
            List<UnitHealthManager> left = SpawnGroup(m[0], centre - axis * gap / 2f, across, Quaternion.LookRotation(axis), true);
            List<UnitHealthManager> right = SpawnGroup(m[1], centre + axis * gap / 2f, across, Quaternion.LookRotation(-axis), false);
            float start = Time.time;
            yield return new WaitForSeconds(0.5f);
            float maxL = left.Sum(u => u.maxHealth), maxR = right.Sum(u => u.maxHealth);
            while (Time.time - start < timeout && left.Any(Alive) && right.Any(Alive))
                yield return new WaitForSeconds(0.5f);
            float t = Time.time - start;
            float pl = left.Where(Alive).Sum(u => u.currentHealth) / Mathf.Max(1f, maxL);
            float pr = right.Where(Alive).Sum(u => u.currentHealth) / Mathf.Max(1f, maxR);
            string winner = !left.Any(Alive) && right.Any(Alive) ? m[1] : !right.Any(Alive) && left.Any(Alive) ? m[0] : "draw";
            string line = $"SIM {Short(m[0]),-30} vs {Short(m[1]),-30} t={t,6:F1}s  left {pl * 100,4:F0}% ({left.Count(Alive)}/{left.Count} alive)  right {pr * 100,4:F0}% ({right.Count(Alive)}/{right.Count} alive)  winner={Short(winner)}";
            Debug.Log(line);
            lines.Add(line);
            foreach (var u in left.Concat(right)) if (u != null) Destroy(u.gameObject);
            foreach (var u in UnitHealthManager.Active.ToList()) if (u != null && u.battleToken == UnitHealthManager.FreeUnitToken) Destroy(u.gameObject);
            yield return new WaitForSeconds(2f);
        }

        File.WriteAllLines(OutPath, lines);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    static bool Alive(UnitHealthManager u) => u != null && !u.IsDead;

    static string Short(string s) => s.Contains("/") ? s.Substring(s.IndexOf('/') + 1) : s;

    List<UnitHealthManager> SpawnGroup(string spec, Vector3 centre, Vector3 across, Quaternion rot, bool attacker)
    {
        string key = spec;
        int count = 1;
        int star = spec.IndexOf('*');
        if (star > 0)
        {
            key = spec.Substring(0, star);
            count = int.Parse(spec.Substring(star + 1));
        }
        float spacing = key.Contains("Squadron") ? 25f : key.Contains("Corvette") || key.Contains("Destroyer") ? 32f : 48f;
        var units = new List<UnitHealthManager>();
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = centre + across * (i - (count - 1) / 2f) * spacing;
            units.Add(Spawn(key, pos, rot, attacker).GetComponent<UnitHealthManager>());
        }
        return units;
    }

    GameObject Spawn(string key, Vector3 pos, Quaternion rot, bool attacker)
    {
        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{key}.prefab");
#endif
        Ship ship = prefab.GetComponent<Ship>();
        if (ship != null) return GameManager.Instance.SpawnShip(ship, pos, rot, attacker, false);
        GameObject go = Instantiate(prefab, pos, rot);
        go.GetComponent<UnitHealthManager>().battleToken = UnitHealthManager.FreeUnitToken;
        GameManager.Instance.ApplyAllegiance(go, attacker);
        return go;
    }
}
