using System.Collections.Generic;
using UnityEngine;

// The turret prefabs aim with hidden stand-in barrels (TurretController.turretBarrel, whose firing points the
// shots and muzzle flashes come from), while the barrels you actually see are part of a single visible model
// that only turns with the base. So whenever a gun elevated, the shots and flashes left the barrels behind.
//
// This splits each visible turret model, once, into its housing (stays on the base) and its barrel group
// (mantlets + barrels: everything ahead of the housing), and hangs the barrel group on the elevating
// transform. TurretController then pitches that group about its trunnion, so what you see, the firing points
// and the shots all line up.
public static class TurretVisualRig
{
    public struct Result
    {
        public bool rigged;
        public Vector3 trunnion; // In turretBase space: the point the barrels pitch about.
    }

    private class Split
    {
        public Mesh housing;
        public Mesh barrels;
    }

    private static readonly Dictionary<(Mesh, int), Split> cache = new Dictionary<(Mesh, int), Split>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => cache.Clear();

    public static Result Rig(TurretController turret)
    {
        Result result = new Result();
        Transform baseT = turret.turretBase, elevator = turret.turretBarrel;
        if (baseT == null || elevator == null) return result;

        // The visible model: enabled mesh renderers on the base that aren't the hidden stand-ins.
        List<MeshRenderer> models = new List<MeshRenderer>();
        foreach (MeshRenderer renderer in baseT.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!renderer.enabled || renderer.transform.IsChildOf(elevator)) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            models.Add(renderer);
        }
        if (models.Count == 0) return result;

        // Find where the housing ends, from the most detailed model: the furthest-forward point that is still
        // as wide as the housing. Everything ahead of it (mantlets, barrels) elevates.
        MeshRenderer detail = models.Find(m => m.name.Contains("LOD0")) ?? models[0];
        Vector3[] verts = detail.GetComponent<MeshFilter>().sharedMesh.vertices;
        float maxWidth = 0f, minZ = float.MaxValue, maxZ = float.MinValue;
        Vector3[] local = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            local[i] = baseT.InverseTransformPoint(detail.transform.TransformPoint(verts[i]));
            maxWidth = Mathf.Max(maxWidth, Mathf.Abs(local[i].x));
            minZ = Mathf.Min(minZ, local[i].z);
            maxZ = Mathf.Max(maxZ, local[i].z);
        }
        float length = maxZ - minZ;
        if (length <= 0.0001f || maxWidth <= 0.0001f) return result;

        float housingEnd = minZ;
        foreach (Vector3 v in local)
        {
            if (Mathf.Abs(v.x) >= maxWidth * 0.8f) housingEnd = Mathf.Max(housingEnd, v.z);
        }

        // The model is built from separate pieces (housing, base plate, mantlets, barrels). The pieces lying in
        // front of the housing are the barrel group.
        Mesh detailMesh = detail.GetComponent<MeshFilter>().sharedMesh;
        bool[] detailFront = FrontPieces(detailMesh, local, housingEnd);
        if (detailFront == null) return result;

        float rear = float.MaxValue, ySum = 0f;
        int yCount = 0;
        int[] detailTris = detailMesh.triangles;
        for (int t = 0; t < detailTris.Length; t += 3)
        {
            if (!detailFront[t / 3]) continue;
            for (int k = 0; k < 3; k++)
            {
                Vector3 v = local[detailTris[t + k]];
                rear = Mathf.Min(rear, v.z);
                ySum += v.y;
                yCount++;
            }
        }
        if (yCount == 0) return result;
        result.trunnion = new Vector3(0f, ySum / yCount, rear);

        foreach (MeshRenderer model in models)
        {
            MeshFilter filter = model.GetComponent<MeshFilter>();
            Split split = SplitMesh(filter.sharedMesh, model.transform, baseT, housingEnd);
            if (split == null || split.barrels == null) continue;

            filter.sharedMesh = split.housing;

            GameObject barrels = new GameObject(model.name + "_Barrels");
            barrels.layer = model.gameObject.layer;
            barrels.transform.SetParent(elevator, false);
            barrels.transform.SetPositionAndRotation(model.transform.position, model.transform.rotation);
            barrels.transform.localScale = Divide(model.transform.lossyScale, elevator.lossyScale);
            barrels.AddComponent<MeshFilter>().sharedMesh = split.barrels;
            MeshRenderer renderer = barrels.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = model.sharedMaterials;
            renderer.shadowCastingMode = model.shadowCastingMode;
            renderer.receiveShadows = model.receiveShadows;
            AddToSameLod(model, renderer);
        }
        result.rigged = true;
        return result;
    }

    // Which triangles belong to pieces (connected groups of triangles, welded by position) whose centre lies
    // ahead of the housing. Null if there are none, or if that would be everything.
    private static bool[] FrontPieces(Mesh mesh, Vector3[] baseSpace, float housingEnd)
    {
        int[] tris = mesh.triangles;
        int triCount = tris.Length / 3;
        if (triCount == 0) return null;

        // Weld vertices by position so pieces split only where the geometry really is separate.
        Dictionary<Vector3Int, int> weld = new Dictionary<Vector3Int, int>();
        int[] welded = new int[baseSpace.Length];
        Vector3[] raw = mesh.vertices;
        float grid = Mathf.Max(1e-5f, mesh.bounds.size.magnitude * 1e-4f);
        for (int i = 0; i < raw.Length; i++)
        {
            Vector3Int key = new Vector3Int(Mathf.RoundToInt(raw[i].x / grid), Mathf.RoundToInt(raw[i].y / grid), Mathf.RoundToInt(raw[i].z / grid));
            if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld[key] = id; }
            welded[i] = id;
        }

        int[] parent = new int[weld.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        for (int t = 0; t < tris.Length; t += 3)
        {
            Union(welded[tris[t]], welded[tris[t + 1]]);
            Union(welded[tris[t]], welded[tris[t + 2]]);
        }

        Dictionary<int, (float sum, int n)> centre = new Dictionary<int, (float, int)>();
        for (int i = 0; i < baseSpace.Length; i++)
        {
            int root = Find(welded[i]);
            centre.TryGetValue(root, out (float sum, int n) c);
            centre[root] = (c.sum + baseSpace[i].z, c.n + 1);
        }

        bool[] front = new bool[triCount];
        int frontCount = 0;
        for (int t = 0; t < triCount; t++)
        {
            (float sum, int n) c = centre[Find(welded[tris[t * 3]])];
            if (c.n > 0 && c.sum / c.n > housingEnd) { front[t] = true; frontCount++; }
        }
        return frontCount == 0 || frontCount == triCount ? null : front;
    }

    private static Split SplitMesh(Mesh mesh, Transform modelT, Transform baseT, float housingEnd)
    {
        int key = Mathf.RoundToInt(housingEnd * 1000f);
        if (cache.TryGetValue((mesh, key), out Split cached)) return cached;

        Vector3[] raw = mesh.vertices;
        Vector3[] baseSpace = new Vector3[raw.Length];
        for (int i = 0; i < raw.Length; i++) baseSpace[i] = baseT.InverseTransformPoint(modelT.TransformPoint(raw[i]));
        bool[] front = FrontPieces(mesh, baseSpace, housingEnd);

        Split split = null;
        if (front != null)
        {
            split = new Split { housing = Subset(mesh, front, false), barrels = Subset(mesh, front, true) };
        }
        cache[(mesh, key)] = split;
        return split;
    }

    // A copy of the mesh keeping only the triangles marked (or unmarked), per submesh.
    private static Mesh Subset(Mesh mesh, bool[] marked, bool keepMarked)
    {
        Mesh copy = Object.Instantiate(mesh);
        copy.name = mesh.name + (keepMarked ? "_Barrels" : "_Housing");
        int offset = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] tris = mesh.GetTriangles(s);
            List<int> kept = new List<int>(tris.Length);
            // Triangle numbering in mesh.triangles runs submesh by submesh.
            for (int t = 0; t < tris.Length; t += 3)
            {
                if (marked[offset + t / 3] == keepMarked) { kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]); }
            }
            offset += tris.Length / 3;
            copy.SetTriangles(kept, s);
        }
        copy.RecalculateBounds();
        return copy;
    }

    // Keep the new barrel renderer in the same LOD level as the model it came from.
    private static void AddToSameLod(Renderer original, Renderer added)
    {
        LODGroup group = original.GetComponentInParent<LODGroup>();
        if (group == null) return;
        LOD[] lods = group.GetLODs();
        bool changed = false;
        for (int i = 0; i < lods.Length; i++)
        {
            if (System.Array.IndexOf(lods[i].renderers, original) < 0) continue;
            Renderer[] grown = new Renderer[lods[i].renderers.Length + 1];
            lods[i].renderers.CopyTo(grown, 0);
            grown[grown.Length - 1] = added;
            lods[i].renderers = grown;
            changed = true;
        }
        if (changed) group.SetLODs(lods);
    }

    private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
}
