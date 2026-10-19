using SolarStudios;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using static SpaceUnit;

public class TurretController : MonoBehaviour
{
    public UnityEvent onFire;

    [Header("Gun parts")]
    public Transform turretBase;  // The base of the turret (azimuth rotation)
    public Transform turretBarrel;  // The barrel of the turret (elevation rotation)
    public List<Transform> firingPoints = new List<Transform>();

    [Header("Traverse settings")]
    public float rotationSpeed = 10f;  // Speed for azimuth rotation
    public float elevationSpeed = 10f;  // Speed for elevation rotation
    public float maxElevationAngle = 30f;  // Max elevation angle
    public float minElevationAngle = -5f;  // Min elevation angle

    [Header("Firing settings")]
    public float range = 50;
    public float baseAccuracy = 0.02f;
    public float rangeFallOffValue = 0.1f;
    public LayerMask targetLayer;
    public Transform target;
    public float fireRate = 1f;
    private float lastFiredTime;
    public GameObject projectilePrefab;
    public float projectileSpeed = 20f;
    public float damage;
    private float timeOutOfLOS = 0f;
    public float maxTimeWithoutLOS = 3f;

    public enum TargetFilter { Any, CapitalShipsOnly, FightersOnly }

    [Header("Role")]
    public TargetFilter targetFilter = TargetFilter.Any;
    public float fighterAccuracyMultiplier = 3f;
    public float fighterDamageMultiplier = 1f;
    public bool preferCapitalShips = true;
    public bool leadFighters;
    public float shotLifetimeRangeMultiplier = 1.5f;

    [Header("CIWS")]
    [Tooltip("Rapid-fire gatling point defence: sprays tracer bursts instead of single bolts.")]
    public bool gatling;
    public float gatlingRoundsPerSecond = 50f;
    public float gatlingBurst = 1.4f;
    public float gatlingCooldown = 0.6f;
    public float gatlingSpread = 0.07f;
    [Tooltip("Spread when firing at fighters, which jink and are harder to track than missiles.")]
    public float gatlingFighterSpread = 0.08f;
    public float gatlingMuzzleSpeed = 170f;
    public float gatlingRoundDamage;

    [Header("Point-defence beam")]
    [Tooltip("Continuous point-defence beam that sweeps slowly onto its target.")]
    public bool beam;
    [Tooltip("Degrees per second the beam can swing - slow to come to bear.")]
    public float beamSlewRate = 40f;
    [Tooltip("Point-defence damage per second while the beam is on target (set by MakeBeam).")]
    public float beamDps;
    public float beamWidth = 0.06f;
    public Color beamColor = new Color(0.55f, 0.75f, 1f);
    public float beamIntensity = 7f;

    [Header("Performance")]
    public float targetSearchInterval = 0.25f;
    public float lineOfSightInterval = 0.2f;

    private static readonly Collider[] overlapBuffer = new Collider[128];

    private float nextSearchAt;
    private float nextMissileScanAt;
    private float nextLineOfSightAt;
    private bool hasLineOfSight;
    private Transform lineOfSightTarget;
    private Transform typedTarget;
    private ShipType targetType;
    private float gatlingBurstEndsAt;
    private float gatlingRestUntil;
    private float gatlingLastAt = float.NegativeInfinity;
    private float gatlingOwed;
    private float gatlingNextFlashAt;
    private int gatlingBarrel;
    private LineRenderer beamLine;
    private Vector3 beamDirection;
    private float beamOwed;
    private float beamTickAt;
    private float beamPower;
    private MaterialPropertyBlock beamBlock;
    private static Material beamMaterial;
    private static Texture2D beamTexture;

    void Awake()
    {
        AlignMuzzleFlashes();
    }

    void AlignMuzzleFlashes()
    {
        int assigned = 0;
        for (int i = 0; i < onFire.GetPersistentEventCount(); i++)
        {
            if (!(onFire.GetPersistentTarget(i) is ParticleSystem flash) || !flash.name.Contains("MF")) continue;

            Transform point = assigned < firingPoints.Count ? firingPoints[assigned] : null;
            if (point != null)
            {
                flash.transform.SetParent(point, true);
                flash.transform.localPosition = Vector3.zero;
                assigned++;
            }
            else
            {
                onFire.SetPersistentListenerState(i, UnityEventCallState.Off);
                flash.gameObject.SetActive(false);
            }
        }
    }

    void Update()
    {
        ValidateTarget();

        if (targetFilter == TargetFilter.FightersOnly && (target == null || targetMissile == null) && Time.time >= nextMissileScanAt)
        {
            nextMissileScanAt = Time.time + targetSearchInterval * Random.Range(0.8f, 1.2f);
            Missile incoming = Missile.NearestHostile(transform.position, range * Missile.InterceptRangeFraction, targetLayer, this); // Missiles before fighters.
            if (incoming != null) target = incoming.transform;
            ValidateTarget();
        }

        if (target == null && Time.time >= nextSearchAt)
        {
            nextSearchAt = Time.time + targetSearchInterval * Random.Range(0.8f, 1.2f);
            AcquireTarget();
        }

        if (target != null)
        {


            Traverse();

            if (target != lineOfSightTarget || Time.time >= nextLineOfSightAt)
            {
                lineOfSightTarget = target;
                nextLineOfSightAt = Time.time + lineOfSightInterval * Random.Range(0.8f, 1.2f);
                hasLineOfSight = targetMissile != null ? HasClearShot(firingPoints[0]) : HasLineOfSight(firingPoints[0]);  //I would use one of the middle guns but I don't want to adjust this for single use guns. Wont matter much anyway.
            }

            if (hasLineOfSight)
            {
                timeOutOfLOS = 0f;
                Shoot();

            }
            else
            {
                timeOutOfLOS += Time.deltaTime;

                if (timeOutOfLOS >= maxTimeWithoutLOS)
                {
                    target = null;
                    timeOutOfLOS = 0f;
                }
            }
        }

        if (beam)
        {
            UpdateBeam(target != null && hasLineOfSight && !GameManager.CombatOver);
        }
    }


    private Transform validatedTarget;
    private UnitHealthManager targetShip;
    private HardpointManager targetShipHardpoints;
    private HardpointHealth targetHardpoint;
    private Fighter targetFighter;
    private Missile targetMissile;

    public bool Accepts(Transform candidate)
    {
        if (candidate == null) return false;
        if (targetFilter == TargetFilter.Any) return true;

        UnitHealthManager ship = candidate.GetComponentInParent<UnitHealthManager>();
        return ship != null && AcceptsShip(ship.gameObject);
    }

    private bool AcceptsShip(GameObject ship)
    {
        if (targetFilter == TargetFilter.Any) return true;

        bool isSquadron = ship.TryGetComponent(out Squadron _);
        return targetFilter == TargetFilter.FightersOnly ? isSquadron : !isSquadron;
    }

    void ValidateTarget()
    {
        if (target == null) return;

        if (target != validatedTarget)
        {
            validatedTarget = target;
            targetShip = target.GetComponentInParent<UnitHealthManager>();
            targetShipHardpoints = targetShip != null ? targetShip.GetComponent<HardpointManager>() : null;
            targetHardpoint = target.GetComponentInParent<HardpointHealth>();
            targetFighter = target.GetComponent<Fighter>();
            targetMissile = target.GetComponent<Missile>();
        }

        if (targetMissile != null)
        {
            if (!targetMissile.InFlight || !targetMissile.IsHostileTo(targetLayer) || !targetMissile.Engage(this)) target = null;
            return;
        }

        if ((targetShip != null && targetShip.IsDead) || (targetShip != null && !AcceptsShip(targetShip.gameObject)))
        {
            target = null;
            return;
        }

        bool hardpointGone = targetHardpoint != null && targetShipHardpoints != null && !targetShipHardpoints.hardpoints.Contains(targetHardpoint);
        if (target.gameObject.activeInHierarchy && !hardpointGone) return;

        target = targetShipHardpoints != null ? targetShipHardpoints.GetRandomHardpoint() : null;
    }

    void AcquireTarget()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, range, overlapBuffer, targetLayer);

        float closestShipSqr = Mathf.Infinity;
        float closestSquadronSqr = Mathf.Infinity;
        HardpointManager closestShip = null;
        HardpointManager closestSquadron = null;

        for (int i = 0; i < count; i++)
        {
            Transform potentialTarget = overlapBuffer[i].transform;
            if (!potentialTarget.root.TryGetComponent(out HardpointManager manager) || !AcceptsShip(manager.gameObject)) continue;

            float distanceSqr = (potentialTarget.position - transform.position).sqrMagnitude;
            if (manager.TryGetComponent(out Squadron _))
            {
                if (distanceSqr < closestSquadronSqr)
                {
                    closestSquadronSqr = distanceSqr;
                    closestSquadron = manager;
                }
            }
            else if (distanceSqr < closestShipSqr)
            {
                closestShipSqr = distanceSqr;
                closestShip = manager;
            }
        }

        HardpointManager chosen;
        if (preferCapitalShips && targetFilter == TargetFilter.Any && closestShip != null) chosen = closestShip;
        else chosen = closestShipSqr <= closestSquadronSqr ? closestShip : closestSquadron;

        target = chosen != null ? chosen.GetRandomHardpoint() : null;
    }
    private bool restCaptured;
    private Quaternion baseRest;
    private Quaternion barrelRest;

    void Traverse()
    {
        if (!restCaptured)
        {
            baseRest = turretBase.localRotation;
            barrelRest = turretBarrel.localRotation;
            restCaptured = true;
        }

        Transform mount = turretBase.parent;
        Quaternion mountFrame = (mount != null ? mount.rotation : Quaternion.identity) * baseRest;
        Vector3 targetDirection = Quaternion.Inverse(mountFrame) * (target.position - turretBase.position);


        Vector3 targetDirectionFlat = new Vector3(targetDirection.x, 0, targetDirection.z);
        if (targetDirectionFlat.sqrMagnitude > 0.01f)
        {
            Quaternion targetAzimuthRotation = baseRest * Quaternion.LookRotation(targetDirectionFlat);
            turretBase.localRotation = Quaternion.Slerp(turretBase.localRotation, targetAzimuthRotation, rotationSpeed * Time.deltaTime);
        }


        float targetElevationAngle = Mathf.Atan2(targetDirection.y, targetDirectionFlat.magnitude) * Mathf.Rad2Deg;
        targetElevationAngle = -targetElevationAngle;
        targetElevationAngle = Mathf.Clamp(targetElevationAngle, minElevationAngle, maxElevationAngle);


        Quaternion targetElevationRotation = barrelRest * Quaternion.Euler(targetElevationAngle, 0, 0);
        turretBarrel.localRotation = Quaternion.Slerp(turretBarrel.localRotation, targetElevationRotation, elevationSpeed * Time.deltaTime);
    }

    void Shoot()
    {
        if (GameManager.CombatOver) return;

        if (gatling)
        {
            FireGatling();
            return;
        }
        if (beam) return; // Driven by UpdateBeam.

        if (Time.time - lastFiredTime >= fireRate)
        {
            onFire.Invoke();
            lastFiredTime = Time.time;

            foreach (Transform firingPoint in firingPoints)
            {
                FireProjectile(firingPoint);
            }
        }
    }

    private static readonly RaycastHit[] losBuffer = new RaycastHit[16];

    bool HasLineOfSight(Transform firingPoint)
    {
        Vector3 direction = (target.position - firingPoint.position).normalized;
        int count = Physics.RaycastNonAlloc(firingPoint.position, direction, losBuffer, range);
        System.Array.Sort(losBuffer, 0, count, LosDistance.Instance);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = losBuffer[i];
            if (hit.transform.root == transform.root && hit.collider.GetComponentInParent<HardpointHealth>() != null) continue;
            if (hit.collider.gameObject.layer == ShieldGenerator.Layer)
            {
                ShieldGenerator shield = hit.collider.GetComponentInParent<ShieldGenerator>();
                if (shield != null && ((1 << shield.gameObject.layer) & targetLayer) != 0) return true;
                continue;
            }
            return hit.transform == target || ((1 << hit.transform.gameObject.layer) & targetLayer) != 0;
        }
        return false;
    }

    // Missiles have no collider, so just check nothing solid is in the way.
    bool HasClearShot(Transform firingPoint)
    {
        return !Physics.Linecast(firingPoint.position, target.position, WreckChunk.BlockingMask | BattleTerrain.BlockingMask, QueryTriggerInteraction.Ignore);
    }

    private class LosDistance : IComparer<RaycastHit>
    {
        public static readonly LosDistance Instance = new LosDistance();
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }
    void FireProjectile(Transform firingPoint)
    {
        if (target == null) return;

        Vector3 aimPoint = target.position;
        if (leadFighters && (targetFighter != null || targetMissile != null) && target == validatedTarget)
        {
            float travel = Vector3.Distance(firingPoint.position, aimPoint) / Mathf.Max(1f, projectileSpeed);
            aimPoint += (targetMissile != null ? targetMissile.Velocity : targetFighter.Velocity) * travel;
        }

        Vector3 direction = (aimPoint - firingPoint.position).normalized;
        float accuracyMultiplier = GetAccuracyMultiplier(target) * BattleTerrain.AccuracyMultiplier(firingPoint.position); // Asteroids spoil aim.
        Vector3 inaccuracyOffset = Random.insideUnitSphere * accuracyMultiplier;
        Vector3 finalDirection = (direction + inaccuracyOffset).normalized;

        ObjectPool pool = ObjectPool.GetPoolFor(projectilePrefab);
        GameObject temp = pool != null
            ? pool.Spawn(firingPoint.position, firingPoint.parent.parent.rotation)
            : Instantiate(projectilePrefab, firingPoint.position, firingPoint.parent.parent.rotation);

        if (temp == null) return;

        Laser laser = temp.GetComponent<Laser>();
        laser.damage = targetFighter != null && target == validatedTarget ? damage * fighterDamageMultiplier : damage;
        laser.layer = targetLayer;
        laser.intercept = target == validatedTarget ? targetMissile : null;
        laser.fromWarship = leadFighters; // Light, fighter-tracking guns; heavy batteries just chip them.
        laser.timeOut = Mathf.Max(0.5f, range * shotLifetimeRangeMultiplier / Mathf.Max(1f, projectileSpeed));
        laser.SetSourcePool(pool);
        laser.Launch(temp.transform.forward * laser.speed + finalDirection * projectileSpeed);
    }

    // Turns a point-defence turret into a CIWS gatling with the same damage output, spread over ~50 rounds a second.
    public void MakeGatling()
    {
        if (gatling) return;
        float bolts = firingPoints.Count * damage / Mathf.Max(0.02f, fireRate);
        float dutyCycle = gatlingBurst / (gatlingBurst + gatlingCooldown);
        gatlingRoundDamage = bolts / (gatlingRoundsPerSecond * dutyCycle);
        gatling = true;
    }

    // Turns a point-defence turret into a continuous beam that has to sweep onto its target.
    public void MakeBeam()
    {
        if (beam || gatling) return;
        beam = true;
        beamDps = firingPoints.Count * damage / Mathf.Max(0.02f, fireRate);
        beamDirection = firingPoints.Count > 0 ? firingPoints[0].forward : transform.forward;
    }

    // The beam swings toward the target at beamSlewRate; while it lies across the target it burns it for
    // beamDps. It fades in and out rather than snapping on.
    void UpdateBeam(bool firing)
    {
        if (firingPoints.Count == 0) return;
        Transform muzzle = firingPoints[0];
        float reach = range * Missile.InterceptRangeFraction;

        if (firing)
        {
            Vector3 toTarget = target.position - muzzle.position;
            if (toTarget.sqrMagnitude > 0.01f)
            {
                beamDirection = Vector3.RotateTowards(beamDirection, toTarget.normalized, beamSlewRate * Mathf.Deg2Rad * Time.deltaTime, 0f);
            }
            reach = Mathf.Min(reach, toTarget.magnitude + 6f);

            float radius = targetMissile != null ? targetMissile.HitRadius : 1.4f;
            Vector3 along = Vector3.Project(toTarget, beamDirection);
            bool onTarget = Vector3.Dot(along, beamDirection) > 0f && (toTarget - along).sqrMagnitude <= radius * radius;
            if (onTarget && target == validatedTarget) beamOwed += beamDps * Time.deltaTime;

            if (beamOwed > 0f && Time.time >= beamTickAt) // Applied in ticks so health bars aren't updated every frame.
            {
                beamTickAt = Time.time + 0.1f;
                if (targetMissile != null) targetMissile.TakeHit(beamOwed, false);
                else if (targetHardpoint != null) targetHardpoint.DealDamage(beamOwed);
                beamOwed = 0f;
            }
        }
        else
        {
            beamOwed = 0f;
        }

        beamPower = Mathf.MoveTowards(beamPower, firing ? 1f : 0f, Time.deltaTime * 4f);
        if (beamPower <= 0f)
        {
            if (beamLine != null) beamLine.enabled = false;
            return;
        }

        if (beamLine == null && !CreateBeamLine()) return;
        beamLine.enabled = true;
        beamLine.SetPosition(0, muzzle.position);
        beamLine.SetPosition(1, muzzle.position + beamDirection * reach);
        beamLine.widthMultiplier = beamWidth * (0.85f + 0.15f * Mathf.Sin(Time.time * 60f + GetInstanceID()));

        beamLine.GetPropertyBlock(beamBlock);
        beamBlock.SetTexture("_MainTex", beamTexture);
        beamBlock.SetColor("_Color", beamColor);
        beamBlock.SetFloat("_Intensity", beamIntensity * beamPower);
        beamLine.SetPropertyBlock(beamBlock);
    }

    bool CreateBeamLine()
    {
        if (beamMaterial == null)
        {
            beamMaterial = Resources.Load<Material>("FTL/FTLGlow");
            if (beamMaterial == null) return false;

            beamTexture = new Texture2D(4, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "PDBeam" };
            Color32[] pixels = new Color32[4 * 32];
            for (int y = 0; y < 32; y++)
            {
                float across = Mathf.Abs((y + 0.5f) / 32f * 2f - 1f);
                byte alpha = (byte)(Mathf.Pow(Mathf.Clamp01(1f - across), 1.5f) * 255f);
                for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32(255, 255, 255, alpha);
            }
            beamTexture.SetPixels32(pixels);
            beamTexture.Apply(false, true);
        }

        beamBlock = new MaterialPropertyBlock();
        GameObject line = new GameObject("PDBeam") { layer = 2 };
        line.transform.SetParent(transform, false);
        beamLine = line.AddComponent<LineRenderer>();
        beamLine.useWorldSpace = true;
        beamLine.positionCount = 2;
        beamLine.alignment = LineAlignment.View;
        beamLine.textureMode = LineTextureMode.Stretch;
        beamLine.numCapVertices = 2;
        beamLine.sharedMaterial = beamMaterial;
        beamLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beamLine.receiveShadows = false;
        Gradient fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.85f), new GradientAlphaKey(0f, 1f) });
        beamLine.colorGradient = fade;
        return true;
    }

    // Bursts of rounds, a short rest to cool the barrels, then again while there's something to shoot.
    void FireGatling()
    {
        float now = Time.time;
        if (now < gatlingRestUntil || firingPoints.Count == 0) return;

        if (now > gatlingLastAt + 0.25f)
        {
            gatlingBurstEndsAt = now + gatlingBurst;
            gatlingOwed = 0f;
        }
        gatlingLastAt = now;

        if (now >= gatlingBurstEndsAt)
        {
            gatlingRestUntil = now + gatlingCooldown;
            return;
        }

        if (now >= gatlingNextFlashAt)
        {
            onFire.Invoke();
            gatlingNextFlashAt = now + 0.07f;
        }

        gatlingOwed += gatlingRoundsPerSecond * Time.deltaTime;
        while (gatlingOwed >= 1f)
        {
            gatlingOwed -= 1f;
            FireRound(firingPoints[gatlingBarrel++ % firingPoints.Count]);
        }
    }

    void FireRound(Transform firingPoint)
    {
        if (target == null) return;

        Vector3 aimPoint = target.position;
        bool tracked = target == validatedTarget;
        if (tracked && (targetFighter != null || targetMissile != null))
        {
            float travel = Vector3.Distance(firingPoint.position, aimPoint) / gatlingMuzzleSpeed;
            aimPoint += (targetMissile != null ? targetMissile.Velocity : targetFighter.Velocity) * travel;
        }

        Vector3 direction = (aimPoint - firingPoint.position).normalized;
        float spread = targetFighter != null ? gatlingFighterSpread : gatlingSpread;
        direction = (direction + Random.insideUnitSphere * spread * BattleTerrain.AccuracyMultiplier(firingPoint.position)).normalized;
        float life = range * 1.2f / gatlingMuzzleSpeed;
        HardpointHealth fighter = tracked && targetFighter != null ? targetHardpoint : null;
        CIWSTracers.Fire(firingPoint.position, direction * gatlingMuzzleSpeed, life, gatlingRoundDamage, tracked ? targetMissile : null, fighter);
    }

    float GetAccuracyMultiplier(Transform target)
    {

        float distance = Vector3.Distance(transform.position, target.position);

        float rangeFalloff = distance / range * rangeFallOffValue;

        if (target != typedTarget)
        {
            typedTarget = target;
            SpaceUnit unit = target.GetComponentInParent<SpaceUnit>();
            targetType = unit != null ? unit.shipType : target.GetComponent<Missile>() != null ? ShipType.Fighter : ShipType.Cruiser;
        }

        switch (targetType)
        {
            case ShipType.Station:
            case ShipType.Battleship:
                return baseAccuracy * 0.5f; 
            case ShipType.Carrier:
            case ShipType.Cruiser:
                return baseAccuracy;
            case ShipType.Destroyer:
            case ShipType.Corvette:
                return baseAccuracy * 1.5f; 
            case ShipType.Fighter:
                return baseAccuracy * fighterAccuracyMultiplier + rangeFalloff * Mathf.Min(1f, fighterAccuracyMultiplier / 3f);
            default:
                return baseAccuracy;
        }
    }

    public void AssignTurretTarget(Transform _target)
    {
        target = _target;
    }
}
