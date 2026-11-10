using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Dev check: the full hierarchy of the first turrets on a ship, and every visible renderer near them.
public static class TurretTreeReport
{
    public static void Run()
    {
        StringBuilder text = new StringBuilder();
        foreach (string name in new[] { "Human/Human_Destroyer", "Human/Human_Battleship", "Avanti/Avanti_Destroyer" })
        {
            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{name}.prefab"));
            text.AppendLine("=== " + name);
            TurretController[] turrets = ship.GetComponentsInChildren<TurretController>(true);
            for (int k = 0; k < Mathf.Min(2, turrets.Length); k++)
            {
                TurretController t = turrets[k];
                text.AppendLine($"-- turret {t.name} at {ship.transform.InverseTransformPoint(t.transform.position):F2}");
                Dump(t.transform, 0, text, ship.transform);
                // Visible renderers within a few units that are NOT under the turret.
                foreach (Renderer r in ship.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.transform.IsChildOf(t.transform) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    float d = Vector3.Distance(r.bounds.center, t.transform.position);
                    if (d < r.bounds.extents.magnitude + 1.5f && r.bounds.extents.magnitude < 6f)
                        text.AppendLine($"     near visible: {Path(r.transform, ship.transform)} centre-dist {d:F2} size {r.bounds.size:F2}");
                }
            }
            Object.DestroyImmediate(ship);
        }
        File.WriteAllText(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Logs", "turret_tree.txt"), text.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static void Dump(Transform t, int depth, StringBuilder text, Transform root)
    {
        Renderer r = t.GetComponent<Renderer>();
        string comps = string.Join(",", System.Array.ConvertAll(t.GetComponents<Component>(), c => c == null ? "MISSING" : c.GetType().Name));
        text.AppendLine($"{new string(' ', 4 + depth * 2)}{t.name} [{comps}] active={t.gameObject.activeSelf} rendered={(r != null && r.enabled)} lpos {t.localPosition:F2} lrot {t.localEulerAngles:F0} lscale {t.localScale:F2}");
        foreach (Transform c in t) Dump(c, depth + 1, text, root);
    }

    private static string Path(Transform t, Transform root)
    {
        string p = t.name;
        for (Transform c = t.parent; c != null && c != root; c = c.parent) p = c.name + "/" + p;
        return p;
    }
}
