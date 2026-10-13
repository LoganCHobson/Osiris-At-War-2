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
