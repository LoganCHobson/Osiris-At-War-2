using System.Collections.Generic;
using UnityEngine;

public class WreckChunk : MonoBehaviour
{
    private static readonly List<WreckChunk> persistent = new List<WreckChunk>();
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static int wreckLayer = -2;

    public static int WreckLayer
    {
        get
        {
            if (wreckLayer == -2) wreckLayer = LayerMask.NameToLayer("Wreck");
            return wreckLayer;
        }
    }

    public static int BlockingMask => WreckLayer >= 0 ? 1 << WreckLayer : 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        persistent.Clear();
        wreckLayer = -2;
    }

    private struct Cap
    {
        public Renderer renderer;
        public int index;
    }

    private struct Burner
    {
        public GameObject effect;
        public float stopAt;
    }

    private readonly List<Mesh> ownedMeshes = new List<Mesh>();
    private readonly List<MeshFilter> filters = new List<MeshFilter>();
    private readonly List<Vector3[]> vertexCache = new List<Vector3[]>();
    private readonly List<Cap> caps = new List<Cap>();
    private readonly List<Burner> burners = new List<Burner>();

    private DestructionProfile profile;
    private MaterialPropertyBlock block;
    private Vector3 velocity;
    private Vector3 spin;
    private float drag;
    private float lifetime;
    private float bornAt;
    private float effectScale;
    private bool blasts;
    private float nextBlastAt;
    private bool fading;
    private float fadeStartedAt;
    private Vector3 baseScale;
    private bool isPersistent;
    private float burnEndsAt;
    private float nextEmberAt;
    private Color emberColor;
    private float radius;
    private float nextJostleAt;

    public bool IsPersistent => isPersistent;

    public void Own(Mesh mesh)
    {
        if (mesh != null) ownedMeshes.Add(mesh);
    }

    public void RegisterCap(Renderer renderer, int submeshIndex)
    {
        caps.Add(new Cap { renderer = renderer, index = submeshIndex });
    }

    public void Launch(DestructionProfile source, Vector3 linearVelocity, Vector3 angularVelocity, float scale, bool persist, float life, bool secondaryBlasts)
    {
        profile = source;
        velocity = linearVelocity;
        spin = angularVelocity;
        effectScale = scale;
        blasts = secondaryBlasts;
        isPersistent = persist;
        bornAt = Time.time;
        baseScale = transform.localScale;
        block = new MaterialPropertyBlock();
        nextBlastAt = Time.time + Random.Range(source.wreckExplosionInterval.x, source.wreckExplosionInterval.y);

        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            filters.Add(filter);
            vertexCache.Add(filter.sharedMesh.vertices);
        }

        if (isPersistent)
        {
            drag = source.wreckDrag;
            float burn = Random.Range(source.burnDuration.x, source.burnDuration.y);
            burnEndsAt = Time.time + burn;
            lifetime = float.PositiveInfinity;
            Ignite(Random.Range(source.wreckFires.x, source.wreckFires.y + 1));
            ScheduleBurners(burn);
            MakeSolid();
            Register();
        }
        else
        {
            drag = 0.03f;
            lifetime = Mathf.Max(life, source.wreckFadeDuration + 0.5f);
            burnEndsAt = Time.time + lifetime;
        }

        if (source.interiorMaterial != null && source.interiorMaterial.HasProperty(EmissionId))
        {
            emberColor = source.interiorMaterial.GetColor(EmissionId);
        }

        nextJostleAt = Time.time + Random.Range(0f, 0.25f);
    }

    private void Ignite(int count)
    {
        if (profile.hullFires == null || profile.hullFires.Length == 0) return;

        for (int i = 0; i < count; i++)
        {
            ParticleSystem fire = profile.hullFires[Random.Range(0, profile.hullFires.Length)];
            DestructionFx.Attach(fire, transform, RandomPoint(), profile.fireScale * effectScale * Random.Range(0.8f, 1.3f));
        }
    }

    private void ScheduleBurners(float burn)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            if (child == transform || child.GetComponent<ParticleSystem>() == null) continue;
            if (child.parent != null && child.parent.GetComponent<ParticleSystem>() != null) continue;
            burners.Add(new Burner { effect = child.gameObject, stopAt = Time.time + burn * Random.Range(0.45f, 1f) });
        }
    }

    private void MakeSolid()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool any = false;

        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>())
        {
            if (any) bounds.Encapsulate(renderer.bounds);
            else bounds = renderer.bounds;
            any = true;

            if (!profile.wrecksBlockLasers) continue;
            if (renderer.GetComponent<Collider>() == null) renderer.gameObject.AddComponent<BoxCollider>();
            else renderer.GetComponent<Collider>().enabled = true;
            if (WreckLayer >= 0) renderer.gameObject.layer = WreckLayer;
        }

        radius = any ? bounds.extents.magnitude * 0.6f : 1f;

        if (profile.wrecksBlockLasers)
        {
            Rigidbody body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    private void Register()
    {
        persistent.Add(this);
        int limit = Mathf.Max(1, profile.maxPersistentChunks);
        while (persistent.Count > limit)
        {
            WreckChunk oldest = persistent[0];
            persistent.RemoveAt(0);
            if (oldest != null) oldest.BeginFade();
        }
    }

    public void BeginFade()
    {
        if (fading) return;
        fading = true;
        fadeStartedAt = Time.time;
        DestructionFx.StopEmitting(gameObject);
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
    }

    private void Update()
    {
        if (profile == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        transform.position += velocity * dt;
        velocity *= Mathf.Max(0f, 1f - drag * dt);

        if (isPersistent && spin.sqrMagnitude > profile.residualSpin * profile.residualSpin)
        {
            spin *= Mathf.Max(0f, 1f - profile.wreckSpinDrag * dt);
        }

        float angle = spin.magnitude * dt;
        if (angle > 0f) transform.rotation = Quaternion.AngleAxis(angle, spin.normalized) * transform.rotation;

        bool burning = Time.time < burnEndsAt;
        if (!burning && isPersistent && vertexCache.Count > 0)
        {
            filters.Clear();
            vertexCache.Clear();
        }

        if (blasts && burning && !fading && Time.time >= nextBlastAt)
        {
            float age = Time.time - bornAt;
            nextBlastAt = Time.time + Random.Range(profile.wreckExplosionInterval.x, profile.wreckExplosionInterval.y) * (1f + age * 0.08f);
            Vector3 point = RandomPoint();
            DestructionFx.Explode(profile.smallExplosion, point, profile.smallExplosionScale * effectScale * Random.Range(0.6f, 1.1f), profile.explosionLifetime);
            DestructionFx.Blast(profile.smallBlastClips, point, profile, 1f);
        }

        if (isPersistent)
        {
            UpdateBurners();
            UpdateEmbers();
            if (Time.time >= nextJostleAt) Jostle();
        }
        else if (!fading && Time.time - bornAt >= lifetime - profile.wreckFadeDuration)
        {
            BeginFade();
        }

        if (fading)
        {
            float t = Mathf.Clamp01((Time.time - fadeStartedAt) / Mathf.Max(0.01f, profile.wreckFadeDuration));
            transform.localScale = baseScale * (1f - t * t);
            if (t >= 1f) Destroy(gameObject);
        }
    }

    private void UpdateBurners()
    {
        for (int i = burners.Count - 1; i >= 0; i--)
        {
            if (Time.time < burners[i].stopAt) continue;
            DestructionFx.StopEmitting(burners[i].effect);
            burners.RemoveAt(i);
        }
    }

    private void UpdateEmbers()
    {
        if (caps.Count == 0 || Time.time < nextEmberAt) return;
        nextEmberAt = Time.time + 0.5f;

        float remaining = Mathf.Clamp01((burnEndsAt - Time.time) / Mathf.Max(1f, profile.burnDuration.y));
        Color color = emberColor * (0.05f + remaining * 0.95f);

        foreach (Cap cap in caps)
        {
            if (cap.renderer == null) continue;
            cap.renderer.GetPropertyBlock(block, cap.index);
            block.SetColor(EmissionId, color);
            cap.renderer.SetPropertyBlock(block, cap.index);
        }

        if (remaining <= 0f) caps.Clear();
    }

    private void Jostle()
    {
        nextJostleAt = Time.time + 0.2f;
        if (fading) return;

        foreach (HullDamage ship in HullDamage.Active)
        {
            if (ship == null || !ship.IsAlive) continue;

            Vector3 offset = transform.position - ship.transform.position;
            float reach = radius + ship.Size * 0.35f;
            float distance = offset.magnitude;
            if (distance >= reach || distance < 0.01f) continue;

            float push = 1f - distance / reach;
            velocity += offset / distance * profile.jostleStrength * push * 0.2f;
            spin += Random.onUnitSphere * push * 4f;
        }
    }

    private Vector3 RandomPoint()
    {
        if (filters.Count == 0) return transform.position;

        int index = Random.Range(0, filters.Count);
        MeshFilter filter = filters[index];
        Vector3[] vertices = vertexCache[index];
        if (filter == null || vertices.Length == 0) return transform.position;

        return filter.transform.TransformPoint(vertices[Random.Range(0, vertices.Length)]);
    }

    private void OnDestroy()
    {
        persistent.Remove(this);
        foreach (Mesh mesh in ownedMeshes)
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
