using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Dev: dumps every balance-relevant number from the faction ship and station prefabs (batchmode:
// -executeMethod BalanceReport.Run -reportPath file). Damage per second is per the prefab values; the game
// then applies battery cadence (same DPS), the per-faction health scale and the doctrine table on spawn.
public static class BalanceReport
{
    [MenuItem("Osiris/Dev/Balance Report")]
    public static void Run()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-reportPath");
        string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine("Logs", "balance_report.txt");

        StringBuilder sb = new StringBuilder();
        string[] folders = { "Assets/Prefabs/Osiris/Human", "Assets/Prefabs/Osiris/Avanti" };
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders).Concat(AssetDatabase.FindAssets("BattleStation t:Prefab", new[] { "Assets/Prefabs" })).Distinct())
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) continue;
            Describe(prefab, assetPath, sb);
        }
        File.WriteAllText(path, sb.ToString());
        Debug.Log("Balance report written to " + path);
    }

    // Dev: which renderers each LOD level of a prefab uses, and where they sit relative to HullDamage's gfxRoot.
    public static void LodReport()
    {
        StringBuilder sb = new StringBuilder();
        foreach (string name in new[] { "Human/Human_Carrier", "Human/Human_Battleship", "Avanti/Avanti_Carrier" })
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{name}.prefab");
            if (prefab == null) continue;
            HullDamage hull = prefab.GetComponent<HullDamage>();
            Transform gfx = hull != null ? hull.gfxRoot : null;
            sb.AppendLine($"=== {name}  gfxRoot={(gfx != null ? PathOf(gfx, prefab.transform) : "none")}");
            foreach (LODGroup group in prefab.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                sb.AppendLine($"  LODGroup on {PathOf(group.transform, prefab.transform)} size {group.size}");
                for (int i = 0; i < lods.Length; i++)
                {
                    int inside = lods[i].renderers.Count(r => r != null && gfx != null && r.transform.IsChildOf(gfx));
                    sb.AppendLine($"    LOD{i} height {lods[i].screenRelativeTransitionHeight}: {lods[i].renderers.Length} renderers, {inside} under gfxRoot; e.g. " +
                        string.Join(", ", lods[i].renderers.Where(r => r != null).Take(4).Select(r => PathOf(r.transform, prefab.transform))));
                }
            }
        }
        File.WriteAllText(Path.Combine("Logs", "lod_report.txt"), sb.ToString());
    }

    private static string PathOf(Transform t, Transform root)
    {
        string path = t.name;
        while (t.parent != null && t != root) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    private static void Describe(GameObject prefab, string path, StringBuilder sb)
    {
        Ship ship = prefab.GetComponent<Ship>();
        SpaceUnit unit = prefab.GetComponent<SpaceUnit>();
        UnityEngine.AI.NavMeshAgent agent = prefab.GetComponent<UnityEngine.AI.NavMeshAgent>();
        sb.AppendLine($"=== {prefab.name}  ({path})");
        if (ship != null) sb.AppendLine($"  ship: role {ship.fleetRole} cost {ship.cost} build {ship.buildTime}s power {ship.combatPower} pop {ship.populationCost}");
        if (unit != null) sb.AppendLine($"  type {unit.shipType} maxRange {unit.maxRange}");
        if (agent != null) sb.AppendLine($"  agent speed {agent.speed} accel {agent.acceleration} angular {agent.angularSpeed}");

        HardpointHealth[] hardpoints = prefab.GetComponentsInChildren<HardpointHealth>(true);
        float hp = hardpoints.Sum(h => h.maxHealth);
        sb.AppendLine($"  hardpoints {hardpoints.Length} total hp {hp:0}: " + string.Join(", ", hardpoints.Select(h => $"{h.name}={h.maxHealth:0}")));

        ShieldGenerator shield = prefab.GetComponent<ShieldGenerator>();
        if (shield != null) sb.AppendLine($"  shield capacity {shield.capacityFraction} x hull, hull x{shield.hullHealthMultiplier}, recharge {shield.rechargeRate}/s after {shield.rechargeDelay}s");

        float shipDps = 0f, pdDps = 0f;
        foreach (TurretController t in prefab.GetComponentsInChildren<TurretController>(true))
        {
            float dps = t.firingPoints.Count * t.damage / Mathf.Max(0.02f, t.fireRate);
            bool pd = t.targetFilter == TurretController.TargetFilter.FightersOnly;
            if (pd) pdDps += dps; else shipDps += dps;
            sb.AppendLine($"    turret {t.name} [{t.targetFilter}] barrels {t.firingPoints.Count} dmg {t.damage} every {t.fireRate}s range {t.range} acc {t.baseAccuracy} speed {t.projectileSpeed} -> {dps:0.0} dps");
        }
        sb.AppendLine($"  guns: anti-ship {shipDps:0.0} dps, point defence {pdDps:0.0} dps");

        foreach (HangarHardpoint hangar in prefab.GetComponentsInChildren<HangarHardpoint>(true))
            sb.AppendLine($"  hangar {hangar.name}: every {hangar.launchInterval}s, " + string.Join(", ", hangar.complement.Select(b => $"{b.count}x {(b.squadron != null ? b.squadron.name : "?")}")));

        Fighter[] craft = prefab.GetComponentsInChildren<Fighter>(true);
        if (craft.Length > 0)
        {
            Fighter f = craft[0];
            sb.AppendLine($"  craft {craft.Length}: speed {f.speed} turn {f.turnRate} shipDmg {f.shipDamage} fighterDmg {f.fighterDamage} every {f.fireRate}s range {f.fireRange}");
        }
        sb.AppendLine();
    }
}
