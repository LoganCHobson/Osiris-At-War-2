using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class MeshSlicer
{
    public class MeshData
    {
        public Vector3[] positions;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Vector2[] uvs;
        public int[][] submeshes;
        public bool hasNormals;
        public bool hasTangents;
        public bool hasUVs;
        public float weldTolerance;

        // Triangles bucketed by position (built once per data, on demand or on a worker thread), so a query
        // around one point - a scorch mark - looks at the triangles near it, not the whole hull.
        public TriangleGrid grid;
        public TriangleGrid Grid => grid ??= TriangleGrid.Build(this);

        public static MeshData Read(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable) return null;

            MeshData data = new MeshData();
            data.positions = mesh.vertices;
            int count = data.positions.Length;

            data.normals = mesh.normals;
            data.hasNormals = data.normals.Length == count;
            if (!data.hasNormals) data.normals = new Vector3[count];

            data.tangents = mesh.tangents;
            data.hasTangents = data.tangents.Length == count;
            if (!data.hasTangents) data.tangents = new Vector4[count];

            data.uvs = mesh.uv;
            data.hasUVs = data.uvs.Length == count;
            if (!data.hasUVs) data.uvs = new Vector2[count];

            data.submeshes = new int[mesh.subMeshCount][];
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                data.submeshes[s] = mesh.GetTriangles(s);
            }

            data.weldTolerance = Mathf.Max(1e-6f, mesh.bounds.size.magnitude * 1e-5f);
            return data;
        }
    }

    private class Piece
    {
        public readonly List<Vector3> positions;
        public readonly List<Vector3> normals;
        public readonly List<Vector4> tangents;
        public readonly List<Vector2> uvs;
        public readonly List<int>[] submeshes;
        public readonly bool hasNormals;
        public readonly bool hasTangents;
        public readonly bool hasUVs;

        public int CapIndex => submeshes.Length - 1;

        public Piece(int sourceSubmeshes, bool normals, bool tangents, bool uvs, int capacity)
        {
            positions = new List<Vector3>(capacity);
            this.normals = new List<Vector3>(capacity);
            this.tangents = new List<Vector4>(capacity);
            this.uvs = new List<Vector2>(capacity);
            submeshes = new List<int>[sourceSubmeshes + 1];
            for (int i = 0; i < submeshes.Length; i++) submeshes[i] = new List<int>();
            hasNormals = normals;
            hasTangents = tangents;
            hasUVs = uvs;
        }

        public int Add(Vector3 position, Vector3 normal, Vector4 tangent, Vector2 uv)
        {
            positions.Add(position);
            normals.Add(normal);
            tangents.Add(tangent);
            uvs.Add(uv);
            return positions.Count - 1;
        }

        public int Copy(Piece source, int index)
        {
            return Add(source.positions[index], source.normals[index], source.tangents[index], source.uvs[index]);
        }

        public int Lerp(Piece source, int a, int b, float t)
        {
            Vector3 normal = Vector3.Lerp(source.normals[a], source.normals[b], t);
            if (normal.sqrMagnitude > 0f) normal.Normalize();
            return Add(
                Vector3.Lerp(source.positions[a], source.positions[b], t),
                normal,
                Vector4.Lerp(source.tangents[a], source.tangents[b], t),
                Vector2.Lerp(source.uvs[a], source.uvs[b], t));
        }

        public bool HasTriangles
        {
            get
            {
                foreach (List<int> list in submeshes)
                {
                    if (list.Count > 0) return true;
                }
                return false;
            }
        }

        public Mesh ToMesh(string name, out bool capped)
        {
            capped = submeshes[CapIndex].Count > 0;
            if (!HasTriangles) return null;

            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            if (hasNormals) mesh.SetNormals(normals);
            if (hasTangents) mesh.SetTangents(tangents);
            if (hasUVs) mesh.SetUVs(0, uvs);

            int count = capped ? submeshes.Length : submeshes.Length - 1;
            mesh.subMeshCount = count;
            for (int s = 0; s < count; s++)
            {
                mesh.SetTriangles(submeshes[s], s, false);
            }

            if (!hasNormals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    public sealed class TriangleGrid
    {
        private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        private readonly float cell;
        // Flat triangle ids: submesh << 26 | first index.
        public static int Id(int submesh, int k) => (submesh << 26) | k;
        public static int Submesh(int id) => id >> 26;
        public static int First(int id) => id & ((1 << 26) - 1);

        private TriangleGrid(float cell) { this.cell = cell; }

        private long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

        public static TriangleGrid Build(MeshData data)
        {
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (Vector3 p in data.positions) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            float size = data.positions.Length > 0 ? Mathf.Max(hi.x - lo.x, Mathf.Max(hi.y - lo.y, hi.z - lo.z)) : 1f;
            TriangleGrid grid = new TriangleGrid(Mathf.Max(1e-4f, size / 32f));
            for (int s = 0; s < data.submeshes.Length; s++)
            {
                int[] tris = data.submeshes[s];
                for (int k = 0; k + 2 < tris.Length; k += 3)
                {
                    Vector3 a = data.positions[tris[k]], b = data.positions[tris[k + 1]], c = data.positions[tris[k + 2]];
                    Vector3 tlo = Vector3.Min(a, Vector3.Min(b, c)), thi = Vector3.Max(a, Vector3.Max(b, c));
                    int x0 = Mathf.FloorToInt(tlo.x / grid.cell), y0 = Mathf.FloorToInt(tlo.y / grid.cell), z0 = Mathf.FloorToInt(tlo.z / grid.cell);
                    int x1 = Mathf.FloorToInt(thi.x / grid.cell), y1 = Mathf.FloorToInt(thi.y / grid.cell), z1 = Mathf.FloorToInt(thi.z / grid.cell);
                    // A huge triangle goes in a capped block of cells around its middle (rare: hulls are mostly small tris).
                    if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 512) { x0 = x1 = (x0 + x1) / 2; y0 = y1 = (y0 + y1) / 2; z0 = z1 = (z0 + z1) / 2; grid.Add(grid.Key(x0, y0, z0), Id(s, k), true); continue; }
                    for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++) grid.Add(grid.Key(x, y, z), Id(s, k), false);
                }
            }
            return grid;
        }

        private readonly List<int> huge = new List<int>();

        private void Add(long key, int id, bool isHuge)
        {
            if (isHuge) { huge.Add(id); return; }
            if (!cells.TryGetValue(key, out List<int> list)) cells[key] = list = new List<int>();
            list.Add(id);
        }

        // Every triangle that may come within `radius` of `centre` (each once).
        public void Near(Vector3 centre, float radius, List<int> results, HashSet<int> seen)
        {
            results.Clear(); seen.Clear();
            int x0 = Mathf.FloorToInt((centre.x - radius) / cell), x1 = Mathf.FloorToInt((centre.x + radius) / cell);
            int y0 = Mathf.FloorToInt((centre.y - radius) / cell), y1 = Mathf.FloorToInt((centre.y + radius) / cell);
            int z0 = Mathf.FloorToInt((centre.z - radius) / cell), z1 = Mathf.FloorToInt((centre.z + radius) / cell);
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 4096) { foreach (List<int> l in cells.Values) foreach (int id in l) if (seen.Add(id)) results.Add(id); }
            else for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
                if (cells.TryGetValue(Key(x, y, z), out List<int> list)) foreach (int id in list) if (seen.Add(id)) results.Add(id);
            foreach (int id in huge) if (seen.Add(id)) results.Add(id);
        }
    }

    public struct Result
    {
        public Mesh mesh;
        public bool capped;
    }

    public static Result[] Slice(MeshData data, Plane[] planes, bool cap, string name, Vector3 referenceUp)
    {
        Piece rest = FromData(data);
        Result[] results = new Result[planes.Length + 1];

        for (int i = 0; i < planes.Length; i++)
        {
            Split(rest, planes[i], cap, data.weldTolerance, referenceUp, out Piece back, out Piece front);
            results[i].mesh = back.ToMesh(name + "_" + i, out results[i].capped);
            rest = front;
        }

        int last = planes.Length;
        results[last].mesh = rest.ToMesh(name + "_" + last, out results[last].capped);
        return results;
    }

    // The same slice in two halves, so the heavy part can run off the main thread: SliceData does the cutting
    // (pure maths on MeshData - safe on a worker thread), BuildMeshes turns the pieces into Meshes (main thread).
    public sealed class Sliced { private Piece[] pieces; internal Sliced(object p) { pieces = (Piece[])p; } public int Count => pieces.Length; internal object Pieces => pieces; }

    public static Sliced SliceData(MeshData data, Plane[] planes, bool cap, Vector3 referenceUp)
    {
        Piece rest = FromData(data);
        Piece[] pieces = new Piece[planes.Length + 1];
        for (int i = 0; i < planes.Length; i++)
        {
            Split(rest, planes[i], cap, data.weldTolerance, referenceUp, out Piece back, out Piece front);
            pieces[i] = back;
            rest = front;
        }
        pieces[planes.Length] = rest;
        return new Sliced(pieces);
    }

    public static Result[] BuildMeshes(Sliced sliced, string name)
    {
        Piece[] pieces = (Piece[])sliced.Pieces;
        Result[] results = new Result[pieces.Length];
        for (int i = 0; i < pieces.Length; i++) results[i].mesh = pieces[i].ToMesh(name + "_" + i, out results[i].capped);
        return results;
    }

    public static Mesh ExtractPatch(MeshData data, Vector3 center, float radius, string name, out Vector3 patchCenter)
    {
        patchCenter = center;
        Piece patch = new Piece(data.submeshes.Length, data.hasNormals, data.hasTangents, data.hasUVs, 256);

        int count = data.positions.Length;
        int[] outer = NewMap(count);
        int[] inner = NewMap(count);
        float radiusSqr = radius * radius;
        Vector3 sum = Vector3.zero;
        int triangles = 0;

        int Outer(int index)
        {
            if (outer[index] < 0) outer[index] = patch.Add(data.positions[index], data.normals[index], data.tangents[index], data.uvs[index]);
            return outer[index];
        }

        int Inner(int index)
        {
            if (inner[index] < 0)
            {
                Vector4 tangent = data.tangents[index];
                inner[index] = patch.Add(data.positions[index], -data.normals[index], new Vector4(-tangent.x, -tangent.y, -tangent.z, tangent.w), data.uvs[index]);
            }
            return inner[index];
        }

        for (int s = 0; s < data.submeshes.Length; s++)
        {
            int[] tris = data.submeshes[s];
            List<int> target = patch.submeshes[s];

            for (int k = 0; k < tris.Length; k += 3)
            {
                int i0 = tris[k], i1 = tris[k + 1], i2 = tris[k + 2];
                Vector3 centroid = (data.positions[i0] + data.positions[i1] + data.positions[i2]) / 3f;
                if ((centroid - center).sqrMagnitude > radiusSqr) continue;

                target.Add(Outer(i0));
                target.Add(Outer(i1));
                target.Add(Outer(i2));

                target.Add(Inner(i0));
                target.Add(Inner(i2));
                target.Add(Inner(i1));

                sum += centroid;
                triangles++;
            }
        }

        if (triangles < 2) return null;

        patchCenter = sum / triangles;
        return patch.ToMesh(name, out _);
    }

    // A chunk of plating torn off the hull: the hull surface around `center`, cut to an irregular polygon of
    // `sides` straight edges (so it comes away as a plate, not as whatever long triangles happen to reach the
    // point - on the Human hulls those made long spikes) and given `thickness`: the outer face, a back face and
    // edge walls, all in the hull's own material, so the plate reads as a solid slab of metal.
    // A plate's cut, as pure maths (safe on a worker thread when given its own `rng`); PlateMesh builds it.
    public sealed class PlateCut { internal object solid; public Vector3 center; }

    public static PlateCut ExtractPlateCut(MeshData data, Vector3 center, Vector3 normal, float halfSize, float thickness, int sides, System.Random rng)
    {
        float Value() => rng != null ? (float)rng.NextDouble() : Random.value;
        float Range(float lo, float hi) => lo + (hi - lo) * Value();
        Vector3 plateCenter;
        plateCenter = center;
        normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
        Piece piece = new Piece(data.submeshes.Length, data.hasNormals, data.hasTangents, data.hasUVs, 256);
        int[] map = NewMap(data.positions.Length);
        float reachSqr = halfSize * halfSize * 2.5f * 2.5f;

        int Take(int index)
        {
            if (map[index] < 0) map[index] = piece.Add(data.positions[index], data.normals[index], data.tangents[index], data.uvs[index]);
            return map[index];
        }

        int found = 0;
        for (int s = 0; s < data.submeshes.Length; s++)
        {
            int[] tris = data.submeshes[s];
            for (int k = 0; k < tris.Length; k += 3)
            {
                Vector3 a = data.positions[tris[k]], b = data.positions[tris[k + 1]], c = data.positions[tris[k + 2]];
                Vector3 faceNormal = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(faceNormal, normal) <= 0.2f * faceNormal.magnitude) continue; // the far side of a thin hull
                if ((ClosestOnTriangle(center, a, b, c) - center).sqrMagnitude > reachSqr) continue;
                piece.submeshes[s].Add(Take(tris[k]));
                piece.submeshes[s].Add(Take(tris[k + 1]));
                piece.submeshes[s].Add(Take(tris[k + 2]));
                found++;
            }
        }
        if (found == 0) return null;

        // Cut to the polygon: each side a plane facing out from the centre; keep what's inside.
        Vector3 across = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 along = Vector3.Cross(normal, across);
        float turn = Value() * Mathf.PI * 2f;
        for (int i = 0; i < sides; i++)
        {
            float angle = turn + (i + Range(-0.2f, 0.2f)) * Mathf.PI * 2f / sides;
            Vector3 outward = across * Mathf.Cos(angle) + along * Mathf.Sin(angle);
            Split(piece, new Plane(outward, center + outward * halfSize * Range(0.7f, 1.1f)), false, data.weldTolerance, normal, out Piece inside, out _);
            piece = inside;
            if (!piece.HasTriangles) return null;
        }

        // Thickness: outer face, back face pushed in along the plate's normal, and walls along the open edges.
        Piece solid = new Piece(data.submeshes.Length, data.hasNormals, data.hasTangents, data.hasUVs, piece.positions.Count * 3);
        Vector3 inset = -normal * thickness;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < piece.positions.Count; i++) sum += piece.positions[i];
        plateCenter = sum / piece.positions.Count;

        Dictionary<(Vector3Int, Vector3Int), int> edgeUses = new Dictionary<(Vector3Int, Vector3Int), int>();
        List<(int a, int b)> edges = new List<(int, int)>();
        float weld = 1f / Mathf.Max(1e-6f, data.weldTolerance * 4f);
        Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * weld), Mathf.RoundToInt(p.y * weld), Mathf.RoundToInt(p.z * weld));
        (Vector3Int, Vector3Int) Edge(Vector3Int u, Vector3Int v) =>
            u.x < v.x || (u.x == v.x && (u.y < v.y || (u.y == v.y && u.z <= v.z))) ? (u, v) : (v, u);

        for (int s = 0; s < piece.submeshes.Length - 1; s++)
        {
            List<int> tris = piece.submeshes[s];
            for (int k = 0; k < tris.Count; k += 3)
            {
                int i0 = tris[k], i1 = tris[k + 1], i2 = tris[k + 2];
                int f0 = solid.Copy(piece, i0), f1 = solid.Copy(piece, i1), f2 = solid.Copy(piece, i2);
                solid.submeshes[s].Add(f0); solid.submeshes[s].Add(f1); solid.submeshes[s].Add(f2);
                int b0 = Back(solid, piece, i0, inset), b1 = Back(solid, piece, i1, inset), b2 = Back(solid, piece, i2, inset);
                solid.submeshes[s].Add(b0); solid.submeshes[s].Add(b2); solid.submeshes[s].Add(b1);

                foreach ((int a, int b) e in new[] { (i0, i1), (i1, i2), (i2, i0) })
                {
                    Vector3Int ka = Key(piece.positions[e.a]), kb = Key(piece.positions[e.b]);
                    var key = Edge(ka, kb);
                    edgeUses.TryGetValue(key, out int uses);
                    edgeUses[key] = uses + 1;
                    edges.Add(e);
                }
            }
        }

        List<int> walls = solid.submeshes[0]; // bare metal edges: the plate's own material, not the wreck interior
        foreach ((int a, int b) in edges)
        {
            Vector3 pa = piece.positions[a], pb = piece.positions[b];
            Vector3Int ka = Key(pa), kb = Key(pb);
            if (edgeUses[Edge(ka, kb)] != 1) continue; // inside the plate
            Vector3 mid = (pa + pb) * 0.5f;
            Vector3 outward = mid - plateCenter;
            outward -= normal * Vector3.Dot(outward, normal);
            Vector3 wallNormal = Vector3.Cross(pb - pa, normal).normalized;
            if (Vector3.Dot(wallNormal, outward) < 0f) wallNormal = -wallNormal;
            Vector4 tangent = new Vector4(normal.x, normal.y, normal.z, 1f);
            int wa = solid.Add(pa, wallNormal, tangent, piece.uvs[a]), wb = solid.Add(pb, wallNormal, tangent, piece.uvs[b]);
            int wc = solid.Add(pb + inset, wallNormal, tangent, piece.uvs[b]), wd = solid.Add(pa + inset, wallNormal, tangent, piece.uvs[a]);
            // Wind so the wall faces out (Unity's front face: cross(p1 - p0, p2 - p0) points at the viewer).
            bool flip = Vector3.Dot(Vector3.Cross(pb - pa, (pb + inset) - pa), wallNormal) < 0f;
            if (!flip) { walls.Add(wa); walls.Add(wb); walls.Add(wc); walls.Add(wa); walls.Add(wc); walls.Add(wd); }
            else { walls.Add(wa); walls.Add(wc); walls.Add(wb); walls.Add(wa); walls.Add(wd); walls.Add(wc); }
        }

        return new PlateCut { solid = solid, center = plateCenter };
    }

    public static Mesh PlateMesh(PlateCut cut, string name) => cut == null ? null : ((Piece)cut.solid).ToMesh(name, out _);

    public static Mesh ExtractPlate(MeshData data, Vector3 center, Vector3 normal, float halfSize, float thickness, int sides, string name, out Vector3 plateCenter)
    {
        PlateCut cut = ExtractPlateCut(data, center, normal, halfSize, thickness, sides, null);
        plateCenter = cut != null ? cut.center : center;
        return PlateMesh(cut, name);
    }

    private static int Back(Piece solid, Piece source, int index, Vector3 inset)
    {
        Vector4 t = source.tangents[index];
        return solid.Add(source.positions[index] + inset, -source.normals[index], new Vector4(-t.x, -t.y, -t.z, t.w), source.uvs[index]);
    }

    public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    private static Piece FromData(MeshData data)
    {
        int count = data.positions.Length;
        Piece piece = new Piece(data.submeshes.Length, data.hasNormals, data.hasTangents, data.hasUVs, count);
        piece.positions.AddRange(data.positions);
        piece.normals.AddRange(data.normals);
        piece.tangents.AddRange(data.tangents);
        piece.uvs.AddRange(data.uvs);
        for (int s = 0; s < data.submeshes.Length; s++)
        {
            piece.submeshes[s].AddRange(data.submeshes[s]);
        }
        return piece;
    }

    private static void Split(Piece source, Plane plane, bool cap, float weldTolerance, Vector3 referenceUp, out Piece back, out Piece front)
    {
        int count = source.positions.Count;
        int submeshCount = source.submeshes.Length - 1;
        back = new Piece(submeshCount, source.hasNormals, source.hasTangents, source.hasUVs, count / 2);
        front = new Piece(submeshCount, source.hasNormals, source.hasTangents, source.hasUVs, count / 2);

        float[] distance = new float[count];
        for (int i = 0; i < count; i++)
        {
            distance[i] = plane.GetDistanceToPoint(source.positions[i]);
        }

        int[] backMap = NewMap(count);
        int[] frontMap = NewMap(count);
        List<Vector3> segments = new List<Vector3>();

        for (int s = 0; s < source.submeshes.Length; s++)
        {
            List<int> tris = source.submeshes[s];
            List<int> backTris = back.submeshes[s];
            List<int> frontTris = front.submeshes[s];

            for (int k = 0; k < tris.Count; k += 3)
            {
                int i0 = tris[k], i1 = tris[k + 1], i2 = tris[k + 2];
                bool f0 = distance[i0] >= 0f;
                bool f1 = distance[i1] >= 0f;
                bool f2 = distance[i2] >= 0f;

                if (f0 && f1 && f2)
                {
                    frontTris.Add(Map(front, frontMap, source, i0));
                    frontTris.Add(Map(front, frontMap, source, i1));
                    frontTris.Add(Map(front, frontMap, source, i2));
                    continue;
                }

                if (!f0 && !f1 && !f2)
                {
                    backTris.Add(Map(back, backMap, source, i0));
                    backTris.Add(Map(back, backMap, source, i1));
                    backTris.Add(Map(back, backMap, source, i2));
                    continue;
                }

                int a, b, c;
                bool loneFront;
                if (f0 == f1) { a = i2; b = i0; c = i1; loneFront = f2; }
                else if (f0 == f2) { a = i1; b = i2; c = i0; loneFront = f1; }
                else { a = i0; b = i1; c = i2; loneFront = f0; }

                float tab = distance[a] / (distance[a] - distance[b]);
                float tac = distance[a] / (distance[a] - distance[c]);

                Piece lone = loneFront ? front : back;
                int[] loneMap = loneFront ? frontMap : backMap;
                Piece other = loneFront ? back : front;
                int[] otherMap = loneFront ? backMap : frontMap;
                List<int> loneTris = lone.submeshes[s];
                List<int> otherTris = other.submeshes[s];

                int la = Map(lone, loneMap, source, a);
                int lab = lone.Lerp(source, a, b, tab);
                int lac = lone.Lerp(source, a, c, tac);
                loneTris.Add(la);
                loneTris.Add(lab);
                loneTris.Add(lac);

                int ob = Map(other, otherMap, source, b);
                int oc = Map(other, otherMap, source, c);
                int oab = other.Lerp(source, a, b, tab);
                int oac = other.Lerp(source, a, c, tac);
                otherTris.Add(oab);
                otherTris.Add(ob);
                otherTris.Add(oc);
                otherTris.Add(oab);
                otherTris.Add(oc);
                otherTris.Add(oac);

                segments.Add(lone.positions[lab]);
                segments.Add(lone.positions[lac]);
            }
        }

        if (cap && segments.Count > 0)
        {
            BuildCaps(segments, plane.normal, weldTolerance, referenceUp, back, front);
        }
    }

    private static void BuildCaps(List<Vector3> segments, Vector3 normal, float tolerance, Vector3 referenceUp, Piece back, Piece front)
    {
        Dictionary<Vector3Int, int> lookup = new Dictionary<Vector3Int, int>();
        List<Vector3> points = new List<Vector3>();
        List<List<int>> neighbours = new List<List<int>>();
        HashSet<long> edges = new HashSet<long>();
        float inverse = 1f / tolerance;

        int Node(Vector3 point)
        {
            Vector3Int key = new Vector3Int(
                Mathf.RoundToInt(point.x * inverse),
                Mathf.RoundToInt(point.y * inverse),
                Mathf.RoundToInt(point.z * inverse));
            if (!lookup.TryGetValue(key, out int id))
            {
                id = points.Count;
                points.Add(point);
                neighbours.Add(new List<int>(2));
                lookup[key] = id;
            }
            return id;
        }

        for (int i = 0; i < segments.Count; i += 2)
        {
            int a = Node(segments[i]);
            int b = Node(segments[i + 1]);
            if (a == b || !edges.Add(EdgeKey(a, b))) continue;
            neighbours[a].Add(b);
            neighbours[b].Add(a);
        }

        Vector3 up = referenceUp.sqrMagnitude > 0f ? referenceUp.normalized : Vector3.up;
        Vector3 reference = Mathf.Abs(Vector3.Dot(normal, up)) < 0.9f ? up : (Mathf.Abs(normal.x) < 0.9f ? Vector3.right : Vector3.forward);
        Vector3 tangent = Vector3.Cross(normal, reference).normalized;
        Vector3 bitangent = Vector3.Cross(normal, tangent);
        float uvScale = inverse * 3e-5f;

        HashSet<long> walked = new HashSet<long>();
        List<int> loop = new List<int>();

        for (int pass = 0; pass < 2; pass++)
        {
            for (int start = 0; start < points.Count; start++)
            {
                if (pass == 0 && neighbours[start].Count == 2) continue;

                while (Walk(start, neighbours, walked, loop))
                {
                    if (loop.Count >= 3) Triangulate(loop, points, normal, tangent, bitangent, uvScale, back, front);
                }
            }
        }
    }

    private static long EdgeKey(int a, int b)
    {
        return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    }

    private static bool Walk(int start, List<List<int>> neighbours, HashSet<long> walked, List<int> loop)
    {
        loop.Clear();
        int next = -1;
        foreach (int candidate in neighbours[start])
        {
            if (!walked.Contains(EdgeKey(start, candidate)))
            {
                next = candidate;
                break;
            }
        }
        if (next < 0) return false;

        loop.Add(start);
        int previous = start;
        int current = next;
        walked.Add(EdgeKey(previous, current));

        while (current != start)
        {
            loop.Add(current);
            int step = -1;
            foreach (int candidate in neighbours[current])
            {
                if (candidate != previous && !walked.Contains(EdgeKey(current, candidate)))
                {
                    step = candidate;
                    break;
                }
            }
            if (step < 0)
            {
                foreach (int candidate in neighbours[current])
                {
                    if (!walked.Contains(EdgeKey(current, candidate)))
                    {
                        step = candidate;
                        break;
                    }
                }
            }
            if (step < 0) break;

            walked.Add(EdgeKey(current, step));
            previous = current;
            current = step;
        }
        return true;
    }

    private static void Triangulate(List<int> loop, List<Vector3> points, Vector3 normal, Vector3 tangent, Vector3 bitangent, float uvScale, Piece back, Piece front)
    {
        List<Vector3> outline = new List<Vector3>(loop.Count);
        List<Vector2> flat = new List<Vector2>(loop.Count);
        foreach (int index in loop)
        {
            Vector3 point = points[index];
            outline.Add(point);
            flat.Add(new Vector2(Vector3.Dot(point, tangent), Vector3.Dot(point, bitangent)));
        }

        float extent = 0f;
        for (int i = 1; i < flat.Count; i++) extent = Mathf.Max(extent, (flat[i] - flat[0]).sqrMagnitude);
        float epsilon = extent * 1e-10f;

        List<int> polygon = new List<int>(flat.Count);
        for (int i = 0; i < flat.Count; i++) polygon.Add(i);

        bool removed = true;
        while (removed && polygon.Count > 3)
        {
            removed = false;
            for (int i = 0; i < polygon.Count && polygon.Count > 3; i++)
            {
                Vector2 a = flat[polygon[(i + polygon.Count - 1) % polygon.Count]];
                Vector2 b = flat[polygon[i]];
                Vector2 c = flat[polygon[(i + 1) % polygon.Count]];
                if (Mathf.Abs(Cross(b - a, c - b)) <= epsilon || (b - a).sqrMagnitude <= epsilon)
                {
                    polygon.RemoveAt(i);
                    removed = true;
                    i--;
                }
            }
        }
        if (polygon.Count < 3) return;

        float area = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = flat[polygon[i]];
            Vector2 b = flat[polygon[(i + 1) % polygon.Count]];
            area += a.x * b.y - b.x * a.y;
        }
        if (area < 0f) polygon.Reverse();

        void Emit(int a, int b, int c)
        {
            AddCapTriangle(back, outline[a], outline[b], outline[c], normal, tangent, bitangent, uvScale);
            AddCapTriangle(front, outline[a], outline[b], outline[c], -normal, tangent, bitangent, uvScale);
        }

        if (polygon.Count > 1500)
        {
            for (int k = 1; k < polygon.Count - 1; k++) Emit(polygon[0], polygon[k], polygon[k + 1]);
            return;
        }

        int cursor = 0;
        int failures = 0;
        while (polygon.Count > 3)
        {
            int count = polygon.Count;
            if (failures >= count)
            {
                for (int k = 1; k < count - 1; k++) Emit(polygon[0], polygon[k], polygon[k + 1]);
                return;
            }

            cursor %= count;
            int ia = polygon[(cursor + count - 1) % count];
            int ib = polygon[cursor];
            int ic = polygon[(cursor + 1) % count];

            if (IsEar(flat, polygon, ia, ib, ic))
            {
                Emit(ia, ib, ic);
                polygon.RemoveAt(cursor);
                failures = 0;
            }
            else
            {
                cursor++;
                failures++;
            }
        }

        Emit(polygon[0], polygon[1], polygon[2]);
    }

    private static bool IsEar(List<Vector2> flat, List<int> polygon, int ia, int ib, int ic)
    {
        Vector2 a = flat[ia];
        Vector2 b = flat[ib];
        Vector2 c = flat[ic];
        if (Cross(b - a, c - b) <= 0f) return false;

        foreach (int index in polygon)
        {
            if (index == ia || index == ib || index == ic) continue;
            Vector2 p = flat[index];
            if (p == a || p == b || p == c) continue;
            if (Cross(b - a, p - a) > 0f && Cross(c - b, p - b) > 0f && Cross(a - c, p - c) > 0f) return false;
        }
        return true;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static void AddCapTriangle(Piece piece, Vector3 center, Vector3 p, Vector3 q, Vector3 normal, Vector3 tangent, Vector3 bitangent, float uvScale)
    {
        Vector3 cross = Vector3.Cross(p - center, q - center);
        if (cross.sqrMagnitude <= 0f) return;
        if (Vector3.Dot(cross, normal) < 0f)
        {
            Vector3 swap = p;
            p = q;
            q = swap;
        }

        Vector4 capTangent = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
        List<int> tris = piece.submeshes[piece.CapIndex];
        tris.Add(piece.Add(center, normal, capTangent, Planar(center, tangent, bitangent, uvScale)));
        tris.Add(piece.Add(p, normal, capTangent, Planar(p, tangent, bitangent, uvScale)));
        tris.Add(piece.Add(q, normal, capTangent, Planar(q, tangent, bitangent, uvScale)));
    }

    private static Vector2 Planar(Vector3 point, Vector3 tangent, Vector3 bitangent, float scale)
    {
        return new Vector2(Vector3.Dot(point, tangent), Vector3.Dot(point, bitangent)) * scale;
    }

    private static int[] NewMap(int count)
    {
        int[] map = new int[count];
        for (int i = 0; i < count; i++) map[i] = -1;
        return map;
    }

    private static int Map(Piece target, int[] map, Piece source, int index)
    {
        if (map[index] < 0) map[index] = target.Copy(source, index);
        return map[index];
    }
}
