using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Dev check: every HP_/mount-like transform on each warship, what's on it, and whether turret models are readable.
public static class MountNamesReport
{
    public static void Run()
    {
        StringBuilder text = new StringBuilder();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Osiris/Human", "Assets/Prefabs/Osiris/Avanti" }))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null || prefab.name.Contains("Squadron")) continue;
            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            text.AppendLine("=== " + prefab.name);
            foreach (Transform t in ship.GetComponentsInChildren<Transform>(true))
            {
                if (!(t.name.StartsWith("HP") || t.name.Contains("Hardpoint") || t.name.Contains("Engine") || t.name.Contains("Hangar"))) continue;
                if (t.GetComponentInParent<TurretController>() != null && t.GetComponent<TurretController>() == null) continue;
                string comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
                Vector3 local = ship.transform.InverseTransformPoint(t.position);
                text.AppendLine($"  {t.name} [{comps}] local {local:F1} up {ship.transform.InverseTransformDirection(t.up):F2} fwd {ship.transform.InverseTransformDirection(t.forward):F2} children {t.childCount}");
            }
            TurretController turret = ship.GetComponentInChildren<TurretController>(true);
            if (turret != null && turret.turretBase != null)
            {
                foreach (MeshFilter f in turret.turretBase.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (turret.turretBarrel != null && f.transform.IsChildOf(turret.turretBarrel)) continue;
                    Renderer r = f.GetComponent<Renderer>();
                    text.AppendLine($"  model {f.name} mesh={f.sharedMesh?.name} readable={f.sharedMesh?.isReadable} verts={f.sharedMesh?.vertexCount} rendered={(r != null && r.enabled)}");
                }
            }
            Object.DestroyImmediate(ship);
        }
        File.WriteAllText(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Logs", "mount_names.txt"), text.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
