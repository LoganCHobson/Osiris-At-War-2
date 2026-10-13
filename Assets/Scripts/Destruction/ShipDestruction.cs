using System.Collections.Generic;
using SolarStudios;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[RequireComponent(typeof(HullDamage))]
public class ShipDestruction : MonoBehaviour
{
    public UnityEvent onBreakup;

    private HullDamage hull;
    private UnitHealthManager health;
    private NavMeshAgent agent;
    private DestructionProfile profile;

    private bool dying;
    private bool brokenUp;
    private float dyingStartedAt;
    private float breakupAt;
    private float nextChainAt;
    private Vector3 coastVelocity;
    private float coastSpeed;
    private float sinkSpeed;
    private float sinkAcceleration;
    private float maxSink;
    private Vector3 angularVelocity;
    private Vector3 angularAcceleration;
    private float maxAngular;
    private float throeScale;
    private float shudder;
    private Vector3 gfxRestPosition;

    public enum Throe { NoseDive, TailDrop, List, Corkscrew, Slew }

    private static Throe lastStyle = Throe.Slew;

    public bool IsBreakingUp => dying && !brokenUp;
    public Throe Style { get; private set; }
    public int PieceCount { get; private set; }

    private Vector3 Velocity => coastVelocity + Vector3.down * sinkSpeed;

    private void Awake()
    {
        hull = GetComponent<HullDamage>();
        health = GetComponent<UnitHealthManager>();
        agent = GetComponent<NavMeshAgent>();
        health.destroyDelay = -1f;
        health.onDie.AddListener(BeginDying);
    }

    private void BeginDying()
    {
        if (dying) return;
        dying = true;

        profile = hull.profile;
        HideInterface();
        StopControl();

        if (profile == null)
        {
            if (hull.gfxRoot != null) hull.gfxRoot.gameObject.SetActive(false);
            Destroy(gameObject, 5f);
            brokenUp = true;
            return;
        }

        float sizeT = hull.SizeT;
        dyingStartedAt = Time.time;
        breakupAt = Time.time + Mathf.Lerp(profile.dyingDuration.x, profile.dyingDuration.y, sizeT) * Random.Range(0.85f, 1.15f);
        nextChainAt = Time.time;

        throeScale = profile.throeIntensity * Mathf.Lerp(1.4f, 0.55f, sizeT) * (agent != null ? 1f : 0.2f);
        Throe style = (Throe)Random.Range(0, 4);
        if (style >= lastStyle) style++;
        Style = style;
        lastStyle = style;
        angularAcceleration = ThroeAcceleration(Style);
        angularVelocity = Random.insideUnitSphere * 3f * throeScale;
        maxAngular = profile.throeMaxAngularSpeed * throeScale * Random.Range(0.7f, 1.2f);

        float sinkBias = Style == Throe.NoseDive ? 1.5f : Style == Throe.TailDrop ? 0.6f : 1f;
        sinkAcceleration = Random.Range(profile.sinkAcceleration.x, profile.sinkAcceleration.y) * sinkBias * (agent != null ? 1f : 0.25f);
        maxSink = profile.maxSinkSpeed * Random.Range(0.6f, 1.2f);

        if (hull.gfxRoot != null) gfxRestPosition = hull.gfxRoot.localPosition;
    }

    private Vector3 ThroeAcceleration(Throe style)
    {
        float a = profile.throeAngularAcceleration * throeScale;
        float side = RandomSign();
        float other = RandomSign();

        switch (style)
        {
            case Throe.NoseDive:
                return new Vector3(a * Random.Range(0.8f, 1.3f), a * 0.1f * other, a * 0.35f * side * Random.value);
            case Throe.TailDrop:
                return new Vector3(-a * Random.Range(0.6f, 1f), a * 0.1f * other, a * 0.3f * side * Random.value);
            case Throe.List:
                return new Vector3(a * 0.3f * other * Random.value, 0f, a * Random.Range(0.8f, 1.3f) * side);
            case Throe.Corkscrew:
                return new Vector3(a * 0.4f * other, a * 0.3f * other, a * 1.5f * side);
            default:
                return new Vector3(a * 0.25f * other, a * Random.Range(0.7f, 1.1f) * side, a * 0.5f * other);
        }
    }

    [ContextMenu("Destroy Now")]
    private void DestroyNow()
    {
        foreach (HardpointHealth hardpoint in GetComponentsInChildren<HardpointHealth>())
        {
            if (hardpoint.currentHealth > 0f) hardpoint.DealDamage(hardpoint.currentHealth + 1f);
        }
    }

    private void HideInterface()
    {
        if (health.healthSlider != null && health.healthSlider.transform.parent != null)
        {
            health.healthSlider.transform.parent.gameObject.SetActive(false);
        }

        if (TryGetComponent(out SpaceUnit unit))
        {
            unit.ToggleSelect(false);
        }
    }

    private void StopControl()
    {
        foreach (PlayerUnitStateMachine machine in GetComponentsInChildren<PlayerUnitStateMachine>(true))
        {
            machine.enabled = false;
        }

        if (agent == null || !agent.enabled) return;

        float driftFactor = profile != null ? profile.driftSpeedFactor : 0.5f;
        coastVelocity = Vector3.Lerp(agent.velocity, transform.forward * agent.speed, 0.3f) * driftFactor;
        coastVelocity.y = 0f;
        coastSpeed = coastVelocity.magnitude;

        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
        agent.velocity = Vector3.zero;
        agent.updatePosition = false;
        agent.updateRotation = false;
    }

    private void Update()
    {
        if (!dying || brokenUp) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float progress = Mathf.InverseLerp(dyingStartedAt, breakupAt, Time.time);

        angularVelocity += angularAcceleration * (0.5f + progress) * dt;
        if (angularVelocity.magnitude > maxAngular) angularVelocity = angularVelocity.normalized * maxAngular;
        transform.rotation = transform.rotation * Quaternion.Euler(angularVelocity * dt);

        coastSpeed *= Mathf.Max(0f, 1f - 0.1f * dt);
        coastVelocity = Vector3.Lerp(coastVelocity, transform.forward * coastSpeed, dt * 0.6f);
        sinkSpeed = Mathf.Min(maxSink, sinkSpeed + sinkAcceleration * dt);
        transform.position += Velocity * dt;

        if (Time.time >= nextChainAt) ChainExplosion(progress);
        UpdateShudder(dt);

        if (Time.time >= breakupAt) Breakup();
    }

    private void ChainExplosion(float progress)
    {
        nextChainAt = Time.time + Mathf.Lerp(profile.chainInterval.x, profile.chainInterval.y, progress) * Random.Range(0.7f, 1.3f);

        Vector3 point = hull.RandomSurfacePoint(out _);
        bool big = Random.value < progress * 0.6f;

        hull.Explode(big ? profile.mediumExplosion : profile.smallExplosion, point, big ? profile.mediumExplosionScale : profile.smallExplosionScale);
        DestructionFx.Blast(big ? profile.largeBlastClips : profile.smallBlastClips, point, profile, Mathf.Lerp(1.1f, 0.75f, hull.SizeT));

        if (Random.value < profile.chainFireChance && hull.DamageEffectCount < 24)
        {
            hull.SpawnDamageEffect(point);
        }

        if (big && Random.value < profile.joltChance)
        {
            angularVelocity += Random.onUnitSphere * profile.joltStrength * throeScale;
            angularAcceleration = Vector3.Lerp(angularAcceleration, ThroeAcceleration((Throe)Random.Range(0, 5)), 0.5f);
            coastVelocity += (transform.position - point).normalized * hull.Size * 0.04f;
            shudder = 1.6f;
        }

        shudder = Mathf.Max(shudder, big ? 1f : 0.5f);
        if (big) ImpactFlash.Pulse(point, 0.12f);
        CameraShake.Shake(point, profile.chainShake * (big ? 1.5f : 1f) * Mathf.Lerp(0.6f, 1.4f, hull.SizeT));
    }

    private void UpdateShudder(float dt)
    {
        if (hull.gfxRoot == null) return;

        shudder = Mathf.MoveTowards(shudder, 0f, dt * 3f);
        float amplitude = shudder * profile.shudderStrength * hull.Size / Mathf.Max(0.0001f, transform.lossyScale.x);
        hull.gfxRoot.localPosition = gfxRestPosition + Random.insideUnitSphere * amplitude;
    }

    private void Breakup()
    {
        brokenUp = true;
        hull.KillLights();
        if (hull.gfxRoot != null) hull.gfxRoot.localPosition = gfxRestPosition;

        float sizeT = hull.SizeT;
        float scale = hull.SizeScale;
        Vector3 center = transform.TransformPoint(hull.LocalBounds.center);

        int pieceCount = Mathf.Clamp(profile.PickPieceCount(sizeT), 1, 8);
        PieceCount = pieceCount;
        bool intact = pieceCount == 1;
        Vector3 inheritedSpin = transform.TransformDirection(angularVelocity) * 0.8f;
        Plane[] planes = BuildCutPlanes(pieceCount, out Vector3 axis, out Vector3[] cutPoints);

        Transform[] containers = new Transform[pieceCount];
        WreckChunk[] chunks = new WreckChunk[pieceCount];
        for (int k = 0; k < pieceCount; k++)
        {
            chunks[k] = NewChunk(name + " Wreck " + k, center);
            containers[k] = chunks[k].transform;
        }

        BreakHull(planes, containers, chunks, center, out MeshRenderer shardSource, out MeshSlicer.MeshData shardData);
        ThrowPlating(shardSource, shardData, center, intact ? 0.5f : 1f);
        MoveAttachments(planes, containers);
        OpenBreaches(planes, cutPoints, containers, scale);

        float blastScale = intact ? 0.6f : 1f;
        foreach (ParticleSystem blast in profile.breakupExplosions)
        {
            hull.Explode(blast, center, profile.breakupExplosionScale * blastScale);
        }
        if (intact)
        {
            for (int i = 0; i < 3; i++) hull.SpawnDamageEffect(hull.RandomSurfacePoint(out _));
            foreach (Transform effect in hull.EffectsRoot) effect.SetParent(containers[0], true);
        }
        DestructionFx.Flash(center, profile.flashColor, profile.flashIntensity * blastScale * Mathf.Lerp(0.6f, 1.5f, sizeT), hull.Size * profile.flashRange, profile.flashDuration * Mathf.Lerp(0.8f, 1.5f, sizeT));
        DestructionFx.Blast(profile.largeBlastClips, center, profile, Mathf.Lerp(1.1f, 0.7f, sizeT));
        CameraShake.Shake(center, profile.breakupShake * blastScale * Mathf.Lerp(0.4f, 1.2f, sizeT));
        ImpactFlash.Pulse(center, Mathf.Lerp(0.35f, 0.9f, sizeT) * blastScale);

        for (int k = 0; k < pieceCount; k++)
        {
            Repivot(containers[k]);

            Vector3 away = containers[k].position - center;
            if (away.sqrMagnitude < 0.0001f) away = axis * (k - (pieceCount - 1) * 0.5f);
            if (away.sqrMagnitude < 0.0001f) away = Random.onUnitSphere * 0.2f;
            away.Normalize();

            float separation = intact ? 0f : profile.separationSpeed * Random.Range(0.7f, 1.3f) * Mathf.Lerp(1.3f, 0.7f, sizeT);
            Vector3 linear = Velocity + away * separation + Random.insideUnitSphere * 0.3f;

            Vector3 hinge = intact ? Vector3.zero : Vector3.Cross(transform.up, away);
            Vector3 angular = inheritedSpin
                + hinge * profile.chunkSpin * Mathf.Lerp(1.5f, 0.5f, sizeT) * Random.Range(0.6f, 1.2f)
                + Random.insideUnitSphere * 2f;

            chunks[k].Launch(profile, linear, angular, scale, profile.persistentWrecks, profile.wreckLifetime * Random.Range(0.8f, 1.2f), true);
        }

        if (hull.gfxRoot != null) hull.gfxRoot.gameObject.SetActive(false);
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        onBreakup.Invoke();
        Destroy(gameObject);
    }

    private Plane[] BuildCutPlanes(int pieceCount, out Vector3 axisWorld, out Vector3[] cutPoints)
    {
        Bounds bounds = hull.LocalBounds;
        Vector3 size = bounds.size;
        int axisIndex = size.z >= size.x ? (size.z >= size.y ? 2 : 1) : (size.x >= size.y ? 0 : 1);
        Vector3 axis = axisIndex == 0 ? Vector3.right : axisIndex == 1 ? Vector3.up : Vector3.forward;

        Plane[] planes = new Plane[pieceCount - 1];
        cutPoints = new Vector3[pieceCount - 1];

        for (int i = 1; i < pieceCount; i++)
        {
            float t = (i + Random.Range(-0.25f, 0.25f)) / pieceCount;
            Vector3 pointLocal = bounds.center;
            pointLocal[axisIndex] = Mathf.Lerp(bounds.min[axisIndex], bounds.max[axisIndex], t);

            Vector3 tiltAxis = Vector3.Cross(axis, Random.onUnitSphere);
            if (tiltAxis.sqrMagnitude < 0.001f) tiltAxis = Vector3.Cross(axis, Vector3.up + Vector3.right);
            Vector3 normalLocal = Quaternion.AngleAxis(Random.Range(-profile.cutTilt, profile.cutTilt), tiltAxis.normalized) * axis;

            Vector3 point = transform.TransformPoint(pointLocal);
            planes[i - 1] = new Plane(transform.TransformDirection(normalLocal).normalized, point);
            cutPoints[i - 1] = point;
        }

        axisWorld = transform.TransformDirection(axis).normalized;
        return planes;
    }

    private void BreakHull(Plane[] planes, Transform[] containers, WreckChunk[] chunks, Vector3 center, out MeshRenderer shardSource, out MeshSlicer.MeshData shardData)
    {
        shardSource = null;
        shardData = null;
        float shardSize = 0f;

        float largest = 0f;
        foreach (MeshRenderer renderer in hull.LodZero)
        {
            if (!Usable(renderer)) continue;
            largest = Mathf.Max(largest, renderer.bounds.size.magnitude);
        }

        foreach (MeshRenderer renderer in hull.LodZero)
        {
            if (!Usable(renderer)) continue;

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            float extent = renderer.bounds.size.magnitude;
            bool large = extent >= largest * profile.largePartFraction;
            MeshSlicer.MeshData data = large ? MeshSlicer.MeshData.Read(filter.sharedMesh) : null;

            if (data != null)
            {
                SliceRenderer(renderer, filter, data, planes, containers, chunks);
                if (extent > shardSize)
                {
                    shardSize = extent;
                    shardSource = renderer;
                    shardData = data;
                }
            }
            else
            {
                DetachPart(renderer, filter, planes, containers, center, !large);
            }
        }
    }

    private static bool Usable(MeshRenderer renderer)
    {
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return false;
        if (HullDamage.IsLight(renderer)) return false;
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter != null && filter.sharedMesh != null;
    }

    private void SliceRenderer(MeshRenderer renderer, MeshFilter filter, MeshSlicer.MeshData data, Plane[] planes, Transform[] containers, WreckChunk[] chunks)
    {
        if (planes.Length == 0)
        {
            CopyRenderer(renderer, filter.sharedMesh, false, containers[0]);
            return;
        }

        Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
        Matrix4x4 toLocal = renderer.transform.worldToLocalMatrix;

        Plane[] localPlanes = new Plane[planes.Length];
        for (int i = 0; i < planes.Length; i++)
        {
            Vector3 normal = toWorld.transpose.MultiplyVector(planes[i].normal).normalized;
            Vector3 point = toLocal.MultiplyPoint3x4(planes[i].normal * -planes[i].distance);
            localPlanes[i] = new Plane(normal, point);
        }

        Vector3 localUp = toLocal.MultiplyVector(transform.up);
        MeshSlicer.Result[] results = MeshSlicer.Slice(data, localPlanes, true, filter.sharedMesh.name, localUp);
        for (int k = 0; k < results.Length; k++)
        {
            if (results[k].mesh == null) continue;
            MeshRenderer piece = CopyRenderer(renderer, results[k].mesh, results[k].capped, containers[k]);
            piece.name = renderer.name + " Wreck";
            chunks[k].Own(results[k].mesh);
            if (results[k].capped) chunks[k].RegisterCap(piece, results[k].mesh.subMeshCount - 1);
        }
    }

    private void DetachPart(MeshRenderer renderer, MeshFilter filter, Plane[] planes, Transform[] containers, Vector3 center, bool canFlyLoose)
    {
        Vector3 partCenter = renderer.bounds.center;

        if (!canFlyLoose || Random.value >= profile.looseDebrisChance)
        {
            CopyRenderer(renderer, filter.sharedMesh, false, containers[PieceIndex(partCenter, planes)]);
            return;
        }

        WreckChunk debris = NewChunk(renderer.name + " Debris", partCenter);
        CopyRenderer(renderer, filter.sharedMesh, false, debris.transform);

        if (Random.value < 0.5f)
        {
            ParticleSystem fire = RandomFire();
            DestructionFx.Attach(fire, debris.transform, partCenter, profile.fireScale * hull.SizeScale * 0.5f);
        }

        Vector3 direction = (partCenter - center).normalized + Random.insideUnitSphere * 0.5f;
        if (direction.sqrMagnitude < 0.001f) direction = Random.onUnitSphere;

        debris.Launch(
            profile,
            Velocity + direction.normalized * profile.debrisSpeed * Random.Range(0.5f, 1.2f) * Mathf.Lerp(1f, 0.6f, hull.SizeT),
            Random.onUnitSphere * Random.Range(profile.debrisSpin.x, profile.debrisSpin.y) * 0.6f,
            hull.SizeScale * 0.5f,
            false,
            Random.Range(profile.debrisLifetime.x, profile.debrisLifetime.y),
            false);
    }

    private void ThrowPlating(MeshRenderer source, MeshSlicer.MeshData data, Vector3 center, float amount)
    {
        if (source == null || data == null) return;

        MeshFilter filter = source.GetComponent<MeshFilter>();
        int count = Mathf.RoundToInt(Mathf.Lerp(profile.plateShards.x, profile.plateShards.y, hull.SizeT) * amount);
        float localScale = Mathf.Max(0.0001f, source.transform.lossyScale.x);

        for (int i = 0; i < count; i++)
        {
            if (!HullSurface.SampleLocal(filter, out Vector3 local, out Vector3 localNormal)) continue;

            float radius = hull.Size * Random.Range(profile.plateShardRadius.x, profile.plateShardRadius.y) / localScale;
            Mesh mesh = MeshSlicer.ExtractPatch(data, local, radius, "Plating", out Vector3 patchCenter);
            if (mesh == null) continue;

            Vector3 worldCenter = source.transform.TransformPoint(patchCenter);
            Vector3 worldNormal = source.transform.TransformDirection(localNormal).normalized;

            WreckChunk shard = NewChunk("Plating", worldCenter);
            CopyRenderer(source, mesh, false, shard.transform);
            shard.Own(mesh);

            if (Random.value < 0.4f)
            {
                DestructionFx.Attach(RandomFire(), shard.transform, worldCenter, profile.fireScale * hull.SizeScale * 0.4f);
            }

            Vector3 outward = worldCenter - center;
            Vector3 direction = (worldNormal + outward.normalized * 0.5f + Random.insideUnitSphere * 0.4f).normalized;

            shard.Launch(
                profile,
                Velocity + direction * profile.debrisSpeed * Random.Range(0.8f, 1.6f),
                Random.onUnitSphere * Random.Range(profile.debrisSpin.x, profile.debrisSpin.y),
                hull.SizeScale * 0.4f,
                false,
                Random.Range(profile.debrisLifetime.x, profile.debrisLifetime.y),
                false);
        }
    }

    private void MoveAttachments(Plane[] planes, Transform[] containers)
    {
        List<Transform> candidates = new List<Transform>();

        foreach (HardpointHealth hardpoint in GetComponentsInChildren<HardpointHealth>(true))
        {
            candidates.Add(hardpoint.transform);
        }
        foreach (Transform effect in hull.EffectsRoot)
        {
            candidates.Add(effect);
        }
        foreach (ParticleSystem system in GetComponentsInChildren<ParticleSystem>(true))
        {
            candidates.Add(TopmostParticle(system.transform));
        }
        foreach (AudioSource source in GetComponentsInChildren<AudioSource>(true))
        {
            candidates.Add(source.transform);
        }

        HashSet<Transform> set = new HashSet<Transform>(candidates);
        HashSet<Transform> moved = new HashSet<Transform>();

        foreach (Transform candidate in candidates)
        {
            if (candidate == null || candidate == transform || moved.Contains(candidate)) continue;
            if (hull.gfxRoot != null && candidate.IsChildOf(hull.gfxRoot)) continue;
            if (HasAncestorIn(candidate, set)) continue;
            if (candidate.GetComponentInParent<Canvas>(true) != null) continue;

            moved.Add(candidate);
            candidate.SetParent(containers[PieceIndex(candidate.position, planes)], true);
            Sanitize(candidate);
        }
    }

    private void OpenBreaches(Plane[] planes, Vector3[] cutPoints, Transform[] containers, float scale)
    {
        float offset = hull.Size * 0.02f;

        for (int i = 0; i < planes.Length; i++)
        {
            Vector3 normal = planes[i].normal;
            Vector3 backSide = cutPoints[i] + normal * offset;
            Vector3 frontSide = cutPoints[i] - normal * offset;

            DestructionFx.Attach(profile.breachVent, containers[i], backSide, profile.breachVentScale * scale);
            DestructionFx.Attach(profile.breachVent, containers[i + 1], frontSide, profile.breachVentScale * scale);

            hull.Explode(profile.mediumExplosion, cutPoints[i], profile.mediumExplosionScale * 1.5f);
        }
    }

    private Transform TopmostParticle(Transform current)
    {
        while (current.parent != null && current.parent != transform && current.parent.GetComponent<ParticleSystem>() != null)
        {
            current = current.parent;
        }
        return current;
    }

    private bool HasAncestorIn(Transform candidate, HashSet<Transform> set)
    {
        Transform parent = candidate.parent;
        while (parent != null && parent != transform)
        {
            if (set.Contains(parent)) return true;
            parent = parent.parent;
        }
        return false;
    }

    private static void Sanitize(Transform root)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.transform == root) canvas.enabled = false;
            else canvas.gameObject.SetActive(false);
        }

        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour.GetType().Name.StartsWith("CFX_")) continue;
            behaviour.enabled = false;
        }
    }

    private static int PieceIndex(Vector3 point, Plane[] planes)
    {
        for (int i = 0; i < planes.Length; i++)
        {
            if (planes[i].GetDistanceToPoint(point) < 0f) return i;
        }
        return planes.Length;
    }

    private WreckChunk NewChunk(string chunkName, Vector3 position)
    {
        GameObject chunk = new GameObject(chunkName);
        chunk.transform.SetPositionAndRotation(position, transform.rotation);
        return chunk.AddComponent<WreckChunk>();
    }

    private MeshRenderer CopyRenderer(MeshRenderer source, Mesh mesh, bool capped, Transform parent)
    {
        GameObject copy = new GameObject(source.name);
        copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        copy.transform.localScale = source.transform.lossyScale;
        copy.transform.SetParent(parent, true);

        copy.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = copy.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = MaterialsFor(source, mesh, capped);
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;
        hull.CopyScorch(source, renderer);
        return renderer;
    }

    private Material[] MaterialsFor(Renderer source, Mesh mesh, bool capped)
    {
        Material[] original = source.sharedMaterials;
        Material[] result = new Material[Mathf.Max(1, mesh.subMeshCount)];
        int regular = capped ? result.Length - 1 : result.Length;

        for (int i = 0; i < regular; i++)
        {
            result[i] = original.Length > 0 ? original[Mathf.Min(i, original.Length - 1)] : null;
        }

        if (capped)
        {
            result[result.Length - 1] = profile.interiorMaterial != null ? profile.interiorMaterial : (original.Length > 0 ? original[0] : null);
        }

        return result;
    }

    private static void Repivot(Transform container)
    {
        Renderer[] renderers = container.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        Vector3 offset = bounds.center - container.position;
        container.position = bounds.center;
        foreach (Transform child in container)
        {
            child.position -= offset;
        }
    }

    private ParticleSystem RandomFire()
    {
        if (profile.hullFires == null || profile.hullFires.Length == 0) return null;
        return profile.hullFires[Random.Range(0, profile.hullFires.Length)];
    }

    private static float RandomSign()
    {
        return Random.value < 0.5f ? -1f : 1f;
    }
}
