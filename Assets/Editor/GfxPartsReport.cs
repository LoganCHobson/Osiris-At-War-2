using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Dev check: each hull renderer's name, materials, and per-submesh bounds in ship space.
public static class GfxPartsReport
{
    public static void Run()
    {
        StringBuilder text = new StringBuilder();
        foreach (string name in new[] { "Human/Human_Destroyer", "Human/Human_Corvette", "Human/Human_Cruiser", "Human/Human_Battleship", "Avanti/Avanti_Destroyer", "Avanti/Avanti_Corvette" })
        {
            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{name}.prefab"));
            text.AppendLine("=== " + name);
            Transform gfx = ship.transform.Find("GFX");
            foreach (MeshRenderer r in (gfx != null ? gfx : ship.transform).GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponentInParent<TurretController>() != null || !r.name.Contains("LOD0") && r.name.Contains("LOD")) continue;
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;
                Mesh m = f.sharedMesh;
                text.AppendLine($"  {r.name} mesh={m.name} readable={m.isReadable} sub={m.subMeshCount} enabled={r.enabled}");
                if (!m.isReadable) continue;
                Vector3[] v = m.vertices;
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    int[] tris = m.GetTriangles(s);
                    if (tris.Length == 0) continue;
                    Bounds b = new Bounds(ship.transform.InverseTransformPoint(f.transform.TransformPoint(v[tris[0]])), Vector3.zero);
                    foreach (int i in tris) b.Encapsulate(ship.transform.InverseTransformPoint(f.transform.TransformPoint(v[i])));
                    Material mat = s < r.sharedMaterials.Length ? r.sharedMaterials[s] : null;
                    Color c = mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                    text.AppendLine($"     sub{s} mat={(mat != null ? mat.name : "-")} col={c} tris={tris.Length / 3} min {b.min:F1} max {b.max:F1}");
                }
            }
            Object.DestroyImmediate(ship);
        }
        File.WriteAllText(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Logs", "gfx_parts.txt"), text.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
