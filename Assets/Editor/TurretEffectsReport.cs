using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

// Dev: the effect objects inside the shared turret prefab and what triggers them (batchmode:
// -executeMethod TurretEffectsReport.Run).
public static class TurretEffectsReport
{
    public static void Run()
    {
        StringBuilder sb = new StringBuilder();
        foreach (string path in new[] { "Assets/Prefabs/Turret.prefab", "Assets/Prefabs/BattleStation.prefab" })
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            sb.AppendLine($"=== {path}");
            foreach (ParticleSystem ps in prefab.GetComponentsInChildren<ParticleSystem>(true).Where(p => p.transform.parent == null || p.transform.parent.GetComponent<ParticleSystem>() == null))
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(ps.gameObject);
                sb.AppendLine($"  effect root {Path(ps.transform, prefab.transform)}  active {ps.gameObject.activeSelf}  loop {ps.main.loop}  playOnAwake {ps.main.playOnAwake}  source {(source != null ? AssetDatabase.GetAssetPath(source) : "-")}  children {ps.GetComponentsInChildren<ParticleSystem>(true).Length}");
            }
            foreach (TurretController t in prefab.GetComponentsInChildren<TurretController>(true)) Events(sb, $"onFire {t.name}", t.onFire, prefab.transform);
            foreach (HardpointHealth h in prefab.GetComponentsInChildren<HardpointHealth>(true)) { Events(sb, $"die {h.name}", h.die, prefab.transform); Events(sb, $"dealDamage {h.name}", h.dealDamage, prefab.transform); }
        }
        File.WriteAllText(System.IO.Path.Combine("Logs", "turret_effects.txt"), sb.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static void Events(StringBuilder sb, string label, UnityEventBase e, Transform root)
    {
        if (e == null) return;
        for (int i = 0; i < e.GetPersistentEventCount(); i++)
        {
            Object target = e.GetPersistentTarget(i);
            string where = target is Component c ? Path(c.transform, root) + " (" + target.GetType().Name + ")" : target is GameObject g ? Path(g.transform, root) + " (GameObject)" : target != null ? target.GetType().Name : "null";
            sb.AppendLine($"  {label} -> {where}.{e.GetPersistentMethodName(i)}");
        }
    }

    private static string Path(Transform t, Transform root)
    {
        string p = t.name;
        while (t.parent != null && t != root) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
