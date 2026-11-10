using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Dev check: cross-section (x/y extents per z slice) of each visible turret model, in turretBase space.
public static class TurretShapeReport
{
    public static void Run()
    {
        StringBuilder text = new StringBuilder();
        foreach (string name in new[] { "Human/Human_Destroyer", "Human/Human_Battleship", "Human/Human_Corvette", "Human/Human_Cruiser", "Human/Human_Carrier", "Human/Human_Station" })
        {
            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{name}.prefab"));
            text.AppendLine("=== " + name);
            foreach (var group in ship.GetComponentsInChildren<TurretController>(true).GroupBy(t => t.name.Split('_')[0]))
            {
                TurretController t = group.First();
                Transform b = t.turretBase;
                foreach (MeshRenderer r in b.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.enabled || (t.turretBarrel != null && r.transform.IsChildOf(t.turretBarrel)) || !r.name.Contains("LOD0")) continue;
                    Mesh m = r.GetComponent<MeshFilter>().sharedMesh;
                    var v = m.vertices.Select(p => b.InverseTransformPoint(r.transform.TransformPoint(p))).ToArray();
                    float zmin = v.Min(p => p.z), zmax = v.Max(p => p.z);
                    text.AppendLine($"  {t.name} ({group.Count()}x) model {r.name} verts {v.Length} z {zmin:F2}..{zmax:F2} x {v.Min(p => p.x):F2}..{v.Max(p => p.x):F2} y {v.Min(p => p.y):F2}..{v.Max(p => p.y):F2}  fps {string.Join(" ", t.firingPoints.Select(f => b.InverseTransformPoint(f.position).ToString("F2")))}");
                    int bins = 14;
                    for (int i = 0; i < bins; i++)
                    {
                        float a = Mathf.Lerp(zmin, zmax, i / (float)bins), c = Mathf.Lerp(zmin, zmax, (i + 1) / (float)bins);
                        var s = v.Where(p => p.z >= a && p.z <= c).ToArray();
                        if (s.Length == 0) { text.AppendLine($"     z {a:F2}..{c:F2}: -"); continue; }
                        text.AppendLine($"     z {a:F2}..{c:F2}: n {s.Length,3} x {s.Min(p => p.x):F2}..{s.Max(p => p.x):F2} y {s.Min(p => p.y):F2}..{s.Max(p => p.y):F2}");
                    }
                }
            }
            Object.DestroyImmediate(ship);
        }
        File.WriteAllText(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Logs", "turret_shape.txt"), text.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
