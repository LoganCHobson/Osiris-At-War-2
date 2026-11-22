using System.Collections.Generic;
using UnityEngine;

// Structural battle damage: the hull's own mesh is bent and torn where it has taken severe punishment, as
// warships' plating was in the photographs of WW2 battle damage - dished in and buckled around a hit, holed
// with the edges of the hole petalled in by a shell (or blown out by a blast inside), whole plates torn loose
// and folded back on their seam. HullScarring calls in here as a patch of hull goes past scarred and breached;
// its decals ride the bent metal (they're reshaped with it) and are cut away over the holes.
//
// Each surface gets its own copy of its mesh the first time it's hit hard (the prefab's mesh is shared), and
// only LOD0 is touched: from far enough away to be on a lower LOD, a dent doesn't show. The breakup slices
// the damaged mesh, so the wreck keeps its holes.
[DisallowMultipleComponent]
public class HullDeformation : MonoBehaviour
{
    // A mesh opened up for editing: its vertices and triangles as lists, so it can be subdivided and cut.
    private class Editable
    {
        public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public readonly List<Vector4> t = new List<Vector4>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color> col = new List<Color>();
        public bool hasN, hasT, hasUV, hasCol;
        public List<int>[] tris;

        public static Editable Read(Mesh mesh)
        {
            Editable e = new Editable();
            mesh.GetVertices(e.v);
            mesh.GetNormals(e.n); e.hasN = e.n.Count == e.v.Count;
            mesh.GetTangents(e.t); e.hasT = e.t.Count == e.v.Count;
            mesh.GetUVs(0, e.uv); e.hasUV = e.uv.Count == e.v.Count;
            mesh.GetColors(e.col); e.hasCol = e.col.Count == e.v.Count;
            e.tris = new List<int>[mesh.subMeshCount];
            for (int s = 0; s < mesh.subMeshCount; s++) e.tris[s] = new List<int>(mesh.GetTriangles(s));
            return e;
        }

        public void Write(Mesh mesh)
        {
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            if (hasN) mesh.SetNormals(n);
            if (hasT) mesh.SetTangents(t);
            if (hasUV) mesh.SetUVs(0, uv);
            if (hasCol) mesh.SetColors(col);
            mesh.subMeshCount = tris.Length;
            for (int s = 0; s < tris.Length; s++) mesh.SetTriangles(tris[s], s, false);
            mesh.RecalculateBounds();
        }

        private int Mid(int a, int b, Dictionary<long, int> made)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (made.TryGetValue(key, out int m)) return m;
            m = v.Count;
            v.Add((v[a] + v[b]) * 0.5f);
            if (hasN) n.Add((n[a] + n[b]).normalized);
            if (hasT) t.Add((t[a] + t[b]) * 0.5f);
            if (hasUV) uv.Add((uv[a] + uv[b]) * 0.5f);
            if (hasCol) col.Add((col[a] + col[b]) * 0.5f);
            made[key] = m;
            return m;
        }

        // Takes the triangles within `reach` of `c` out of the mesh into their own lists (one per submesh), so
        // the work below only ever touches the neighbourhood of the hit, not the whole hull. Merge puts them back.
        public List<int>[] Region(Vector3 c, float reach)
        {
            float reachSqr = reach * reach;
            List<int>[] near = new List<int>[tris.Length];
            for (int s = 0; s < tris.Length; s++)
            {
                List<int> list = tris[s];
                List<int> inside = new List<int>();
                int write = 0;
                for (int k = 0; k < list.Count; k += 3)
                {
                    int a = list[k], b = list[k + 1], d = list[k + 2];
                    Vector3 pa = v[a], pb = v[b], pd = v[d];
                    // Cheap box test first; the exact distance only for triangles whose box reaches the sphere.
                    Vector3 lo = Vector3.Min(pa, Vector3.Min(pb, pd)), hi = Vector3.Max(pa, Vector3.Max(pb, pd));
                    Vector3 nearest = Vector3.Max(lo, Vector3.Min(c, hi));
                    bool close = (nearest - c).sqrMagnitude <= reachSqr && (MeshSlicer.ClosestOnTriangle(c, pa, pb, pd) - c).sqrMagnitude <= reachSqr;
                    if (close) { inside.Add(a); inside.Add(b); inside.Add(d); }
                    else { list[write++] = a; list[write++] = b; list[write++] = d; }
                }
                list.RemoveRange(write, list.Count - write);
                near[s] = inside;
            }
            return near;
        }

        public void Merge(List<int>[] near)
        {
            for (int s = 0; s < tris.Length; s++) tris[s].AddRange(near[s]);
        }

        // Splits the region's triangles into four until their edges are under `target`, so there's metal enough to
        // bend and to cut a hole in (the hulls are built of few, large triangles). Triangles that share an edge
        // share its midpoint, so the finer mesh stays closed.
        public void Subdivide(List<int>[] near, Vector3 c, float reach, float target, int budget = 12000)
        {
            float targetSqr = target * target, reachSqr = reach * reach;
            Dictionary<long, int> made = new Dictionary<long, int>();
            for (int pass = 0; pass < 7 && budget > 0; pass++)
            {
                bool split = false;
                for (int s = 0; s < near.Length; s++)
                {
                    List<int> src = near[s];
                    List<int> dst = new List<int>(src.Count * 2);
                    for (int k = 0; k < src.Count; k += 3)
                    {
                        int a = src[k], b = src[k + 1], d = src[k + 2];
                        Vector3 pa = v[a], pb = v[b], pd = v[d];
                        float longest = Mathf.Max((pa - pb).sqrMagnitude, Mathf.Max((pb - pd).sqrMagnitude, (pd - pa).sqrMagnitude));
                        if (budget <= 0 || longest <= targetSqr || (MeshSlicer.ClosestOnTriangle(c, pa, pb, pd) - c).sqrMagnitude > reachSqr)
                        {
                            dst.Add(a); dst.Add(b); dst.Add(d);
                            continue;
                        }
                        int ab = Mid(a, b, made), bd = Mid(b, d, made), da = Mid(d, a, made);
                        dst.Add(a); dst.Add(ab); dst.Add(da);
                        dst.Add(ab); dst.Add(b); dst.Add(bd);
                        dst.Add(da); dst.Add(bd); dst.Add(d);
                        dst.Add(ab); dst.Add(bd); dst.Add(da);
                        budget -= 3;
                        split = true;
                    }
                    near[s] = dst;
                }
                if (!split) break;
            }
        }

        // Moves the region's vertices by `offset`, drops the region's triangles `remove` picks, and gives the moved
        // vertices new normals from their own triangles. (A vertex within reach only ever belongs to triangles
        // within reach, so nothing outside the region needs touching.)
        public bool Reshape(List<int>[] near, System.Func<Vector3, Vector3> offset, System.Func<Vector3, Vector3, Vector3, bool> remove)
        {
            HashSet<int> region = new HashSet<int>();
            foreach (List<int> list in near) foreach (int i in list) region.Add(i);
            Dictionary<int, bool> moved = new Dictionary<int, bool>(region.Count);
            bool any = false;
            foreach (int i in region)
            {
                Vector3 o = offset(v[i]);
                if (o.sqrMagnitude <= 0f) continue;
                v[i] += o;
                moved[i] = true;
                any = true;
            }
            bool cut = false;
            if (remove != null)
            {
                foreach (List<int> list in near)
                {
                    int write = 0;
                    for (int k = 0; k < list.Count; k += 3)
                    {
                        if (remove(v[list[k]], v[list[k + 1]], v[list[k + 2]])) { cut = true; continue; }
                        list[write++] = list[k]; list[write++] = list[k + 1]; list[write++] = list[k + 2];
                    }
                    list.RemoveRange(write, list.Count - write);
                }
            }
            if (any && hasN)
            {
                Dictionary<int, Vector3> sum = new Dictionary<int, Vector3>(moved.Count);
                foreach (List<int> list in near)
                {
                    for (int k = 0; k < list.Count; k += 3)
                    {
                        int a = list[k], b = list[k + 1], d = list[k + 2];
                        if (!moved.ContainsKey(a) && !moved.ContainsKey(b) && !moved.ContainsKey(d)) continue;
                        Vector3 face = Vector3.Cross(v[b] - v[a], v[d] - v[a]);
                        foreach (int i in new[] { a, b, d })
                        {
                            if (!moved.ContainsKey(i)) continue;
                            sum.TryGetValue(i, out Vector3 acc);
                            sum[i] = acc + face;
                        }
                    }
                }
                foreach (KeyValuePair<int, Vector3> pair in sum) if (pair.Value.sqrMagnitude > 0f) n[pair.Key] = pair.Value.normalized;
            }
            return any || cut;
        }

        // The whole edit around one hit: carve out the neighbourhood, refine, reshape, put back.
        private static readonly Unity.Profiling.ProfilerMarker RegionMarker = new Unity.Profiling.ProfilerMarker("HullDeformation.Region"),
            SubdivideMarker = new Unity.Profiling.ProfilerMarker("HullDeformation.Subdivide"), ReshapeMarker = new Unity.Profiling.ProfilerMarker("HullDeformation.Reshape");

        public bool Edit(Vector3 c, float reach, float target, int budget, System.Func<Vector3, Vector3> offset, System.Func<Vector3, Vector3, Vector3, bool> remove)
        {
            RegionMarker.Begin();
            List<int>[] near = Region(c, reach);
            RegionMarker.End();
            bool changed = false;
            foreach (List<int> list in near) if (list.Count > 0) { changed = true; break; }
            if (changed)
            {
                SubdivideMarker.Begin();
                Subdivide(near, c, reach, target, budget);
                SubdivideMarker.End();
                ReshapeMarker.Begin();
                changed = Reshape(near, offset, remove);
                ReshapeMarker.End();
            }
            Merge(near);
            return changed;
        }
    }

    private class Surface
    {
        public Mesh mesh;
        public Editable edit;
    }

    private readonly Dictionary<MeshFilter, Surface> surfaces = new Dictionary<MeshFilter, Surface>();
    private readonly List<Mesh> owned = new List<Mesh>(); // the cavities
    private Transform cavities;
    private int holes;
    private const int MaxHoles = 14;

    // Off: hulls only get HullScarring's decals (for a before/after; HullDamageShowcase -noDeform).
    public static bool Enabled = true;

    public static HullDeformation For(GameObject ship) =>
        ship.TryGetComponent(out HullDeformation found) ? found : ship.AddComponent<HullDeformation>();

    public int Holes => holes;
    // Where the holes are (in this ship's space, with the hull's outward normal): for dev close-ups.
    public readonly List<(Vector3 point, Vector3 normal)> HoleSpots = new List<(Vector3, Vector3)>();

    // ---------------- Damage ----------------

    // Plating dished in around a hit and buckled in ripples round it. `radius` and `depth` in world units.
    public void Dent(MeshFilter filter, Vector3 point, Vector3 normal, float radius, float depth, Transform marks)
    {
        if (!Local(filter, point, normal, radius, out Vector3 c, out Vector3 n, out float r, out float scale)) return;
        float d = depth / scale;
        float seed = Random.value * 100f;
        Displace(filter, marks, c, r * 1.05f, r / 7f, p =>
        {
            Vector3 offset = p - c;
            float along = Vector3.Dot(offset, n);
            float across = (offset - n * along).magnitude;
            if (across >= r || Mathf.Abs(along) > r * 0.6f) return Vector3.zero;
            float t = across / r;
            float bowl = (1f - t * t) * (1f - t * t);
            // Buckles: rings of plating that crumpled as the middle went in, uneven round the hit.
            float buckle = Mathf.Sin(t * Mathf.PI * 3f) * 0.22f * (1f - t);
            float uneven = 0.75f + 0.5f * Mathf.PerlinNoise(seed + p.x * 3f / r, seed + p.z * 3f / r);
            return -n * d * (bowl + buckle) * uneven;
        });
    }

    // A shell hole: plating cut out in a ragged ring, the edges round it petalled inward (a shell going in) or
    // outward (an explosion inside), and a burnt-out cavity behind it, glowing.
    public void Hole(MeshFilter filter, Vector3 point, Vector3 normal, float radius, bool blownOut, Material interior, Transform marks)
    {
        HullDamage hullDamage = GetComponent<HullDamage>();
        int maxHoles = hullDamage != null ? Mathf.RoundToInt(MaxHoles * Mathf.Clamp(hullDamage.Size / 27f, 1f, 2f)) : MaxHoles;
        if (holes >= maxHoles) return;
        if (!Local(filter, point, normal, radius, out Vector3 c, out Vector3 n, out float r, out float scale)) return;
        holes++;
        HoleSpots.Add((transform.InverseTransformPoint(point), transform.InverseTransformDirection(normal)));
        Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 v = Vector3.Cross(n, u);
        float seed = Random.value * 100f;
        int petals = Random.Range(4, 8);
        float twist = Random.value * Mathf.PI * 2f;

        float Ragged(Vector3 p)
        {
            Vector3 o = p - c;
            float angle = Mathf.Atan2(Vector3.Dot(o, v), Vector3.Dot(o, u));
            return r * (0.75f + 0.5f * Mathf.PerlinNoise(seed + Mathf.Cos(angle) * 1.7f, seed + Mathf.Sin(angle) * 1.7f));
        }

        // The petals: plating round the hole bent in (or out), most at the edge of the hole, in torn tongues.
        Displace(filter, marks, c, r * 2.5f, r / 6f, p =>
        {
            Vector3 o = p - c;
            float along = Vector3.Dot(o, n);
            Vector3 flat = o - n * along;
            float across = flat.magnitude;
            float edge = Ragged(p);
            if (across > edge * 1.9f || Mathf.Abs(along) > r) return Vector3.zero;
            float t = Mathf.Clamp01((edge * 1.9f - across) / (edge * 0.9f)); // 1 at the hole's edge, 0 out at the rim
            float angle = Mathf.Atan2(Vector3.Dot(o, v), Vector3.Dot(o, u));
            float tongue = 0.55f + 0.45f * Mathf.Cos(angle * petals + twist);
            float bend = t * t * tongue * r * 0.55f;
            Vector3 radial = across > 1e-5f ? flat / across : Vector3.zero;
            return (blownOut ? n : -n) * bend + radial * bend * (blownOut ? 0.25f : -0.15f);
        }, (a, b, cc) =>
        {
            Vector3 centroid = (a + b + cc) / 3f;
            Vector3 o = centroid - c;
            float along = Vector3.Dot(o, n);
            return Mathf.Abs(along) < r * 0.8f && (o - n * along).magnitude < Ragged(centroid) * 0.62f;
        });

        Cavity(filter, c, n, r * 0.6f, scale, interior);
    }

    // A plate blown loose and folded back on its seam: a strip of hull hinged along one edge and bent out.
    public void Peel(MeshFilter filter, Vector3 point, Vector3 normal, float size, Transform marks)
    {
        if (!Local(filter, point, normal, size, out Vector3 c, out Vector3 n, out float s, out _)) return;
        Vector3 hingeDir = Vector3.Cross(n, Random.onUnitSphere).normalized;
        if (hingeDir.sqrMagnitude < 0.5f) return;
        Vector3 outDir = Vector3.Cross(hingeDir, n);
        float width = s * Random.Range(0.5f, 0.8f), length = s * Random.Range(0.8f, 1.2f);
        float angle = Random.Range(35f, 75f) * Mathf.Deg2Rad;
        Vector3 hinge = c - outDir * length * 0.5f;
        Displace(filter, marks, c, Mathf.Max(width, length) * 1.3f, s / 7f, p =>
        {
            Vector3 o = p - hinge;
            float along = Vector3.Dot(o, n);
            float x = Vector3.Dot(o, hingeDir), y = Vector3.Dot(o, outDir);
            if (Mathf.Abs(x) > width || y < 0f || y > length || Mathf.Abs(along) > length * 0.3f) return Vector3.zero;
            // Bent progressively from the hinge (a curl, not a fold), tapering off at the strip's ends.
            float k = y / length;
            float bent = angle * k;
            float taper = Mathf.Clamp01((width - Mathf.Abs(x)) / (width * 0.25f));
            Vector3 target = hinge + hingeDir * x + (outDir * Mathf.Cos(bent) + n * Mathf.Sin(bent)) * y + n * along;
            return (target - p) * taper;
        });
    }

    // ---------------- Mesh work ----------------

    private bool Local(MeshFilter filter, Vector3 point, Vector3 normal, float radius, out Vector3 c, out Vector3 n, out float r, out float scale)
    {
        c = n = default;
        r = scale = 1f;
        if (!Enabled || filter == null || filter.sharedMesh == null) return false;
        Surface surface = Get(filter);
        if (surface == null) return false;
        Transform t = filter.transform;
        scale = Mathf.Max(1e-5f, t.lossyScale.x);
        c = t.InverseTransformPoint(point);
        n = t.InverseTransformDirection(normal).normalized;
        r = radius / scale;
        return true;
    }

    private Surface Get(MeshFilter filter)
    {
        if (surfaces.TryGetValue(filter, out Surface surface)) return surface;
        Mesh source = filter.sharedMesh;
        if (!source.isReadable) return null;
        Mesh copy = Instantiate(source);
        copy.name = source.name;
        copy.MarkDynamic();
        filter.sharedMesh = copy; // Not freed with the ship: its wreck's loose parts may still draw it (UnloadUnusedAssets does).
        surface = new Surface { mesh = copy, edit = Editable.Read(copy) };
        surfaces[filter] = surface;
        return surface;
    }

    // Refines the surface (and the decals laid on it) around `c` to edges of `target`, then moves every vertex
    // by `offset` (surface-local space) and drops the triangles `remove` picks. Vertices are moved by where they
    // are, so the mesh's split vertices (hard edges, UV seams) move together and nothing tears that shouldn't.
    private static readonly Unity.Profiling.ProfilerMarker DisplaceMarker = new Unity.Profiling.ProfilerMarker("HullDeformation.Displace"),
        GetMarker = new Unity.Profiling.ProfilerMarker("HullDeformation.Get"), EditMarker = new Unity.Profiling.ProfilerMarker("HullDeformation.Edit"),
        WriteMarker = new Unity.Profiling.ProfilerMarker("HullDeformation.Write"), MarksMarker = new Unity.Profiling.ProfilerMarker("HullDeformation.Marks");

    private void Displace(MeshFilter filter, Transform marks, Vector3 c, float reach, float target,
        System.Func<Vector3, Vector3> offset, System.Func<Vector3, Vector3, Vector3, bool> remove = null)
    {
        using var displaceTiming = DisplaceMarker.Auto();
        GetMarker.Begin();
        Surface surface = Get(filter);
        GetMarker.End();
        if (surface == null) return;
        EditMarker.Begin();
        bool edited = surface.edit.Edit(c, reach, target, 12000, offset, remove);
        EditMarker.End();
        if (!edited) return;
        WriteMarker.Begin();
        surface.edit.Write(surface.mesh);
        WriteMarker.End();
        HullScarring.Forget(surface.mesh);

        // Decals cut from this surface share its transform and local space: bend (and cut) them with it - over the
        // next few frames, two a frame, with a coarser refinement than the hull's (doing the twenty-odd decals
        // round a hit at once was a 140 ms hitch).
        if (marks == null) return;
        foreach (MeshFilter mark in marks.GetComponentsInChildren<MeshFilter>())
        {
            if (mark.name != "Mark" || mark.sharedMesh == null || !mark.sharedMesh.isReadable) continue;
            if ((mark.transform.position - filter.transform.position).sqrMagnitude > 1e-4f || Quaternion.Angle(mark.transform.rotation, filter.transform.rotation) > 0.1f) continue;
            Bounds near = mark.sharedMesh.bounds;
            near.Expand(reach * 2f);
            if (!near.Contains(c)) continue;
            pendingMarks.Enqueue(() =>
            {
                if (mark == null || mark.sharedMesh == null) return;
                Editable e = Editable.Read(mark.sharedMesh);
                if (e.Edit(c, reach, target * 2f, 800, offset, remove)) e.Write(mark.sharedMesh);
            });
        }
    }

    private readonly Queue<System.Action> pendingMarks = new Queue<System.Action>();

    private void Update()
    {
        if (pendingMarks.Count == 0) return;
        using var marksTiming = MarksMarker.Auto();
        for (int i = 0; i < 2 && pendingMarks.Count > 0; i++) pendingMarks.Dequeue()();
    }

    // The burnt-out space behind a hole: a dark cup set into the hull, its inside glowing, so a hole reads as
    // a hole into the ship rather than a window onto the stars.
    private void Cavity(MeshFilter filter, Vector3 c, Vector3 n, float r, float scale, Material interior)
    {
        if (interior == null) return;
        if (cavities == null)
        {
            cavities = new GameObject("HullCavities").transform;
            cavities.SetParent(filter.transform.parent != null ? filter.transform.parent : transform, false);
        }
        const int rings = 4, segments = 12;
        Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 v = Vector3.Cross(n, u);
        List<Vector3> points = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();
        for (int ring = 0; ring <= rings; ring++)
        {
            float t = ring / (float)rings;           // 0 at the mouth, 1 at the bottom
            float ringRadius = r * 1.25f * Mathf.Cos(t * Mathf.PI * 0.45f) * (ring == rings ? 0.2f : 1f);
            float depth = r * 1.1f * Mathf.Sin(t * Mathf.PI * 0.5f) + r * 0.05f;
            for (int s = 0; s < segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                float wobble = 1f + Random.Range(-0.15f, 0.15f);
                Vector3 radial = u * Mathf.Cos(a) + v * Mathf.Sin(a);
                points.Add(c + radial * ringRadius * wobble - n * depth);
                normals.Add((-radial * 0.6f + n).normalized); // facing out of the cup, toward whoever looks in
                uvs.Add(new Vector2(s / (float)segments, t));
            }
        }
        for (int ring = 0; ring < rings; ring++)
        {
            for (int s = 0; s < segments; s++)
            {
                int a = ring * segments + s, b = ring * segments + (s + 1) % segments;
                int c2 = a + segments, d = b + segments;
                // Seen from outside the hull (along -n), the inside of the cup faces the viewer.
                tris.Add(a); tris.Add(c2); tris.Add(b);
                tris.Add(b); tris.Add(c2); tris.Add(d);
            }
        }
        Mesh mesh = new Mesh { name = "HullCavity" };
        mesh.SetVertices(points);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        // Make sure it faces out of the hull whichever way the winding came out.
        Vector3 face = Vector3.Cross(points[tris[1]] - points[tris[0]], points[tris[2]] - points[tris[0]]);
        if (Vector3.Dot(face, n) < 0f)
        {
            for (int k = 0; k < tris.Count; k += 3) (tris[k + 1], tris[k + 2]) = (tris[k + 2], tris[k + 1]);
            mesh.SetTriangles(tris, 0);
        }
        mesh.RecalculateBounds();
        owned.Add(mesh);

        GameObject go = new GameObject("HullCavity");
        go.layer = filter.gameObject.layer;
        go.transform.SetParent(cavities, false);
        go.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
        go.transform.localScale = filter.transform.lossyScale;
        go.transform.SetParent(cavities, true);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CavityMaterial(interior);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Inside a hole: near black, with the burnt metal glowing only round the lip and dying off into the dark.
    private static Material cavityMaterial;
    private static Material CavityMaterial(Material interior)
    {
        if (cavityMaterial != null) return cavityMaterial;
        cavityMaterial = new Material(interior) { name = "HullCavity" };
        const int w = 32, h = 64;
        Texture2D glow = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HullCavityGlow" };
        Color[] pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float depth = y / (h - 1f); // the cavity's v: 0 at the lip, 1 at the bottom
            for (int x = 0; x < w; x++)
            {
                float flicker = 0.55f + 0.45f * Mathf.PerlinNoise(x * 0.35f, 3.1f);
                float heat = Mathf.Clamp01(1f - depth / (0.22f + 0.12f * flicker)) * flicker;
                pixels[y * w + x] = new Color(heat, heat * 0.42f, heat * 0.12f, 1f);
            }
        }
        glow.SetPixels(pixels);
        glow.Apply(false, true);
        if (cavityMaterial.HasProperty("_BaseMap")) cavityMaterial.SetTexture("_BaseMap", null);
        if (cavityMaterial.HasProperty("_MainTex")) cavityMaterial.SetTexture("_MainTex", null);
        if (cavityMaterial.HasProperty("_BaseColor")) cavityMaterial.SetColor("_BaseColor", new Color(0.03f, 0.026f, 0.024f, 1f));
        if (cavityMaterial.HasProperty("_Smoothness")) cavityMaterial.SetFloat("_Smoothness", 0.05f);
        if (cavityMaterial.HasProperty("_Metallic")) cavityMaterial.SetFloat("_Metallic", 0f);
        cavityMaterial.SetTexture("_EmissionMap", glow);
        cavityMaterial.SetColor("_EmissionColor", new Color(2.2f, 2.2f, 2.2f, 1f));
        cavityMaterial.EnableKeyword("_EMISSION");
        return cavityMaterial;
    }

    private void OnDestroy()
    {
        foreach (Mesh mesh in owned) if (mesh != null) Destroy(mesh);
    }
}
