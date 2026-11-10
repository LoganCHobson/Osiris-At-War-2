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
    public float rotationSpeed = 10f;  // Unused since traverse became a fixed rate (see TraverseRate); kept for prefabs.
    public float elevationSpeed = 10f;  // Unused, as above.
    [Tooltip("Degrees per second the turret swings. 0 = by mount: CIWS fast, then corvette, destroyer, cruiser, battleship slowest.")]
    public float traverseDegreesPerSecond = 0f;
    public float maxElevationAngle = 30f;  // Max elevation angle
    public float minElevationAngle = -5f;  // Min elevation angle

    [Header("Firing settings")]
    public float range = 50;

    // Range after hero buffs (see HeroForces). Use this, not `range`, for anything to do with reach.
    public float Range => range * (1f + Hero(HeroBuff.Range)) * (Ability != null ? Ability.RangeMultiplier : 1f);
    private float HeroSpread => (1f - Hero(HeroBuff.Accuracy)) * (Ability != null ? Ability.SpreadMultiplier : 1f);
    private float HeroDamage => (1f + Hero(HeroBuff.Damage)) * (Ability != null ? Ability.DamageMultiplier : 1f);
    private UnitHealthManager owner;
    private ShipAbility ability;
    private bool abilityLooked;

    private ShipAbility Ability
    {
        get
        {
            if (!abilityLooked) { ability = ShipAbility.Of(this); abilityLooked = true; }
            return ability;
        }
    }

    private float Hero(HeroBuff buff)
    {
        if (owner == null) owner = GetComponentInParent<UnitHealthManager>();
        return owner != null ? HeroForces.Buff(owner.isAttackerSide, buff) : 0f;
    }
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
    private float typedRangeFalloff;
    private bool ordered;          // Ordered by the player or AI: stick to it until it's destroyed.
    private bool orderedWholeShip; // Ordered onto a ship (not one hardpoint): work through its hardpoints.
    private float gatlingBurstEndsAt;
    private float gatlingRestUntil;
    private float gatlingLastAt = float.NegativeInfinity;
    private float gatlingOwed;
    private float gatlingNextFlashAt;
    private int gatlingBarrel;
    private bool? avantiGuns;
    private LineRenderer beamLine;
    private Vector3 beamDirection;
    private float beamOwed;
    private float beamTickAt;
    private float beamPower;
    private MaterialPropertyBlock beamBlock;
    private static Material beamMaterial;
    private static Texture2D beamTexture;

    // Leading moving ships: the target's velocity, measured from its motion, and where the gun should point.
    // The barrels swing onto that lead point at their own traverse rate, so a slow gun can't keep up with a
    // fast crossing target (and won't fire until it does). Shots still miss by the gun's accuracy, widened
    // the faster the target moves, and the lead itself is only ever an estimate.
    private Transform velocityTarget;
    private Vector3 velocityLastPos;
    private Vector3 targetVelocity;
    private const float MovingSpreadPerSpeed = 1f / 10f; // +100% spread at 10 units/second.

    private void TrackTargetVelocity()
    {
        if (target == null || targetFighter != null || targetMissile != null) { velocityTarget = null; targetVelocity = Vector3.zero; return; }
        Transform hull = targetShip != null ? targetShip.transform : target;
        if (hull != velocityTarget)
        {
            velocityTarget = hull;
            velocityLastPos = hull.position;
            targetVelocity = Vector3.zero;
            return;
        }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 measured = (hull.position - velocityLastPos) / dt;
        velocityLastPos = hull.position;
        targetVelocity = Vector3.Lerp(targetVelocity, measured, 1f - Mathf.Exp(-4f * dt));
    }

    // Where to point to hit a ship that's moving: its position plus how far it'll go while the shot flies.
    private Vector3 LeadPoint(Vector3 from)
    {
        if (target == null) return from;
        if (targetFighter != null || targetMissile != null) return target.position;
        float shotSpeed = Mathf.Max(1f, projectileSpeed);
        Vector3 centre = AimCentre();
        float travel = Vector3.Distance(from, centre) / shotSpeed;
        return centre + targetVelocity * travel;
    }

    private float outOfArcFor;
    private Transform shelvedOrder;
    private bool shelvedWholeShip;
    private float nextShelvedCheckAt;

    // Whether the barrels can be brought to bear on this point: within the turret's elevation limits as
    // mounted (bearing all round is fine - the turret turns).
    public bool CanBear(Vector3 point)
    {
        if (turretBase == null) return true;
        Transform mount = turretBase.parent;
        Quaternion rest = restCaptured ? baseRest : turretBase.localRotation;
        Quaternion frame = (mount != null ? mount.rotation : Quaternion.identity) * rest;
        Vector3 local = Quaternion.Inverse(frame) * (point - turretBase.position);
        float flat = new Vector2(local.x, local.z).magnitude;
        float elevation = Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg;
        return elevation <= MaxElevationUp + 2f && elevation >= -MaxElevationDown - 2f;
    }

    // The visible barrels pitch with the aim about this point (see TurretVisualRig).
    private TurretVisualRig.Result rig;
    private Vector3 barrelRestPosition;

    // How far the barrels can pitch: well up over the housing, a little below the deck.
    public const float MaxElevationUp = 80f;
    public const float MaxElevationDown = 22f;

    // Main batteries are heavy guns: they fire slowly, by the size of the ship mounting them, hitting harder in
    // proportion so each gun's damage per second is unchanged. (The prefabs had Avanti batteries firing every
    // 0.35s - a machine gun.) Avanti emitters cycle a little faster than Human cannon. Point defence is untouched.
    private bool cadenceSet;

    public void ApplyBatteryCadence(bool avanti)
    {
        if (cadenceSet || gatling || beam || targetFilter == TargetFilter.FightersOnly) return;
        SpaceUnit mountUnit = GetComponentInParent<SpaceUnit>();
        if (mountUnit == null) return;
        float interval = mountUnit.shipType switch
        {
            ShipType.Battleship => 4f,
            ShipType.Carrier => 3f,
            ShipType.Cruiser => 3f,
            ShipType.Destroyer => 2f,
            ShipType.Corvette => 1.2f,
            ShipType.Station => 3f,
            _ => fireRate,
        };
        // Avanti batteries keep the Human cadence; with FleetDoctrine.AvantiFireRate taking off the damage, each
        // shot hits as hard as before and they simply fire a little less often.
        if (interval <= fireRate) return; // Already slow enough.
        damage *= interval / Mathf.Max(0.01f, fireRate);
        fireRate = interval;
        cadenceSet = true;
        lastFiredTime = Time.time - Random.Range(0f, interval); // Batteries don't all fire in lockstep.
    }

    // Point defence, gatlings, beams, or the main guns of light ships (corvettes, destroyers) can engage strike
    // craft; cruiser, battleship, carrier and station batteries can't.
    // Set by FleetDoctrine: an anti-ship battery that leaves strike craft to the point defence.
    [System.NonSerialized] public bool shipsOnly;

    private bool CanTrackCraft
    {
        get
        {
            if (targetFilter == TargetFilter.FightersOnly || gatling || beam) return true;
            if (shipsOnly) return false;
            if (leadFighters) return true;
            SpaceUnit mountUnit = GetComponentInParent<SpaceUnit>();
            ShipType type = mountUnit != null ? mountUnit.shipType : ShipType.Station;
            return type == ShipType.Corvette || type == ShipType.Destroyer || type == ShipType.Fighter;
        }
    }

    // Bolts fly fast enough that a strike craft can't keep pace with them (some batteries were set to 20
    // units a second - slower than a fighter).
    private const float MinShotSpeed = 100f;

    void Awake()
    {
        if (targetFilter != TargetFilter.FightersOnly) projectileSpeed = Mathf.Max(projectileSpeed, MinShotSpeed); // Batteries only - point defence keeps its own.
        if (turretBase != null && turretBarrel != null) rig = TurretVisualRig.Rig(this);
        AlignMuzzleFlashes();
        MuteBuiltInShotSound();
    }

    // The turret prefab plays its shot through its own AudioSource; Sfx plays it in 3D instead (see Shoot).
    void MuteBuiltInShotSound()
    {
        for (int i = 0; i < onFire.GetPersistentEventCount(); i++)
        {
            if (onFire.GetPersistentTarget(i) is AudioSource) onFire.SetPersistentListenerState(i, UnityEventCallState.Off);
        }
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

        if (targetFilter == TargetFilter.FightersOnly && !ordered && (target == null || targetMissile == null) && Time.time >= nextMissileScanAt)
        {
            nextMissileScanAt = Time.time + targetSearchInterval * Random.Range(0.8f, 1.2f);
            Missile incoming = Missile.NearestHostile(transform.position, Range * Missile.InterceptRangeFraction, targetLayer, this); // Missiles before fighters.
            if (incoming != null) target = incoming.transform;
            ValidateTarget();
        }

        // A target that has drifted out of the barrels' reach: free guns let it go and find another; guns
        // under orders put it aside, fight whatever they can reach, and go back to it once it's in their arc.
        if (target != null && targetMissile == null && turretBase != null && !CanBear(target.position))
        {
            outOfArcFor += Time.deltaTime;
            if (outOfArcFor > 1f)
            {
                if (ordered)
                {
                    shelvedOrder = target;
                    shelvedWholeShip = orderedWholeShip;
                    ordered = false;
                }
                target = null;
                nextSearchAt = Time.time;
                outOfArcFor = 0f;
            }
        }
        else
        {
            outOfArcFor = 0f;
        }
        if (shelvedOrder != null && Time.time >= nextShelvedCheckAt)
        {
            nextShelvedCheckAt = Time.time + 0.5f;
            if (!shelvedOrder.gameObject.activeInHierarchy || shelvedOrder.GetComponentInParent<UnitHealthManager>() is UnitHealthManager s && s.IsDead) shelvedOrder = null;
            else if (CanBear(shelvedOrder.position) && Vector3.Distance(transform.position, shelvedOrder.position) <= Range)
            {
                target = shelvedOrder;
                ordered = true;
                orderedWholeShip = shelvedWholeShip;
                shelvedOrder = null;
            }
        }

        if (target == null && Time.time >= nextSearchAt)
        {
            nextSearchAt = Time.time + targetSearchInterval * Random.Range(0.8f, 1.2f);
            AcquireTarget();
        }

        if (target != null)
        {
            TrackTargetVelocity();
            Traverse();

            if (target != lineOfSightTarget || Time.time >= nextLineOfSightAt)
            {
                lineOfSightTarget = target;
                nextLineOfSightAt = Time.time + lineOfSightInterval * Random.Range(0.8f, 1.2f);
                hasLineOfSight = (targetMissile != null ? HasClearShot(firingPoints[0]) : HasLineOfSight(firingPoints[0]));  //I would use one of the middle guns but I don't want to adjust this for single use guns. Wont matter much anyway.
            }

            if (hasLineOfSight)
            {
                timeOutOfLOS = 0f;
                if (OnTarget) Shoot(); // Still swinging onto it: hold fire.

            }
            else
            {
                timeOutOfLOS += Time.deltaTime;

                // No shot for a while - out of this gun's reach, or masked by its own hull. Free guns find
                // another target. Guns under orders put the order aside (as they do for one out of their arc),
                // fight whatever they can hit, and go back to it once it's in reach; measured, ordered guns
                // were spending a third to half of every battle waiting on targets they could never see.
                if (timeOutOfLOS >= maxTimeWithoutLOS)
                {
                    if (ordered)
                    {
                        shelvedOrder = target;
                        shelvedWholeShip = orderedWholeShip;
                        ordered = false;
                        nextShelvedCheckAt = Time.time + 4f; // Give the other target a few seconds first.
                    }
                    target = null;
                    nextSearchAt = Time.time;
                    timeOutOfLOS = 0f;
                }
            }
        }

        if (beam)
        {
            UpdateBeam(target != null && hasLineOfSight && OnTarget && !GameManager.CombatOver);
        }

        if (DamageLedger.Enabled && targetFilter != TargetFilter.FightersOnly && owner != null && Time.time >= nextGunSampleAt)
        {
            nextGunSampleAt = Time.time + 0.5f;
            DamageLedger.GunSample(owner, GunState(out float nearestRatio), nearestRatio);
        }
    }

    // Dev balance runs: what this battery is doing right now (see DamageLedger.GunState).
    private float nextGunSampleAt;

    private DamageLedger.GunState GunState(out float nearestRatio)
    {
        nearestRatio = -1f;
        if (target == null)
        {
            float nearest = float.MaxValue;
            foreach (UnitHealthManager u in UnitHealthManager.Active)
            {
                if (u == null || u.IsDead || u.isAttackerSide == owner.isAttackerSide || u.GetComponent<Squadron>() != null) continue;
                nearest = Mathf.Min(nearest, Vector3.Distance(transform.position, u.transform.position));
            }
            if (nearest == float.MaxValue) return DamageLedger.GunState.NoEnemy;
            nearestRatio = nearest / Mathf.Max(1f, Range);
            return nearest <= Range ? DamageLedger.GunState.IdleEnemyInRange : DamageLedger.GunState.OutRanged;
        }
        if (turretBase != null && targetMissile == null && !CanBear(target.position)) return DamageLedger.GunState.OutOfArc;
        if (!hasLineOfSight) { DamageLedger.Blocker(owner, LosBlocker(firingPoints[0])); return DamageLedger.GunState.Blocked; }
        if (!OnTarget) return DamageLedger.GunState.Swinging;
        return DamageLedger.GunState.Firing;
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
        // Ordered onto strike craft: heavy batteries can't track them - they keep on whatever ship they had.
        if (!CanTrackCraft && candidate.GetComponentInParent<Squadron>() != null) return false;
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
            ordered = false; // Order done: free to pick targets again.
            return;
        }

        bool hardpointGone = targetHardpoint != null && targetShipHardpoints != null && !targetShipHardpoints.hardpoints.Contains(targetHardpoint);
        bool hidden = targetShipHardpoints != null && targetShipHardpoints.TryGetComponent(out UnitHealthManager targetUnit) && !BattleSensors.CanSee(this, targetUnit);
        if (hidden)
        {
            target = null; // Slipped out of sight.
            ordered = false;
            return;
        }
        if (target.gameObject.activeInHierarchy && !hardpointGone) return;

        if (ordered && !orderedWholeShip)
        {
            target = null; // The ordered hardpoint is down: free to pick targets again.
            ordered = false;
            return;
        }
        target = targetShipHardpoints != null ? targetShipHardpoints.GetRandomHardpoint() : null;
    }

    void AcquireTarget()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, Range, overlapBuffer, targetLayer);

        float closestShipSqr = Mathf.Infinity;
        float closestSquadronSqr = Mathf.Infinity;
        HardpointManager closestShip = null;
        HardpointManager closestSquadron = null;

        for (int i = 0; i < count; i++)
        {
            Transform potentialTarget = overlapBuffer[i].transform;
            if (!potentialTarget.root.TryGetComponent(out HardpointManager manager) || !AcceptsShip(manager.gameObject)) continue;
            if (manager.TryGetComponent(out UnitHealthManager seen) && !BattleSensors.CanSee(this, seen)) continue; // Hidden by sensors/asteroids.
            if (!CanBear(potentialTarget.position)) continue; // Above or below what the barrels can reach.

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
        // Heavy batteries can't track strike craft (and only spray bolts into empty space trying): they leave
        // fighters to the point defence and take ships only. Light guns (corvettes, destroyers) still will.
        if (chosen == closestSquadron && closestSquadron != null && !CanTrackCraft)
            chosen = closestShip;

        target = chosen != null ? chosen.GetRandomHardpoint() : null;
        ordered = false;
    }
    private bool restCaptured;
    private Quaternion baseRest;
    private Quaternion barrelRest;

    // Turrets swing at a fixed rate set by the gun's size - the big batteries on a battleship are slow,
    // point defence is very fast - and only fire once they're actually laid on the target.
    private const float AimTolerance = 4f;
    private const float GatlingAimTolerance = 12f;
    private float traverseRate = -1f;
    private float aimError = 180f;

    public bool OnTarget => aimError <= (gatling || beam ? GatlingAimTolerance : AimTolerance);

    private float TraverseRate
    {
        get
        {
            if (traverseRate >= 0f) return traverseRate;
            if (traverseDegreesPerSecond > 0f) return traverseRate = traverseDegreesPerSecond;
            if (gatling || targetFilter == TargetFilter.FightersOnly) return traverseRate = 150f;
            SpaceUnit mountUnit = GetComponentInParent<SpaceUnit>();
            ShipType type = mountUnit != null ? mountUnit.shipType : ShipType.Station;
            traverseRate = type switch
            {
                ShipType.Battleship => 14f,
                ShipType.Carrier => 20f,
                ShipType.Cruiser => 22f,
                ShipType.Destroyer => 34f,
                ShipType.Corvette => 50f,
                ShipType.Fighter => 120f,
                _ => 18f, // Stations and shipyards: heavy emplacements.
            };
            return traverseRate;
        }
    }

    void Traverse()
    {
        if (!restCaptured)
        {
            baseRest = turretBase.localRotation;
            barrelRest = turretBarrel.localRotation;
            barrelRestPosition = turretBarrel.localPosition;
            restCaptured = true;
        }

        Transform mount = turretBase.parent;
        Quaternion mountFrame = (mount != null ? mount.rotation : Quaternion.identity) * baseRest;
        Vector3 targetDirection = Quaternion.Inverse(mountFrame) * (LeadPoint(turretBase.position) - turretBase.position);


        Vector3 targetDirectionFlat = new Vector3(targetDirection.x, 0, targetDirection.z);
        if (targetDirectionFlat.sqrMagnitude > 0.01f)
        {
            Quaternion targetAzimuthRotation = baseRest * Quaternion.LookRotation(targetDirectionFlat);
            turretBase.localRotation = Quaternion.RotateTowards(turretBase.localRotation, targetAzimuthRotation, TraverseRate * Time.deltaTime);
            aimError = Quaternion.Angle(turretBase.localRotation, targetAzimuthRotation);
        }


        // Elevation (positive = up). Beyond what the barrels can reach the gun can't bear on the target at all:
        // it lays as close as it can but holds fire (the ship's angling - ShipAltitude - brings it to bear).
        float elevation = Mathf.Atan2(targetDirection.y, targetDirectionFlat.magnitude) * Mathf.Rad2Deg;
        bool inArc = elevation <= MaxElevationUp + 2f && elevation >= -MaxElevationDown - 2f;
        elevation = Mathf.Clamp(elevation, -MaxElevationDown, MaxElevationUp);

        Quaternion targetElevationRotation = barrelRest * Quaternion.Euler(-elevation, 0, 0);
        turretBarrel.localRotation = Quaternion.RotateTowards(turretBarrel.localRotation, targetElevationRotation, TraverseRate * 1.3f * Time.deltaTime);
        aimError = Mathf.Max(aimError, Quaternion.Angle(turretBarrel.localRotation, targetElevationRotation) - 1f);
        if (!inArc) aimError = 180f;

        // Pitch about the trunnion rather than the turret's centre, so the barrels stay seated in the housing.
        if (rig.rigged)
        {
            Quaternion pitch = turretBarrel.localRotation * Quaternion.Inverse(barrelRest);
            Vector3 arm = rig.trunnion - barrelRestPosition;
            turretBarrel.localPosition = barrelRestPosition + arm - pitch * arm;
        }
    }

    void Shoot()
    {
        if (GameManager.CombatOver) return;
        if (owner == null) owner = GetComponentInParent<UnitHealthManager>();
        if (owner != null)
        {
            if (owner.HoldFire) return;
            owner.LastFiredAt = Time.time;
        }

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
            avantiGuns ??= GetComponentInParent<ShieldGenerator>() != null;
            Sfx.Play(avantiGuns.Value ? Sfx.Cue.LaserAvanti : Sfx.Cue.LaserHeavy, firingPoints[0].position);

            foreach (Transform firingPoint in firingPoints)
            {
                FireProjectile(firingPoint);
            }
        }
    }

    private static readonly RaycastHit[] losBuffer = new RaycastHit[16];

    private static int losMask = -1;
    private static int LosMask => losMask != -1 ? losMask : losMask = ~LayerMask.GetMask("Ground", "UI", "Ignore Raycast", "RedLaser", "BlueLaser", "FleetToken", "Planet");

    // The middle of what we're shooting at: a hardpoint's collider, not its pivot (which on most turrets sits on
    // the deck at its foot, so shots aimed at it hit the hull underneath).
    private Transform aimCentreFor;
    private Collider aimCollider;

    private Vector3 AimCentre()
    {
        if (target == null) return transform.position;
        if (targetFighter != null || targetMissile != null) return target.position;
        if (aimCentreFor != target)
        {
            aimCentreFor = target;
            aimCollider = target.GetComponent<Collider>();
            if (aimCollider == null)
            {
                foreach (Collider c in target.GetComponentsInChildren<Collider>())
                {
                    if (c.enabled && !c.isTrigger && c.gameObject.layer != LayerMask.NameToLayer("UI")) { aimCollider = c; break; }
                }
            }
        }
        return aimCollider != null && aimCollider.enabled ? aimCollider.bounds.center : target.position;
    }

    bool HasLineOfSight(Transform firingPoint)
    {
        Vector3 direction = (AimCentre() - firingPoint.position).normalized;
        // Only solid things block a shot: not the battle plane (ships fight above and below it), not the
        // clickable UI markers floating over ships, not lasers or map tokens.
        int count = Physics.RaycastNonAlloc(firingPoint.position, direction, losBuffer, Range, LosMask, QueryTriggerInteraction.Ignore);
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

    // Dev balance runs: what's in the way when HasLineOfSight says no.
    private string LosBlocker(Transform firingPoint)
    {
        Vector3 direction = (AimCentre() - firingPoint.position).normalized;
        float toTarget = Vector3.Distance(firingPoint.position, AimCentre());
        int count = Physics.RaycastNonAlloc(firingPoint.position, direction, losBuffer, Range, LosMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(losBuffer, 0, count, LosDistance.Instance);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = losBuffer[i];
            if (hit.transform.root == transform.root && hit.collider.GetComponentInParent<HardpointHealth>() != null) continue;
            if (hit.collider.gameObject.layer == ShieldGenerator.Layer) { ShieldGenerator sg = hit.collider.GetComponentInParent<ShieldGenerator>(); if (sg != null && ((1 << sg.gameObject.layer) & targetLayer) != 0) return "?"; continue; }
            if (hit.transform.root == transform.root) return "own hull (" + LayerMask.LayerToName(hit.collider.gameObject.layer) + ")";
            UnitHealthManager other = hit.collider.GetComponentInParent<UnitHealthManager>();
            if (other != null && owner != null && other.isAttackerSide == owner.isAttackerSide) return other.GetComponent<Squadron>() != null ? "friendly craft" : "friendly ship";
            if (other != null) return "other enemy unit (layer " + LayerMask.LayerToName(hit.collider.gameObject.layer) + ")";
            return "terrain/other: " + LayerMask.LayerToName(hit.collider.gameObject.layer) + (hit.distance < toTarget ? "" : " (beyond target)");
        }
        return toTarget > Range ? "nothing hit - target beyond range" : "nothing hit - ray missed target colliders";
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
        if ((leadFighters || CanTrackCraft) && (targetFighter != null || targetMissile != null) && target == validatedTarget)
        {
            float travel = Vector3.Distance(firingPoint.position, aimPoint) / Mathf.Max(1f, projectileSpeed);
            aimPoint += (targetMissile != null ? targetMissile.Velocity : targetFighter.Velocity) * travel;
        }
        else if (targetFighter == null && targetMissile == null)
        {
            // A ship: lead it, but the estimate is never exact - sometimes short, sometimes long.
            Vector3 centre = AimCentre();
            Vector3 lead = LeadPoint(firingPoint.position) - centre;
            aimPoint = centre + lead * Random.Range(0.75f, 1.2f);
        }

        Vector3 direction = (aimPoint - firingPoint.position).normalized;
        float movingPenalty = targetFighter == null && targetMissile == null ? 1f + targetVelocity.magnitude * MovingSpreadPerSpeed : 1f;
        float accuracyMultiplier = GetAccuracyMultiplier(target) * BattleTerrain.AccuracyMultiplier(firingPoint.position) * HeroSpread * movingPenalty; // Asteroids spoil aim; heroes sharpen it; a moving target is harder.
        Vector3 inaccuracyOffset = Random.insideUnitSphere * accuracyMultiplier;
        Vector3 finalDirection = (direction + inaccuracyOffset).normalized;

        ObjectPool pool = ObjectPool.GetPoolFor(projectilePrefab);
        GameObject temp = pool != null
            ? pool.Spawn(firingPoint.position, firingPoint.parent.parent.rotation)
            : Instantiate(projectilePrefab, firingPoint.position, firingPoint.parent.parent.rotation);

        if (temp == null) return;

        Laser laser = temp.GetComponent<Laser>();
        laser.damage = (targetFighter != null && target == validatedTarget ? damage * fighterDamageMultiplier : damage) * HeroDamage;
        laser.layer = targetLayer;
        laser.intercept = target == validatedTarget ? targetMissile : null;
        laser.fromWarship = leadFighters; // Light, fighter-tracking guns; heavy batteries just chip them.
        laser.timeOut = Mathf.Max(0.5f, Range * shotLifetimeRangeMultiplier / Mathf.Max(1f, projectileSpeed));
        laser.SetSourcePool(pool);
        laser.source = owner;
        laser.weapon = targetFilter == TargetFilter.FightersOnly ? DamageLedger.Weapon.PointDefence : DamageLedger.Weapon.Battery;
        DamageLedger.Fired(owner, laser.weapon);
        laser.intended = target;
        laser.intendedIsCraft = targetFighter != null || targetMissile != null || target.GetComponentInParent<Squadron>() != null;
        laser.fireDistance = Vector3.Distance(firingPoint.position, target.position);
        laser.targetSpeed = laser.intendedIsCraft ? (targetFighter != null ? targetFighter.Velocity.magnitude : 0f) : targetVelocity.magnitude;
        UnityEngine.AI.NavMeshAgent ownAgent = GetComponentInParent<UnityEngine.AI.NavMeshAgent>();
        laser.shooterSpeed = ownAgent != null ? ownAgent.velocity.magnitude : 0f;
        laser.Launch(temp.transform.forward * laser.speed + finalDirection * projectileSpeed);
    }

    // Whether the gun has a clear shot at its target right now (its own hull can block it).
    public bool CanFire => target != null && hasLineOfSight;

    // Turns a point-defence turret into a CIWS gatling with the same damage output, spread over ~50 rounds a second.
    public void MakeGatling()
    {
        if (gatling) return;
        float bolts = firingPoints.Count * damage / Mathf.Max(0.02f, fireRate);
        float dutyCycle = gatlingBurst / (gatlingBurst + gatlingCooldown);
        gatlingRoundDamage = bolts / (gatlingRoundsPerSecond * dutyCycle);
        gatling = true;
    }

    // The Avanti picket beams burn a little harder than the gatling damage they're converted from.
    public const float BeamDamageBonus = 1.2f;

    // Turns a point-defence turret into a continuous beam that has to sweep onto its target.
    public void MakeBeam()
    {
        if (beam || gatling) return;
        beam = true;
        beamDps = firingPoints.Count * damage / Mathf.Max(0.02f, fireRate) * BeamDamageBonus;
        beamDirection = firingPoints.Count > 0 ? firingPoints[0].forward : transform.forward;
    }

    // The beam swings toward the target at beamSlewRate; while it lies across the target it burns it for
    // beamDps. It fades in and out rather than snapping on.
    void UpdateBeam(bool firing)
    {
        if (firingPoints.Count == 0) return;
        Transform muzzle = firingPoints[0];
        float reach = Range * Missile.InterceptRangeFraction;

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
                DamageLedger.Begin(owner, DamageLedger.Weapon.Beam);
                if (targetMissile != null) targetMissile.TakeHit(beamOwed, false);
                else if (targetHardpoint != null) targetHardpoint.DealDamage(beamOwed);
                DamageLedger.End();
                beamOwed = 0f;
            }
        }
        else
        {
            beamOwed = 0f;
        }

        if (firing && beamPower <= 0f) Sfx.Play(Sfx.Cue.Beam, muzzle.position);
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
            Sfx.Play(Sfx.Cue.Ciws, firingPoints[0].position);
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
        direction = (direction + Random.insideUnitSphere * spread * BattleTerrain.AccuracyMultiplier(firingPoint.position) * HeroSpread).normalized;
        float life = Range * 1.2f / gatlingMuzzleSpeed;
        HardpointHealth fighter = tracked && targetFighter != null ? targetHardpoint : null;
        CIWSTracers.Fire(firingPoint.position, direction * gatlingMuzzleSpeed, life, gatlingRoundDamage, tracked ? targetMissile : null, fighter, owner, DamageLedger.Weapon.Gatling);
        DamageLedger.Fired(owner, DamageLedger.Weapon.Gatling);
    }

    float GetAccuracyMultiplier(Transform target)
    {

        float distance = Vector3.Distance(transform.position, target.position);

        typedRangeFalloff = distance / Range * rangeFallOffValue;

        if (target != typedTarget)
        {
            typedTarget = target;
            SpaceUnit unit = target.GetComponentInParent<SpaceUnit>();
            targetType = unit != null ? unit.shipType : target.GetComponent<Missile>() != null ? ShipType.Fighter : ShipType.Cruiser;
        }

        return TypeAccuracy() * Aspect(target);
    }

    // Spread multiplier for the target's profile: easier to hit side-on, harder head-on or stern-on.
    private const float HeadOnSpread = 1.35f;
    private const float BroadsideSpread = 0.8f;

    private float Aspect(Transform aimPoint)
    {
        if (targetType == ShipType.Fighter || targetType == ShipType.Station) return 1f;

        Transform ship = aimPoint.root;
        Vector3 lineOfFire = ship.position - transform.position;
        lineOfFire.y = 0f;
        Vector3 heading = ship.forward;
        heading.y = 0f;
        if (lineOfFire.sqrMagnitude < 0.01f || heading.sqrMagnitude < 0.01f) return 1f;

        float side = Mathf.Abs(Vector3.Cross(heading.normalized, lineOfFire.normalized).y); // 1 = side-on.
        return Mathf.Lerp(HeadOnSpread, BroadsideSpread, side);
    }

    private float TypeAccuracy()
    {
        float rangeFalloff = typedRangeFalloff;
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
        Order(_target, false);
    }

    // An ordered target: fired on until it's destroyed (a hardpoint), or until its ship is (wholeShip),
    // then the gun picks its own targets again.
    public void Order(Transform aim, bool wholeShip)
    {
        shelvedOrder = null;
        outOfArcFor = 0f;
        target = aim;
        ordered = aim != null;
        orderedWholeShip = wholeShip;
        timeOutOfLOS = 0f;
    }
}
