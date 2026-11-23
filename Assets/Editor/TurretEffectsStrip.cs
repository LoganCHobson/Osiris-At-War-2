using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

// One-off: takes the per-turret effect copies out of the turret prefabs. Every turret carried four muzzle-flash
// rigs, a blast, a fire and an unused explosion - about 40 particle systems a mount - and instantiating a
// ship spent most of its time creating them (an Avanti Capital: 321 ms, 38 ms without). The effects are saved
// once as templates in Resources/TurretFx (each keeping the pose and scale it had on the mount) and played from
// pools instead (TurretController.MuzzleFlash, HardpointWreck). Batchmode: -executeMethod TurretEffectsStrip.Run.
public static class TurretEffectsStrip
{
    private const string Folder = "Assets/Resources/TurretFx";
    private static readonly string[] Prefabs = { "Assets/Prefabs/Turret.prefab", "Assets/Prefabs/BattleStation.prefab" };

    public static void Run()
    {
        StringBuilder log = new StringBuilder();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "TurretFx");
        bool templatesSaved = File.Exists(Folder + "/MuzzleFlash.prefab");

        foreach (string path in Prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            int removed = 0;
            foreach (TurretController turret in root.GetComponentsInChildren<TurretController>(true))
            {
                Transform mount = turret.transform;
                List<GameObject> doomed = new List<GameObject>();
                foreach (ParticleSystem ps in mount.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.transform.parent != null && ps.transform.parent.GetComponent<ParticleSystem>() != null) continue; // a child of an effect
                    string n = ps.name;
                    bool flash = n.StartsWith("WFX_MF"), blast = n.StartsWith("WFX_ExplosiveSmoke"), fire = n.StartsWith("WFX_Fire"), unused = n.StartsWith("WFX_Explosion Simple");
                    if (!flash && !blast && !fire && !unused) continue;
                    if (!templatesSaved)
                    {
                        if (flash) SaveTemplate(ps, "MuzzleFlash", log);
                        if (blast) SaveTemplate(ps, "TurretBlast", log);
                        if (fire) SaveTemplate(ps, "TurretFire", log);
                    }
                    doomed.Add(ps.gameObject);
                }
                if (!templatesSaved && File.Exists(Folder + "/MuzzleFlash.prefab") && File.Exists(Folder + "/TurretBlast.prefab") && File.Exists(Folder + "/TurretFire.prefab")) templatesSaved = true;

                HashSet<Object> gone = new HashSet<Object>(doomed.SelectMany(g => g.GetComponentsInChildren<Component>(true)).Cast<Object>().Concat(doomed));
                removed += Strip(turret.onFire, gone);
                HardpointHealth health = turret.GetComponent<HardpointHealth>();
                if (health != null) { removed += Strip(health.die, gone); removed += Strip(health.dealDamage, gone); }
                foreach (GameObject g in doomed) Object.DestroyImmediate(g);
                log.AppendLine($"{path} {PathOf(mount, root.transform)}: removed {doomed.Count} effects");
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            log.AppendLine($"{path}: {removed} event listeners removed");
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine("Logs", "turret_effects_strip.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // The effect saved as a template whose root keeps the pose and scale it had on its parent.
    private static void SaveTemplate(ParticleSystem source, string name, StringBuilder log)
    {
        string path = $"{Folder}/{name}.prefab";
        if (File.Exists(path)) return;
        GameObject copy = Object.Instantiate(source.gameObject);
        copy.name = name;
        copy.transform.localPosition = source.transform.localPosition;
        copy.transform.localRotation = source.transform.localRotation;
        copy.transform.localScale = source.transform.localScale;
        copy.SetActive(true);
        PrefabUtility.SaveAsPrefabAsset(copy, path);
        Object.DestroyImmediate(copy);
        log.AppendLine($"saved template {path} from {source.name} (local pos {source.transform.localPosition}, scale {source.transform.localScale})");
    }

    private static int Strip(UnityEventBase e, HashSet<Object> gone)
    {
        if (e == null) return 0;
        int removed = 0;
        for (int i = e.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            Object target = e.GetPersistentTarget(i);
            if (target != null && gone.Contains(target)) { UnityEventTools.RemovePersistentListener(e, i); removed++; }
        }
        return removed;
    }

    private static string PathOf(Transform t, Transform root)
    {
        string p = t.name;
        while (t.parent != null && t != root) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
