using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// What the gallery shows about a replay without opening it (kept uncompressed at the front of the file).
[Serializable]
public class ReplayHeader
{
    public int format = ReplayFile.Format;
    public string gameVersion, campaignId, campaignStarted, recordedAt, system, attacker, defender, winner;
    public bool skirmish, playerWon;
    public float duration;
    public int terrainSeed;
    public string appearance; // the planet's look (JSON), for the battle sky
    public int skirmishAsteroids = -1, skirmishNebulae = -1, skirmishIonStorm = -1;

    [NonSerialized] public string path;
    [NonSerialized] public long bytes;
    [NonSerialized] public DateTime when;
    public bool Kept => path != null && File.Exists(path + ".keep");
}

// Saved battle replays: <persistent data>/Replays/<campaign>/<game version>/<date> <system>.osreplay.
//
// A file is a small header, then (compressed) a table of every Unity object the recording refers to and the
// recording itself. Objects that are part of the game (models, materials, textures, sounds, effect prefabs) are
// named by their asset id (ReplayAssets); ones made during the battle (wreck slices, scorch decals, runtime
// materials and textures) are written out in full; a hull's damage is kept as its model plus the recorded changes.
// Saving reads the Unity objects on the main thread, a little at a time, and writes the file on a worker.
public static class ReplayFile
{
    public const int Format = 1;
    private const string Magic = "OSRP";
    public const string Extension = ".osreplay";
    public static string Folder => Path.Combine(Application.persistentDataPath, "Replays");

    // ---------------- Listing ----------------

    public static List<ReplayHeader> List()
    {
        List<ReplayHeader> all = new List<ReplayHeader>();
        if (!Directory.Exists(Folder)) return all;
        foreach (string path in Directory.GetFiles(Folder, "*" + Extension, SearchOption.AllDirectories))
        {
            ReplayHeader h = ReadHeader(path);
            if (h != null) all.Add(h);
        }
        all.Sort((a, b) => b.when.CompareTo(a.when));
        return all;
    }

    public static ReplayHeader ReadHeader(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);
            using BinaryReader r = new BinaryReader(file);
            if (new string(r.ReadChars(4)) != Magic) return null;
            r.ReadInt32();
            ReplayHeader h = JsonUtility.FromJson<ReplayHeader>(r.ReadString());
            h.path = path;
            h.bytes = file.Length;
            DateTime.TryParse(h.recordedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out h.when);
            return h;
        }
        catch (Exception) { return null; }
    }

    public static void Delete(ReplayHeader h)
    {
        try { File.Delete(h.path); if (File.Exists(h.path + ".keep")) File.Delete(h.path + ".keep"); }
        catch (Exception e) { Debug.LogException(e); }
    }

    public static void SetKept(ReplayHeader h, bool keep)
    {
        try { if (keep) File.WriteAllText(h.path + ".keep", ""); else if (File.Exists(h.path + ".keep")) File.Delete(h.path + ".keep"); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // Oldest first, never one marked to keep: by total size or age, as Settings > Battle Replays says.
    public static void Prune()
    {
        int mode = GameSettings.ReplayPruneMode;
        if (mode == 0) return;
        List<ReplayHeader> all = List(); // newest first
        if (mode == 1)
        {
            long limit = (long)(GameSettings.ReplayPruneGigabytes * 1024L * 1024L * 1024L);
            long total = 0;
            foreach (ReplayHeader h in all) total += h.bytes;
            for (int i = all.Count - 1; i >= 0 && total > limit; i--)
            {
                if (all[i].Kept) continue;
                total -= all[i].bytes;
                Delete(all[i]);
            }
        }
        else if (mode == 2)
        {
            DateTime cutoff = DateTime.Now.AddDays(-GameSettings.ReplayPruneDays);
            foreach (ReplayHeader h in all) if (h.when < cutoff && !h.Kept) Delete(h);
        }
    }

    // ---------------- Saving ----------------

    private class Entry
    {
        public int kind;       // 0 missing, 1 asset, 2 mesh data, 3 hull (history), 4 material, 5 texture
        public int type;       // asset: 1 mesh 2 material 3 texture 4 clip 5 profile 6 prefab
        public string key;
        public ReplayMeshState mesh;
        public string name;
        // material
        public string shader;
        public int queue;
        public string[] keywords;
        public List<(string name, int type, Vector4 value, int texture)> props;
        // texture
        public byte[] png;
        public int wrap, filter;
    }

    // The battle just fought, saved for the gallery. The Unity-side reading is spread over frames; the file is
    // written in the background (this returns once only the writing is left).
    public static IEnumerator Save(ReplayRecorder recording)
    {
        if (recording == null || recording.anchors.Count == 0) yield break;
        recording.Finish();
        ReplayHeader header = HeaderFor(recording);
        ReplayAssets.Prepare(); // the asset list, loaded here on the main thread

        List<Object> objects = new List<Object>();
        Dictionary<Object, int> index = new Dictionary<Object, int>(ReferenceComparer.Instance);
        int Register(Object o)
        {
            lock (index)
            {
                if (index.TryGetValue(o, out int i)) return i;
                index[o] = objects.Count;
                objects.Add(o);
                return objects.Count - 1;
            }
        }

        // A hull whose model isn't in the asset list is saved with its first version, read here (main thread).
        foreach (ReplayMeshHistory history in recording.meshes.Values) if (ReplayAssets.KeyOf(history.Source) == null) _ = history.First;

        // 1. Every object the recording names (worker: a dry run of the writing).
        List<(string, long)> marks = new List<(string, long)>();
        Task collect = Task.Run(() => WriteBody(new ReplayIO { w = new BinaryWriter(new CountingStream()), refOf = Register }, recording, marks));
        while (!collect.IsCompleted) yield return null;
        if (collect.IsFaulted) { Debug.LogException(collect.Exception); yield break; }

        // 2. Read what's needed from each (main thread, a few milliseconds a frame).
        List<Entry> entries = new List<Entry>();
        System.Diagnostics.Stopwatch slice = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < objects.Count; i++)
        {
            entries.Add(Describe(objects[i], recording, Register));
            if (slice.Elapsed.TotalMilliseconds > 6) { yield return null; slice.Restart(); }
        }

        // What takes the room (uncompressed), for the log.
        long[] kindBytes = new long[6]; int[] kindCount = new int[6];
        foreach (Entry e in entries)
        {
            kindCount[e.kind]++;
            kindBytes[e.kind] += e.mesh != null ? e.mesh.Bytes : e.png != null ? e.png.Length : 0;
        }
        System.Text.StringBuilder sizes = new System.Text.StringBuilder("Replay sizes (uncompressed):");
        string[] kinds = { "missing", "assets", "runtime meshes", "hulls", "materials", "textures" };
        for (int k = 0; k < 6; k++) sizes.Append($" {kinds[k]} {kindCount[k]} ({kindBytes[k] >> 20} MB)");
        long last = 0;
        foreach ((string what, long at) in marks) { sizes.Append($" | {what} {(at - last) >> 20} MB"); last = at; }
        Debug.Log(sizes.ToString());
        LastSizes = sizes.ToString();

        // 3. Write it all (worker), then make room by the pruning setting.
        string folder = Path.Combine(Folder, Safe(header.skirmish ? "Skirmish" : header.campaignId), Safe(header.gameVersion));
        string path = Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} {Safe(header.system)}{Extension}");
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(folder);
                string temp = path + ".tmp";
                using (FileStream file = File.Create(temp))
                {
                    BinaryWriter head = new BinaryWriter(file);
                    head.Write(Magic.ToCharArray());
                    head.Write(Format);
                    head.Write(JsonUtility.ToJson(header));
                    head.Flush();
                    using GZipStream zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal, true);
                    using BinaryWriter w = new BinaryWriter(zip);
                    ReplayIO io = new ReplayIO { w = w, refOf = o => index.TryGetValue(o, out int i) ? i : -1 };
                    WriteTable(io, entries);
                    WriteBody(io, recording);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                Prune();
                Debug.Log($"Replay saved: {path} ({new FileInfo(path).Length / 1048576f:F1} MB)");
            }
            catch (Exception e) { Debug.LogException(e); }
        });
    }

    private static string Safe(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Unknown";
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }

    private static ReplayHeader HeaderFor(ReplayRecorder recording)
    {
        BattleContext c = BattleContext.Instance;
        bool attackerWon = c != null && c.defenderLost;
        return new ReplayHeader
        {
            gameVersion = Application.version,
            campaignId = Campaign.Id,
            campaignStarted = Campaign.Started,
            recordedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            system = c != null && !string.IsNullOrEmpty(c.destinationPlanetName) ? c.destinationPlanetName : "Deep space",
            attacker = Name(c != null ? c.attackerFaction : null, "Attackers"),
            defender = Name(c != null ? c.defenderFaction : null, "Defenders"),
            winner = c == null ? "" : c.defenderLost ? Name(c.attackerFaction, "Attackers") : c.attackerLost ? Name(c.defenderFaction, "Defenders") : "",
            playerWon = GameManager.Instance != null && attackerWon == GameManager.Instance.PlayerIsAttacker,
            skirmish = c != null && c.isSkirmish,
            duration = recording.EndTime - recording.StartTime,
            terrainSeed = GameManager.TerrainSeed,
            appearance = c != null && c.destinationAppearance != null ? JsonUtility.ToJson(c.destinationAppearance) : null,
            skirmishAsteroids = c != null ? c.skirmishAsteroids : -1,
            skirmishNebulae = c != null ? c.skirmishNebulae : -1,
            skirmishIonStorm = c != null ? c.skirmishIonStorm : -1,
        };
    }

    private static string Name(Faction f, string fallback) => f == null ? fallback : !string.IsNullOrEmpty(f.factionName) ? f.factionName : f.name;

    private static Entry Describe(Object o, ReplayRecorder recording, Func<Object, int> register)
    {
        if (o == null) return new Entry();
        string key = ReplayAssets.KeyOf(o);
        if (key != null)
        {
            int type = o is Mesh ? 1 : o is Material ? 2 : o is Texture ? 3 : o is AudioClip ? 4 : o is DestructionProfile ? 5 : o is GameObject ? 6 : 0;
            return new Entry { kind = 1, type = type, key = key };
        }
        switch (o)
        {
            case Mesh mesh:
                if (recording.meshes.TryGetValue(mesh, out ReplayMeshHistory history)) return new Entry { kind = 3, name = mesh.name };
                if (!mesh.isReadable) return new Entry();
                return new Entry { kind = 2, name = mesh.name, mesh = ReplayMeshState.Read(mesh) };
            case Material material:
                Entry m = new Entry
                {
                    kind = 4, name = material.name, shader = material.shader.name, queue = material.renderQueue, keywords = material.shaderKeywords,
                    props = new List<(string, int, Vector4, int)>(),
                };
                Shader shader = material.shader;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    string name = shader.GetPropertyName(i);
                    switch (shader.GetPropertyType(i))
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Color: Color col = material.GetColor(name); m.props.Add((name, 0, new Vector4(col.r, col.g, col.b, col.a), -1)); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Vector: m.props.Add((name, 1, material.GetVector(name), -1)); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range: m.props.Add((name, 2, new Vector4(material.GetFloat(name), 0, 0, 0), -1)); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Int: m.props.Add((name, 3, new Vector4(material.GetInteger(name), 0, 0, 0), -1)); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Texture:
                            Texture tex = material.GetTexture(name);
                            m.props.Add((name, 4, Vector4.zero, tex != null ? register(tex) : -1));
                            break;
                    }
                }
                return m;
            case Texture2D texture:
                byte[] png = Png(texture);
                return png == null ? new Entry() : new Entry { kind = 5, name = texture.name, png = png, wrap = (int)texture.wrapMode, filter = (int)texture.filterMode };
        }
        return new Entry();
    }

    // A texture made during the battle, as a PNG (copied through the GPU if its pixels aren't readable).
    private static byte[] Png(Texture2D texture)
    {
        try
        {
            if (texture.isReadable) return texture.EncodeToPNG();
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(texture, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            byte[] png = copy.EncodeToPNG();
            Object.Destroy(copy);
            return png;
        }
        catch (Exception) { return null; }
    }

    private static void WriteTable(ReplayIO io, List<Entry> entries)
    {
        BinaryWriter w = io.w;
        w.Write(entries.Count);
        foreach (Entry e in entries)
        {
            w.Write(e.kind);
            switch (e.kind)
            {
                case 1: w.Write(e.type); w.Write(e.key); break;
                case 2: io.Str(e.name); io.CompactMesh(e.mesh); break;
                case 3: io.Str(e.name); break;
                case 4:
                    io.Str(e.name); io.Str(e.shader); w.Write(e.queue);
                    w.Write(e.keywords?.Length ?? 0);
                    if (e.keywords != null) foreach (string k in e.keywords) w.Write(k);
                    w.Write(e.props.Count);
                    foreach (var p in e.props) { w.Write(p.name); w.Write(p.type); io.V4(p.value); w.Write(p.texture); }
                    break;
                case 5: io.Str(e.name); w.Write(e.wrap); w.Write(e.filter); w.Write(e.png.Length); w.Write(e.png); break;
            }
        }
    }

    // The recording itself, in a fixed order (also run dry to find every object it names).
    public static string LastSizes;

    private class CountingStream : Stream
    {
        private long length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => length;
        public override long Position { get => length; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => length;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => length += count;
    }

    private static void WriteBody(ReplayIO io, ReplayRecorder rec, List<(string, long)> marks = null)
    {
        BinaryWriter w = io.w;
        void Mark(string what) { if (marks == null) return; w.Flush(); marks.Add((what, w.BaseStream.Position)); }
        w.Write(rec.StartTime); w.Write(rec.EndTime);

        w.Write(rec.anchors.Count);
        foreach (ReplayAnchor a in rec.anchors)
        {
            w.Write(a.id); w.Write((int)a.kind); w.Write(a.size); w.Write(a.attackerSide); io.Str(a.name); w.Write(a.born); w.Write(a.gone);
            io.Pose(a.pose);
        }

        Mark("anchors");
        w.Write(rec.items.Count);
        foreach (ReplayItem item in rec.items)
        {
            w.Write(item.TypeTag);
            w.Write(item.id); w.Write(item.born); w.Write(item.gone); w.Write((int)item.subject); w.Write(item.subjectSize); w.Write(item.attackerSide); io.Str(item.label);
            io.Steps(item.anchors, a => w.Write(a != null ? a.id : -1), null);
            io.Pose(item.pose);
            io.Steps(item.visible, b => w.Write(b), null);
            item.Save(io);
        }

        Mark("items");
        w.Write(rec.meshes.Count);
        foreach (KeyValuePair<Mesh, ReplayMeshHistory> pair in rec.meshes)
        {
            io.Obj(pair.Key);
            Mesh source = pair.Value.Source;
            string sourceKey = source != null ? ReplayAssets.KeyOf(source) : null;
            io.Str(sourceKey);
            if (sourceKey == null) io.MeshState(pair.Value.First);
            w.Write(pair.Value.diffs.Count);
            for (int i = 0; i < pair.Value.diffs.Count; i++) { w.Write(pair.Value.times[i]); io.MeshDiff(pair.Value.diffs[i]); }
        }

        Mark("hull history");
        Shots(io, rec.bolts);
        Shots(io, rec.tracers);
        Mark("shots");

        w.Write(rec.sounds.Count);
        foreach (ReplaySound s in rec.sounds)
        {
            w.Write(s.t); w.Write((int)s.cue); io.Objs(s.blast); io.Obj(s.profile); io.V3(s.position); w.Write(s.volume); w.Write(s.pitch);
        }

        w.Write(rec.puffs.Count);
        foreach (ReplayPuff p in rec.puffs)
        {
            w.Write(p.t); io.Str(p.key); io.Obj(p.template != null ? p.template.gameObject : null); io.V3(p.position); w.Write(p.scale); w.Write(p.count);
        }

        w.Write(rec.streaks.Count);
        foreach (ReplayStreak s in rec.streaks)
        {
            w.Write(s.t);
            FTLJump.ReadLook(s.look, out List<(Mesh mesh, Material[] materials, Matrix4x4 local, int layer)> parts);
            w.Write(parts.Count);
            foreach (var part in parts)
            {
                io.Obj(part.mesh); io.Objs(part.materials);
                for (int k = 0; k < 16; k++) w.Write(part.local[k]);
                w.Write(part.layer);
            }
            io.V3(s.position); io.Q(s.rotation); io.V3(s.scale);
        }
        Mark("sounds, puffs, streaks");
    }

    private static void Shots(ReplayIO io, ReplayShots shots)
    {
        BinaryWriter w = io.w;
        w.Write(shots.Count);
        for (int i = 0; i < shots.Count; i++)
        {
            w.Write(shots.starts[i]); w.Write(shots.ends[i]); io.V3(shots.heads[i]); io.V3(shots.velocities[i]); io.Col(shots.colors[i]); w.Write(shots.widths[i]);
        }
    }

    // ---------------- Effects ----------------

    public static string PathFrom(Transform root, Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private static string Bare(string name)
    {
        if (name == null) return null;
        int cut = name.IndexOf("(Clone)", StringComparison.Ordinal);
        return (cut >= 0 ? name.Substring(0, cut) : name).Trim();
    }

    // Which prefab (and where in it) an effect was made from: one of its own name, or the place in its
    // top-level object's prefab.
    public static void EffectSource(ReplayParticleItem item, out string key, out string at)
    {
        key = null; at = null;
        GameObject own = ReplayAssets.PrefabNamed(Bare(item.sourceName));
        if (own != null) { key = ReplayAssets.KeyOf(own); at = ""; return; }
        GameObject root = ReplayAssets.PrefabNamed(Bare(item.rootName));
        if (root == null || string.IsNullOrEmpty(item.path)) return;
        key = ReplayAssets.KeyOf(root);
        at = item.path;
    }

    private static Transform templateHolder;
    private static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();

    public static GameObject EffectTemplate(string key, string at)
    {
        if (key == null) return null;
        string id = key + "|" + at;
        if (templates.TryGetValue(id, out GameObject cached) && cached != null) return cached;
        GameObject prefab = ReplayAssets.Find<GameObject>(key);
        if (prefab == null) return null;
        Transform source = string.IsNullOrEmpty(at) ? prefab.transform : prefab.transform.Find(at);
        if (source == null || source.GetComponent<ParticleSystem>() == null) return null;
        if (templateHolder == null)
        {
            GameObject h = new GameObject("ReplayTemplates");
            h.SetActive(false);
            templateHolder = h.transform;
        }
        GameObject copy = Object.Instantiate(source.gameObject, templateHolder, false);
        ReplayRecorder.StripToParticles(copy);
        templates[id] = copy;
        return copy;
    }

    // ---------------- Loading ----------------

    private class Loaded
    {
        public ReplayHeader header;
        public List<Entry> entries = new List<Entry>();
        public Stream file, zip;
        public BinaryReader r;
    }

    // Reads a saved replay into a playback-only recording (worker, then the main thread for its Unity objects).
    public static IEnumerator Load(string path, Action<ReplayRecorder, string> done)
    {
        ReplayAssets.Prepare();
        System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
        Task<Loaded> open = Task.Run(() => OpenFile(path));
        while (!open.IsCompleted) yield return null;
        if (open.IsFaulted || open.Result == null) { done(null, "That replay couldn't be read."); yield break; }
        Loaded l = open.Result;

        // Unity objects: textures and meshes, then materials (which use the textures).
        Object[] objects = new Object[l.entries.Count];
        for (int pass = 0; pass < 2; pass++)
        {
            System.Diagnostics.Stopwatch slice = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < l.entries.Count; i++)
            {
                Entry e = l.entries[i];
                if ((e.kind == 4) != (pass == 1)) continue;
                objects[i] = Create(e, objects);
                if (slice.Elapsed.TotalMilliseconds > 8) { yield return null; slice.Restart(); }
            }
        }

        double opened = timer.Elapsed.TotalSeconds;
        ReplayRecorder recording = ReplayRecorder.ForPlayback();
        Task body = Task.Run(() => ReadBody(new ReplayIO { r = l.r, objects = objects, anchors = new Dictionary<int, ReplayAnchor>() }, recording));
        while (!body.IsCompleted) yield return null;
        l.r.Dispose(); l.zip.Dispose(); l.file.Dispose();
        if (body.IsFaulted) { Debug.LogException(body.Exception); Object.Destroy(recording.gameObject); done(null, "That replay is damaged."); yield break; }

        double bodyRead = timer.Elapsed.TotalSeconds;
        // Effects come from their prefabs (main thread).
        foreach (ReplayItem item in recording.items)
        {
            if (!(item is ReplayParticleItem p)) continue;
            p.template = EffectTemplate(p.templateKey, p.templatePath);
            if (p.template == null) p.segments.Clear();
        }
        LastLoadTimings = $"table+objects {opened:F1} s, recording {bodyRead - opened:F1} s, effects {timer.Elapsed.TotalSeconds - bodyRead:F1} s";
        done(recording, null);
    }

    public static string LastLoadTimings;

    private static Loaded OpenFile(string path)
    {
        FileStream file = File.OpenRead(path);
        BinaryReader head = new BinaryReader(file);
        if (new string(head.ReadChars(4)) != Magic) { file.Dispose(); return null; }
        int format = head.ReadInt32();
        if (format > Format) { file.Dispose(); return null; }
        Loaded l = new Loaded { header = JsonUtility.FromJson<ReplayHeader>(head.ReadString()), file = file };
        l.zip = new GZipStream(file, CompressionMode.Decompress, true);
        l.r = new BinaryReader(new BufferedStream(l.zip, 1 << 16));
        ReplayIO io = new ReplayIO { r = l.r };
        int n = l.r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            Entry e = new Entry { kind = l.r.ReadInt32() };
            switch (e.kind)
            {
                case 1: e.type = l.r.ReadInt32(); e.key = l.r.ReadString(); break;
                case 2: e.name = io.Str(); e.mesh = io.CompactMesh(); break;
                case 3: e.name = io.Str(); break;
                case 4:
                    e.name = io.Str(); e.shader = io.Str(); e.queue = l.r.ReadInt32();
                    e.keywords = new string[l.r.ReadInt32()];
                    for (int k = 0; k < e.keywords.Length; k++) e.keywords[k] = l.r.ReadString();
                    int props = l.r.ReadInt32();
                    e.props = new List<(string, int, Vector4, int)>(props);
                    for (int k = 0; k < props; k++) e.props.Add((l.r.ReadString(), l.r.ReadInt32(), io.V4(), l.r.ReadInt32()));
                    break;
                case 5: e.name = io.Str(); e.wrap = l.r.ReadInt32(); e.filter = l.r.ReadInt32(); e.png = l.r.ReadBytes(l.r.ReadInt32()); break;
            }
            l.entries.Add(e);
        }
        return l;
    }

    private static Object Create(Entry e, Object[] objects)
    {
        switch (e.kind)
        {
            case 1:
                switch (e.type)
                {
                    case 1: return ReplayAssets.Find<Mesh>(e.key);
                    case 2: return ReplayAssets.Find<Material>(e.key);
                    case 3: return ReplayAssets.Find<Texture>(e.key);
                    case 4: return ReplayAssets.Find<AudioClip>(e.key);
                    case 5: return ReplayAssets.Find<DestructionProfile>(e.key);
                    case 6: return ReplayAssets.Find<GameObject>(e.key);
                }
                return null;
            case 2:
                Mesh mesh = new Mesh { name = e.name };
                e.mesh.Write(mesh);
                return mesh;
            case 3:
                return new Mesh { name = e.name };
            case 4:
                Shader shader = Shader.Find(e.shader);
                if (shader == null) return null;
                Material m = new Material(shader) { name = e.name, renderQueue = e.queue, shaderKeywords = e.keywords };
                foreach (var p in e.props)
                {
                    switch (p.type)
                    {
                        case 0: m.SetColor(p.name, new Color(p.value.x, p.value.y, p.value.z, p.value.w)); break;
                        case 1: m.SetVector(p.name, p.value); break;
                        case 2: m.SetFloat(p.name, p.value.x); break;
                        case 3: m.SetInteger(p.name, (int)p.value.x); break;
                        case 4: if (p.texture >= 0 && p.texture < objects.Length) m.SetTexture(p.name, objects[p.texture] as Texture); break;
                    }
                }
                return m;
            case 5:
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = e.name, wrapMode = (TextureWrapMode)e.wrap, filterMode = (FilterMode)e.filter };
                tex.LoadImage(e.png);
                return tex;
        }
        return null;
    }

    private static void ReadBody(ReplayIO io, ReplayRecorder rec)
    {
        BinaryReader r = io.r;
        float start = r.ReadSingle(), end = r.ReadSingle();
        rec.SetSpan(start, end);

        int anchors = r.ReadInt32();
        for (int i = 0; i < anchors; i++)
        {
            ReplayAnchor a = new ReplayAnchor
            {
                id = r.ReadInt32(), kind = (ReplayKind)r.ReadInt32(), size = r.ReadSingle(), attackerSide = r.ReadBoolean(), name = io.Str(), born = r.ReadSingle(), gone = r.ReadSingle(),
            };
            io.Pose(a.pose);
            io.anchors[a.id] = a;
            rec.anchors.Add(a);
        }

        int items = r.ReadInt32();
        for (int i = 0; i < items; i++)
        {
            int tag = r.ReadInt32();
            ReplayItem item = tag switch
            {
                1 => new ReplayMeshItem(),
                2 => new ReplayShieldItem(),
                3 => new ReplayLineItem(),
                4 => new ReplayTrailItem(),
                5 => new ReplayParticleItem(),
                6 => new ReplayLightItem(),
                _ => throw new InvalidDataException("unknown item " + tag),
            };
            item.id = r.ReadInt32(); item.born = r.ReadSingle(); item.gone = r.ReadSingle(); item.subject = (ReplayKind)r.ReadInt32(); item.subjectSize = r.ReadSingle();
            item.attackerSide = r.ReadBoolean(); item.label = io.Str();
            io.Steps(item.anchors, null, () => io.anchors.TryGetValue(r.ReadInt32(), out ReplayAnchor a) ? a : null);
            io.Pose(item.pose);
            io.Steps(item.visible, null, r.ReadBoolean);
            item.Load(io);
            rec.items.Add(item);
        }

        int meshes = r.ReadInt32();
        for (int i = 0; i < meshes; i++)
        {
            Mesh live = io.Obj<Mesh>();
            string sourceKey = io.Str();
            ReplayMeshState first = sourceKey == null ? io.MeshState() : null;
            ReplayMeshHistory history = ReplayMeshHistory.Loaded(live, sourceKey, first);
            int n = r.ReadInt32();
            for (int k = 0; k < n; k++) { history.times.Add(r.ReadSingle()); history.diffs.Add(io.MeshDiff()); }
            if (live != null) rec.meshes[live] = history;
        }

        ReadShots(io, rec.bolts);
        ReadShots(io, rec.tracers);

        int sounds = r.ReadInt32();
        for (int i = 0; i < sounds; i++)
        {
            rec.sounds.Add(new ReplaySound
            {
                t = r.ReadSingle(), cue = (Sfx.Cue)r.ReadInt32(), blast = io.Objs<AudioClip>(), profile = io.Obj<DestructionProfile>(), position = io.V3(), volume = r.ReadSingle(), pitch = r.ReadSingle(),
            });
        }

        int puffs = r.ReadInt32();
        for (int i = 0; i < puffs; i++)
        {
            ReplayPuff p = new ReplayPuff { t = r.ReadSingle(), key = io.Str(), prefab = io.Obj<GameObject>(), position = io.V3(), scale = r.ReadSingle(), count = r.ReadInt32() };
            rec.puffs.Add(p);
        }

        int streaks = r.ReadInt32();
        for (int i = 0; i < streaks; i++)
        {
            float t = r.ReadSingle();
            int n = r.ReadInt32();
            List<(Mesh mesh, Material[] materials, Matrix4x4 local, int layer)> parts = new List<(Mesh, Material[], Matrix4x4, int)>(n);
            for (int k = 0; k < n; k++)
            {
                Mesh mesh = io.Obj<Mesh>();
                Material[] materials = io.Objs<Material>() ?? new Material[0];
                Matrix4x4 local = new Matrix4x4();
                for (int j = 0; j < 16; j++) local[j] = r.ReadSingle();
                parts.Add((mesh, materials, local, r.ReadInt32()));
            }
            rec.streaks.Add(new ReplayStreak { t = t, look = FTLJump.MakeLook(parts), position = io.V3(), rotation = io.Q(), scale = io.V3() });
        }
    }

    private static void ReadShots(ReplayIO io, ReplayShots shots)
    {
        BinaryReader r = io.r;
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            shots.starts.Add(r.ReadSingle()); shots.ends.Add(r.ReadSingle()); shots.heads.Add(io.V3()); shots.velocities.Add(io.V3()); shots.colors.Add(io.Col()); shots.widths.Add(r.ReadSingle());
        }
    }
}

// Watching a saved replay from the main menu: the battle scene is opened with no battle in it - the stage is made
// from the replay's settings (the same terrain and sky) and the recording plays on it. Leaving returns to the menu.
public static class ReplayLaunch
{
    public static ReplayHeader Header { get; private set; }
    public static bool Active => Header != null;

    public static void Watch(ReplayHeader header)
    {
        Header = header;
        BattleContext context = BattleContext.Instance;
        if (context == null) context = new GameObject("BattleContext").AddComponent<BattleContext>();
        context.Clear();
        context.hasPendingBattle = false;
        context.destinationPlanetName = header.system;
        context.destinationAppearance = string.IsNullOrEmpty(header.appearance) ? null : JsonUtility.FromJson<PlanetAppearance>(header.appearance);
        context.isSkirmish = header.skirmish;
        context.skirmishAsteroids = header.skirmishAsteroids;
        context.skirmishNebulae = header.skirmishNebulae;
        context.skirmishIonStorm = header.skirmishIonStorm;
        GameSpeed.Reset();
        SceneManager.LoadScene("BattleScene");
    }

    // GameManager runs this in place of a battle.
    public static IEnumerator Play()
    {
        Canvas canvas = UIKit.Canvas("ReplayLoading", 960);
        UIKit.Image(UIKit.Fill("Dim", canvas.transform), Color.black);
        TMPro.TMP_Text text = UIKit.Text(canvas.transform, "LOADING REPLAY...", 34f, UIKit.Accent, TMPro.TextAlignmentOptions.Center);
        ReplayRecorder recording = null;
        string error = null;
        yield return ReplayFile.Load(Header.path, (r, e) => { recording = r; error = e; });
        if (recording == null)
        {
            text.text = error ?? "That replay couldn't be opened.";
            yield return new WaitForSecondsRealtime(2.5f);
        }
        else
        {
            Object.Destroy(canvas.gameObject);
            yield return ReplayPlayer.Run();
            Object.Destroy(recording.gameObject);
        }
        Header = null;
        if (BattleContext.Instance != null) Object.Destroy(BattleContext.Instance.gameObject);
        GameSpeed.Reset();
        SceneManager.LoadScene("MainMenu");
    }
}
