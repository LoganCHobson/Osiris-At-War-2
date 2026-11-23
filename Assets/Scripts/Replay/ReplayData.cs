using System;
using System.Collections.Generic;
using UnityEngine;

// The building blocks of a battle replay: tracks of values over battle time (Time.time while the battle ran).
// Everything here is plain managed maths, safe to use on worker threads.

public struct ReplayPose
{
    public Vector3 p;
    public Quaternion r;
    public Vector3 s;

    public static readonly ReplayPose Identity = new ReplayPose { p = Vector3.zero, r = Quaternion.identity, s = Vector3.one };

    // Position, rotation and scale of a transform matrix (no shear, positive scale).
    public static ReplayPose FromMatrix(in Matrix4x4 m)
    {
        Vector3 c0 = new Vector3(m.m00, m.m10, m.m20), c1 = new Vector3(m.m01, m.m11, m.m21), c2 = new Vector3(m.m02, m.m12, m.m22);
        float sx = Mathf.Sqrt(c0.x * c0.x + c0.y * c0.y + c0.z * c0.z);
        float sy = Mathf.Sqrt(c1.x * c1.x + c1.y * c1.y + c1.z * c1.z);
        float sz = Mathf.Sqrt(c2.x * c2.x + c2.y * c2.y + c2.z * c2.z);
        ReplayPose pose = new ReplayPose { p = new Vector3(m.m03, m.m13, m.m23), s = new Vector3(sx, sy, sz) };
        if (sx < 1e-8f || sy < 1e-8f || sz < 1e-8f) { pose.r = Quaternion.identity; return pose; }
        c0 /= sx; c1 /= sy; c2 /= sz;
        pose.r = FromBasis(c0, c1, c2);
        return pose;
    }

    private static Quaternion FromBasis(Vector3 x, Vector3 y, Vector3 z)
    {
        float trace = x.x + y.y + z.z;
        Quaternion q;
        if (trace > 0f)
        {
            float s = Mathf.Sqrt(trace + 1f) * 2f;
            q = new Quaternion((y.z - z.y) / s, (z.x - x.z) / s, (x.y - y.x) / s, 0.25f * s);
        }
        else if (x.x > y.y && x.x > z.z)
        {
            float s = Mathf.Sqrt(1f + x.x - y.y - z.z) * 2f;
            q = new Quaternion(0.25f * s, (y.x + x.y) / s, (z.x + x.z) / s, (y.z - z.y) / s);
        }
        else if (y.y > z.z)
        {
            float s = Mathf.Sqrt(1f + y.y - x.x - z.z) * 2f;
            q = new Quaternion((y.x + x.y) / s, 0.25f * s, (z.y + y.z) / s, (z.x - x.z) / s);
        }
        else
        {
            float s = Mathf.Sqrt(1f + z.z - x.x - y.y) * 2f;
            q = new Quaternion((z.x + x.z) / s, (z.y + y.z) / s, 0.25f * s, (x.y - y.x) / s);
        }
        return Normalize(q);
    }

    // `child` expressed in `parent`'s space.
    public static ReplayPose Relative(in ReplayPose parent, in ReplayPose child)
    {
        Quaternion inv = Conjugate(parent.r);
        Vector3 local = inv * (child.p - parent.p);
        return new ReplayPose
        {
            p = new Vector3(Div(local.x, parent.s.x), Div(local.y, parent.s.y), Div(local.z, parent.s.z)),
            r = Normalize(inv * child.r),
            s = new Vector3(Div(child.s.x, parent.s.x), Div(child.s.y, parent.s.y), Div(child.s.z, parent.s.z)),
        };
    }

    private static float Div(float a, float b) => Mathf.Abs(b) < 1e-8f ? 0f : a / b;

    public static Quaternion Conjugate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

    public static Quaternion Normalize(Quaternion q)
    {
        float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return m < 1e-8f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
    }

    public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
    {
        float dot = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        if (dot < 0f) { b = new Quaternion(-b.x, -b.y, -b.z, -b.w); dot = -dot; }
        if (dot > 0.9995f)
        {
            return Normalize(new Quaternion(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t));
        }
        float theta = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
        float sin = Mathf.Sin(theta);
        float wa = Mathf.Sin((1f - t) * theta) / sin, wb = Mathf.Sin(t * theta) / sin;
        return new Quaternion(a.x * wa + b.x * wb, a.y * wa + b.y * wb, a.z * wa + b.z * wb, a.w * wa + b.w * wb);
    }

    public static float AngleBetween(Quaternion a, Quaternion b)
    {
        float dot = Mathf.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w);
        return dot >= 1f ? 0f : Mathf.Acos(dot) * 2f * Mathf.Rad2Deg;
    }

    public static ReplayPose Lerp(in ReplayPose a, in ReplayPose b, float t) => new ReplayPose
    {
        p = a.p + (b.p - a.p) * t,
        r = Slerp(a.r, b.r, t),
        s = a.s + (b.s - a.s) * t,
    };

    public static ReplayPose Combine(in ReplayPose parent, in ReplayPose local) => new ReplayPose
    {
        p = parent.p + parent.r * Vector3.Scale(parent.s, local.p),
        r = parent.r * local.r,
        s = Vector3.Scale(parent.s, local.s),
    };
}

// Poses over time, thinned as they're recorded: a key is kept only where moving in a straight line (and turning at
// a steady rate) from the last key stops describing the motion, so a ship cruising or a turret at rest costs almost
// nothing. Played back, positions are interpolated linearly and rotations spherically between keys; recorded
// samples are always within about twice the tolerance of what's played back.
public class PoseTrack
{
    // A key is 20 bytes: time, position, packed rotation (scale only kept per key if it ever changes).
    public readonly List<float> times = new List<float>();
    public readonly List<Vector3> positions = new List<Vector3>();
    public readonly List<uint> rotations = new List<uint>();
    public List<Vector3> scales;
    public Vector3 scale = Vector3.one;

    private bool hasRef, hasPrev;
    private float keyT, refT, prevT;
    private ReplayPose key, reference, prev;

    public int Count => times.Count;

    public void Add(float t, in ReplayPose x, float posEps, float rotEps, bool force = false)
    {
        if (times.Count == 0 || force)
        {
            if (force) Flush();
            Emit(t, x);
            return;
        }
        if (t <= keyT) return;
        if (!hasRef)
        {
            reference = x; refT = t; hasRef = true;
            prev = x; prevT = t; hasPrev = true;
            return;
        }
        float u = (t - keyT) / Mathf.Max(1e-6f, refT - keyT);
        Vector3 predictedP = key.p + (reference.p - key.p) * u;
        Vector3 predictedS = key.s + (reference.s - key.s) * u;
        bool off = (x.p - predictedP).sqrMagnitude > posEps * posEps
            || (x.s - predictedS).sqrMagnitude > posEps * posEps * 0.01f
            || ReplayPose.AngleBetween(ReplayPose.Slerp(key.r, reference.r, u), x.r) > rotEps; // turning at the same rate
        if (off)
        {
            Emit(prevT, prev);
            reference = x; refT = t; hasRef = true;
        }
        prev = x; prevT = t; hasPrev = true;
    }

    private void Emit(float t, in ReplayPose x)
    {
        int last = times.Count - 1;
        if (last >= 0 && t <= times[last])
        {
            positions[last] = x.p;
            rotations[last] = PackRotation(x.r);
            SetScale(last, x.s);
        }
        else
        {
            times.Add(t);
            positions.Add(x.p);
            rotations.Add(PackRotation(x.r));
            SetScale(times.Count - 1, x.s);
        }
        key = x; keyT = t;
        hasRef = false; hasPrev = false;
    }

    // Scale almost never changes: one value for the track until it does.
    private void SetScale(int index, Vector3 s)
    {
        if (scales == null)
        {
            if (index == 0) { scale = s; return; }
            if (s == scale) return;
            scales = new List<Vector3>(times.Count);
            for (int i = 0; i < times.Count; i++) scales.Add(scale);
        }
        while (scales.Count <= index) scales.Add(s);
        scales[index] = s;
    }

    public ReplayPose Key(int i) => new ReplayPose { p = positions[i], r = UnpackRotation(rotations[i]), s = scales != null ? scales[i] : scale };

    // A rotation in 32 bits: the largest component dropped, the other three in 10 bits each (about 0.1 degree).
    public static uint PackRotation(Quaternion q)
    {
        float ax = Mathf.Abs(q.x), ay = Mathf.Abs(q.y), az = Mathf.Abs(q.z), aw = Mathf.Abs(q.w);
        int largest = ax >= ay && ax >= az && ax >= aw ? 0 : ay >= az && ay >= aw ? 1 : az >= aw ? 2 : 3;
        float l = largest == 0 ? q.x : largest == 1 ? q.y : largest == 2 ? q.z : q.w;
        float sign = l < 0f ? -1f : 1f;
        float a, b, c;
        switch (largest)
        {
            case 0: a = q.y; b = q.z; c = q.w; break;
            case 1: a = q.x; b = q.z; c = q.w; break;
            case 2: a = q.x; b = q.y; c = q.w; break;
            default: a = q.x; b = q.y; c = q.z; break;
        }
        return ((uint)largest << 30) | (Ten(a * sign) << 20) | (Ten(b * sign) << 10) | Ten(c * sign);
    }

    // [-1/sqrt2, 1/sqrt2] -> 10 bits.
    private static uint Ten(float v) => (uint)Mathf.Clamp(Mathf.RoundToInt((v * 0.70710678f + 0.5f) * 1023f), 0, 1023);
    private static float FromTen(uint bits) => ((bits & 1023u) / 1023f - 0.5f) * 1.41421356f;

    public static Quaternion UnpackRotation(uint packed)
    {
        int largest = (int)(packed >> 30);
        float a = FromTen(packed >> 20), b = FromTen(packed >> 10), c = FromTen(packed);
        float d = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));
        switch (largest)
        {
            case 0: return new Quaternion(d, a, b, c);
            case 1: return new Quaternion(a, d, b, c);
            case 2: return new Quaternion(a, b, d, c);
            default: return new Quaternion(a, b, c, d);
        }
    }

    // Writes out the last sample, so the track ends exactly where the thing was last seen.
    public void Flush()
    {
        if (hasPrev && prevT > keyT) Emit(prevT, prev);
        hasRef = false; hasPrev = false;
    }

    public ReplayPose Evaluate(float t, ref int cursor)
    {
        int n = times.Count;
        if (n == 0) return ReplayPose.Identity;
        if (n == 1 || t <= times[0]) { cursor = 0; return Key(0); }
        if (t >= times[n - 1]) { cursor = n - 1; return Key(n - 1); }
        cursor = Seek(times, t, cursor);
        float a = times[cursor], b = times[cursor + 1];
        return ReplayPose.Lerp(Key(cursor), Key(cursor + 1), (t - a) / Mathf.Max(1e-6f, b - a));
    }

    // Index i with times[i] <= t < times[i + 1], starting from a hint (playback mostly moves a frame at a time).
    public static int Seek(List<float> times, float t, int hint)
    {
        int n = times.Count;
        if (hint < 0 || hint >= n) hint = 0;
        if (times[hint] <= t)
        {
            for (int k = 0; k < 4; k++)
            {
                if (hint + 1 >= n || times[hint + 1] > t) return hint;
                hint++;
            }
        }
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (times[mid] <= t) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
}

// A value that changes in steps (visibility, which mesh or material is on, a light's colour).
public class StepTrack<T>
{
    public readonly List<float> times = new List<float>();
    public readonly List<T> values = new List<T>();
    private readonly T fallback;

    public StepTrack(T fallback = default) { this.fallback = fallback; }

    public int Count => times.Count;
    public T Last => values.Count > 0 ? values[values.Count - 1] : fallback;

    public void Set(float t, T value)
    {
        int n = times.Count;
        if (n > 0 && EqualityComparer<T>.Default.Equals(values[n - 1], value)) return;
        if (n > 0 && t <= times[n - 1]) { values[n - 1] = value; return; }
        times.Add(t);
        values.Add(value);
    }

    public T Evaluate(float t, ref int cursor)
    {
        int n = times.Count;
        if (n == 0 || t < times[0]) { cursor = 0; return fallback; }
        cursor = PoseTrack.Seek(times, t, cursor);
        return values[cursor];
    }
}

// ---------------- Mesh history ----------------
// Hull meshes are bent and holed in place over the fight. Each change is kept as the runs of vertices that differ
// from the version before (new values only: stepping back replays the changes from the first version) and the
// triangles taken out and put in. Normals are kept packed in 4 bytes.

public class ReplayMeshState
{
    public Vector3[] v;
    public uint[] n; // packed (see Pack)
    public Vector4[] t;
    public Vector2[] uv;
    public Color[] c;
    public int[][] tris;
    public UnityEngine.Rendering.IndexFormat format;

    public static ReplayMeshState Read(Mesh mesh)
    {
        ReplayMeshState s = new ReplayMeshState { v = mesh.vertices, n = Pack(mesh.normals), t = mesh.tangents, uv = mesh.uv, c = mesh.colors, format = mesh.indexFormat };
        s.tris = new int[mesh.subMeshCount][];
        for (int i = 0; i < s.tris.Length; i++) s.tris[i] = mesh.GetTriangles(i);
        return s;
    }

    public void Write(Mesh mesh)
    {
        mesh.Clear(false);
        mesh.indexFormat = v.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : format;
        mesh.vertices = v;
        if (n != null && n.Length == v.Length) mesh.normals = Unpack(n);
        if (t != null && t.Length == v.Length) mesh.tangents = t;
        if (uv != null && uv.Length == v.Length) mesh.uv = uv;
        if (c != null && c.Length == v.Length) mesh.colors = c;
        mesh.subMeshCount = tris.Length;
        for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i, false);
        mesh.RecalculateBounds();
    }

    public ReplayMeshState Copy() => new ReplayMeshState { v = v, n = n, t = t, uv = uv, c = c, tris = tris, format = format };

    public long Bytes => (v?.Length ?? 0) * 12L + (n?.Length ?? 0) * 4L + (t?.Length ?? 0) * 16L + (uv?.Length ?? 0) * 8L + (c?.Length ?? 0) * 16L + Sum(tris) * 4L;
    private static long Sum(int[][] a) { long s = 0; if (a != null) foreach (int[] x in a) s += x?.Length ?? 0; return s; }

    // Unit vectors in 32 bits (octahedral, 16 bits a coordinate): about a hundredth of a degree.
    public static uint[] Pack(IList<Vector3> normals)
    {
        if (normals == null || normals.Count == 0) return null;
        uint[] packed = new uint[normals.Count];
        for (int i = 0; i < packed.Length; i++) packed[i] = Pack(normals[i]);
        return packed;
    }

    public static uint Pack(Vector3 v)
    {
        float l1 = Mathf.Abs(v.x) + Mathf.Abs(v.y) + Mathf.Abs(v.z);
        if (l1 < 1e-8f) return 0x7FFF7FFFu;
        float x = v.x / l1, y = v.y / l1;
        if (v.z < 0f)
        {
            float ox = (1f - Mathf.Abs(y)) * (x >= 0f ? 1f : -1f), oy = (1f - Mathf.Abs(x)) * (y >= 0f ? 1f : -1f);
            x = ox; y = oy;
        }
        uint ux = (uint)Mathf.Clamp(Mathf.RoundToInt((x * 0.5f + 0.5f) * 65535f), 0, 65535);
        uint uy = (uint)Mathf.Clamp(Mathf.RoundToInt((y * 0.5f + 0.5f) * 65535f), 0, 65535);
        return ux | (uy << 16);
    }

    public static Vector3[] Unpack(uint[] packed)
    {
        Vector3[] normals = new Vector3[packed.Length];
        for (int i = 0; i < packed.Length; i++)
        {
            float x = (packed[i] & 0xFFFF) / 65535f * 2f - 1f, y = (packed[i] >> 16) / 65535f * 2f - 1f;
            float z = 1f - Mathf.Abs(x) - Mathf.Abs(y);
            if (z < 0f)
            {
                float ox = (1f - Mathf.Abs(y)) * (x >= 0f ? 1f : -1f), oy = (1f - Mathf.Abs(x)) * (y >= 0f ? 1f : -1f);
                x = ox; y = oy;
            }
            normals[i] = new Vector3(x, y, z).normalized;
        }
        return normals;
    }
}

public class ReplayArrayDiff<T>
{
    public int newLength;
    public readonly List<int> starts = new List<int>();
    public readonly List<T[]> runs = new List<T[]>();
    public T[] tail;

    public static ReplayArrayDiff<T> Make(T[] a, T[] b, Func<T, T, bool> same)
    {
        a ??= Array.Empty<T>();
        b ??= Array.Empty<T>();
        ReplayArrayDiff<T> d = new ReplayArrayDiff<T> { newLength = b.Length };
        int common = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < common)
        {
            if (same(a[i], b[i])) { i++; continue; }
            int start = i, last = i, j = i + 1;
            while (j < common)
            {
                if (!same(a[j], b[j])) last = j;
                else if (j - last > 3) break;
                j++;
            }
            int length = last - start + 1;
            T[] run = new T[length];
            Array.Copy(b, start, run, 0, length);
            d.starts.Add(start); d.runs.Add(run);
            i = last + 1;
        }
        if (b.Length > common) { d.tail = new T[b.Length - common]; Array.Copy(b, common, d.tail, 0, d.tail.Length); }
        return d;
    }

    public T[] Forward(T[] a)
    {
        a ??= Array.Empty<T>();
        if (starts.Count == 0 && tail == null && newLength == a.Length) return a;
        T[] result = new T[newLength];
        int common = Math.Min(a.Length, newLength);
        Array.Copy(a, 0, result, 0, common);
        for (int k = 0; k < starts.Count; k++) Array.Copy(runs[k], 0, result, starts[k], runs[k].Length);
        if (tail != null) Array.Copy(tail, 0, result, newLength - tail.Length, tail.Length);
        return result;
    }

    public long Bytes(int size)
    {
        long s = tail?.Length ?? 0;
        foreach (T[] x in runs) s += x.Length;
        return s * size;
    }
}

public class ReplayMeshDiff
{
    public ReplayArrayDiff<Vector3> v;
    public ReplayArrayDiff<uint> n;
    public ReplayArrayDiff<Vector4> t;
    public ReplayArrayDiff<Vector2> uv;
    public ReplayArrayDiff<Color> c;
    public int newSubmeshes;
    public ReplayTriangleDiff[] tris;

    private static readonly Func<Vector3, Vector3, bool> Same3 = (a, b) => a.x == b.x && a.y == b.y && a.z == b.z;
    private static readonly Func<uint, uint, bool> SameU = (a, b) => a == b;
    private static readonly Func<Vector4, Vector4, bool> Same4 = (a, b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    private static readonly Func<Vector2, Vector2, bool> Same2 = (a, b) => a.x == b.x && a.y == b.y;
    private static readonly Func<Color, Color, bool> SameC = (a, b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    public static ReplayMeshDiff Make(ReplayMeshState a, ReplayMeshState b)
    {
        ReplayMeshDiff d = new ReplayMeshDiff
        {
            v = ReplayArrayDiff<Vector3>.Make(a.v, b.v, Same3),
            n = ReplayArrayDiff<uint>.Make(a.n, b.n, SameU),
            t = ReplayArrayDiff<Vector4>.Make(a.t, b.t, Same4),
            uv = ReplayArrayDiff<Vector2>.Make(a.uv, b.uv, Same2),
            c = ReplayArrayDiff<Color>.Make(a.c, b.c, SameC),
            newSubmeshes = b.tris.Length,
        };
        d.tris = new ReplayTriangleDiff[b.tris.Length];
        for (int s = 0; s < b.tris.Length; s++) d.tris[s] = ReplayTriangleDiff.Make(s < a.tris.Length ? a.tris[s] : null, b.tris[s]);
        return d;
    }

    public void Forward(ReplayMeshState s)
    {
        s.v = v.Forward(s.v); s.n = n.Forward(s.n); s.t = t.Forward(s.t); s.uv = uv.Forward(s.uv); s.c = c.Forward(s.c);
        int[][] tri = new int[newSubmeshes][];
        for (int i = 0; i < newSubmeshes; i++) tri[i] = tris[i].Forward(i < s.tris.Length ? s.tris[i] : null);
        s.tris = tri;
    }

    public long Bytes
    {
        get
        {
            long b = v.Bytes(12) + n.Bytes(4) + t.Bytes(16) + uv.Bytes(8) + c.Bytes(16);
            foreach (ReplayTriangleDiff d in tris) b += d.Bytes;
            return b;
        }
    }
}

// A submesh's triangles as the ones taken out and the ones put in. The hull editor lifts the triangles around a hit
// out of the list and puts the reworked ones back at its end, so compared in order nearly the whole list would
// differ after every dent; as a set, only the neighbourhood of the hit does. (Rebuilt, the list's order can differ
// from the live one's; what's drawn doesn't.)
public class ReplayTriangleDiff
{
    public int[] removed, added;

    private static long Key(int[] t, int i) => ((long)t[i] << 42) | ((long)t[i + 1] << 21) | (long)t[i + 2];

    public static ReplayTriangleDiff Make(int[] a, int[] b)
    {
        a ??= Array.Empty<int>();
        b ??= Array.Empty<int>();
        Dictionary<long, int> count = new Dictionary<long, int>(a.Length / 3);
        for (int i = 0; i + 2 < a.Length; i += 3) { long k = Key(a, i); count.TryGetValue(k, out int c); count[k] = c + 1; }
        List<int> added = new List<int>();
        for (int i = 0; i + 2 < b.Length; i += 3)
        {
            long k = Key(b, i);
            if (count.TryGetValue(k, out int c) && c > 0) count[k] = c - 1;
            else { added.Add(b[i]); added.Add(b[i + 1]); added.Add(b[i + 2]); }
        }
        List<int> removed = new List<int>();
        for (int i = 0; i + 2 < a.Length; i += 3)
        {
            long k = Key(a, i);
            if (count.TryGetValue(k, out int c) && c > 0) { count[k] = c - 1; removed.Add(a[i]); removed.Add(a[i + 1]); removed.Add(a[i + 2]); }
        }
        return new ReplayTriangleDiff { removed = removed.ToArray(), added = added.ToArray() };
    }

    public int[] Forward(int[] a)
    {
        a ??= Array.Empty<int>();
        if (removed.Length == 0 && added.Length == 0) return a;
        Dictionary<long, int> drop = new Dictionary<long, int>(removed.Length / 3);
        for (int i = 0; i + 2 < removed.Length; i += 3) { long k = Key(removed, i); drop.TryGetValue(k, out int c); drop[k] = c + 1; }
        int[] result = new int[Math.Max(0, a.Length - removed.Length) + added.Length];
        int n = 0, keep = result.Length - added.Length;
        for (int i = 0; i + 2 < a.Length && n + 3 <= keep; i += 3)
        {
            long k = Key(a, i);
            if (drop.TryGetValue(k, out int c) && c > 0) { drop[k] = c - 1; continue; }
            result[n++] = a[i]; result[n++] = a[i + 1]; result[n++] = a[i + 2];
        }
        Array.Copy(added, 0, result, n, added.Length);
        return result;
    }

    public long Bytes => (removed.Length + added.Length) * 4L;
}

// Every version a runtime mesh went through. The diffs are worked out on a worker, one after another.
public class ReplayMeshHistory
{
    public Mesh live;
    private readonly Mesh source;
    private ReplayMeshState first;
    public readonly List<float> times = new List<float>();
    public readonly List<ReplayMeshDiff> diffs = new List<ReplayMeshDiff>();
    private ReplayMeshState latest;
    private System.Threading.Tasks.Task chain = System.Threading.Tasks.Task.CompletedTask;
    private readonly object gate = new object();
    private long bytes;

    // The version before any change. For a hull copied from the ship's model it's the model's, read from it only
    // when the replay is watched, rather than kept.
    public ReplayMeshState First => first ??= ReplayMeshState.Read(source != null ? source : live);
    public long FirstBytes => source == null && first != null ? first.Bytes : 0;

    public Mesh Source => source;

    // A saved hull's history: its first version is its model's (or was written out with it).
    public static ReplayMeshHistory Loaded(Mesh live, string sourceKey, ReplayMeshState firstState)
    {
        Mesh model = sourceKey != null ? ReplayAssets.Find<Mesh>(sourceKey) : null;
        ReplayMeshHistory h = new ReplayMeshHistory(live, model, true);
        h.first = firstState;
        return h;
    }

    private ReplayMeshHistory(Mesh mesh, Mesh copiedFrom, bool loaded) { live = mesh; source = copiedFrom; }

    public ReplayMeshHistory(Mesh mesh, Mesh copiedFrom = null)
    {
        live = mesh;
        source = copiedFrom;
        if (source == null) first = ReplayMeshState.Read(mesh);
    }

    // Changes a burst of hits makes within half a second are kept as one (shown at the first of them).
    private const float Merge = 0.5f;
    private ReplayMeshState held;
    private float heldTime = float.NegativeInfinity;

    public void Add(float time, ReplayMeshState next)
    {
        latest ??= source != null ? ReplayMeshState.Read(source) : first;
        if (held != null && time - heldTime < Merge) { held = next; return; }
        Commit();
        held = next;
        heldTime = time;
    }

    private void Commit()
    {
        if (held == null) return;
        ReplayMeshState next = held;
        float time = heldTime;
        held = null;
        chain = chain.ContinueWith(_ =>
        {
            ReplayMeshDiff diff = ReplayMeshDiff.Make(latest, next);
            latest = next;
            lock (gate) { times.Add(time); diffs.Add(diff); bytes += diff.Bytes; }
        }, System.Threading.Tasks.TaskScheduler.Default);
    }

    public void Finish()
    {
        Commit();
        try { chain.Wait(); } catch (Exception e) { Debug.LogException(e); }
        latest = null;
    }

    public long Bytes { get { lock (gate) return FirstBytes + bytes; } }
}
