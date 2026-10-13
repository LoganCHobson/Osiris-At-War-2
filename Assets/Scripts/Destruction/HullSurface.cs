using System.Collections.Generic;
using UnityEngine;

public static class HullSurface
{
    private class Sampler
    {
        public Vector3[] positions;
        public int[] triangles;
        public float[] cumulative;
        public float total;
    }

    private static readonly Dictionary<Mesh, Sampler> cache = new Dictionary<Mesh, Sampler>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        cache.Clear();
    }

    public static float WorldArea(MeshFilter filter)
    {
        if (filter == null) return 0f;
        Sampler sampler = Get(filter.sharedMesh);
        Vector3 scale = filter.transform.lossyScale;
        float factor = Mathf.Abs(scale.x * scale.z);
        if (sampler == null)
        {
            Vector3 size = filter.sharedMesh != null ? filter.sharedMesh.bounds.size : Vector3.one;
            return (size.x * size.z + size.x * size.y + size.y * size.z) * 2f * factor;
        }
        return sampler.total * factor;
    }

    public static MeshFilter Pick(IList<MeshFilter> filters)
    {
        if (filters == null || filters.Count == 0) return null;

        float total = 0f;
        for (int i = 0; i < filters.Count; i++) total += WorldArea(filters[i]);
        if (total <= 0f) return filters[Random.Range(0, filters.Count)];

        float roll = Random.value * total;
        for (int i = 0; i < filters.Count; i++)
        {
            roll -= WorldArea(filters[i]);
            if (roll <= 0f) return filters[i];
        }
        return filters[filters.Count - 1];
    }

    public static bool Sample(IList<MeshFilter> filters, out Vector3 worldPoint, out Vector3 worldNormal)
    {
        MeshFilter filter = Pick(filters);
        if (filter == null)
        {
            worldPoint = Vector3.zero;
            worldNormal = Vector3.up;
            return false;
        }
        return Sample(filter, out worldPoint, out worldNormal);
    }

    public static bool Sample(MeshFilter filter, out Vector3 worldPoint, out Vector3 worldNormal)
    {
        bool found = SampleLocal(filter, out Vector3 local, out Vector3 localNormal);
        worldPoint = filter.transform.TransformPoint(local);
        worldNormal = filter.transform.TransformDirection(localNormal).normalized;
        return found;
    }

    public static bool SampleLocal(MeshFilter filter, out Vector3 localPoint, out Vector3 localNormal)
    {
        Mesh mesh = filter.sharedMesh;
        Sampler sampler = Get(mesh);

        if (sampler == null)
        {
            Bounds bounds = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            localPoint = bounds.center + Vector3.Scale(bounds.extents, new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)));
            localNormal = Vector3.up;
            return false;
        }

        float roll = Random.value * sampler.total;
        int low = 0;
        int high = sampler.cumulative.Length - 1;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (sampler.cumulative[mid] < roll) low = mid + 1;
            else high = mid;
        }

        int t = low * 3;
        Vector3 a = sampler.positions[sampler.triangles[t]];
        Vector3 b = sampler.positions[sampler.triangles[t + 1]];
        Vector3 c = sampler.positions[sampler.triangles[t + 2]];

        float u = Random.value;
        float v = Random.value;
        if (u + v > 1f)
        {
            u = 1f - u;
            v = 1f - v;
        }

        localPoint = a + (b - a) * u + (c - a) * v;
        localNormal = Vector3.Cross(b - a, c - a).normalized;
        return true;
    }

    private static Sampler Get(Mesh mesh)
    {
        if (mesh == null || !mesh.isReadable) return null;
        if (cache.TryGetValue(mesh, out Sampler cached)) return cached;

        Sampler sampler = new Sampler();
        sampler.positions = mesh.vertices;
        sampler.triangles = mesh.triangles;

        int count = sampler.triangles.Length / 3;
        if (count == 0)
        {
            cache[mesh] = null;
            return null;
        }

        sampler.cumulative = new float[count];
        float running = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = sampler.positions[sampler.triangles[i * 3]];
            Vector3 b = sampler.positions[sampler.triangles[i * 3 + 1]];
            Vector3 c = sampler.positions[sampler.triangles[i * 3 + 2]];
            running += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            sampler.cumulative[i] = running;
        }
        sampler.total = running;

        cache[mesh] = sampler;
        return sampler;
    }
}
