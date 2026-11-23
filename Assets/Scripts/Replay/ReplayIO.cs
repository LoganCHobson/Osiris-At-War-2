using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

// Reading and writing a replay's data (see ReplayFile). Unity objects are written as indices into the file's
// object table; everything else as plain values.
public class ReplayIO
{
    public BinaryWriter w;
    public BinaryReader r;
    public Func<Object, int> refOf;     // writing
    public Object[] objects;            // reading
    public Dictionary<int, ReplayAnchor> anchors;

    public bool Writing => w != null;

    public void Obj(Object o) => w.Write((object)o == null ? -1 : refOf(o)); // (no Unity null check: this runs on workers)
    public T Obj<T>() where T : Object
    {
        int i = r.ReadInt32();
        return i >= 0 && i < objects.Length ? objects[i] as T : null;
    }

    public void Objs(Object[] list)
    {
        w.Write(list?.Length ?? -1);
        if (list != null) for (int i = 0; i < list.Length; i++) Obj(list[i]);
    }

    public T[] Objs<T>() where T : Object
    {
        int n = r.ReadInt32();
        if (n < 0) return null;
        T[] list = new T[n];
        for (int i = 0; i < n; i++) list[i] = Obj<T>();
        return list;
    }

    public void Str(string s) { w.Write(s != null); if (s != null) w.Write(s); }
    public string Str() => r.ReadBoolean() ? r.ReadString() : null;

    public void V3(Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    public Vector3 V3() => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    public void V4(Vector4 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
    public Vector4 V4() => new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    public void Col(Color c) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
    public Color Col() => new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    public void Q(Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
    public Quaternion Q() => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    public void Floats(List<float> list) { w.Write(list.Count); foreach (float f in list) w.Write(f); }
    public void Floats(List<float> list, int n) { for (int i = 0; i < n; i++) list.Add(r.ReadSingle()); }

    public void Pose(PoseTrack p)
    {
        if (Writing)
        {
            int n = p.times.Count;
            w.Write(n);
            for (int i = 0; i < n; i++) { w.Write(p.times[i]); V3(p.positions[i]); w.Write(p.rotations[i]); }
            w.Write(p.scales != null);
            if (p.scales != null) foreach (Vector3 s in p.scales) V3(s);
            else V3(p.scale);
        }
        else
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) { p.times.Add(r.ReadSingle()); p.positions.Add(V3()); p.rotations.Add(r.ReadUInt32()); }
            if (r.ReadBoolean()) { p.scales = new List<Vector3>(n); for (int i = 0; i < n; i++) p.scales.Add(V3()); }
            else p.scale = V3();
        }
    }

    public void Steps<T>(StepTrack<T> track, Action<T> write, Func<T> read)
    {
        if (Writing)
        {
            w.Write(track.times.Count);
            for (int i = 0; i < track.times.Count; i++) { w.Write(track.times[i]); write(track.values[i]); }
        }
        else
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) { track.times.Add(r.ReadSingle()); track.values.Add(read()); }
        }
    }

    public void Gradient(Gradient g)
    {
        GradientColorKey[] colors = g?.colorKeys ?? new GradientColorKey[0];
        GradientAlphaKey[] alphas = g?.alphaKeys ?? new GradientAlphaKey[0];
        w.Write(colors.Length);
        foreach (GradientColorKey k in colors) { Col(k.color); w.Write(k.time); }
        w.Write(alphas.Length);
        foreach (GradientAlphaKey k in alphas) { w.Write(k.alpha); w.Write(k.time); }
    }

    public Gradient Gradient()
    {
        GradientColorKey[] colors = new GradientColorKey[r.ReadInt32()];
        for (int i = 0; i < colors.Length; i++) colors[i] = new GradientColorKey(Col(), r.ReadSingle());
        GradientAlphaKey[] alphas = new GradientAlphaKey[r.ReadInt32()];
        for (int i = 0; i < alphas.Length; i++) alphas[i] = new GradientAlphaKey(r.ReadSingle(), r.ReadSingle());
        Gradient g = new Gradient();
        g.SetKeys(colors, alphas);
        return g;
    }

    public void Curve(AnimationCurve c)
    {
        Keyframe[] keys = c?.keys ?? new Keyframe[0];
        w.Write(keys.Length);
        foreach (Keyframe k in keys) { w.Write(k.time); w.Write(k.value); w.Write(k.inTangent); w.Write(k.outTangent); }
    }

    public AnimationCurve Curve()
    {
        Keyframe[] keys = new Keyframe[r.ReadInt32()];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        return new AnimationCurve(keys);
    }

    // ---------------- Meshes ----------------

    public void MeshState(ReplayMeshState s)
    {
        w.Write((int)s.format);
        w.Write(s.v.Length);
        foreach (Vector3 v in s.v) V3(v);
        UInts(s.n);
        Array(s.t, V4);
        Array(s.uv, x => { w.Write(x.x); w.Write(x.y); });
        Array(s.c, Col);
        w.Write(s.tris.Length);
        foreach (int[] t in s.tris) Ints(t);
    }

    public ReplayMeshState MeshState()
    {
        ReplayMeshState s = new ReplayMeshState { format = (UnityEngine.Rendering.IndexFormat)r.ReadInt32() };
        s.v = new Vector3[r.ReadInt32()];
        for (int i = 0; i < s.v.Length; i++) s.v[i] = V3();
        s.n = UInts();
        s.t = Array(V4);
        s.uv = Array(() => new Vector2(r.ReadSingle(), r.ReadSingle()));
        s.c = Array(Col);
        s.tris = new int[r.ReadInt32()][];
        for (int i = 0; i < s.tris.Length; i++) s.tris[i] = Ints();
        return s;
    }

    // A battle-made mesh (decal, wreck slice) in about a third of the room: positions in 16 bits across its bounds,
    // UVs as half floats, tangents and colours in 8 bits a channel. (A few hundredths of a percent of its size.)
    public void CompactMesh(ReplayMeshState s)
    {
        s = Trimmed(s);
        w.Write((int)s.format);
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        foreach (Vector3 v in s.v) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        if (s.v.Length == 0) min = max = Vector3.zero;
        Vector3 size = Vector3.Max(max - min, Vector3.one * 1e-6f);
        V3(min); V3(size);
        w.Write(s.v.Length);
        foreach (Vector3 v in s.v)
        {
            w.Write((ushort)Mathf.RoundToInt((v.x - min.x) / size.x * 65535f));
            w.Write((ushort)Mathf.RoundToInt((v.y - min.y) / size.y * 65535f));
            w.Write((ushort)Mathf.RoundToInt((v.z - min.z) / size.z * 65535f));
        }
        UInts(s.n);
        w.Write(s.t?.Length ?? -1);
        if (s.t != null) foreach (Vector4 t in s.t) { w.Write(Snorm(t.x)); w.Write(Snorm(t.y)); w.Write(Snorm(t.z)); w.Write(Snorm(t.w)); }
        w.Write(s.uv?.Length ?? -1);
        if (s.uv != null) foreach (Vector2 uv in s.uv) { w.Write(Mathf.FloatToHalf(uv.x)); w.Write(Mathf.FloatToHalf(uv.y)); }
        w.Write(s.c?.Length ?? -1);
        if (s.c != null) foreach (Color c in s.c) { Color32 c32 = c; w.Write(c32.r); w.Write(c32.g); w.Write(c32.b); w.Write(c32.a); }
        w.Write(s.tris.Length);
        foreach (int[] t in s.tris) Ints(t);
    }

    public ReplayMeshState CompactMesh()
    {
        ReplayMeshState s = new ReplayMeshState { format = (UnityEngine.Rendering.IndexFormat)r.ReadInt32() };
        Vector3 min = V3(), size = V3();
        s.v = new Vector3[r.ReadInt32()];
        for (int i = 0; i < s.v.Length; i++)
            s.v[i] = new Vector3(min.x + r.ReadUInt16() / 65535f * size.x, min.y + r.ReadUInt16() / 65535f * size.y, min.z + r.ReadUInt16() / 65535f * size.z);
        s.n = UInts();
        int n = r.ReadInt32();
        if (n >= 0) { s.t = new Vector4[n]; for (int i = 0; i < n; i++) s.t[i] = new Vector4(FromSnorm(r.ReadSByte()), FromSnorm(r.ReadSByte()), FromSnorm(r.ReadSByte()), FromSnorm(r.ReadSByte())); }
        n = r.ReadInt32();
        if (n >= 0) { s.uv = new Vector2[n]; for (int i = 0; i < n; i++) s.uv[i] = new Vector2(Mathf.HalfToFloat(r.ReadUInt16()), Mathf.HalfToFloat(r.ReadUInt16())); }
        n = r.ReadInt32();
        if (n >= 0) { s.c = new Color[n]; for (int i = 0; i < n; i++) s.c[i] = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte()); }
        s.tris = new int[r.ReadInt32()][];
        for (int i = 0; i < s.tris.Length; i++) s.tris[i] = Ints();
        return s;
    }

    // Only the vertices its triangles use. Wreck pieces and turret parts are cut from a whole model and keep all
    // of its vertices; most of them are never drawn.
    private static ReplayMeshState Trimmed(ReplayMeshState s)
    {
        int count = s.v.Length;
        int[] remap = new int[count];
        for (int i = 0; i < count; i++) remap[i] = -1;
        int used = 0;
        foreach (int[] tris in s.tris) if (tris != null) foreach (int i in tris) if (i >= 0 && i < count && remap[i] < 0) remap[i] = used++;
        if (used == count) return s;
        ReplayMeshState t = new ReplayMeshState { format = used > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        t.v = Pick(s.v, remap, used);
        t.n = s.n != null && s.n.Length == count ? Pick(s.n, remap, used) : null;
        t.t = s.t != null && s.t.Length == count ? Pick(s.t, remap, used) : null;
        t.uv = s.uv != null && s.uv.Length == count ? Pick(s.uv, remap, used) : null;
        t.c = s.c != null && s.c.Length == count ? Pick(s.c, remap, used) : null;
        t.tris = new int[s.tris.Length][];
        for (int k = 0; k < s.tris.Length; k++)
        {
            int[] src = s.tris[k] ?? new int[0];
            int[] dst = new int[src.Length];
            for (int i = 0; i < src.Length; i++) dst[i] = src[i] >= 0 && src[i] < count ? remap[src[i]] : 0;
            t.tris[k] = dst;
        }
        return t;
    }

    private static T[] Pick<T>(T[] source, int[] remap, int used)
    {
        T[] result = new T[used];
        for (int i = 0; i < remap.Length; i++) if (remap[i] >= 0) result[remap[i]] = source[i];
        return result;
    }

    private static sbyte Snorm(float v) => (sbyte)Mathf.Clamp(Mathf.RoundToInt(v * 127f), -127, 127);
    private static float FromSnorm(sbyte v) => v / 127f;

    public void MeshDiff(ReplayMeshDiff d)
    {
        Runs(d.v, V3); Runs(d.n, x => w.Write(x)); Runs(d.t, V4); Runs(d.uv, x => { w.Write(x.x); w.Write(x.y); }); Runs(d.c, Col);
        w.Write(d.newSubmeshes);
        foreach (ReplayTriangleDiff t in d.tris) { Ints(t.removed); Ints(t.added); }
    }

    public ReplayMeshDiff MeshDiff()
    {
        ReplayMeshDiff d = new ReplayMeshDiff
        {
            v = Runs(V3), n = Runs(r.ReadUInt32), t = Runs(V4), uv = Runs(() => new Vector2(r.ReadSingle(), r.ReadSingle())), c = Runs(Col),
            newSubmeshes = r.ReadInt32(),
        };
        d.tris = new ReplayTriangleDiff[d.newSubmeshes];
        for (int i = 0; i < d.tris.Length; i++) d.tris[i] = new ReplayTriangleDiff { removed = Ints(), added = Ints() };
        return d;
    }

    private void Runs<T>(ReplayArrayDiff<T> d, Action<T> write)
    {
        w.Write(d.newLength);
        w.Write(d.starts.Count);
        for (int i = 0; i < d.starts.Count; i++) { w.Write(d.starts[i]); Array(d.runs[i], write); }
        Array(d.tail, write);
    }

    private ReplayArrayDiff<T> Runs<T>(Func<T> read)
    {
        ReplayArrayDiff<T> d = new ReplayArrayDiff<T> { newLength = r.ReadInt32() };
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) { d.starts.Add(r.ReadInt32()); d.runs.Add(Array(read)); }
        d.tail = Array(read);
        return d;
    }

    private void Array<T>(T[] a, Action<T> write)
    {
        w.Write(a?.Length ?? -1);
        if (a != null) foreach (T x in a) write(x);
    }

    private T[] Array<T>(Func<T> read)
    {
        int n = r.ReadInt32();
        if (n < 0) return null;
        T[] a = new T[n];
        for (int i = 0; i < n; i++) a[i] = read();
        return a;
    }

    public void Ints(int[] a) { w.Write(a?.Length ?? -1); if (a != null) foreach (int x in a) w.Write(x); }
    public int[] Ints() { int n = r.ReadInt32(); if (n < 0) return null; int[] a = new int[n]; for (int i = 0; i < n; i++) a[i] = r.ReadInt32(); return a; }
    public void UInts(uint[] a) { w.Write(a?.Length ?? -1); if (a != null) foreach (uint x in a) w.Write(x); }
    public uint[] UInts() { int n = r.ReadInt32(); if (n < 0) return null; uint[] a = new uint[n]; for (int i = 0; i < n; i++) a[i] = r.ReadUInt32(); return a; }
}

// Compares Unity objects by reference only (safe off the main thread).
public class ReferenceComparer : IEqualityComparer<Object>
{
    public static readonly ReferenceComparer Instance = new ReferenceComparer();
    public bool Equals(Object a, Object b) => ReferenceEquals(a, b);
    public int GetHashCode(Object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
}
