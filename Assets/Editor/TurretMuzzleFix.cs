using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

public static class TurretMuzzleFix
{
    [MenuItem("Osiris/Fix Turret Muzzle Flashes")]
    public static void Fix()
    {
        List<string> fixedPrefabs = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || asset.GetComponent<TurretController>() == null) continue;
            if (PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (FixTurret(root.GetComponent<TurretController>()))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    fixedPrefabs.Add(path);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log($"Turret muzzle fix: updated {fixedPrefabs.Count} prefab(s): {string.Join(", ", fixedPrefabs)}");
    }

    private static bool FixTurret(TurretController turret)
    {
        List<Transform> points = turret.firingPoints.FindAll(point => point != null);
        if (points.Count == 0) return false;

        bool changed = false;
        int assigned = 0;
        List<GameObject> extras = new List<GameObject>();

        for (int i = 0; i < turret.onFire.GetPersistentEventCount(); i++)
        {
            ParticleSystem flash = turret.onFire.GetPersistentTarget(i) as ParticleSystem;
            if (flash == null || !flash.name.Contains("MF")) continue;

            if (assigned < points.Count)
            {
                Transform point = points[assigned];
                if (flash.transform.parent != point || flash.transform.localPosition != Vector3.zero)
                {
                    flash.transform.SetParent(point, true);
                    flash.transform.localPosition = Vector3.zero;
                    changed = true;
                }
                assigned++;
            }
            else
            {
                extras.Add(flash.gameObject);
            }
        }

        for (int i = turret.onFire.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            Component target = turret.onFire.GetPersistentTarget(i) as Component;
            if (target != null && extras.Contains(target.gameObject))
            {
                UnityEventTools.RemovePersistentListener(turret.onFire, i);
                changed = true;
            }
        }

        foreach (GameObject extra in extras)
        {
            Object.DestroyImmediate(extra, true);
        }

        return changed;
    }

    public static void FixAndQuit()
    {
        Fix();
        MuzzleDiagnostics.Report();
    }
}
