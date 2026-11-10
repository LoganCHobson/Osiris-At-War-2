using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

// Flies every laser bolt in the battle from one place, once per frame. Big battles have thousands of bolts in
// the air; giving each its own FixedUpdate and raycast cost more than everything else in the game put together
// (and FixedUpdate runs several times a frame when the game is busy, making it worse). Here all the hit tests
// go out as one batch of RaycastCommands, spread across the worker threads, and the results are handled in order.
[DefaultExecutionOrder(50)]
public class LaserSystem : MonoBehaviour
{
    private const int MaxHits = 4; // Enough to see past a friendly shield bubble or two.

    private static LaserSystem instance;
    private static readonly List<Laser> active = new List<Laser>();

    private Laser[] batch = new Laser[0];
    private Vector3[] froms = new Vector3[0];
    private Vector3[] tos = new Vector3[0];
    private NativeArray<RaycastCommand> commands;
    private NativeArray<RaycastHit> results;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        active.Clear();
    }

    public static void Register(Laser laser)
    {
        if (instance == null)
        {
            GameObject host = new GameObject("LaserSystem");
            instance = host.AddComponent<LaserSystem>();
        }
        active.Add(laser);
    }

    public static void Unregister(Laser laser)
    {
        active.Remove(laser);
    }

    // ---------------- Drawing ----------------
    // Every bolt as a glowing energy streak: a soft coloured halo and a hot, near-white core, brightest at the
    // head and tapering off behind, stretched along its flight and always turned to face the camera. One mesh
    // for the lot, additive and HDR so the bloom picks it up (the same look as the CIWS tracers and beams).
    private Mesh streaks;
    private MeshRenderer streakRenderer;
    private Vector3[] sv = new Vector3[0];
    private Vector2[] suv = new Vector2[0];
    private Color[] sc = new Color[0];
    private int[] st = new int[0];
    private static Texture2D streakTexture, boltTexture;

    // Two looks. Default: compact glowing bolts - short, rounded at both ends, no tail. LongStreaks: the
    // brighter version with a long tapering tail (kept in case it's wanted back).
    public static bool LongStreaks = false;

    private void EnsureDrawing(int bolts)
    {
        if (streaks == null)
        {
            Material source = Resources.Load<Material>("FTL/FTLGlow");
            if (source == null) return;
            Material material = new Material(source);
            material.SetTexture("_MainTex", LongStreaks ? streakTexture ??= StreakTexture() : boltTexture ??= BoltTexture());
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Intensity", 1f);
            GameObject go = new GameObject("LaserStreaks");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = streaks = new Mesh { name = "LaserStreaks", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            streaks.MarkDynamic();
            streakRenderer = go.AddComponent<MeshRenderer>();
            streakRenderer.sharedMaterial = material;
            streakRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            streakRenderer.receiveShadows = false;
        }
        int quads = bolts * 2;
        if (sv.Length < quads * 4)
        {
            int size = Mathf.NextPowerOfTwo(quads * 4);
            sv = new Vector3[size];
            suv = new Vector2[size];
            sc = new Color[size];
            st = new int[size / 4 * 6];
        }
    }

    private void LateUpdate()
    {
        int count = active.Count;
        EnsureDrawing(Mathf.Max(1, count));
        if (streaks == null) return;

        Camera cam = Camera.main;
        Vector3 eye = cam != null ? cam.transform.position : Vector3.up * 1000f;
        int q = 0;
        for (int i = 0; i < count; i++)
        {
            Laser laser = active[i];
            if (laser == null || laser.Spent) continue;
            Vector3 v = laser.Velocity;
            float speed = v.magnitude;
            if (speed < 0.01f) continue;
            Vector3 dir = v / speed;
            Vector3 head = laser.transform.position;
            // Thick enough to read at any distance: never thinner on screen than a few pixels (the bolt models
            // are only ~0.05 across, a pixel or two at battle range).
            float distance = Vector3.Distance(eye, head);
            float sizeScale = Mathf.Clamp(laser.BoltWidth / 0.05f, 0.45f, 2f); // Fighters' bolts are smaller.
            float w = Mathf.Max(laser.BoltWidth, distance * (LongStreaks ? 0.0011f : 0.0009f) * sizeScale);
            float length = LongStreaks
                ? Mathf.Max(Mathf.Clamp(speed * 0.06f, 3f, 10f) * sizeScale, w * 14f)
                : Mathf.Max(Mathf.Clamp(speed * 0.02f, 1.4f, 3.2f) * sizeScale, w * 8f);
            Vector3 tail = head - dir * length;
            Vector3 side = Vector3.Cross(dir, eye - head);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(dir, Vector3.up);
            side.Normalize();

            Color c = laser.BoltColor;
            Color tint = new Color(c.r, c.g, c.b, 1f);
            if (LongStreaks)
            {
                Quad(ref q, head + dir * w * 2f, tail, side * w * 4.5f, tint * 1.6f);
                Quad(ref q, head, tail + dir * length * 0.25f, side * w * 1.4f, Color.Lerp(tint, Color.white, 0.65f) * 3.2f);
            }
            else
            {
                // A soft coloured glow around a brighter core, the same length end to end.
                Quad(ref q, head + dir * w * 1.5f, tail - dir * w * 1.5f, side * w * 3.2f, tint * 0.9f);
                Quad(ref q, head, tail, side * w * 1.1f, Color.Lerp(tint, Color.white, 0.45f) * 1.7f);
            }
        }

        // Unused quads collapse to nothing.
        for (int k = q * 4; k < Mathf.Min(sv.Length, (lastQuads * 4)); k++) sv[k] = Vector3.zero;
        lastQuads = q;

        streaks.Clear(false);
        streaks.SetVertices(sv, 0, Mathf.Max(4, q * 4));
        streaks.SetUVs(0, suv, 0, Mathf.Max(4, q * 4));
        streaks.SetColors(sc, 0, Mathf.Max(4, q * 4));
        streaks.SetTriangles(st, 0, Mathf.Max(6, q * 6), 0, false);
        streaks.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
    }

    private int lastQuads;

    private void Quad(ref int q, Vector3 head, Vector3 tail, Vector3 halfWidth, Color color)
    {
        int v = q * 4, t = q * 6;
        sv[v] = tail - halfWidth; sv[v + 1] = tail + halfWidth; sv[v + 2] = head - halfWidth; sv[v + 3] = head + halfWidth;
        suv[v] = new Vector2(0f, 0f); suv[v + 1] = new Vector2(0f, 1f); suv[v + 2] = new Vector2(1f, 0f); suv[v + 3] = new Vector2(1f, 1f);
        sc[v] = sc[v + 1] = sc[v + 2] = sc[v + 3] = color;
        st[t] = v; st[t + 1] = v + 2; st[t + 2] = v + 1;
        st[t + 3] = v + 1; st[t + 4] = v + 2; st[t + 5] = v + 3;
        q++;
    }

    // An even bolt rounded off at both ends (u along, v across), soft-edged.
    private static Texture2D BoltTexture()
    {
        const int w = 64, h = 16;
        Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float across = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;
            for (int x = 0; x < w; x++)
            {
                float along = Mathf.Abs((x + 0.5f) / w - 0.5f) * 2f;
                // Capsule: flat through the middle, rounded at the ends.
                float endU = Mathf.Max(0f, (along - 0.75f) / 0.25f);
                float r = Mathf.Sqrt(endU * endU + across * across);
                float a = Mathf.Exp(-r * r * 3.5f) * Mathf.Clamp01((1f - r) * 5f);
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    // u: tail (0) to head (1), brightening and then rounding off at the tip; v: across, soft-edged.
    private static Texture2D StreakTexture()
    {
        const int w = 64, h = 16;
        Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float across = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;
            float soft = Mathf.Exp(-across * across * 5f) * Mathf.Clamp01((1f - across) * 6f);
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float along = Mathf.Pow(u, 1.6f) * Mathf.Clamp01((1f - u) * 14f + 0.15f);
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(soft * along) * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void OnDestroy()
    {
        if (commands.IsCreated) commands.Dispose();
        if (results.IsCreated) results.Dispose();
        if (instance == this) instance = null;
    }

    private void Update()
    {
        int count = active.Count;
        if (count == 0) return;
        float dt = Time.deltaTime;

        if (batch.Length < count)
        {
            int size = Mathf.NextPowerOfTwo(count);
            batch = new Laser[size];
            froms = new Vector3[size];
            tos = new Vector3[size];
            if (commands.IsCreated) commands.Dispose();
            if (results.IsCreated) results.Dispose();
            commands = new NativeArray<RaycastCommand>(size, Allocator.Persistent);
            results = new NativeArray<RaycastHit>(size * MaxHits, Allocator.Persistent);
        }
        active.CopyTo(0, batch, 0, count); // Bolts that land are removed from `active` while we work.

        for (int i = 0; i < count; i++)
        {
            Laser laser = batch[i];
            laser.Advance(dt, out froms[i], out tos[i]);
            Vector3 segment = tos[i] - froms[i];
            float length = segment.magnitude;
            Vector3 direction = length > 0.0001f ? segment / length : Vector3.forward;
            commands[i] = new RaycastCommand(froms[i], direction, new QueryParameters(laser.HitMask), Mathf.Max(length, 0.0001f));
        }

        JobHandle handle = RaycastCommand.ScheduleBatch(commands.GetSubArray(0, count), results.GetSubArray(0, count * MaxHits), 32, MaxHits);
        handle.Complete();

        for (int i = 0; i < count; i++)
        {
            Laser laser = batch[i];
            if (laser == null) continue;
            DamageLedger.Begin(laser.source, laser.weapon);
            laser.Resolve(froms[i], tos[i], results, i * MaxHits, MaxHits, dt);
            DamageLedger.End();
            batch[i] = null;
        }
    }
}
