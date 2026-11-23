using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.SceneManagement;

// Records a battle so it can be replayed exactly, from any camera.
//
// It watches the scene rather than the game: every object that appears after the battle's setup (ships, craft,
// missiles, wreckage, effects, lights) becomes an anchor, and everything inside it that draws becomes an item,
// with its pose, visibility, mesh, materials and shader values tracked over battle time. Hull meshes bent and
// holed during the fight keep every version (as diffs). Laser bolts and CIWS rounds, which are drawn as one big
// mesh each, are kept as shots (where, which way, how fast, when it ended); sounds, puffs and FTL streaks as
// events. Nothing here touches the battle itself, so a replay shows what happened, wherever the camera was.
//
// Per frame: a parallel job reads every tracked transform; the poses are thinned (see PoseTrack) on worker
// threads; the rest is a cheap poll per item. Settings > Display > Battle Replays switches it off.
[DefaultExecutionOrder(32000)]
public class ReplayRecorder : MonoBehaviour
{
    public static ReplayRecorder Instance { get; private set; }
    public static bool Recording => Instance != null && Instance.recording;
    public static bool Available => Instance != null && Instance.anchors.Count > 0;

    private const float AnchorPosEps = 0.01f, AnchorRotEps = 0.12f, ItemPosEps = 0.005f, ItemRotEps = 0.3f;
    private const double RescanBudgetMs = 1.5;

    public float StartTime { get; private set; }
    public float EndTime { get; private set; }
    private bool recording;
    private const long MeshBudgetBytes = 600L << 20;
    private bool meshBudgetSpent;
    private int meshWrites;

    public readonly List<ReplayAnchor> anchors = new List<ReplayAnchor>();
    public readonly List<ReplayItem> items = new List<ReplayItem>();
    private readonly List<ReplayAnchor> liveAnchors = new List<ReplayAnchor>();
    private readonly List<ReplayItem> liveItems = new List<ReplayItem>();
    private readonly Dictionary<int, ReplayItem> itemsByComponent = new Dictionary<int, ReplayItem>();
    private readonly Dictionary<int, ReplayAnchor> anchorsByRoot = new Dictionary<int, ReplayAnchor>();
    private readonly HashSet<int> environment = new HashSet<int>(), seenRoots = new HashSet<int>(), ignored = new HashSet<int>();
    public readonly Dictionary<Mesh, ReplayMeshHistory> meshes = new Dictionary<Mesh, ReplayMeshHistory>();
    private readonly List<Mesh> kept = new List<Mesh>();
    public readonly ReplayShots bolts = new ReplayShots(), tracers = new ReplayShots();
    public readonly List<ReplaySound> sounds = new List<ReplaySound>();
    public readonly List<ReplayPuff> puffs = new List<ReplayPuff>();
    public readonly List<ReplayStreak> streaks = new List<ReplayStreak>();
    public HashSet<int> Environment => environment;
    private int nextId = 1;
    private Transform holder;

    private TransformAccessArray transforms;
    private readonly List<object> slotOwners = new List<object>();
    private NativeArray<Matrix4x4> matrices;
    private NativeArray<byte> valid;
    private Matrix4x4[] worldMatrices = new Matrix4x4[0];
    private byte[] validSlots = new byte[0];
    private readonly List<GameObject> rootBuffer = new List<GameObject>();
    private readonly List<Renderer> rendererBuffer = new List<Renderer>();
    private readonly List<ParticleSystem> particleBuffer = new List<ParticleSystem>();
    private readonly List<Light> lightBuffer = new List<Light>();
    private readonly HashSet<Transform> excludedBranches = new HashSet<Transform>(), excludedObjects = new HashSet<Transform>();
    private readonly HashSet<Renderer> lowerLods = new HashSet<Renderer>();
    private int lastRootCount = -1;
    private float lastSampleAt = float.NegativeInfinity;

    // Costs, for the dev report.
    private readonly Stopwatch clock = new Stopwatch();
    public double TotalMs { get; private set; }
    public double WorstMs { get; private set; }
    public int Frames { get; private set; }
    private readonly double[] phases = new double[5];
    private void Phase(int i, ref double mark) { double now = clock.Elapsed.TotalMilliseconds; phases[i] += now - mark; mark = now; }

    public static void Begin()
    {
        if (Instance != null || !GameSettings.RecordReplays || HullScarring.Headless || Application.isBatchMode) return;
        new GameObject("ReplayRecorder").AddComponent<ReplayRecorder>();
    }

    // A recording read from a file, for playback only.
    private static bool forPlayback;
    public static ReplayRecorder ForPlayback()
    {
        forPlayback = true;
        try { return new GameObject("ReplayRecording").AddComponent<ReplayRecorder>(); }
        finally { forPlayback = false; }
    }

    public void SetSpan(float start, float end) { StartTime = start; EndTime = end; }

    private void Awake()
    {
        Instance = this;
        if (forPlayback)
        {
            // Everything already in the scene is the stage the replay plays on.
            gameObject.scene.GetRootGameObjects(rootBuffer);
            foreach (GameObject root in rootBuffer) environment.Add(root.GetInstanceID());
            environment.Add(gameObject.GetInstanceID());
            return;
        }
        StartTime = Time.time;
        recording = true;
        GameObject h = new GameObject("ReplayTemplates");
        h.SetActive(false);
        h.transform.SetParent(transform, false);
        holder = h.transform;
        transforms = new TransformAccessArray(1024);
        // Everything already in the scene - the map, terrain, cameras, interface - is the stage, not the battle.
        gameObject.scene.GetRootGameObjects(rootBuffer);
        foreach (GameObject root in rootBuffer) { environment.Add(root.GetInstanceID()); seenRoots.Add(root.GetInstanceID()); }
        environment.Add(gameObject.GetInstanceID());
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Dispose();
        foreach (Mesh m in kept) if (m != null) Destroy(m);
        kept.Clear();
    }

    private void Dispose()
    {
        if (transforms.isCreated) transforms.Dispose();
        if (matrices.IsCreated) matrices.Dispose();
        if (valid.IsCreated) valid.Dispose();
    }

    // Stops recording: everything still on the field is written out as it was last seen.
    public void Finish()
    {
        if (!recording) return;
        recording = false;
        EndTime = Mathf.Max(lastSampleAt, StartTime);
        foreach (ReplayAnchor a in liveAnchors) a.pose.Flush();
        foreach (ReplayItem item in liveItems) { item.pose.Flush(); item.Close(EndTime); }
        foreach (ReplayMeshHistory history in meshes.Values) history.Finish();
        bolts.Close(EndTime);
        tracers.Close(EndTime);
        Dispose();
    }

    // ---------------- Sampling ----------------

    private void LateUpdate()
    {
        if (!recording) return;
        float t = Time.time;
        if (t <= lastSampleAt) return; // Paused: nothing moves.
        clock.Restart();
        lastSampleAt = t;
        int frame = Time.frameCount;

        double mark = 0;
        DiscoverRoots(frame);
        Phase(0, ref mark);
        for (int i = liveAnchors.Count - 1; i >= 0; i--)
        {
            ReplayAnchor a = liveAnchors[i];
            if (a.live == null) { EndAnchor(a, t, i); continue; }
            // Parts added to it since its last look (decals, embers, fires): looked for again, at most a few times a second.
            if (a.live.hierarchyCount != a.expectedCount && t >= a.nextRescanAt) a.rescan = true;
        }
        Phase(1, ref mark);
        Stopwatch budget = Stopwatch.StartNew();
        foreach (ReplayAnchor a in liveAnchors)
        {
            if (!a.rescan) continue;
            Rescan(a, t);
            if (budget.Elapsed.TotalMilliseconds > RescanBudgetMs) break;
        }

        Phase(2, ref mark);
        for (int i = liveItems.Count - 1; i >= 0; i--)
        {
            ReplayItem item = liveItems[i];
            if (!item.Alive) { EndItem(item, t, i); continue; }
            if (item.anchor.live == null || ((frame + item.id) % 10) == 0) Reanchor(item, t);
            // Ship and wreck parts (most of what's tracked) are looked at every other frame; effects, lights and
            // beams every frame.
            if (item is ReplayMeshItem && item.polled && ((frame + item.id) & 1) != 0) continue;
            item.polled = true;
            bool shown = item.Poll(t, frame);
            item.visible.Set(t, shown);
            if (shown && !item.wasVisible) item.forceKey = true;
            item.wasVisible = shown;
        }

        Phase(3, ref mark);
        SamplePoses(t);
        Phase(4, ref mark);
        double ms = clock.Elapsed.TotalMilliseconds;
        TotalMs += ms;
        Frames++;
        if (ms > WorstMs && Frames > 30) WorstMs = ms;
    }

    private struct ReadJob : IJobParallelForTransform
    {
        public NativeArray<Matrix4x4> matrices;
        public NativeArray<byte> valid;

        public void Execute(int index, TransformAccess transform)
        {
            valid[index] = (byte)(transform.isValid ? 1 : 0);
            if (transform.isValid) matrices[index] = transform.localToWorldMatrix;
        }
    }

    private void SamplePoses(float t)
    {
        int n = transforms.length;
        if (n == 0) return;
        if (!matrices.IsCreated || matrices.Length < n)
        {
            if (matrices.IsCreated) matrices.Dispose();
            if (valid.IsCreated) valid.Dispose();
            int size = Mathf.NextPowerOfTwo(n);
            matrices = new NativeArray<Matrix4x4>(size, Allocator.Persistent);
            valid = new NativeArray<byte>(size, Allocator.Persistent);
        }
        new ReadJob { matrices = matrices, valid = valid }.Schedule(transforms).Complete();
        // Copied out for the worker threads below (native arrays are for jobs).
        if (worldMatrices.Length < n) { worldMatrices = new Matrix4x4[matrices.Length]; validSlots = new byte[matrices.Length]; }
        NativeArray<Matrix4x4>.Copy(matrices, worldMatrices, n);
        NativeArray<byte>.Copy(valid, validSlots, n);
        Matrix4x4[] m = worldMatrices;
        byte[] ok = validSlots;
        foreach (ReplayAnchor a in liveAnchors)
        {
            if (a.slot < 0 || ok[a.slot] == 0) { a.placed = false; continue; }
            a.now = ReplayPose.FromMatrix(m[a.slot]);
            a.placed = true;
            a.pose.Add(t, a.now, AnchorPosEps * Mathf.Max(1f, a.size * 0.02f), AnchorRotEps);
        }

        // Items in their anchor's space: a turret, a hull plate, a decal sit still in their ship's, so a fleet
        // on the move costs one track per ship, not one per part.
        System.Threading.Tasks.Parallel.For(0, (liveItems.Count + 255) / 256, chunk =>
        {
            int end = Mathf.Min(liveItems.Count, (chunk + 1) * 256);
            for (int i = chunk * 256; i < end; i++)
            {
                ReplayItem item = liveItems[i];
                if (!item.wasVisible || item.slot < 0 || ok[item.slot] == 0 || !item.anchor.placed) continue;
                ReplayPose world = ReplayPose.FromMatrix(m[item.slot]);
                ReplayPose local = ReplayPose.Relative(item.anchor.now, world);
                item.pose.Add(t, local, ItemPosEps, ItemRotEps, item.forceKey);
                item.forceKey = false;
            }
        });
    }

    // ---------------- Discovery ----------------

    private void DiscoverRoots(int frame)
    {
        Scene scene = gameObject.scene;
        int count = scene.rootCount;
        if ((count == lastRootCount || (frame & 1) != 0) && frame % 15 != 0) return;
        lastRootCount = count;
        scene.GetRootGameObjects(rootBuffer);
        float t = Time.time;
        foreach (GameObject root in rootBuffer)
        {
            int key = root.GetInstanceID();
            if (seenRoots.Contains(key)) continue;
            seenRoots.Add(key);
            if (ExcludedRoot(root)) continue;
            AnchorFor(root.transform, t);
        }
    }

    private ReplayAnchor AnchorFor(Transform root, float t)
    {
        int key = root.GetInstanceID();
        if (anchorsByRoot.TryGetValue(key, out ReplayAnchor known)) return known;
        ReplayAnchor a = new ReplayAnchor { id = nextId++, live = root, born = t, name = root.name };
        Classify(a);
        a.slot = AddSlot(root, a);
        anchorsByRoot[key] = a;
        anchors.Add(a);
        liveAnchors.Add(a);
        a.now = ReplayPose.FromMatrix(root.localToWorldMatrix);
        a.placed = true;
        a.pose.Add(t, a.now, 0f, 0f, true);
        return a;
    }

    private static void Classify(ReplayAnchor a)
    {
        Transform root = a.live;
        if (root.TryGetComponent(out Squadron _)) a.kind = ReplayKind.Craft;
        else if (root.TryGetComponent(out UnitHealthManager unit)) { a.kind = ReplayKind.Ship; a.attackerSide = unit.isAttackerSide; }
        else if (root.TryGetComponent(out Missile _)) a.kind = ReplayKind.Missile;
        else if (root.GetComponentInChildren<WreckChunk>(true) != null) a.kind = ReplayKind.Debris;
        if (root.TryGetComponent(out UnitHealthManager side)) a.attackerSide = side.isAttackerSide;
        root.TryGetComponent(out a.sensors);
    }

    private static bool ExcludedRoot(GameObject root)
    {
        if (root.layer == 5) return true;
        if (root.TryGetComponent(out Canvas _) || root.TryGetComponent(out Camera _)) return true;
        if (root.TryGetComponent(out Laser _) || root.TryGetComponent(out LaserSystem _) || root.TryGetComponent(out CIWSTracers _)) return true;
        if (root.TryGetComponent(out PlaneShadows _) || root.TryGetComponent(out HardpointOverlay _) || root.TryGetComponent(out OrderPreview _)
            || root.TryGetComponent(out DropZoneOverlay _) || root.TryGetComponent(out SquadronIconLayout _)) return true;
        if (root.TryGetComponent(out ReplayPlayer _) || root.TryGetComponent(out ReplayRecorder _) || root.TryGetComponent(out UnityEngine.EventSystems.EventSystem _)) return true;
        string n = root.name;
        return n == "AltitudeMarkers" || n == "HeightPickPreview" || n == "ReplayStage";
    }

    private void Rescan(ReplayAnchor a, float t)
    {
        a.rescan = false;
        a.nextRescanAt = t + 0.3f;
        Transform root = a.live;
        if (root == null) return;
        a.expectedCount = root.hierarchyCount;

        excludedBranches.Clear();
        foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true)) excludedBranches.Add(c.transform);
        // (A ship's path line sits on the ship itself: only that line is left out.)
        excludedObjects.Clear();
        foreach (PathVisualization p in root.GetComponentsInChildren<PathVisualization>(true)) excludedObjects.Add(p.transform);
        lowerLods.Clear();
        foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
        {
            LOD[] lods = group.GetLODs();
            for (int i = 1; i < lods.Length; i++) foreach (Renderer r in lods[i].renderers) if (r != null) lowerLods.Add(r);
            if (lods.Length > 0) foreach (Renderer r in lods[0].renderers) if (r != null) lowerLods.Remove(r); // also in the full-detail level
        }

        bool sized = a.size > 1f;
        Bounds bounds = default;
        bool any = false;
        ReplayItem hull = null;
        float hullSize = 0f;
        int hullDetail = 0;
        root.GetComponentsInChildren(true, rendererBuffer);
        foreach (Renderer r in rendererBuffer)
        {
            if (r == null) continue;
            int key = r.GetInstanceID();
            if (itemsByComponent.TryGetValue(key, out ReplayItem item))
            {
                if (item.anchor != a) Switch(item, a, t);
            }
            else
            {
                if (ignored.Contains(key)) continue;
                item = Make(r);
                if (item == null) { ignored.Add(key); continue; }
                Add(item, key, a, t);
            }
            if (!(item is ReplayMeshItem) || item is ReplayShieldItem || !r.enabled) continue;
            Bounds b = r.bounds;
            if (!sized) { if (!any) { bounds = b; any = true; } else bounds.Encapsulate(b); }
            // The hull: the most detailed mesh (the biggest can be a glow or a plate).
            ReplayMeshItem part = (ReplayMeshItem)item;
            int detail = part.filter != null && part.filter.sharedMesh != null ? part.filter.sharedMesh.vertexCount : 0;
            if (detail > hullDetail) { hullDetail = detail; hullSize = b.extents.sqrMagnitude; hull = item; }
        }
        if (!sized && any) a.size = Mathf.Max(1f, bounds.extents.magnitude);
        // The ship's own hull: what the replay's cinematic camera frames.
        if (a.kind == ReplayKind.Ship && hull != null && !a.marked)
        {
            a.marked = true;
            hull.subject = ReplayKind.Ship;
            hull.subjectSize = Mathf.Max(1f, Mathf.Sqrt(hullSize));
            hull.label = a.name;
        }

        root.GetComponentsInChildren(true, particleBuffer);
        foreach (ParticleSystem ps in particleBuffer)
        {
            if (ps == null) continue;
            Transform parent = ps.transform.parent;
            if (parent != null && parent.GetComponentInParent<ParticleSystem>(true) != null) continue; // part of a bigger effect
            int key = ps.GetInstanceID();
            if (itemsByComponent.TryGetValue(key, out ReplayItem known)) { if (known.anchor != a) Switch(known, a, t); continue; }
            if (ignored.Contains(key)) continue;
            if (Excluded(ps.transform, ps.gameObject.layer)) { ignored.Add(key); continue; }
            Add(new ReplayParticleItem(ps, holder), key, a, t);
        }

        root.GetComponentsInChildren(true, lightBuffer);
        foreach (Light l in lightBuffer)
        {
            if (l == null) continue;
            int key = l.GetInstanceID();
            if (itemsByComponent.TryGetValue(key, out ReplayItem known)) { if (known.anchor != a) Switch(known, a, t); continue; }
            if (ignored.Contains(key)) continue;
            if (Excluded(l.transform, l.gameObject.layer)) { ignored.Add(key); continue; }
            Add(new ReplayLightItem(l), key, a, t);
        }

        if (a.kind == ReplayKind.Craft) MarkCraft(root);
    }

    private readonly Dictionary<string, int> rejected = new Dictionary<string, int>();
    private ReplayItem Reject(string why) { rejected.TryGetValue(why, out int c); rejected[why] = c + 1; return null; }

    private ReplayItem Make(Renderer r)
    {
        if (r is ParticleSystemRenderer) return null;
        if (r is SpriteRenderer || r is SkinnedMeshRenderer || r is BillboardRenderer) return Reject(r.GetType().Name);
        if (r.transform is RectTransform) return Reject("rect");
        if (lowerLods.Contains(r)) return Reject("lod");
        if (Excluded(r.transform, r.gameObject.layer)) return Reject("excluded");
        switch (r)
        {
            case MeshRenderer mesh:
                if (!mesh.TryGetComponent(out MeshFilter filter)) return Reject("nofilter");
                ShieldGenerator shield = mesh.GetComponentInParent<ShieldGenerator>();
                if (shield != null && shield.BubbleRenderer == mesh) return new ReplayShieldItem(mesh, filter, shield);
                return new ReplayMeshItem(mesh, filter);
            case LineRenderer line: return new ReplayLineItem(line);
            case TrailRenderer trail: return new ReplayTrailItem(trail);
        }
        return null;
    }

    private bool Excluded(Transform t, int layer)
    {
        if (layer == 5) return true;
        if (excludedObjects.Contains(t)) return true;
        for (Transform p = t; p != null; p = p.parent) if (excludedBranches.Contains(p)) return true;
        return false;
    }

    private void Add(ReplayItem item, int key, ReplayAnchor a, float t)
    {
        item.id = nextId++;
        item.born = t;
        item.anchor = a;
        item.anchors.Set(t, a);
        item.attackerSide = a.attackerSide;
        item.slot = AddSlot(item.live, item);
        itemsByComponent[key] = item;
        items.Add(item);
        liveItems.Add(item);
    }

    private void MarkCraft(Transform root)
    {
        foreach (Fighter fighter in root.GetComponentsInChildren<Fighter>(true))
        {
            ReplayItem body = null;
            float best = 0f;
            foreach (MeshRenderer r in fighter.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!itemsByComponent.TryGetValue(r.GetInstanceID(), out ReplayItem item)) continue;
                float size = r.bounds.extents.sqrMagnitude;
                if (size > best) { best = size; body = item; }
            }
            if (body != null && body.subject == ReplayKind.Other) { body.subject = ReplayKind.Craft; body.subjectSize = Mathf.Sqrt(best); body.label = root.name; }
        }
    }

    // Moved to another top-level object (wreckage breaking away, a turret blown off): it follows that one now.
    private void Reanchor(ReplayItem item, float t)
    {
        if (item.live == null) return;
        Transform root = item.live.root;
        if (item.anchor.live == root) return;
        int key = root.GetInstanceID();
        if (!anchorsByRoot.TryGetValue(key, out ReplayAnchor a))
        {
            seenRoots.Add(key);
            a = AnchorFor(root, t);
        }
        Switch(item, a, t);
    }

    private void Switch(ReplayItem item, ReplayAnchor a, float t)
    {
        item.pose.Flush();
        item.anchor = a;
        item.anchors.Set(t, a);
        item.forceKey = true;
    }

    private void EndAnchor(ReplayAnchor a, float t, int index)
    {
        a.gone = t;
        a.pose.Flush();
        RemoveSlot(a.slot);
        a.slot = -1;
        liveAnchors.RemoveAt(index);
    }

    private void EndItem(ReplayItem item, float t, int index)
    {
        item.gone = t;
        item.pose.Flush();
        item.Close(t);
        RemoveSlot(item.slot);
        item.slot = -1;
        liveItems[index] = liveItems[liveItems.Count - 1];
        liveItems.RemoveAt(liveItems.Count - 1);
    }

    private int AddSlot(Transform t, object owner)
    {
        transforms.Add(t);
        slotOwners.Add(owner);
        return transforms.length - 1;
    }

    private void RemoveSlot(int slot)
    {
        if (slot < 0) return;
        int last = transforms.length - 1;
        transforms.RemoveAtSwapBack(slot);
        object moved = slotOwners[last];
        slotOwners[slot] = moved;
        slotOwners.RemoveAt(last);
        if (slot == last) return;
        if (moved is ReplayAnchor a) a.slot = slot;
        else if (moved is ReplayItem i) i.slot = slot;
    }

    // An effect copy stripped to its particle systems (no scripts, sounds, lights or meshes - those are recorded
    // on their own), switched off.
    public static void StripToParticles(GameObject copy)
    {
        copy.SetActive(false);
        foreach (MonoBehaviour b in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(b);
        foreach (AudioSource s in copy.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(s);
        foreach (Light l in copy.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody r in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(r);
        foreach (LineRenderer r in copy.GetComponentsInChildren<LineRenderer>(true)) Object.DestroyImmediate(r);
        foreach (TrailRenderer r in copy.GetComponentsInChildren<TrailRenderer>(true)) Object.DestroyImmediate(r);
        foreach (MeshRenderer r in copy.GetComponentsInChildren<MeshRenderer>(true)) Object.DestroyImmediate(r);
        foreach (MeshFilter f in copy.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(f);
    }

    // ---------------- Hooks (called by the game) ----------------

    // A new random pattern for an effect about to play, readable afterwards (the engine's own is not).
    public static void Seed(ParticleSystem root)
    {
        if (!Recording || root == null) return;
        foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            bool playing = ps.isPlaying;
            if (playing) continue; // The seed can only change while stopped.
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)Random.Range(1, int.MaxValue);
        }
    }

    public static int BoltFired(Vector3 head, Vector3 velocity, Color color, float width) =>
        Recording ? Instance.bolts.Add(Time.time, head, velocity, color, width) : 0;

    public static void BoltEnded(int id) { if (Recording && id > 0) Instance.bolts.End(id, Time.time); }

    public static int TracerFired(Vector3 position, Vector3 velocity) =>
        Recording ? Instance.tracers.Add(Time.time, position, velocity, Color.white, 0f) : 0;

    public static void TracerEnded(int id) { if (Recording && id > 0) Instance.tracers.End(id, Time.time); }

    public static void Sound(Sfx.Cue cue, Vector3 position, float volume, float pitch)
    {
        if (Recording) Instance.sounds.Add(new ReplaySound { t = Time.time, cue = cue, position = position, volume = volume, pitch = pitch });
    }

    public static void Blast(AudioClip[] clips, Vector3 position, DestructionProfile profile, float pitch)
    {
        if (Recording) Instance.sounds.Add(new ReplaySound { t = Time.time, blast = clips, profile = profile, position = position, pitch = pitch });
    }

    public static void Puff(string key, ParticleSystem template, Vector3 position, float scale, int perSystem)
    {
        if (Recording) Instance.puffs.Add(new ReplayPuff { t = Time.time, key = key, template = template, position = position, scale = scale, count = perSystem });
    }

    public static void Streak(object look, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (Recording) Instance.streaks.Add(new ReplayStreak { t = Time.time, look = look, position = position, rotation = rotation, scale = scale });
    }

    // A copy of a ship's model about to be bent: its first version is the model's (not kept twice).
    public static void MeshCopied(Mesh copy, Mesh source)
    {
        if (!Recording || copy == null || source == null || Instance.meshes.ContainsKey(copy)) return;
        Instance.meshes[copy] = new ReplayMeshHistory(copy, source);
    }

    // A runtime mesh about to be rewritten (a dent, a hole): its first version is kept before it changes.
    // (Only hulls are followed - see MeshCopied. Scorch decals bent along with a dent are replayed as they ended up.)
    public static void MeshWriting(Mesh mesh) { }

    public static void MeshWritten(Mesh mesh, List<Vector3> v, List<Vector3> n, List<Vector4> t, List<Vector2> uv, List<Color> c, List<int>[] tris, bool hasN, bool hasT, bool hasUV, bool hasC)
    {
        if (!Recording || mesh == null || !Instance.meshes.TryGetValue(mesh, out ReplayMeshHistory history)) return;
        // A cap on what hull damage may take: past it, dents made later aren't kept (the replay shows the hull
        // as it last was).
        if (Instance.meshBudgetSpent) return;
        if ((++Instance.meshWrites & 63) == 0)
        {
            long total = 0;
            foreach (ReplayMeshHistory h in Instance.meshes.Values) total += h.Bytes;
            Instance.meshBudgetSpent = total > MeshBudgetBytes;
        }
        ReplayMeshState state = new ReplayMeshState
        {
            v = v.ToArray(), n = hasN ? ReplayMeshState.Pack(n) : null, t = hasT ? t.ToArray() : null, uv = hasUV ? uv.ToArray() : null, c = hasC ? c.ToArray() : null,
            format = mesh.indexFormat, tris = new int[tris.Length][],
        };
        for (int i = 0; i < tris.Length; i++) state.tris[i] = tris[i].ToArray();
        history.Add(Time.time, state);
    }

    // A runtime mesh its owner is done with: kept for the replay if one is being recorded (true), else the
    // owner destroys it as before.
    public static bool Keep(Mesh mesh)
    {
        if (Instance == null || mesh == null) return false;
        Instance.kept.Add(mesh);
        return true;
    }

    // ---------------- Report ----------------

    public string Summary()
    {
        long meshBytes = 0, firstBytes = 0, bv = 0, bn = 0, bt = 0, buv = 0, bc = 0, btri = 0, versions = 0;
        foreach (ReplayMeshHistory h in meshes.Values)
        {
            meshBytes += h.Bytes; firstBytes += h.FirstBytes;
            foreach (ReplayMeshDiff d in h.diffs)
            {
                versions++;
                bv += d.v.Bytes(12); bn += d.n.Bytes(4); bt += d.t.Bytes(16); buv += d.uv.Bytes(8); bc += d.c.Bytes(16);
                foreach (ReplayTriangleDiff tri in d.tris) btri += tri.Bytes;
            }
        }
        string meshDetail = $" versions {versions}: v {bv >> 20} n {bn >> 20} t {bt >> 20} uv {buv >> 20} c {bc >> 20} tris {btri >> 20} MB; rejected:";
        foreach (KeyValuePair<string, int> kv in rejected) meshDetail += $" {kv.Key} {kv.Value}";
        int listed = 0;
        foreach (ReplayAnchor a in anchors)
        {
            if (a.kind != ReplayKind.Ship || listed++ >= 6) continue;
            int mine = 0;
            foreach (ReplayItem i in items) if (i.anchors.Count > 0 && i.anchors.values[0] == a) mine++;
            meshDetail += $"\n  ship {a.name}: items {mine}, renderers now {(a.live != null ? a.live.GetComponentsInChildren<Renderer>(true).Length : -1)}, mesh renderers {(a.live != null ? a.live.GetComponentsInChildren<MeshRenderer>(true).Length : -1)}, lodgroups {(a.live != null ? a.live.GetComponentsInChildren<LODGroup>(true).Length : -1)}";
        }
        Dictionary<ReplayKind, int> byKind = new Dictionary<ReplayKind, int>();
        foreach (ReplayItem i in items)
        {
            ReplayKind k = i.anchors.Count > 0 ? i.anchors.values[0].kind : ReplayKind.Other;
            byKind.TryGetValue(k, out int c);
            byKind[k] = c + 1;
        }
        System.Text.StringBuilder kinds = new System.Text.StringBuilder();
        foreach (KeyValuePair<ReplayKind, int> kv in byKind) kinds.Append($" {kv.Key}:{kv.Value}");
        long keys = 0;
        foreach (ReplayAnchor a in anchors) keys += a.pose.Count;
        foreach (ReplayItem i in items) keys += i.pose.Count;
        int particles = 0, lights = 0, lines = 0;
        foreach (ReplayItem i in items) { if (i is ReplayParticleItem) particles++; else if (i is ReplayLightItem) lights++; else if (i is ReplayLineItem || i is ReplayTrailItem) lines++; }
        return $"replay: {EndTime - StartTime:F0}s anchors={anchors.Count} items={items.Count} (particles {particles}, lights {lights}, lines {lines}) poseKeys={keys} " +
            $"(~{keys * 20 / 1048576f:F1} MB) meshes={meshes.Count} (~{meshBytes / 1048576f:F1} MB, first versions {firstBytes / 1048576f:F1} MB) items by kind{kinds}{meshDetail} bolts={bolts.Count} tracers={tracers.Count} sounds={sounds.Count} " +
            $"record avg {(Frames > 0 ? TotalMs / Frames : 0):F2} ms, worst {WorstMs:F1} ms (discover {phases[0] / Mathf.Max(1, Frames):F2} anchors {phases[1] / Mathf.Max(1, Frames):F2} rescans {phases[2] / Mathf.Max(1, Frames):F2} poll {phases[3] / Mathf.Max(1, Frames):F2} poses {phases[4] / Mathf.Max(1, Frames):F2}) mesh budget spent {meshBudgetSpent}";
    }
}

// Shots drawn as streaks (laser bolts, CIWS rounds): each flies straight from where it was first seen.
public class ReplayShots
{
    public readonly List<float> starts = new List<float>(), ends = new List<float>(), widths = new List<float>();
    public readonly List<Vector3> heads = new List<Vector3>(), velocities = new List<Vector3>();
    public readonly List<Color> colors = new List<Color>();
    public int Count => starts.Count;

    public int Add(float t, Vector3 head, Vector3 velocity, Color color, float width)
    {
        starts.Add(t); ends.Add(float.PositiveInfinity); heads.Add(head); velocities.Add(velocity); colors.Add(color); widths.Add(width);
        return starts.Count;
    }

    public void End(int id, float t)
    {
        int i = id - 1;
        if (i >= 0 && i < ends.Count && ends[i] == float.PositiveInfinity) ends[i] = t;
    }

    public void Close(float t)
    {
        for (int i = 0; i < ends.Count; i++) if (ends[i] == float.PositiveInfinity) ends[i] = t;
    }
}

public struct ReplaySound
{
    public float t;
    public Sfx.Cue cue;
    public AudioClip[] blast;
    public DestructionProfile profile;
    public Vector3 position;
    public float volume, pitch;
}

public struct ReplayPuff
{
    public float t;
    public string key;
    public ParticleSystem template;
    public GameObject prefab; // (saved replays: the template's prefab)
    public Vector3 position;
    public float scale;
    public int count;
}

public struct ReplayStreak
{
    public float t;
    public object look;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;
}
