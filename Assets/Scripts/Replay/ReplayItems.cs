using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// What a replay records: every top-level object of the battle (an anchor) and everything inside it that draws (an
// item). Each item records itself (Poll, on the main thread, every frame of the battle) and plays itself back on a
// look-only ghost (Build, Apply). Poses are sampled for all of them at once by ReplayRecorder.

public enum ReplayKind { Other, Ship, Craft, Missile, Debris }

public class ReplayAnchor
{
    public int id;
    public Transform live;
    public int slot = -1;
    public readonly PoseTrack pose = new PoseTrack();
    public float born, gone = float.PositiveInfinity;
    public ReplayKind kind;
    public float size = 1f;
    public bool attackerSide;
    public string name;
    public int expectedCount = -1;
    public SensorVisibility sensors; // (recording) a ship the player's sensors can hide
    public bool rescan = true;
    public bool marked;
    public float nextRescanAt;

    // Playback
    public Transform ghost;
    public int cursor;
    public int shown;      // items drawn this frame
    public ReplayPose now; // world pose this frame
    public bool placed;

    public bool AliveAt(float t) => t >= born && t < gone;
}

public abstract class ReplayItem
{
    public int id;
    public Transform live;
    public int slot = -1;
    public ReplayAnchor anchor;
    public readonly StepTrack<ReplayAnchor> anchors = new StepTrack<ReplayAnchor>();
    public readonly PoseTrack pose = new PoseTrack();
    public readonly StepTrack<bool> visible = new StepTrack<bool>(false);
    public float born, gone = float.PositiveInfinity;
    public bool wasVisible;
    public bool forceKey = true;
    public bool polled;
    public ReplayKind subject; // Ship hull / a craft: something the replay's cinematic camera can follow.
    public float subjectSize;
    public bool attackerSide;
    public string label;

    public abstract bool Alive { get; }

    // Recording: whether it's drawing this frame, noting anything else about it that changed.
    public abstract bool Poll(float t, int frame);
    public virtual void Close(float t) { }

    // Playback
    public GameObject ghost;
    public Transform ghostT;
    public bool ghostOn;
    public ReplayAnchor ghostParent;
    public int anchorCursor, poseCursor, visibleCursor;
    public bool posed;

    public abstract void Build(ReplayPlayer player);
    public virtual void Apply(ReplayPlayer player, float t, bool jumped) { }
    public virtual void Shown(ReplayPlayer player, float t) { }
    public virtual void Hidden(ReplayPlayer player) { }
    public virtual Vector3 Scale(Vector3 recorded) => recorded; // the ghost's local scale for a recorded one
    public virtual bool FacesCamera => false;

    public static string NameOf(Transform t) => t != null ? t.name : "item";

    // Saved replays (ReplayFile): what kind it is, and its own data (the common tracks are ReplayFile's).
    public abstract int TypeTag { get; }
    public abstract void Save(ReplayIO io);
    public abstract void Load(ReplayIO io);
}

// ---------------- Renderers ----------------

public class ReplayMpb
{
    private static readonly int ColorId = Shader.PropertyToID("_Color"), BaseColorId = Shader.PropertyToID("_BaseColor"),
        TintId = Shader.PropertyToID("_TintColor"), EmissionId = Shader.PropertyToID("_EmissionColor"),
        IntensityId = Shader.PropertyToID("_Intensity"), MainTexId = Shader.PropertyToID("_MainTex"), BaseMapId = Shader.PropertyToID("_BaseMap");

    public int mask;
    public Color color, baseColor, tint, emission;
    public float intensity;
    public Texture mainTex, baseMap;

    public static ReplayMpb Read(MaterialPropertyBlock b)
    {
        ReplayMpb m = new ReplayMpb();
        if (b.HasColor(ColorId)) { m.mask |= 1; m.color = b.GetColor(ColorId); }
        if (b.HasColor(BaseColorId)) { m.mask |= 2; m.baseColor = b.GetColor(BaseColorId); }
        if (b.HasColor(TintId)) { m.mask |= 4; m.tint = b.GetColor(TintId); }
        if (b.HasColor(EmissionId)) { m.mask |= 8; m.emission = b.GetColor(EmissionId); }
        if (b.HasFloat(IntensityId)) { m.mask |= 16; m.intensity = b.GetFloat(IntensityId); }
        if (b.HasTexture(MainTexId)) { m.mask |= 32; m.mainTex = b.GetTexture(MainTexId); }
        if (b.HasTexture(BaseMapId)) { m.mask |= 64; m.baseMap = b.GetTexture(BaseMapId); }
        return m;
    }

    public bool Same(ReplayMpb o) => o != null && mask == o.mask && color == o.color && baseColor == o.baseColor && tint == o.tint
        && emission == o.emission && intensity == o.intensity && mainTex == o.mainTex && baseMap == o.baseMap;

    public void Write(MaterialPropertyBlock b)
    {
        b.Clear();
        if ((mask & 1) != 0) b.SetColor(ColorId, color);
        if ((mask & 2) != 0) b.SetColor(BaseColorId, baseColor);
        if ((mask & 4) != 0) b.SetColor(TintId, tint);
        if ((mask & 8) != 0) b.SetColor(EmissionId, emission);
        if ((mask & 16) != 0) b.SetFloat(IntensityId, intensity);
        if ((mask & 32) != 0 && mainTex != null) b.SetTexture(MainTexId, mainTex);
        if ((mask & 64) != 0 && baseMap != null) b.SetTexture(BaseMapId, baseMap);
    }
}

public abstract class ReplayRendererItem : ReplayItem
{
    public Renderer renderer;
    public readonly StepTrack<Material[]> materials = new StepTrack<Material[]>();
    public readonly StepTrack<ReplayMpb> blocks = new StepTrack<ReplayMpb>();
    public ShadowCastingMode shadows;
    public bool receiveShadows;
    public int layer;
    public uint renderingLayerMask;
    private bool blockCandidate, polled;
    private static readonly List<Material> materialBuffer = new List<Material>();
    private static MaterialPropertyBlock readBlock;

    protected Renderer ghostRenderer;
    private Material[] shownMaterials;
    private ReplayMpb shownBlock;
    private int materialCursor, blockCursor;
    protected static MaterialPropertyBlock writeBlock;

    public override bool Alive => renderer != null;

    protected void SaveRenderer(ReplayIO io)
    {
        io.Steps(materials, m => io.Objs(m), null);
        io.Steps(blocks, b =>
        {
            io.w.Write(b != null);
            if (b == null) return;
            io.w.Write(b.mask); io.Col(b.color); io.Col(b.baseColor); io.Col(b.tint); io.Col(b.emission); io.w.Write(b.intensity);
            io.Obj(b.mainTex); io.Obj(b.baseMap);
        }, null);
        io.w.Write((int)shadows); io.w.Write(receiveShadows); io.w.Write(layer); io.w.Write(renderingLayerMask);
    }

    protected void LoadRenderer(ReplayIO io)
    {
        io.Steps(materials, null, () => io.Objs<Material>());
        io.Steps(blocks, null, () =>
        {
            if (!io.r.ReadBoolean()) return null;
            return new ReplayMpb
            {
                mask = io.r.ReadInt32(), color = io.Col(), baseColor = io.Col(), tint = io.Col(), emission = io.Col(), intensity = io.r.ReadSingle(),
                mainTex = io.Obj<Texture>(), baseMap = io.Obj<Texture>(),
            };
        });
        shadows = (ShadowCastingMode)io.r.ReadInt32(); receiveShadows = io.r.ReadBoolean(); layer = io.r.ReadInt32(); renderingLayerMask = io.r.ReadUInt32();
    }

    protected void Capture(Renderer r)
    {
        renderer = r;
        live = r.transform;
        shadows = r.shadowCastingMode;
        receiveShadows = r.receiveShadows;
        layer = r.gameObject.layer;
        renderingLayerMask = r.renderingLayerMask;
        label = NameOf(live);
    }

    protected bool PollRenderer(float t, int frame)
    {
        if (renderer == null) return false;
        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) return false;
        // Hidden from view - unless only the player's sensors hide it (fog of war): the replay sees everything.
        if (renderer.forceRenderingOff && (anchor == null || anchor.sensors == null || anchor.sensors.Visible)) return false;
        int phase = frame + id;
        if (!polled || (phase & 15) == 0)
        {
            renderer.GetSharedMaterials(materialBuffer);
            Material[] last = materials.Last;
            bool changed = last == null || last.Length != materialBuffer.Count;
            for (int i = 0; !changed && i < last.Length; i++) changed = last[i] != materialBuffer[i];
            if (changed) materials.Set(t, materialBuffer.ToArray());
        }
        if (!polled || blockCandidate || (phase & 7) == 0)
        {
            if (renderer.HasPropertyBlock())
            {
                blockCandidate = true;
                readBlock ??= new MaterialPropertyBlock();
                renderer.GetPropertyBlock(readBlock);
                ReplayMpb now = ReplayMpb.Read(readBlock);
                if (!now.Same(blocks.Last)) blocks.Set(t, now);
            }
            else if (blocks.Last != null) blocks.Set(t, null);
        }
        polled = true;
        return true;
    }

    protected void SetupGhostRenderer(Renderer r)
    {
        ghostRenderer = r;
        r.shadowCastingMode = shadows;
        r.receiveShadows = receiveShadows;
        r.renderingLayerMask = renderingLayerMask;
        ghost.layer = layer;
    }

    protected void ApplyRenderer(float t)
    {
        Material[] m = materials.Evaluate(t, ref materialCursor);
        if (m != shownMaterials) { ghostRenderer.sharedMaterials = m ?? new Material[0]; shownMaterials = m; }
        ReplayMpb b = blocks.Evaluate(t, ref blockCursor);
        if (b != shownBlock)
        {
            if (b == null) ghostRenderer.SetPropertyBlock(null);
            else
            {
                writeBlock ??= new MaterialPropertyBlock();
                b.Write(writeBlock);
                ghostRenderer.SetPropertyBlock(writeBlock);
            }
            shownBlock = b;
        }
    }
}

public class ReplayMeshItem : ReplayRendererItem
{
    public MeshFilter filter;
    public readonly StepTrack<Mesh> meshes = new StepTrack<Mesh>();
    private bool polledMesh;
    private int meshCursor;
    private Mesh shownMesh;
    private int shownVersion = -1;
    protected MeshFilter ghostFilter;

    public ReplayMeshItem(MeshRenderer r, MeshFilter f) { Capture(r); filter = f; }

    // Glow cards the game turns to face its camera (a strike craft's shield flash, FTL glows): recorded facing the
    // battle's camera, so a replay turns them to face its own.
    public override bool FacesCamera => facesCamera ??= label == "ShieldFlash" || label == "FTLCore" || label == "FTLFlash";
    private bool? facesCamera;
    public ReplayMeshItem() { }

    public override int TypeTag => 1;
    public override void Save(ReplayIO io) { SaveRenderer(io); io.Steps(meshes, m => io.Obj(m), null); }
    public override void Load(ReplayIO io) { LoadRenderer(io); io.Steps(meshes, null, () => io.Obj<Mesh>()); }

    public override bool Poll(float t, int frame)
    {
        if (!PollRenderer(t, frame)) return false;
        if (!polledMesh || ((frame + id) & 3) == 0)
        {
            Mesh m = filter != null ? filter.sharedMesh : null;
            if (m != meshes.Last || meshes.Count == 0) meshes.Set(t, m);
            polledMesh = true;
        }
        return true;
    }

    public override void Build(ReplayPlayer player)
    {
        ghost = new GameObject(label);
        ghostT = ghost.transform;
        ghostFilter = ghost.AddComponent<MeshFilter>();
        SetupGhostRenderer(ghost.AddComponent<MeshRenderer>());
    }

    public override void Apply(ReplayPlayer player, float t, bool jumped)
    {
        Mesh m = meshes.Evaluate(t, ref meshCursor);
        int version = -1;
        Mesh drawn = m != null ? player.MeshAt(m, t, out version) : null;
        if (drawn != shownMesh || version != shownVersion) { ghostFilter.sharedMesh = drawn; shownMesh = drawn; shownVersion = version; }
        ApplyRenderer(t);
    }

    public Mesh MeshAt(float t)
    {
        int c = 0;
        return meshes.Evaluate(t, ref c);
    }
}

// A shield bubble: drawn as a mesh, its ripples and charge fed to the shader as the generator does.
public class ReplayShieldItem : ReplayMeshItem
{
    public ShieldGenerator generator;
    public readonly StepTrack<Vector4[]> hits = new StepTrack<Vector4[]>(), hitData = new StepTrack<Vector4[]>();
    public readonly StepTrack<float> fraction = new StepTrack<float>(1f);
    private int ripples = -1, hitsCursor, dataCursor, fractionCursor;

    public ReplayShieldItem(MeshRenderer r, MeshFilter f, ShieldGenerator g) : base(r, f) { generator = g; }
    public ReplayShieldItem() { }

    public override int TypeTag => 2;
    public override void Save(ReplayIO io)
    {
        base.Save(io);
        io.Steps(hits, a => { io.w.Write(a.Length); foreach (Vector4 v in a) io.V4(v); }, null);
        io.Steps(hitData, a => { io.w.Write(a.Length); foreach (Vector4 v in a) io.V4(v); }, null);
        io.Steps(fraction, f => io.w.Write(f), null);
    }
    public override void Load(ReplayIO io)
    {
        base.Load(io);
        System.Func<Vector4[]> read = () => { Vector4[] a = new Vector4[io.r.ReadInt32()]; for (int i = 0; i < a.Length; i++) a[i] = io.V4(); return a; };
        io.Steps(hits, null, read);
        io.Steps(hitData, null, read);
        io.Steps(fraction, null, io.r.ReadSingle);
    }

    public override bool Poll(float t, int frame)
    {
        if (!base.Poll(t, frame)) return false;
        if (generator == null) return true;
        if (generator.RippleCount != ripples)
        {
            ripples = generator.RippleCount;
            hits.Set(t, (Vector4[])generator.Hits.Clone());
            hitData.Set(t, (Vector4[])generator.HitData.Clone());
        }
        float f = Mathf.Round(generator.Fraction * 200f) / 200f;
        fraction.Set(t, f);
        return true;
    }

    public override void Apply(ReplayPlayer player, float t, bool jumped)
    {
        base.Apply(player, t, jumped);
        Vector4[] h = hits.Evaluate(t, ref hitsCursor), d = hitData.Evaluate(t, ref dataCursor);
        if (h == null || d == null) return;
        writeBlock ??= new MaterialPropertyBlock();
        writeBlock.Clear();
        writeBlock.SetVectorArray(ShieldGenerator.HitsId, h);
        writeBlock.SetVectorArray(ShieldGenerator.HitDataId, d);
        writeBlock.SetVector(ShieldGenerator.BubbleScaleId, ghostT.lossyScale);
        writeBlock.SetFloat(ShieldGenerator.ShieldTimeId, t);
        writeBlock.SetFloat(ShieldGenerator.FractionId, fraction.Evaluate(t, ref fractionCursor));
        ghostRenderer.SetPropertyBlock(writeBlock);
    }
}

public class ReplayLineItem : ReplayRendererItem
{
    public LineRenderer line;
    public readonly StepTrack<Vector3[]> positions = new StepTrack<Vector3[]>();
    public readonly StepTrack<float> width = new StepTrack<float>(1f);
    private bool worldSpace, loop;
    private LineAlignment alignment;
    private LineTextureMode textureMode;
    private int caps, corners;
    private Gradient gradient;
    private AnimationCurve widthCurve;
    private static readonly Vector3[] buffer = new Vector3[64];
    private LineRenderer ghostLine;
    private int positionsCursor, widthCursor;
    private Vector3[] shownPositions;

    public ReplayLineItem() { }

    public override int TypeTag => 3;
    public override void Save(ReplayIO io)
    {
        SaveRenderer(io);
        io.w.Write(worldSpace); io.w.Write(loop); io.w.Write((int)alignment); io.w.Write((int)textureMode); io.w.Write(caps); io.w.Write(corners);
        io.Gradient(gradient); io.Curve(widthCurve);
        io.Steps(positions, a => { io.w.Write(a.Length); foreach (Vector3 v in a) io.V3(v); }, null);
        io.Steps(width, f => io.w.Write(f), null);
    }
    public override void Load(ReplayIO io)
    {
        LoadRenderer(io);
        worldSpace = io.r.ReadBoolean(); loop = io.r.ReadBoolean(); alignment = (LineAlignment)io.r.ReadInt32(); textureMode = (LineTextureMode)io.r.ReadInt32();
        caps = io.r.ReadInt32(); corners = io.r.ReadInt32();
        gradient = io.Gradient(); widthCurve = io.Curve();
        io.Steps(positions, null, () => { Vector3[] a = new Vector3[io.r.ReadInt32()]; for (int i = 0; i < a.Length; i++) a[i] = io.V3(); return a; });
        io.Steps(width, null, io.r.ReadSingle);
    }

    public ReplayLineItem(LineRenderer r)
    {
        Capture(r);
        line = r;
        worldSpace = r.useWorldSpace; loop = r.loop; alignment = r.alignment; textureMode = r.textureMode;
        caps = r.numCapVertices; corners = r.numCornerVertices;
        gradient = r.colorGradient; widthCurve = r.widthCurve;
    }

    public override bool Poll(float t, int frame)
    {
        if (!PollRenderer(t, frame)) return false;
        int count = Mathf.Min(line.positionCount, buffer.Length);
        line.GetPositions(buffer);
        Vector3[] last = positions.Last;
        bool changed = last == null || last.Length != count;
        for (int i = 0; !changed && i < count; i++) changed = last[i] != buffer[i];
        if (changed)
        {
            Vector3[] copy = new Vector3[count];
            System.Array.Copy(buffer, copy, count);
            positions.Set(t, copy);
        }
        width.Set(t, line.widthMultiplier);
        if (frame % 30 == 0) gradient = line.colorGradient;
        return true;
    }

    public override void Build(ReplayPlayer player)
    {
        ghost = new GameObject(label);
        ghostT = ghost.transform;
        ghostLine = ghost.AddComponent<LineRenderer>();
        ghostLine.useWorldSpace = worldSpace;
        ghostLine.loop = loop;
        ghostLine.alignment = alignment;
        ghostLine.textureMode = textureMode;
        ghostLine.numCapVertices = caps;
        ghostLine.numCornerVertices = corners;
        ghostLine.colorGradient = gradient;
        ghostLine.widthCurve = widthCurve;
        SetupGhostRenderer(ghostLine);
    }

    public override void Apply(ReplayPlayer player, float t, bool jumped)
    {
        Vector3[] p = positions.Evaluate(t, ref positionsCursor);
        if (p != shownPositions && p != null)
        {
            ghostLine.positionCount = p.Length;
            ghostLine.SetPositions(p);
            shownPositions = p;
        }
        ghostLine.widthMultiplier = width.Evaluate(t, ref widthCursor);
        ApplyRenderer(t);
    }
}

// A missile's smoke trail: redrawn from where the missile actually flew.
public class ReplayTrailItem : ReplayRendererItem
{
    public TrailRenderer trail;
    public readonly StepTrack<bool> emitting = new StepTrack<bool>(true);
    private float time, widthMultiplier;
    private Gradient gradient;
    private AnimationCurve widthCurve;
    private LineAlignment alignment;
    private LineTextureMode textureMode;
    private int caps;
    private LineRenderer ghostLine;
    private readonly List<Vector3> points = new List<Vector3>();
    private Vector3[] pointArray = new Vector3[48];

    public ReplayTrailItem() { }

    public override int TypeTag => 4;
    public override void Save(ReplayIO io)
    {
        SaveRenderer(io);
        io.w.Write(time); io.w.Write(widthMultiplier); io.Gradient(gradient); io.Curve(widthCurve);
        io.w.Write((int)alignment); io.w.Write((int)textureMode); io.w.Write(caps);
        io.Steps(emitting, b => io.w.Write(b), null);
    }
    public override void Load(ReplayIO io)
    {
        LoadRenderer(io);
        time = io.r.ReadSingle(); widthMultiplier = io.r.ReadSingle(); gradient = io.Gradient(); widthCurve = io.Curve();
        alignment = (LineAlignment)io.r.ReadInt32(); textureMode = (LineTextureMode)io.r.ReadInt32(); caps = io.r.ReadInt32();
        io.Steps(emitting, null, io.r.ReadBoolean);
    }

    public ReplayTrailItem(TrailRenderer r)
    {
        Capture(r);
        trail = r;
        time = r.time; widthMultiplier = r.widthMultiplier; gradient = r.colorGradient; widthCurve = r.widthCurve;
        alignment = r.alignment; textureMode = r.textureMode; caps = r.numCapVertices;
    }

    public override bool Poll(float t, int frame)
    {
        if (!PollRenderer(t, frame)) return false;
        emitting.Set(t, trail.emitting);
        return true;
    }

    public override void Build(ReplayPlayer player)
    {
        ghost = new GameObject(label);
        ghostT = ghost.transform;
        ghostLine = ghost.AddComponent<LineRenderer>();
        ghostLine.useWorldSpace = true;
        ghostLine.alignment = alignment;
        ghostLine.textureMode = textureMode;
        ghostLine.numCapVertices = caps;
        ghostLine.colorGradient = gradient;
        ghostLine.widthCurve = widthCurve;
        ghostLine.widthMultiplier = widthMultiplier;
        SetupGhostRenderer(ghostLine);
    }

    public override void Apply(ReplayPlayer player, float t, bool jumped)
    {
        // Newest point first (the trail's gradient and width run head to tail), back over the trail's lifetime.
        points.Clear();
        int steps = pointArray.Length;
        int anchorC = anchorCursor, poseC = poseCursor, emitC = 0;
        for (int i = 0; i < steps; i++)
        {
            float at = t - time * i / (steps - 1);
            if (at < born) break;
            if (i > 0 && !emitting.Evaluate(at, ref emitC)) break;
            int visC = 0;
            if (!visible.Evaluate(at, ref visC)) break; // (a pooled missile before this flight)
            ReplayAnchor a = anchors.Evaluate(at, ref anchorC);
            if (a == null || (i > 0 && a != ghostParent)) break;
            int c = 0;
            ReplayPose world = ReplayPose.Combine(a.pose.Evaluate(at, ref c), pose.Evaluate(at, ref poseC));
            points.Add(world.p);
        }
        for (int i = 0; i < points.Count; i++) pointArray[i] = points[i];
        ghostLine.positionCount = points.Count;
        ghostLine.SetPositions(pointArray);
        ApplyRenderer(t);
    }
}

// ---------------- Effects ----------------

public class ReplayParticleItem : ReplayItem
{
    public class Segment
    {
        public float start, stopEmit = float.PositiveInfinity, end = float.PositiveInfinity;
        public uint[] seeds;
    }

    public ParticleSystem system;
    public ParticleSystem[] systems;
    public GameObject template;
    public readonly List<Segment> segments = new List<Segment>();
    private Segment current;

    private ParticleSystem ghostSystem;
    private ParticleSystem[] ghostSystems;
    private float[] baseSpeeds;
    private Segment shown;
    private int segmentCursor;
    private float shownSpeed = -1f;
    private bool shownPaused;

    public override bool Alive => system != null;

    // Where the effect came from, for saved replays: a prefab of its own name, or its place in the prefab its
    // top-level object was made from.
    public string sourceName, rootName, path;
    public string templateKey, templatePath;

    public ReplayParticleItem() { }

    public override int TypeTag => 5;
    public override void Save(ReplayIO io)
    {
        ReplayFile.EffectSource(this, out string key, out string at);
        io.Str(key); io.Str(at);
        io.w.Write(segments.Count);
        foreach (Segment s in segments)
        {
            io.w.Write(s.start); io.w.Write(s.stopEmit); io.w.Write(s.end);
            io.w.Write(s.seeds.Length);
            foreach (uint seed in s.seeds) io.w.Write(seed);
        }
    }
    public override void Load(ReplayIO io)
    {
        string key = io.Str(), at = io.Str();
        templateKey = key; templatePath = at; // the template itself is made on the main thread (ReplayFile.Load)
        int n = io.r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            Segment s = new Segment { start = io.r.ReadSingle(), stopEmit = io.r.ReadSingle(), end = io.r.ReadSingle(), seeds = new uint[io.r.ReadInt32()] };
            for (int k = 0; k < s.seeds.Length; k++) s.seeds[k] = io.r.ReadUInt32();
            segments.Add(s);
        }
    }

    public ReplayParticleItem(ParticleSystem ps, Transform holder)
    {
        sourceName = ps.name;
        Transform root = ps.transform.root;
        rootName = root.name;
        path = root == ps.transform ? "" : ReplayFile.PathFrom(root, ps.transform);
        system = ps;
        live = ps.transform;
        label = NameOf(live);
        systems = ps.GetComponentsInChildren<ParticleSystem>(true);
        // A copy taken now, while the effect still exists: switched off and stripped to its particle systems.
        template = Object.Instantiate(ps.gameObject, holder, false); // (its own scale kept: see Scale)
        ReplayRecorder.StripToParticles(template);
    }

    public override bool Poll(float t, int frame)
    {
        if (system == null) return false;
        bool active = system.gameObject.activeInHierarchy;
        bool playing = active && system.isPlaying;
        bool on = playing || (active && current != null && system.IsAlive(true));
        if (current == null)
        {
            if (playing) Begin(t);
        }
        else if (!on)
        {
            current.end = t;
            if (current.stopEmit > t) current.stopEmit = t;
            current = null;
        }
        else
        {
            bool emitting = playing && system.isEmitting;
            if (!emitting && current.stopEmit == float.PositiveInfinity) current.stopEmit = t;
            else if (emitting && current.stopEmit != float.PositiveInfinity)
            {
                current.end = t;
                Begin(t); // Played again.
            }
        }
        return current != null;
    }

    private void Begin(float t)
    {
        current = new Segment { start = t, seeds = new uint[systems.Length] };
        for (int i = 0; i < systems.Length; i++) current.seeds[i] = systems[i] != null && !systems[i].useAutoRandomSeed ? systems[i].randomSeed : 0u;
        segments.Add(current);
    }

    public override void Close(float t)
    {
        if (current != null) { current.end = t; if (current.stopEmit > t) current.stopEmit = t; current = null; }
    }

    public override void Build(ReplayPlayer player)
    {
        if (template == null) { ghost = new GameObject(label); ghostT = ghost.transform; return; }
        // The effect inside a holder: the holder carries the recorded pose, the effect its own scale, as an
        // effect's particles are usually sized by its own scale alone (not its ship's).
        ghost = new GameObject(label);
        ghost.SetActive(false);
        ghostT = ghost.transform;
        GameObject effect = Object.Instantiate(template, ghostT, false);
        effect.transform.localPosition = Vector3.zero;
        effect.transform.localRotation = Quaternion.identity;
        effect.SetActive(true);
        ownScale = template.transform.localScale;
        ghostSystem = effect.GetComponent<ParticleSystem>();
        ghostSystems = effect.GetComponentsInChildren<ParticleSystem>(true);
        baseSpeeds = new float[ghostSystems.Length];
        for (int i = 0; i < ghostSystems.Length; i++)
        {
            ParticleSystem.MainModule main = ghostSystems[i].main;
            main.playOnAwake = false;
            main.useUnscaledTime = true; // The battle is frozen while its replay plays.
            baseSpeeds[i] = main.simulationSpeed;
        }
    }

    private Segment SegmentAt(float t)
    {
        for (int k = 0; k < 3 && segmentCursor < segments.Count - 1 && segments[segmentCursor].end <= t; k++) segmentCursor++;
        if (segmentCursor < segments.Count && segments[segmentCursor].start <= t && t < segments[segmentCursor].end) return segments[segmentCursor];
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].start <= t && t < segments[i].end) { segmentCursor = i; return segments[i]; }
            if (segments[i].start > t) break;
        }
        return null;
    }

    private Vector3 ownScale = Vector3.one;

    public override Vector3 Scale(Vector3 recorded) => new Vector3(Div(recorded.x, ownScale.x), Div(recorded.y, ownScale.y), Div(recorded.z, ownScale.z));
    private static float Div(float a, float b) => Mathf.Abs(b) < 1e-6f ? a : a / b;

    public override void Hidden(ReplayPlayer player) { shown = null; }

    public override void Apply(ReplayPlayer player, float t, bool jumped)
    {
        if (ghostSystem == null) return;
        Segment seg = SegmentAt(t);
        if (seg == null) { if (shown != null) { ghostSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); shown = null; } return; }
        if (seg != shown || jumped)
        {
            ghostSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            for (int i = 0; i < ghostSystems.Length && i < seg.seeds.Length; i++)
            {
                if (seg.seeds[i] == 0u) continue;
                ghostSystems[i].useAutoRandomSeed = false;
                ghostSystems[i].randomSeed = seg.seeds[i];
            }
            float elapsed = Mathf.Min(t - seg.start, ghostSystem.main.loop ? 6f : 12f);
            if (elapsed > 0.02f) ghostSystem.Simulate(elapsed, true, true, false);
            ghostSystem.Play(true);
            if (t >= seg.stopEmit) ghostSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            shown = seg;
            shownSpeed = -1f;
            shownPaused = false;
        }
        else if (t >= seg.stopEmit && ghostSystem.isEmitting)
        {
            ghostSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        bool paused = player.Paused;
        if (paused != shownPaused)
        {
            if (paused) ghostSystem.Pause(true); else ghostSystem.Play(true);
            if (!paused && t >= seg.stopEmit) ghostSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            shownPaused = paused;
        }
        if (player.Speed != shownSpeed)
        {
            for (int i = 0; i < ghostSystems.Length; i++)
            {
                ParticleSystem.MainModule main = ghostSystems[i].main;
                main.simulationSpeed = baseSpeeds[i] * player.Speed;
            }
            shownSpeed = player.Speed;
        }
    }
}

public class ReplayLightItem : ReplayItem
{
    public struct State : System.IEquatable<State>
    {
        public float intensity, range;
        public Color color;
        public bool Equals(State o) => intensity == o.intensity && range == o.range && color == o.color;
    }

    public Light light;
    public readonly StepTrack<State> states = new StepTrack<State>();
    private LightType type;
    private float spotAngle, innerSpotAngle;
    private Light ghostLight;
    private int stateCursor;

    public override bool Alive => light != null;

    public ReplayLightItem() { }

    public override int TypeTag => 6;
    public override void Save(ReplayIO io)
    {
        io.w.Write((int)type); io.w.Write(spotAngle); io.w.Write(innerSpotAngle);
        io.Steps(states, x => { io.w.Write(x.intensity); io.w.Write(x.range); io.Col(x.color); }, null);
    }
    public override void Load(ReplayIO io)
    {
        type = (LightType)io.r.ReadInt32(); spotAngle = io.r.ReadSingle(); innerSpotAngle = io.r.ReadSingle();
        io.Steps(states, null, () => new State { intensity = io.r.ReadSingle(), range = io.r.ReadSingle(), color = io.Col() });
    }

    public ReplayLightItem(Light l)
    {
        light = l;
        live = l.transform;
        label = NameOf(live);
        type = l.type; spotAngle = l.spotAngle; innerSpotAngle = l.innerSpotAngle;
    }

    public override bool Poll(float t, int frame)
    {
        if (light == null || !light.enabled || !light.gameObject.activeInHierarchy) return false;
        states.Set(t, new State { intensity = light.intensity, range = light.range, color = light.color });
        return true;
    }

    public override void Build(ReplayPlayer player)
    {
        ghost = new GameObject(label);
        ghostT = ghost.transform;
        ghostLight = ghost.AddComponent<Light>();
        ghostLight.type = type;
        ghostLight.spotAngle = spotAngle;
        ghostLight.innerSpotAngle = innerSpotAngle;
        ghostLight.shadows = LightShadows.None;
    }

    public override void Apply(ReplayPlayer player, float t, bool jumped)
    {
        State s = states.Evaluate(t, ref stateCursor);
        ghostLight.intensity = s.intensity;
        ghostLight.range = s.range;
        ghostLight.color = s.color;
    }
}
