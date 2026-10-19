using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// FTL arrivals and departures in battle.
//
// Arrival: a thin tear opens along the approach line, the ship snaps in from far back almost instantly
// (stretched along its heading), then the jump energy implodes back into the hull.
// Departure: energy gathers around the hull while the ship spools up, then it jumps out just as fast.
//
// The streak draws the ship's meshes directly for a few frames (no copy of the ship is made), so it's cheap
// and the real ship's navigation, AI and weapons are never moved around. Everything is built at runtime.
public class FTLJump : MonoBehaviour
{
    private const string MaterialPath = "FTL/FTLGlow";

    [Header("Look")]
    public Color energyColor = new Color(0.45f, 0.75f, 1f);
    public Color coreColor = new Color(0.85f, 0.95f, 1f);
    public float glowIntensity = 6f;
    public float lightIntensity = 8f;

    [Header("Arrival")]
    public float tearTime = 0.25f;
    public float dropTime = 0.12f;
    [Tooltip("How far back the ship starts its drop, in ship lengths.")]
    public float jumpDistanceInLengths = 15f;
    [Tooltip("How stretched the ship is at full FTL speed.")]
    public float stretch = 10f;
    public float implosionTime = 0.5f;
    public float arrivalShake = 0.25f;

    [Header("Departure")]
    public float jumpTime = 0.16f;
    public float afterglowTime = 0.45f;
    public float departureShake = 0.15f;
    [Tooltip("Ships jumping out together leave a fraction of a second apart.")]
    public float maxStagger = 0.45f;
    [Tooltip("Spool-up used when a ship jumps without being told to spool first.")]
    public float quickSpoolTime = 0.6f;

    private static FTLJump instance;

    private Material glowMaterial;
    private Texture2D glowTexture;
    private Texture2D lineTexture;
    private Mesh quadMesh;
    private Camera cam;
    private MaterialPropertyBlock block;
    private readonly Dictionary<GameObject, Spool> spools = new Dictionary<GameObject, Spool>();
    private readonly Dictionary<GameObject, List<Behaviour>> hiddenParts = new Dictionary<GameObject, List<Behaviour>>();

    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int IntensityID = Shader.PropertyToID("_Intensity");

    private class Spool
    {
        public GameObject root;
        public ParticleSystem particles;
        public MeshRenderer glow;
        public ParticleSystemRenderer particleRenderer;
        public float length;
        public float started;
        public float duration;
    }

    private static FTLJump Instance
    {
        get
        {
            if (instance == null) instance = new GameObject("FTLJump").AddComponent<FTLJump>();
            return instance;
        }
    }

    public static void Arrive(GameObject ship)
    {
        if (ship == null || !Instance.Ready()) return;
        Instance.StartCoroutine(Instance.ArriveRoutine(ship));
    }

    public static void BeginSpool(GameObject ship, float duration)
    {
        if (ship == null || !Instance.Ready()) return;
        Instance.StartSpool(ship, duration);
    }

    // How long a jump-out takes once it starts (spool already done), including its afterglow.
    public static float JumpOutDuration => instance != null ? instance.jumpTime + instance.afterglowTime : 0.6f;

    // Jumps the ship out and deactivates it. Falls back to just deactivating if the effect can't run.
    public static void Depart(GameObject ship)
    {
        Depart(ship, Random.Range(0f, Instance.maxStagger));
    }

    public static void Depart(GameObject ship, float delay)
    {
        if (ship == null) return;
        if (!Instance.Ready())
        {
            ship.SetActive(false);
            return;
        }
        Instance.StartCoroutine(Instance.DepartRoutine(ship, delay));
    }

    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (glowTexture != null) Destroy(glowTexture);
        if (lineTexture != null) Destroy(lineTexture);
        if (quadMesh != null) Destroy(quadMesh);
    }

    private bool Ready()
    {
        if (glowMaterial != null) return true;

        glowMaterial = Resources.Load<Material>(MaterialPath);
        if (glowMaterial == null)
        {
            Debug.LogWarning($"FTLJump: missing Resources/{MaterialPath} material - ships will appear and vanish without the FTL effect.");
            return false;
        }

        glowTexture = MakeTexture(64, 64, (x, y) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y)), 2.2f));
        lineTexture = MakeTexture(4, 64, (x, y) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(y)), 2f));
        quadMesh = BuildQuad();
        return true;
    }

    // ---------------- Arrival ----------------

    private IEnumerator ArriveRoutine(GameObject ship)
    {
        yield return null; // Let the spawner finish tagging/placing the ship first.
        if (ship == null) yield break;

        Measure(ship, out Vector3 localCentre, out float length);
        SetHidden(ship, true);

        float distance = length * jumpDistanceInLengths;
        Vector3 Centre() => ship.transform.TransformPoint(localCentre);

        // 1. The tear: a thin line of light opens along the approach path.
        LineRenderer tear = CreateLine("FTLTear", energyColor);
        Light flare = CreateLight(Centre(), length * 4f);
        MeshRenderer core = CreateSprite("FTLCore", coreColor);

        for (float t = 0f; t < tearTime; t += Time.deltaTime)
        {
            if (ship == null) { Cleanup(tear, flare, core); yield break; }

            float u = t / tearTime;
            Vector3 centre = Centre();
            Vector3 back = ship.transform.forward;
            tear.SetPosition(0, centre);
            tear.SetPosition(1, centre - back * distance * u * u);
            tear.widthMultiplier = length * 0.03f * u;
            SetGlow(tear, energyColor, glowIntensity * u);
            PlaceSprite(core, centre, length * 0.4f * u, glowIntensity * u);
            flare.transform.position = centre;
            flare.intensity = lightIntensity * 0.4f * u;
            yield return null;
        }

        if (ship == null) { Cleanup(tear, flare, core); yield break; }

        // 2. The drop: the ship streaks in from far back in a fraction of a second.
        List<LookPart> look = CaptureLook(ship);
        Vector3 baseScale = ship.transform.lossyScale;

        for (float t = 0f; t < dropTime; t += Time.deltaTime)
        {
            if (ship == null) break;

            float u = t / dropTime;
            float k = 1f - Mathf.Pow(1f - u, 3f); // Ease out - slams to a stop.
            Vector3 end = ship.transform.position;
            Vector3 start = end - ship.transform.forward * distance;

            Vector3 position = Vector3.Lerp(start, end, k);
            DrawLook(look, position, ship.transform.rotation, Vector3.Scale(baseScale, new Vector3(1f, 1f, Mathf.Lerp(stretch, 1f, k))));

            tear.SetPosition(0, position);
            tear.SetPosition(1, start);
            tear.widthMultiplier = length * Mathf.Lerp(0.18f, 0.08f, u);
            SetGlow(tear, energyColor, glowIntensity * 1.5f);
            yield return null;
        }

        if (ship == null) { Cleanup(tear, flare, core); yield break; }
        SetHidden(ship, false);

        // 3. The implosion: the jump energy collapses back into the hull.
        Vector3 arrival = Centre();
        CameraShake.Shake(arrival, arrivalShake);

        MeshRenderer flash = CreateSprite("FTLFlash", coreColor);
        LineRenderer ring = CreateRing("FTLRing", energyColor);
        ParticleSystem implosion = CreateConverging(arrival, length * 2f, implosionTime, 90, length);

        for (float t = 0f; t < implosionTime; t += Time.deltaTime)
        {
            float u = t / implosionTime;
            Vector3 centre = ship != null ? Centre() : arrival;
            Vector3 up = ship != null ? ship.transform.up : Vector3.up;

            PlaceSprite(flash, centre, length * Mathf.Lerp(2.2f, 0.2f, u * u), glowIntensity * 2.5f * (1f - u));
            PlaceSprite(core, centre, length * Mathf.Lerp(0.9f, 0f, u), glowIntensity * (1f - u));
            PlaceRing(ring, centre, up, length * Mathf.Lerp(2.6f, 0.15f, u * u), length * 0.05f, glowIntensity * (1f - u * u));
            implosion.transform.position = centre;

            float fade = 1f - Mathf.Clamp01(u * 2.5f);
            tear.widthMultiplier = length * 0.08f * fade;
            SetGlow(tear, energyColor, glowIntensity * fade);

            flare.transform.position = centre;
            flare.intensity = lightIntensity * (1f - u) * (1f - u);
            yield return null;
        }

        Cleanup(tear, flare, core, flash, ring);
        Destroy(implosion.gameObject, 1f);
    }

    // ---------------- Departure ----------------

    private void StartSpool(GameObject ship, float duration)
    {
        if (spools.ContainsKey(ship)) return;

        Measure(ship, out Vector3 localCentre, out float length);

        GameObject root = new GameObject("FTLSpool");
        root.transform.SetParent(ship.transform, false);
        root.transform.localPosition = localCentre;

        ParticleSystem particles = CreateParticles("Energy", root.transform);
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        float lifetime = 0.7f;
        main.startLifetime = lifetime;
        main.startSpeed = -length * 0.75f / lifetime;
        main.startSize = new ParticleSystem.MinMaxCurve(length * 0.012f, length * 0.03f);
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = length * 0.75f;
        shape.radiusThickness = 0f;
        shape.scale = new Vector3(0.6f, 0.4f, 1f); // Hull-shaped: long along the ship's heading.
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 20f;
        particles.Play();

        MeshRenderer glow = CreateSprite("Charge", coreColor);
        glow.transform.SetParent(root.transform, true);

        spools[ship] = new Spool
        {
            root = root,
            particles = particles,
            glow = glow,
            particleRenderer = particles.GetComponent<ParticleSystemRenderer>(),
            length = length,
            started = Time.unscaledTime,
            duration = Mathf.Max(0.1f, duration)
        };
    }

    private void Update()
    {
        if (spools.Count == 0) return;

        List<GameObject> gone = null;
        foreach (KeyValuePair<GameObject, Spool> entry in spools)
        {
            if (entry.Key == null || entry.Value.root == null)
            {
                (gone ??= new List<GameObject>()).Add(entry.Key);
                continue;
            }

            Spool spool = entry.Value;
            float u = Mathf.Clamp01((Time.unscaledTime - spool.started) / spool.duration);
            Transform ship = entry.Key.transform;

            ParticleSystem.EmissionModule emission = spool.particles.emission;
            emission.rateOverTime = Mathf.Lerp(20f, 220f, u);
            SetGlow(spool.particleRenderer, energyColor, glowIntensity * Mathf.Lerp(0.4f, 1f, u));

            Vector3 bow = spool.root.transform.position + ship.forward * spool.length * 0.5f;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(6f, 28f, u));
            PlaceSprite(spool.glow, bow, spool.length * Mathf.Lerp(0.05f, 0.7f, u * u) * pulse, glowIntensity * u);
        }

        if (gone != null)
        {
            foreach (GameObject key in gone) spools.Remove(key);
        }
    }

    private IEnumerator DepartRoutine(GameObject ship, float delay)
    {
        for (float t = 0f; t < delay; t += Time.unscaledDeltaTime) yield return null;
        if (ship == null || !ship.activeInHierarchy) yield break;
        UnitHealthManager health = ship.GetComponent<UnitHealthManager>();
        if (health != null && health.IsDead) yield break; // Shot down while waiting its turn.

        if (!spools.ContainsKey(ship))
        {
            StartSpool(ship, quickSpoolTime);
            for (float t = 0f; t < quickSpoolTime; t += Time.unscaledDeltaTime) yield return null;
            if (ship == null) yield break;
        }

        Measure(ship, out Vector3 localCentre, out float length);
        Vector3 forward = ship.transform.forward;
        Vector3 origin = ship.transform.position;
        Vector3 bow = ship.transform.TransformPoint(localCentre) + forward * length * 0.5f;
        float distance = length * jumpDistanceInLengths;

        List<LookPart> look = CaptureLook(ship);
        Vector3 baseScale = ship.transform.lossyScale;
        Quaternion rotation = ship.transform.rotation;

        if (spools.TryGetValue(ship, out Spool spool))
        {
            if (spool.root != null) Destroy(spool.root);
            spools.Remove(ship);
        }

        ship.SetActive(false);
        CameraShake.Shake(bow, departureShake);

        MeshRenderer flash = CreateSprite("FTLFlash", coreColor);
        LineRenderer streak = CreateLine("FTLStreak", energyColor);
        LineRenderer ring = CreateRing("FTLRing", energyColor);
        Light flare = CreateLight(bow, length * 4f);

        for (float t = 0f; t < jumpTime; t += Time.unscaledDeltaTime)
        {
            float u = t / jumpTime;
            float k = u * u * u; // Ease in - punches out at full speed.
            Vector3 position = origin + forward * distance * k;
            DrawLook(look, position, rotation, Vector3.Scale(baseScale, new Vector3(1f, 1f, Mathf.Lerp(1f, stretch, u))));

            streak.SetPosition(0, bow);
            streak.SetPosition(1, position + forward * length * 0.5f);
            streak.widthMultiplier = length * Mathf.Lerp(0.15f, 0.06f, u);
            SetGlow(streak, energyColor, glowIntensity * 1.5f);

            PlaceSprite(flash, bow, length * Mathf.Lerp(0.6f, 1.8f, u), glowIntensity * 2.5f);
            PlaceRing(ring, bow, forward, length * Mathf.Lerp(0.2f, 1.2f, u), length * 0.04f, glowIntensity);
            flare.intensity = lightIntensity;
            yield return null;
        }

        Vector3 far = origin + forward * distance;
        for (float t = 0f; t < afterglowTime; t += Time.unscaledDeltaTime)
        {
            float u = t / afterglowTime;
            float fade = (1f - u) * (1f - u);
            streak.SetPosition(0, bow);
            streak.SetPosition(1, far);
            streak.widthMultiplier = length * 0.06f * (1f - u);
            SetGlow(streak, energyColor, glowIntensity * fade);
            PlaceSprite(flash, bow, length * Mathf.Lerp(1.8f, 0.4f, u), glowIntensity * 2.5f * fade);
            PlaceRing(ring, bow, forward, length * Mathf.Lerp(1.2f, 2f, u), length * 0.04f * (1f - u), glowIntensity * fade);
            flare.intensity = lightIntensity * fade;
            yield return null;
        }

        Cleanup(flash, streak, ring, flare);
    }

    // ---------------- Ship helpers ----------------

    private struct LookPart
    {
        public Mesh mesh;
        public Material[] materials;
        public Matrix4x4 local;
        public int layer;
    }

    // Records the ship's visible meshes (highest LOD only) relative to the ship, for drawing the streak.
    private static List<LookPart> CaptureLook(GameObject ship)
    {
        HashSet<Renderer> lowerLods = new HashSet<Renderer>();
        foreach (LODGroup group in ship.GetComponentsInChildren<LODGroup>())
        {
            LOD[] lods = group.GetLODs();
            for (int i = 1; i < lods.Length; i++)
            {
                foreach (Renderer lodRenderer in lods[i].renderers) lowerLods.Add(lodRenderer);
            }
            if (lods.Length > 0)
            {
                foreach (Renderer lodRenderer in lods[0].renderers) lowerLods.Remove(lodRenderer);
            }
        }

        Matrix4x4 toShip = Matrix4x4.TRS(ship.transform.position, ship.transform.rotation, ship.transform.lossyScale).inverse;
        List<LookPart> parts = new List<LookPart>();
        foreach (MeshRenderer meshRenderer in ship.GetComponentsInChildren<MeshRenderer>())
        {
            if (!meshRenderer.enabled || lowerLods.Contains(meshRenderer)) continue;

            MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            parts.Add(new LookPart
            {
                mesh = filter.sharedMesh,
                materials = meshRenderer.sharedMaterials,
                local = toShip * meshRenderer.localToWorldMatrix,
                layer = meshRenderer.gameObject.layer
            });
        }
        return parts;
    }

    private static void DrawLook(List<LookPart> parts, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Matrix4x4 root = Matrix4x4.TRS(position, rotation, scale);
        foreach (LookPart part in parts)
        {
            Matrix4x4 matrix = root * part.local;
            for (int sub = 0; sub < part.mesh.subMeshCount; sub++)
            {
                Material material = part.materials.Length > 0 ? part.materials[Mathf.Min(sub, part.materials.Length - 1)] : null;
                if (material == null) continue;

                RenderParams renderParams = new RenderParams(material)
                {
                    layer = part.layer,
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = false
                };
                Graphics.RenderMesh(renderParams, part.mesh, sub, matrix);
            }
        }
    }

    private static void Measure(GameObject ship, out Vector3 localCentre, out float length)
    {
        Bounds bounds = new Bounds(ship.transform.position, Vector3.zero);
        bool any = false;
        foreach (MeshRenderer meshRenderer in ship.GetComponentsInChildren<MeshRenderer>())
        {
            if (!any) bounds = meshRenderer.bounds;
            else bounds.Encapsulate(meshRenderer.bounds);
            any = true;
        }

        localCentre = ship.transform.InverseTransformPoint(bounds.center);
        length = Mathf.Max(any ? Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) : 30f, 5f);
    }

    // Hides the arriving ship (and stops its guns) until the streak lands.
    private void SetHidden(GameObject ship, bool hidden)
    {
        foreach (Renderer part in ship.GetComponentsInChildren<Renderer>(true))
        {
            part.forceRenderingOff = hidden;
        }

        if (hidden)
        {
            List<Behaviour> parts = new List<Behaviour>();
            foreach (Canvas canvas in ship.GetComponentsInChildren<Canvas>())
            {
                if (canvas.enabled) parts.Add(canvas);
            }
            foreach (TurretController turret in ship.GetComponentsInChildren<TurretController>())
            {
                if (turret.enabled) parts.Add(turret);
            }
            foreach (Behaviour part in parts) part.enabled = false;
            hiddenParts[ship] = parts;
        }
        else if (hiddenParts.TryGetValue(ship, out List<Behaviour> parts))
        {
            foreach (Behaviour part in parts)
            {
                if (part != null) part.enabled = true;
            }
            hiddenParts.Remove(ship);
        }
    }

    // ---------------- Effect building blocks ----------------

    private MeshRenderer CreateSprite(string name, Color color)
    {
        GameObject sprite = new GameObject(name);
        sprite.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        MeshRenderer meshRenderer = sprite.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = glowMaterial;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        SetGlow(meshRenderer, color, 0f, glowTexture);
        return meshRenderer;
    }

    private void PlaceSprite(MeshRenderer sprite, Vector3 position, float size, float intensity)
    {
        if (sprite == null) return;
        if (cam == null) cam = Camera.main;

        sprite.transform.position = position;
        if (cam != null) sprite.transform.rotation = cam.transform.rotation; // Billboard.
        sprite.transform.localScale = Vector3.one * Mathf.Max(size, 0.001f);

        block ??= new MaterialPropertyBlock();
        sprite.GetPropertyBlock(block);
        block.SetFloat(IntensityID, Mathf.Max(0f, intensity));
        sprite.SetPropertyBlock(block);
    }

    private LineRenderer CreateLine(string name, Color color)
    {
        LineRenderer line = new GameObject(name).AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 4;
        line.sharedMaterial = glowMaterial;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f));
        line.colorGradient = Fade(1f, 0f);
        SetGlow(line, color, 0f, lineTexture);
        return line;
    }

    private LineRenderer CreateRing(string name, Color color)
    {
        LineRenderer ring = new GameObject(name).AddComponent<LineRenderer>();
        ring.positionCount = 48;
        ring.loop = true;
        ring.useWorldSpace = true;
        ring.alignment = LineAlignment.View;
        ring.textureMode = LineTextureMode.Stretch;
        ring.sharedMaterial = glowMaterial;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetGlow(ring, color, 0f, lineTexture);
        return ring;
    }

    private void PlaceRing(LineRenderer ring, Vector3 centre, Vector3 axis, float radius, float width, float intensity)
    {
        if (ring == null) return;

        Vector3 a = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up).normalized;
        Vector3 b = Vector3.Cross(axis, a).normalized;
        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / ring.positionCount;
            ring.SetPosition(i, centre + (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) * radius);
        }
        ring.widthMultiplier = Mathf.Max(width, 0.001f);
        SetGlow(ring, energyColor, Mathf.Max(0f, intensity));
    }

    private Light CreateLight(Vector3 position, float range)
    {
        Light light = new GameObject("FTLLight").AddComponent<Light>();
        light.type = LightType.Point;
        light.color = energyColor;
        light.range = range;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        light.transform.position = position;
        return light;
    }

    private ParticleSystem CreateParticles(string name, Transform parent)
    {
        GameObject holder = new GameObject(name);
        holder.SetActive(false); // Configure before the system wakes up and starts playing.
        holder.transform.SetParent(parent, false);

        ParticleSystem particles = holder.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.gravityModifier = 0f;
        main.maxParticles = 400;
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, energyColor);
        main.scalingMode = ParticleSystemScalingMode.Shape;

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.2f)));

        ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
        colour.enabled = true;
        colour.color = new ParticleSystem.MinMaxGradient(Fade(0f, 1f, 0.25f));

        ParticleSystemRenderer particleRenderer = holder.GetComponent<ParticleSystemRenderer>();
        particleRenderer.sharedMaterial = glowMaterial;
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.velocityScale = 0.04f;
        particleRenderer.lengthScale = 2.5f;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetGlow(particleRenderer, energyColor, glowIntensity, glowTexture);

        holder.SetActive(true);
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    // Particles on a sphere rushing inward, all reaching the centre as the effect ends.
    private ParticleSystem CreateConverging(Vector3 centre, float radius, float duration, int count, float length)
    {
        ParticleSystem particles = CreateParticles("FTLImplosion", null);
        particles.transform.position = centre;

        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.duration = duration;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = duration;
        main.startSpeed = -radius / duration;
        main.startSize = new ParticleSystem.MinMaxCurve(length * 0.015f, length * 0.04f);

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        shape.radiusThickness = 0f;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        particles.Play();
        return particles;
    }

    private void SetGlow(Renderer target, Color color, float intensity, Texture texture = null)
    {
        if (target == null) return;

        block ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(block);
        if (texture != null) block.SetTexture(MainTexID, texture);
        block.SetColor(ColorID, color);
        block.SetFloat(IntensityID, Mathf.Max(0f, intensity));
        target.SetPropertyBlock(block);
    }

    private static Gradient Fade(float startAlpha, float endAlpha, float holdUntil = 0f)
    {
        Gradient gradient = new Gradient();
        GradientAlphaKey[] alpha = holdUntil > 0f
            ? new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(1f, holdUntil), new GradientAlphaKey(endAlpha, 1f) }
            : new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(endAlpha, 1f) };
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alpha);
        return gradient;
    }

    private static void Cleanup(params Component[] parts)
    {
        foreach (Component part in parts)
        {
            if (part != null) Destroy(part.gameObject);
        }
    }

    private static Mesh BuildQuad()
    {
        Mesh mesh = new Mesh { name = "FTLQuad" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
        return mesh;
    }

    private static Texture2D MakeTexture(int width, int height, System.Func<float, float, float> alphaAt)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "FTLGlow"
        };

        Color32[] pixels = new Color32[width * height];
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                float x = (px + 0.5f) / width * 2f - 1f;
                float y = (py + 0.5f) / height * 2f - 1f;
                byte alpha = (byte)(Mathf.Clamp01(alphaAt(x, y)) * 255f);
                pixels[py * width + px] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
