using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Dev check: for every ship prefab, which way each turret's barrel model actually points compared with the
// axis the turret code aims (turretBase +Z), where its firing points sit relative to the barrel tips, and every
// transform that looks like a missile bay/tube. Writes Logs/weapon_mounts.txt.
// Run: Unity -batchmode -projectPath ... -executeMethod WeaponMountReport.Run -quit
public static class WeaponMountReport
{
    public static void Run()
    {
        StringBuilder text = new StringBuilder();
        string[] folders = { "Assets/Prefabs/Osiris/Human", "Assets/Prefabs/Osiris/Avanti" };
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<Ship>() == null && prefab.GetComponent<UnitHealthManager>() == null) continue;

            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            text.AppendLine($"=== {prefab.name}  scale {ship.transform.lossyScale.x:0.###}");

            Dictionary<string, int> seen = new Dictionary<string, int>();
            foreach (TurretController turret in ship.GetComponentsInChildren<TurretController>(true))
            {
                string key = Signature(turret);
                if (seen.ContainsKey(key)) { seen[key]++; continue; }
                seen[key] = 1;
                text.AppendLine(Describe(turret));
            }
            foreach (KeyValuePair<string, int> entry in seen.Where(e => e.Value > 1))
                text.AppendLine($"   ({entry.Value} turrets share signature {entry.Key})");

            foreach (Transform t in ship.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("missile") || n.Contains("launch") || n.Contains("tube") || n.Contains("vls") || n.Contains("silo") || n.Contains("torpedo") || (n.Contains("bay") && !n.Contains("hangar")))
                {
                    Vector3 local = ship.transform.InverseTransformPoint(t.position);
                    Vector3 fwd = ship.transform.InverseTransformDirection(t.forward);
                    Vector3 up = ship.transform.InverseTransformDirection(t.up);
                    text.AppendLine($"   bay? {PathOf(t, ship.transform)}  local {local:F2}  fwd {fwd:F2}  up {up:F2}  renderer={(t.GetComponent<Renderer>() != null)} hp={(t.GetComponentInParent<HardpointHealth>() != null)}");
                }
            }
            Object.DestroyImmediate(ship);
        }
        string output = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "weapon_mounts.txt");
        File.WriteAllText(output, text.ToString());
        Debug.Log("WEAPON MOUNTS\n" + text);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static string Signature(TurretController turret)
    {
        Transform b = turret.turretBase;
        return $"{turret.name}|{(b != null ? b.name : "-")}|{turret.firingPoints.Count}";
    }

    private static string Describe(TurretController turret)
    {
        StringBuilder s = new StringBuilder();
        Transform baseT = turret.turretBase, barrel = turret.turretBarrel;
        s.Append($"  turret {turret.name}  base={baseT?.name} barrel={barrel?.name} points={turret.firingPoints.Count} gatling={turret.gatling} beam={turret.beam} filter={turret.targetFilter}");
        if (baseT == null) return s.ToString();

        // Barrel geometry in turretBase space: centroid of all mesh vertices under the barrel transform
        // (or the base when there is no separate barrel), and the tip = furthest vertex along the centroid direction.
        Transform geometryRoot = barrel != null ? barrel : baseT;
        List<Vector3> verts = new List<Vector3>();
        foreach (MeshFilter filter in geometryRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            foreach (Vector3 v in filter.sharedMesh.vertices) verts.Add(baseT.InverseTransformPoint(filter.transform.TransformPoint(v)));
        }
        Vector3 pivot = barrel != null ? baseT.InverseTransformPoint(barrel.position) : Vector3.zero;
        if (verts.Count > 0)
        {
            Vector3 centroid = verts.Aggregate(Vector3.zero, (a, v) => a + v) / verts.Count;
            Vector3 dir = (centroid - pivot).normalized;
            float far = verts.Max(v => Vector3.Dot(v - pivot, dir));
            Vector3 tipZ = verts.OrderByDescending(v => v.z).First();
            s.Append($"\n     barrel mesh: {verts.Count} verts, centroid-from-pivot dir {dir:F2} (aim axis is +Z: dot {Vector3.Dot(dir, Vector3.forward):F2}), extent along dir {far:F2}, max +Z vertex {tipZ:F2}");
            Bounds bb = new Bounds(verts[0], Vector3.zero);
            foreach (Vector3 v in verts) bb.Encapsulate(v);
            s.Append($"\n     barrel bounds (base space) min {bb.min:F2} max {bb.max:F2}");
        }
        else s.Append("\n     barrel mesh: none readable");
        foreach (MeshFilter filter in geometryRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds mb = filter.sharedMesh.bounds;
            Vector3 c = baseT.InverseTransformPoint(filter.transform.TransformPoint(mb.center));
            Renderer r = filter.GetComponent<Renderer>();
            s.Append($"\n       mesh {filter.name}/{filter.sharedMesh.name} centre {c:F2} active {filter.gameObject.activeInHierarchy} rendered {(r != null && r.enabled)} verts {filter.sharedMesh.vertexCount}");
        }

        for (int i = 0; i < turret.firingPoints.Count; i++)
        {
            Transform p = turret.firingPoints[i];
            if (p == null) continue;
            Vector3 local = baseT.InverseTransformPoint(p.position);
            Vector3 fwd = baseT.InverseTransformDirection(p.forward);
            s.Append($"\n     fp{i} {p.name} pos {local:F2} fwd {fwd:F2} parent {p.parent?.name}");
            MeshFilter tube = p.parent != null ? p.parent.GetComponent<MeshFilter>() : null;
            if (tube != null && tube.sharedMesh != null)
            {
                Bounds mb = tube.sharedMesh.bounds;
                Vector3 c = baseT.InverseTransformPoint(tube.transform.TransformPoint(mb.center));
                Vector3 lx = baseT.InverseTransformDirection(tube.transform.TransformDirection(Vector3.right));
                Vector3 ly = baseT.InverseTransformDirection(tube.transform.TransformDirection(Vector3.up));
                Vector3 lz = baseT.InverseTransformDirection(tube.transform.TransformDirection(Vector3.forward));
                Vector3 ext = Vector3.Scale(mb.extents, tube.transform.lossyScale);
                s.Append($"\n        tube {tube.sharedMesh.name} centre {c:F2} extents(world) {ext:F3} X->{lx:F2} Y->{ly:F2} Z->{lz:F2} baseScale {baseT.lossyScale.x:F3}");
            }
        }
        foreach (ParticleSystem flash in turret.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (!flash.name.Contains("MF")) continue;
            Vector3 local = baseT.InverseTransformPoint(flash.transform.position);
            Vector3 fwd = baseT.InverseTransformDirection(flash.transform.forward);
            var shape = flash.shape;
            s.Append($"\n     flash {flash.name} pos {local:F2} fwd {fwd:F2} shape {shape.shapeType} rot {shape.rotation:F0}");
        }
        return s.ToString();
    }

    private static string PathOf(Transform t, Transform root)
    {
        List<string> parts = new List<string>();
        for (Transform c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
