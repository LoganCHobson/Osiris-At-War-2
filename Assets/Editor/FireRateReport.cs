using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Dev check: every gun on every warship - fire interval, damage, range, shot speed, barrels.
public static class FireRateReport
{
    public static void Run()
    {
        StringBuilder text = new StringBuilder();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Osiris/Human", "Assets/Prefabs/Osiris/Avanti" }))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null || prefab.name.Contains("Squadron")) continue;
            var guns = prefab.GetComponentsInChildren<TurretController>(true);
            text.AppendLine($"=== {prefab.name}: {guns.Length} guns");
            foreach (var g in guns.GroupBy(t => $"{t.targetFilter} every {t.fireRate:0.###}s dmg {t.damage} range {t.range} speed {t.projectileSpeed} barrels {t.firingPoints.Count}"))
            {
                float shotsPerSecond = g.Sum(t => Mathf.Max(1, t.firingPoints.Count) / Mathf.Max(0.01f, t.fireRate));
                text.AppendLine($"   {g.Count()}x {g.Key}   -> ship total {shotsPerSecond:0.#} shots/s, {g.Sum(t => t.damage / Mathf.Max(0.01f, t.fireRate)):0.#} dps");
            }
        }
        File.WriteAllText(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Logs", "fire_rates.txt"), text.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
