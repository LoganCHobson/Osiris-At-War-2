using UnityEngine;
using UnityEngine.SceneManagement;

// Every CIWS round in the battle, simulated and drawn here as one mesh of glowing tracer streaks (thousands
// of pooled projectiles would be far too slow at 50 rounds a second per gun). A round fired at a fighter or a
// missile only checks that target; everything else it misses harmlessly. A round fired at a warship (a Human
// fighter's strafing run) is hit-tested against the real geometry, as a laser bolt is: it strikes the first
// enemy shield, hardpoint or hull in its path. (It used to check only for passing within 1.4 of the aimed
// hardpoint's centre, so on a big hull nearly every round went straight through.)
public class CIWSTracers : MonoBehaviour
{
    private const int MaxRounds = 3000;
    private const string GlowMaterialPath = "FTL/FTLGlow";

    public static Color TracerColor = new Color(1f, 0.78f, 0.35f);
    public static float TracerLength = 1.6f;
    public static float TracerWidth = 0.045f;
    public static float TracerIntensity = 2.2f;

    private struct Round
    {
        public Vector3 position;
        public Vector3 velocity;
        public float life;
        public float damage;
        public Missile missile;
        public HardpointHealth fighter;
        public UnitHealthManager source;
        public DamageLedger.Weapon weapon;
        public float fighterRadius;
        public int shipMask;      // non-zero: aimed at a warship, hit-tested against colliders on these layers
        public bool attackerSide; // the shooter's side, so its own ships and shields don't stop the round
        public int replayId;      // its shot in the battle replay
    }

    private const float HullDamageMultiplier = 0.35f; // as Laser: a round in bare hull does less than one in a hardpoint

    private static CIWSTracers instance;

    private readonly Round[] rounds = new Round[MaxRounds];
    private int count;

    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private Vector3[] vertices;
    private Vector2[] uvs;
    private int[] triangles;
    private Camera cam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        instance = null;
    }

    public static void Fire(Vector3 position, Vector3 velocity, float life, float damage, Missile missile, HardpointHealth fighter,
        UnitHealthManager source = null, DamageLedger.Weapon weapon = DamageLedger.Weapon.Gatling)
    {
        if (instance == null && !Create()) return;
        instance.Add(position, velocity, life, damage, missile, fighter, source, weapon);
    }

    // Soft streak across its width, like the FTL lines.
    private static Texture2D tracerTexture;
    public static Texture2D TracerTexture()
    {
        if (tracerTexture != null) return tracerTexture;
        Texture2D texture = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "CIWSTracer" };
        Color32[] pixels = new Color32[4 * 64];
        for (int y = 0; y < 64; y++)
        {
            float across = Mathf.Abs((y + 0.5f) / 64f * 2f - 1f);
            byte alpha = (byte)(Mathf.Pow(Mathf.Clamp01(1f - across), 1.6f) * 255f);
            for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32(255, 255, 255, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return tracerTexture = texture;
    }

    private static bool Create()
    {
        Material material = Resources.Load<Material>(GlowMaterialPath);
        if (material == null) return false;

        GameObject go = new GameObject("CIWSTracers") { layer = 2 };
        SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
        instance = go.AddComponent<CIWSTracers>();
        instance.Build(material);
        return true;
    }

    private void Build(Material material)
    {
        vertices = new Vector3[MaxRounds * 4];
        uvs = new Vector2[MaxRounds * 4];
        triangles = new int[MaxRounds * 6];
        for (int i = 0; i < MaxRounds; i++)
        {
            int v = i * 4;
            uvs[v] = new Vector2(0f, 0f);
            uvs[v + 1] = new Vector2(0f, 1f);
            uvs[v + 2] = new Vector2(1f, 0f);
            uvs[v + 3] = new Vector2(1f, 1f);
        }

        mesh = new Mesh { name = "CIWSTracers", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.MarkDynamic();
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetTexture("_MainTex", TracerTexture());
        block.SetColor("_Color", TracerColor);
        block.SetFloat("_Intensity", TracerIntensity);
        meshRenderer.SetPropertyBlock(block);
    }

    private void Add(Vector3 position, Vector3 velocity, float life, float damage, Missile missile, HardpointHealth fighter, UnitHealthManager source, DamageLedger.Weapon weapon)
    {
        if (count >= MaxRounds) return;
        int shipMask = 0;
        if (fighter != null && fighter.GetComponentInParent<Fighter>() == null)
        {
            // A warship's hardpoint: hit whatever of the enemy's is really in the way.
            shipMask = (1 << fighter.gameObject.layer) | (1 << fighter.transform.root.gameObject.layer) | ShieldGenerator.Mask
                | WreckChunk.BlockingMask | BattleTerrain.BlockingMask;
            fighter = null;
        }
        rounds[count++] = new Round
        {
            position = position,
            velocity = velocity,
            life = life,
            damage = damage,
            missile = missile,
            fighter = fighter,
            fighterRadius = 1.4f,
            source = source,
            weapon = weapon,
            shipMask = shipMask,
            attackerSide = source != null && source.isAttackerSide,
            replayId = ReplayRecorder.TracerFired(position, velocity),
        };
    }

    private void Update()
    {
        if (GameManager.CombatOver)
        {
            for (int i = 0; i < count; i++) ReplayRecorder.TracerEnded(rounds[i].replayId);
            count = 0;
        }

        float dt = Time.deltaTime;
        for (int i = count - 1; i >= 0; i--)
        {
            ref Round round = ref rounds[i];
            Vector3 from = round.position;
            Vector3 to = from + round.velocity * dt;
            round.position = to;
            round.life -= dt;

            if (round.life <= 0f && round.shipMask != 0) Laser.Count(TallyKey(round), 4); // a strafing round that hit nothing
            if (round.life <= 0f || Hit(ref round, from, to))
            {
                ReplayRecorder.TracerEnded(round.replayId);
                rounds[i] = rounds[--count];
            }
        }
    }

    private static readonly RaycastHit[] shipHits = new RaycastHit[6];

    private static bool Hit(ref Round round, Vector3 from, Vector3 to)
    {
        if (round.shipMask != 0) return HitShip(ref round, from, to);
        if (round.missile != null)
        {
            if (!round.missile.InFlight)
            {
                round.missile = null;
                return false;
            }
            if (!Passes(from, to, round.missile.transform.position, round.missile.HitRadius)) return false;
            DamageLedger.Begin(round.source, round.weapon);
            round.missile.TakeHit(round.damage, true);
            DamageLedger.End();
            return true;
        }

        if (round.fighter != null)
        {
            if (!round.fighter.gameObject.activeInHierarchy)
            {
                round.fighter = null;
                return false;
            }
            if (!Passes(from, to, round.fighter.transform.position, round.fighterRadius)) return false;
            DamageLedger.Begin(round.source, round.weapon);
            round.fighter.DealDamage(round.damage);
            DamageLedger.End();
            return true;
        }
        return false;
    }

    // A round at a warship: the nearest enemy shield (if it's up), hardpoint or hull along this step.
    private static bool HitShip(ref Round round, Vector3 from, Vector3 to)
    {
        Vector3 step = to - from;
        float length = step.magnitude;
        if (length < 1e-4f) return false;
        int hits = Physics.RaycastNonAlloc(from, step / length, shipHits, length, round.shipMask, QueryTriggerInteraction.Collide);
        float best = float.MaxValue;
        bool struck = false;
        for (int pass = 0; pass < hits; pass++)
        {
            // Nearest first, without sorting the buffer: pick the closest not yet ruled out.
            int index = -1;
            for (int i = 0; i < hits; i++)
            {
                if (shipHits[i].collider == null) continue;
                if (index < 0 || shipHits[i].distance < shipHits[index].distance) index = i;
            }
            if (index < 0) break;
            RaycastHit hit = shipHits[index];
            shipHits[index] = default;
            if (hit.distance > best) break;
            Collider collider = hit.collider;
            int layer = collider.gameObject.layer;
            if (layer == WreckChunk.WreckLayer || layer == BattleTerrain.AsteroidLayer) { Laser.Count(TallyKey(round), 3); return true; } // soaked up

            if (layer == ShieldGenerator.Layer)
            {
                ShieldGenerator shield = collider.GetComponentInParent<ShieldGenerator>();
                UnitHealthManager owner = shield != null ? shield.GetComponentInParent<UnitHealthManager>() : null;
                if (owner == null || owner.isAttackerSide == round.attackerSide) continue; // our own side's bubble
                DamageLedger.Begin(round.source, round.weapon);
                bool absorbed = shield.Absorb(round.damage, hit.point);
                DamageLedger.End();
                if (absorbed) { Sfx.Play(Sfx.Cue.HitShield, hit.point); Laser.Count(TallyKey(round), 2); return true; }
                continue; // down: on to the hull behind it
            }

            UnitHealthManager ship = collider.GetComponentInParent<UnitHealthManager>();
            if (ship == null || ship.IsDead || ship.isAttackerSide == round.attackerSide) continue;
            DamageLedger.Begin(round.source, round.weapon);
            HullScarring.Impact(collider, hit.point, round.velocity, round.damage);
            HardpointHealth hardpoint = collider.GetComponentInParent<HardpointHealth>();
            if (hardpoint == null) hardpoint = collider.GetComponentInChildren<HardpointHealth>();
            Laser.Count(TallyKey(round), hardpoint != null ? 0 : 1);
            if (hardpoint != null) hardpoint.DealDamage(round.damage);
            else ship.DealRandomDamage(round.damage * HullDamageMultiplier);
            DamageLedger.End();
            struck = true;
            break;
        }
        return struck;
    }

    private static string TallyKey(in Round round) => "tracer " + (round.attackerSide ? "attacker" : "defender");

    private static bool Passes(Vector3 from, Vector3 to, Vector3 point, float radius)
    {
        Vector3 segment = to - from;
        float along = segment.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(point - from, segment) / segment.sqrMagnitude) : 0f;
        return (point - (from + segment * along)).sqrMagnitude <= radius * radius;
    }

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Round round = rounds[i];
            float speed = round.velocity.magnitude;
            Vector3 dir = speed > 0f ? round.velocity / speed : Vector3.forward;
            Vector3 tail = round.position - dir * TracerLength;
            Vector3 side = Vector3.Cross(dir, round.position - eye);
            side = side.sqrMagnitude > 0.0001f ? side.normalized * TracerWidth : Vector3.up * TracerWidth;

            int v = i * 4;
            vertices[v] = tail - side;
            vertices[v + 1] = tail + side;
            vertices[v + 2] = round.position - side;
            vertices[v + 3] = round.position + side;

            int t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v + 2;
            triangles[t + 4] = v + 1;
            triangles[t + 5] = v + 3;
        }

        meshRenderer.enabled = count > 0;
        if (count == 0) return;
        mesh.Clear(true);
        mesh.SetVertices(vertices, 0, count * 4);
        mesh.SetUVs(0, uvs, 0, count * 4);
        mesh.SetTriangles(triangles, 0, count * 6, 0, false);
    }
}
