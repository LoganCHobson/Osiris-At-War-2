using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Plays a recorded battle back. The live battle is frozen and hidden; in its place a stage of look-only ghosts
// is driven from the recording at the replay's own time, which can run at any speed, pause, and jump back or
// forward. Nothing is simulated, so a replay costs about what drawing the battle did, and always shows exactly
// what happened.
public class ReplayPlayer : MonoBehaviour
{
    public static ReplayPlayer Instance { get; private set; }
    public static bool Active => Instance != null;

    public ReplayRecorder Data { get; private set; }
    public Transform Stage { get; private set; }
    public float Start0 => Data.StartTime;
    public float End => Data.EndTime;
    public float Now { get; private set; }
    public float Speed { get; private set; } = 1f;
    public bool Paused { get; private set; }
    public ReplayCamera Camera { get; private set; }

    public static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };

    private bool jumped = true;
    private float previous;
    private readonly Dictionary<Mesh, GhostMesh> ghostMeshes = new Dictionary<Mesh, GhostMesh>();
    private ReplayShotRenderer shots;
    private int soundCursor, puffCursor, streakCursor;
    private readonly Dictionary<string, ParticleSystem[]> puffEmitters = new Dictionary<string, ParticleSystem[]>();
    private bool exiting;

    private class GhostMesh
    {
        public Mesh mesh;
        public ReplayMeshHistory history;
        public ReplayMeshState state;
        public int version;
        public float writtenAt = float.NegativeInfinity;
    }

    // Starts the replay of the battle just recorded; finishes when the player leaves it.
    public static IEnumerator Run()
    {
        ReplayRecorder recorder = ReplayRecorder.Instance;
        if (recorder == null) yield break;
        recorder.Finish();
        ReplayPlayer player = new GameObject("ReplayPlayer").AddComponent<ReplayPlayer>();
        player.Begin(recorder);
        while (Instance != null) yield return null;
    }

    private void Begin(ReplayRecorder recorder)
    {
        Instance = this;
        Data = recorder;
        Now = recorder.StartTime;
        previous = Now;

        if (CinematicCamera.Active) FindFirstObjectByType<CinematicCamera>()?.Exit();
        GameSpeed.SetSuspended(true); // The battle stays frozen behind the replay.
        HideBattle();

        Stage = new GameObject("ReplayStage").transform;
        shots = new GameObject("ReplayShots").AddComponent<ReplayShotRenderer>();
        shots.transform.SetParent(Stage, false);
        shots.Build(this);

        Camera = gameObject.AddComponent<ReplayCamera>();
        Camera.Begin(this);
        gameObject.AddComponent<ReplayHud>().Begin(this);
        Debug.Log(recorder.Summary());
    }

    // Everything that appeared during the battle is put away; the stage it was fought on stays.
    private void HideBattle()
    {
        // (Only what draws: the sound players stay, the replay uses them.)
        List<GameObject> roots = new List<GameObject>();
        gameObject.scene.GetRootGameObjects(roots);
        foreach (GameObject root in roots)
        {
            if (root == gameObject || root == Data.gameObject || Data.Environment.Contains(root.GetInstanceID())) continue;
            if (root.GetComponentInChildren<Renderer>(true) != null || root.GetComponentInChildren<Light>(true) != null) root.SetActive(false);
        }
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if (canvas.isRootCanvas) canvas.enabled = false;
        // The battle grid and anything else the cinematic camera hides.
        foreach (CinematicCamera cinematic in FindObjectsByType<CinematicCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cinematic.hideWhileActive == null) continue;
            foreach (Renderer r in cinematic.hideWhileActive) if (r != null) r.enabled = false;
        }
        // The battle's own camera and controls stay off (switching the cinematic camera off turns the ones it had
        // paused back on, so it goes first, and they're held off every frame after).
        foreach (CinematicCamera c in FindObjectsByType<CinematicCamera>(FindObjectsSortMode.None)) c.enabled = false;
        foreach (MonoBehaviour b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (b is RTSCameraController || b is PlayerSpaceManager || b is UnitSelectionBox || b is PauseMenu) { b.enabled = false; heldOff.Add(b); }
        }
    }

    private readonly List<MonoBehaviour> heldOff = new List<MonoBehaviour>();

    private void HoldOff()
    {
        foreach (MonoBehaviour b in heldOff) if (b != null && b.enabled) b.enabled = false;
    }

    public void Exit()
    {
        if (exiting) return;
        exiting = true;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Stage != null) Destroy(Stage.gameObject);
        foreach (GhostMesh g in ghostMeshes.Values) if (g.mesh != null) Destroy(g.mesh);
    }

    // ---------------- Controls ----------------

    public void Seek(float t)
    {
        Now = Mathf.Clamp(t, Start0, End);
        jumped = true;
    }

    public void Skip(float seconds) => Seek(Now + seconds);
    public void SetPaused(bool paused) { Paused = paused; if (!paused && Now >= End - 0.01f) Seek(Start0); }
    public void SetSpeed(float speed) => Speed = Mathf.Clamp(speed, 0.05f, 8f);

    private void Update()
    {
        HoldOff();
        if (!Paused)
        {
            Now += Time.unscaledDeltaTime * Speed;
            if (Now >= End) { Now = End; Paused = true; }
        }
        Show(Now);
    }

    // ---------------- Playback ----------------

    private void Show(float t)
    {
        foreach (ReplayAnchor a in Data.anchors) { a.shown = 0; a.placed = false; }

        List<ReplayItem> items = Data.items;
        for (int i = 0; i < items.Count; i++)
        {
            ReplayItem item = items[i];
            bool on = t >= item.born && t < item.gone && item.visible.Evaluate(t, ref item.visibleCursor);
            ReplayAnchor a = on ? item.anchors.Evaluate(t, ref item.anchorCursor) : null;
            if (a == null)
            {
                if (item.ghostOn) { item.ghost.SetActive(false); item.ghostOn = false; item.Hidden(this); }
                continue;
            }
            Place(a, t);
            if (item.ghost == null)
            {
                item.Build(this);
                item.ghost.SetActive(false);
                item.ghostT.SetParent(Stage, false);
            }
            if (item.ghostParent != a) { item.ghostT.SetParent(a.ghost, false); item.ghostParent = a; item.posed = false; }
            if (!item.posed || item.pose.Count > 1)
            {
                ReplayPose p = item.pose.Evaluate(t, ref item.poseCursor);
                item.ghostT.localPosition = p.p;
                item.ghostT.localRotation = p.r;
                item.ghostT.localScale = item.Scale(p.s);
                item.posed = true;
            }
            bool appearing = !item.ghostOn;
            if (appearing) { item.ghost.SetActive(true); item.ghostOn = true; item.Shown(this, t); }
            item.Apply(this, t, jumped || appearing);
            a.shown++;
        }

        shots.Show(t);
        Events(t);
        previous = t;
        jumped = false;
    }

    private void Place(ReplayAnchor a, float t)
    {
        if (a.placed) return;
        if (a.ghost == null)
        {
            a.ghost = new GameObject(a.name).transform;
            a.ghost.SetParent(Stage, false);
        }
        a.now = a.pose.Evaluate(t, ref a.cursor);
        a.ghost.SetPositionAndRotation(a.now.p, a.now.r);
        a.ghost.localScale = a.now.s;
        a.placed = true;
    }

    // A hull mesh as it was at time t (built up from its recorded changes).
    public Mesh MeshAt(Mesh mesh, float t, out int version)
    {
        version = -1;
        if (!Data.meshes.TryGetValue(mesh, out ReplayMeshHistory history)) return mesh;
        if (!ghostMeshes.TryGetValue(mesh, out GhostMesh g))
        {
            g = new GhostMesh { history = history, state = history.First, mesh = new Mesh { name = mesh.name + " (replay)" } };
            g.mesh.MarkDynamic();
            g.state.Write(g.mesh);
            ghostMeshes[mesh] = g;
        }
        int target = UpperBound(history.times, t);
        // Played fast, a hull's damage is rebuilt a few times a second, not every frame.
        if (target != g.version && !jumped && Speed > 1.01f && Time.unscaledTime - g.writtenAt < 0.2f) target = g.version;
        if (target != g.version)
        {
            if (target < g.version) { g.state = history.First; g.version = 0; } // back: rebuilt from the start
            while (g.version < target) history.diffs[g.version++].Forward(g.state = g.state.Copy());
            g.state.Write(g.mesh);
            g.writtenAt = Time.unscaledTime;
        }
        version = g.version;
        return g.mesh;
    }


    private static int UpperBound(List<float> times, float t)
    {
        int lo = 0, hi = times.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (times[mid] <= t) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    // Sounds, puffs of smoke and FTL streaks happen as the replay passes them (not when jumping about).
    private void Events(float t)
    {
        if (jumped)
        {
            soundCursor = FirstAfter(Data.sounds, s => s.t, t);
            puffCursor = FirstAfter(Data.puffs, p => p.t, t);
            streakCursor = FirstAfter(Data.streaks, s => s.t, t);
            return;
        }
        if (t <= previous) return;
        bool audible = Speed >= 0.5f && Speed <= 2f;
        while (soundCursor < Data.sounds.Count && Data.sounds[soundCursor].t <= t)
        {
            ReplaySound s = Data.sounds[soundCursor++];
            if (!audible || s.t <= previous) continue;
            if (s.blast != null) DestructionFx.Blast(s.blast, s.position, s.profile, s.pitch * Speed);
            else Sfx.Play(s.cue, s.position, s.volume, s.pitch * Speed);
        }
        while (puffCursor < Data.puffs.Count && Data.puffs[puffCursor].t <= t)
        {
            ReplayPuff p = Data.puffs[puffCursor++];
            if (p.t > previous) Puff(p);
        }
        while (streakCursor < Data.streaks.Count && Data.streaks[streakCursor].t <= t)
        {
            ReplayStreak s = Data.streaks[streakCursor++];
            if (s.t > previous) FTLJump.DrawRecordedLook(s.look, s.position, s.rotation, s.scale);
        }
    }

    private static int FirstAfter<T>(List<T> list, System.Func<T, float> time, float t)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (time(list[mid]) <= t) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private void Puff(ReplayPuff p)
    {
        if (p.template == null && p.prefab != null) p.template = p.prefab.GetComponent<ParticleSystem>();
        if (p.template == null) return;
        if (!puffEmitters.TryGetValue(p.key, out ParticleSystem[] systems) || systems == null || systems.Length == 0 || systems[0] == null)
        {
            GameObject copy = Instantiate(p.template.gameObject, Stage, false);
            ReplayRecorder.StripToParticles(copy);
            copy.SetActive(true);
            systems = copy.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.MainModule main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.loop = true;
                main.useUnscaledTime = true;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                ParticleSystem.EmissionModule emission = system.emission;
                emission.enabled = false;
                system.Play(false);
            }
            puffEmitters[p.key] = systems;
        }
        systems[0].transform.root.localScale = p.template.transform.localScale * p.scale;
        ParticleSystem.EmitParams at = new ParticleSystem.EmitParams { position = p.position, applyShapeToPosition = true };
        foreach (ParticleSystem system in systems) if (system != null) system.Emit(at, p.count);
    }
}

// The battle's laser bolts and CIWS rounds as they flew, drawn as the live game draws them.
public class ReplayShotRenderer : MonoBehaviour
{
    private ReplayPlayer player;
    private Mesh boltMesh, tracerMesh;
    private List<int>[] boltBuckets, tracerBuckets;
    private const float BucketSeconds = 1f;
    private Vector3[] v = new Vector3[0];
    private Vector2[] uv = new Vector2[0];
    private Color[] c = new Color[0];
    private int[] tris = new int[0];

    public void Build(ReplayPlayer owner)
    {
        player = owner;
        boltBuckets = Bucket(player.Data.bolts);
        tracerBuckets = Bucket(player.Data.tracers);
        boltMesh = Make("ReplayBolts", LaserSystem.StreakMaterial(), null);
        MaterialPropertyBlock tracerLook = new MaterialPropertyBlock();
        tracerLook.SetTexture("_MainTex", CIWSTracers.TracerTexture());
        tracerLook.SetColor("_Color", CIWSTracers.TracerColor);
        tracerLook.SetFloat("_Intensity", CIWSTracers.TracerIntensity);
        tracerMesh = Make("ReplayTracers", Resources.Load<Material>("FTL/FTLGlow"), tracerLook);
    }

    private Mesh Make(string name, Material material, MaterialPropertyBlock block)
    {
        GameObject go = new GameObject(name) { layer = 2 };
        go.transform.SetParent(transform, false);
        Mesh mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (block != null) r.SetPropertyBlock(block);
        return mesh;
    }

    private List<int>[] Bucket(ReplayShots shots)
    {
        float start = player.Start0;
        int count = Mathf.Max(1, Mathf.CeilToInt((player.End - start) / BucketSeconds) + 1);
        List<int>[] buckets = new List<int>[count];
        for (int i = 0; i < shots.Count; i++)
        {
            int a = Mathf.Clamp(Mathf.FloorToInt((shots.starts[i] - start) / BucketSeconds), 0, count - 1);
            float end = Mathf.Min(shots.ends[i], player.End);
            int b = Mathf.Clamp(Mathf.FloorToInt((end - start) / BucketSeconds), 0, count - 1);
            for (int k = a; k <= b; k++) (buckets[k] ??= new List<int>()).Add(i);
        }
        return buckets;
    }

    public void Show(float t)
    {
        Camera cam = Camera.main;
        Vector3 eye = cam != null ? cam.transform.position : Vector3.up * 1000f;
        int bucket = Mathf.Clamp(Mathf.FloorToInt((t - player.Start0) / BucketSeconds), 0, boltBuckets.Length - 1);
        Draw(boltMesh, player.Data.bolts, boltBuckets[bucket], t, eye, true);
        Draw(tracerMesh, player.Data.tracers, tracerBuckets[bucket], t, eye, false);
    }

    private void Draw(Mesh mesh, ReplayShots shots, List<int> candidates, float t, Vector3 eye, bool bolts)
    {
        int q = 0;
        int needed = (candidates?.Count ?? 0) * (bolts ? 2 : 1);
        Ensure(needed);
        if (candidates != null)
        {
            foreach (int i in candidates)
            {
                if (t < shots.starts[i] || t >= shots.ends[i]) continue;
                Vector3 velocity = shots.velocities[i];
                Vector3 head = shots.heads[i] + velocity * (t - shots.starts[i]);
                if (bolts) Bolt(ref q, head, velocity, shots.colors[i], shots.widths[i], eye);
                else Tracer(ref q, head, velocity, eye);
            }
        }
        mesh.Clear(false);
        int verts = Mathf.Max(4, q * 4);
        for (int k = q * 4; k < verts; k++) v[k] = Vector3.zero;
        mesh.SetVertices(v, 0, verts);
        mesh.SetUVs(0, uv, 0, verts);
        mesh.SetColors(c, 0, verts);
        mesh.SetTriangles(tris, 0, Mathf.Max(6, q * 6), 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
    }

    private void Ensure(int quads)
    {
        quads = Mathf.Max(1, quads);
        if (v.Length >= quads * 4) return;
        int size = Mathf.NextPowerOfTwo(quads * 4);
        v = new Vector3[size]; uv = new Vector2[size]; c = new Color[size]; tris = new int[size / 4 * 6];
    }

    // As LaserSystem draws a bolt.
    private void Bolt(ref int q, Vector3 head, Vector3 velocity, Color color, float width, Vector3 eye)
    {
        float speed = velocity.magnitude;
        if (speed < 0.01f) return;
        Vector3 dir = velocity / speed;
        float distance = Vector3.Distance(eye, head);
        float sizeScale = Mathf.Clamp(width / 0.05f, 0.45f, 2f);
        bool longStreaks = LaserSystem.LongStreaks;
        float w = Mathf.Max(width, distance * (longStreaks ? 0.0011f : 0.0009f) * sizeScale);
        float length = longStreaks
            ? Mathf.Max(Mathf.Clamp(speed * 0.06f, 3f, 10f) * sizeScale, w * 14f)
            : Mathf.Max(Mathf.Clamp(speed * 0.02f, 1.4f, 3.2f) * sizeScale, w * 8f);
        Vector3 tail = head - dir * length;
        Vector3 side = Vector3.Cross(dir, eye - head);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(dir, Vector3.up);
        side.Normalize();
        Color tint = new Color(color.r, color.g, color.b, 1f);
        if (longStreaks)
        {
            Quad(ref q, head + dir * w * 2f, tail, side * w * 4.5f, tint * 1.6f);
            Quad(ref q, head, tail + dir * length * 0.25f, side * w * 1.4f, Color.Lerp(tint, Color.white, 0.65f) * 3.2f);
        }
        else
        {
            Quad(ref q, head + dir * w * 1.5f, tail - dir * w * 1.5f, side * w * 3.2f, tint * 0.9f);
            Quad(ref q, head, tail, side * w * 1.1f, Color.Lerp(tint, Color.white, 0.45f) * 1.7f);
        }
    }

    // As CIWSTracers draws a round.
    private void Tracer(ref int q, Vector3 position, Vector3 velocity, Vector3 eye)
    {
        float speed = velocity.magnitude;
        Vector3 dir = speed > 0f ? velocity / speed : Vector3.forward;
        Vector3 tail = position - dir * CIWSTracers.TracerLength;
        Vector3 side = Vector3.Cross(dir, position - eye);
        side = side.sqrMagnitude > 0.0001f ? side.normalized * CIWSTracers.TracerWidth : Vector3.up * CIWSTracers.TracerWidth;
        Quad(ref q, position, tail, side, Color.white);
    }

    private void Quad(ref int q, Vector3 head, Vector3 tail, Vector3 halfWidth, Color color)
    {
        int i = q * 4, k = q * 6;
        v[i] = tail - halfWidth; v[i + 1] = tail + halfWidth; v[i + 2] = head - halfWidth; v[i + 3] = head + halfWidth;
        uv[i] = new Vector2(0f, 0f); uv[i + 1] = new Vector2(0f, 1f); uv[i + 2] = new Vector2(1f, 0f); uv[i + 3] = new Vector2(1f, 1f);
        c[i] = c[i + 1] = c[i + 2] = c[i + 3] = color;
        tris[k] = i; tris[k + 1] = i + 2; tris[k + 2] = i + 1;
        tris[k + 3] = i + 1; tris[k + 4] = i + 2; tris[k + 5] = i + 3;
        q++;
    }
}
