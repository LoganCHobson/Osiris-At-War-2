using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Fakes a 3D battlefield on top of the flat navmesh. Ships still path on the plane, but each one flies at
// its own height above or below it (the NavMeshAgent's base offset, so the hull, hardpoints, turrets and
// hit-boxes all go up together) and can pitch and roll its GFX - the child that holds the model and every
// gun - to bring more guns to bear on something above or below it.
//
// Attitude is automatic by default: every so often the ship tries a few pitch/roll angles towards its
// target and keeps the one that puts the most firepower on the target's side of each gun's deck (guns
// can't shoot through their own hull). A manual pitch/roll order switches that off until it's levelled.
//
// Ships at clearly different heights don't collide (see ShipSpacing), so fleets can pass over and under
// each other. A thin stalk and ring under each raised ship show where it is relative to the plane.
public class ShipAltitude : MonoBehaviour
{
    public const float MaxAltitude = 120f;
    public const float Step = 25f;
    public const float AttitudeStep = 15f;
    public const float MaxManualAngle = 60f;
    public const float MaxAutoAngle = 60f;

    private const float AutoInterval = 0.4f;
    // Climbing and tilting follow the ship's own (live) speed and turn rate: a nimble corvette changes height
    // and angle quickly, a battleship slowly - and engine damage, asteroids or a group pace slow it too.
    private const float ClimbShare = 0.5f;      // Vertical speed as a share of current flight speed.
    private const float AttitudeShare = 0.35f;  // Pitch/roll rate as a share of the agent's turn rate.
    private const float EaseDistance = 12f;     // Slows into the final few units of a climb.
    private const float MarkerThreshold = 6f;
    private const int RingSegments = 24;
    private static readonly float[] AutoCandidates = { -60f, -45f, -30f, -15f, 0f, 15f, 30f, 45f, 60f };

    public static readonly List<ShipAltitude> All = new List<ShipAltitude>();

    public float TargetAltitude { get; private set; }
    public bool HiddenBySensors { get; set; } // Set by SensorVisibility: no marker for ships the player can't see.
    public float Altitude { get; private set; }
    public bool AutoAttitude { get; private set; } = true;
    public bool Airborne { get; private set; }
    public NavMeshAgent Agent { get; private set; }
    public float Radius { get; private set; }
    public float HalfHeight { get; private set; }
    // Firepower on guns mounted on top of the hull vs underneath it - the AI comes at you from the weaker side.
    public float DorsalFirepower { get; private set; }
    public float VentralFirepower { get; private set; }

    // Banking into turns, fed by the move state.
    public float Bank { get; set; }

    private float ClimbRate => Mathf.Max(1f, (Agent != null ? Agent.speed : 10f) * ClimbShare);
    private float AttitudeRate => Mathf.Max(4f, (Agent != null ? Agent.angularSpeed : 60f) * AttitudeShare);

    // How high the hull sits above the plane at altitude 0 (the prefab's own offset), in world units.
    public float Lift => baseWorldOffset;

    // The altitude the ship will have after flying this far along its route (it climbs at ClimbShare of
    // its flight speed, so the change is spread over the distance).
    public float AltitudeAfter(float distance)
    {
        return Mathf.MoveTowards(Altitude, TargetAltitude, distance * ClimbShare);
    }

    // Height of the battle plane under this ship.
    public float PlaneY => Agent != null && Agent.enabled && Agent.isOnNavMesh ? Agent.nextPosition.y : transform.position.y - baseWorldOffset - Altitude;

    private Transform gfx;
    private Quaternion gfxRest = Quaternion.identity;
    private float baseOffset;
    private float baseWorldOffset;
    private float offsetScale = 1f;

    private float manualPitch;
    private float manualRoll;
    private Quaternion attitude = Quaternion.identity;
    private Quaternion autoAttitude = Quaternion.identity;
    private float nextAutoAt;

    private UnitHealthManager health;
    private HardpointManager hardpoints;
    private readonly List<(TurretController turret, Vector3 up, Vector3 position, float weight)> guns = new List<(TurretController, Vector3, Vector3, float)>();

    private LineRenderer stalk;
    private LineRenderer ring;
    private bool playerSide;

    public static void Fit(GameObject ship, bool playerSide)
    {
        if (!ship.TryGetComponent(out NavMeshAgent _) || !ship.TryGetComponent(out SpaceUnit unit)) return;
        if (unit.shipType == ShipType.Station) return;

        if (!ship.TryGetComponent(out ShipAltitude altitude)) altitude = ship.AddComponent<ShipAltitude>();
        altitude.playerSide = playerSide;
        altitude.Airborne = ship.GetComponent<Squadron>() != null;
    }

    // True when two ships' hulls overlap in height, so they'd actually collide.
    public static bool SharesHeight(Transform a, Transform b)
    {
        ShipAltitude first = a != null ? a.GetComponentInParent<ShipAltitude>() : null;
        ShipAltitude second = b != null ? b.GetComponentInParent<ShipAltitude>() : null;
        if (first == null || second == null) return true;
        return Mathf.Abs(first.Altitude - second.Altitude) < first.HalfHeight + second.HalfHeight;
    }

    public static float AltitudeOf(Transform unit)
    {
        ShipAltitude altitude = unit != null ? unit.GetComponentInParent<ShipAltitude>() : null;
        return altitude != null ? altitude.Altitude : 0f;
    }

    private void Awake()
    {
        Agent = GetComponent<NavMeshAgent>();
        health = GetComponent<UnitHealthManager>();
        hardpoints = GetComponent<HardpointManager>();

        SolarStudios.PlayerUnitMoveState move = GetComponentInChildren<SolarStudios.PlayerUnitMoveState>(true);
        gfx = move != null && move.gfx != null ? move.gfx.transform : transform.Find("GFX");
        if (gfx != null) gfxRest = gfx.localRotation;

        float scale = Mathf.Max(0.0001f, transform.lossyScale.x);
        Radius = Agent != null ? Agent.radius * scale : FleetFormation.DefaultRadius;
        HalfHeight = Agent != null ? Mathf.Max(4f, Agent.height * Mathf.Max(0.0001f, transform.lossyScale.y) * 0.5f) : 8f;
        offsetScale = Mathf.Max(0.0001f, transform.lossyScale.y);
        baseOffset = Agent != null ? Agent.baseOffset : 0f;
        baseWorldOffset = baseOffset * offsetScale;
    }

    private void OnEnable()
    {
        All.Add(this);
        ShipSpacing.Ensure();
    }

    private void OnDisable()
    {
        All.Remove(this);
        SetMarkers(false);
    }

    private void Start()
    {
        if (Agent != null && !Airborne) Agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance; // ShipSpacing does it in 3D.
        CacheGuns();
    }

    private void CacheGuns()
    {
        guns.Clear();
        if (gfx == null) return;

        foreach (TurretController turret in GetComponentsInChildren<TurretController>(true))
        {
            if (turret.targetFilter == TurretController.TargetFilter.FightersOnly || turret.turretBase == null) continue;
            Transform mount = turret.turretBase.parent != null ? turret.turretBase.parent : turret.turretBase;
            Vector3 up = Quaternion.Inverse(gfx.rotation) * mount.up;
            Vector3 position = gfx.InverseTransformPoint(turret.turretBase.position);
            float weight = turret.damage * Mathf.Max(1, turret.firingPoints.Count) / Mathf.Max(0.05f, turret.fireRate);
            guns.Add((turret, up, Vector3.Scale(position, gfx.lossyScale), Mathf.Max(0.01f, weight)));
        }

        DorsalFirepower = 0f;
        VentralFirepower = 0f;
        foreach ((TurretController _, Vector3 up, Vector3 _, float weight) in guns)
        {
            if (up.y > 0.3f) DorsalFirepower += weight;
            else if (up.y < -0.3f) VentralFirepower += weight;
        }
    }

    // ---------------- Orders ----------------

    public void SetTargetAltitude(float altitude)
    {
        TargetAltitude = Mathf.Clamp(altitude, -MaxAltitude, MaxAltitude);
    }

    // Appear already at this height (reinforcements dropped in at altitude).
    public void SnapAltitude(float altitude)
    {
        TargetAltitude = Altitude = Mathf.Clamp(altitude, -MaxAltitude, MaxAltitude);
    }

    public void ChangeAltitude(float delta)
    {
        SetTargetAltitude(TargetAltitude + delta);
    }

    public void Pitch(float degrees)
    {
        TakeManualControl();
        manualPitch = Mathf.Clamp(manualPitch + degrees, -MaxManualAngle, MaxManualAngle);
    }

    public void Roll(float degrees)
    {
        TakeManualControl();
        manualRoll = Mathf.Clamp(manualRoll + degrees, -MaxManualAngle, MaxManualAngle);
    }

    // Back to level flight with automatic angling.
    public void Level()
    {
        AutoAttitude = true;
        manualPitch = 0f;
        manualRoll = 0f;
    }

    private void TakeManualControl()
    {
        if (!AutoAttitude) return;
        AutoAttitude = false;
        // Start from whatever angle the autopilot had it at.
        Vector3 euler = autoAttitude.eulerAngles;
        manualPitch = Mathf.DeltaAngle(0f, euler.x);
        manualRoll = Mathf.DeltaAngle(0f, euler.z);
    }

    // ---------------- Frame ----------------

    private void LateUpdate()
    {
        if (health != null && health.IsDead)
        {
            SetMarkers(false);
            return;
        }

        Calibrate();

        float dt = Time.deltaTime;
        float remaining = Mathf.Abs(TargetAltitude - Altitude);
        float ease = Mathf.Clamp(remaining / EaseDistance, 0.25f, 1f);
        Altitude = Mathf.MoveTowards(Altitude, TargetAltitude, ClimbRate * ease * dt);
        if (Agent != null && Agent.enabled) Agent.baseOffset = baseOffset + Altitude / offsetScale;

        if (gfx != null && !Airborne)
        {
            Quaternion wanted = AutoAttitude ? AutoAngle() : Quaternion.Euler(manualPitch, 0f, manualRoll);
            attitude = Quaternion.RotateTowards(attitude, wanted, AttitudeRate * dt);
            gfx.localRotation = gfxRest * attitude * Quaternion.Euler(0f, 0f, Bank);
        }

        UpdateMarkers();
    }

    // Measures how far one unit of base offset actually lifts the ship (it depends on the prefab's scale),
    // once, while the ship is still at plane height, so altitudes are in true world units.
    private bool calibrated;
    private int calibrationTries;

    private void Calibrate()
    {
        if (calibrated || Agent == null || !Agent.enabled || !Agent.isOnNavMesh) return;
        if (Mathf.Abs(Agent.baseOffset) < 0.01f || ++calibrationTries > 30)
        {
            calibrated = true; // Nothing to measure, or never settled - keep the scale-based guess.
            return;
        }

        float measured = (transform.position.y - Agent.nextPosition.y) / Agent.baseOffset; // Whatever offset is applied right now.
        if (measured <= 0.0001f) return; // The agent hasn't placed the ship yet - try next frame.
        calibrated = true;
        if (Mathf.Abs(measured - offsetScale) / offsetScale > 0.05f)
        {
            offsetScale = measured;
            baseWorldOffset = baseOffset * offsetScale;
        }
    }

    private Quaternion AutoAngle()
    {
        if (Time.time < nextAutoAt) return autoAttitude;
        nextAutoAt = Time.time + AutoInterval * Random.Range(0.85f, 1.15f);

        Transform target = CurrentTarget();
        if (target == null || guns.Count == 0)
        {
            autoAttitude = Quaternion.identity;
            return autoAttitude;
        }

        // Work in the ship's own (yaw-only) frame.
        Vector3 local = Quaternion.Inverse(transform.rotation) * (target.position - gfx.position);
        Vector3 flat = new Vector3(local.x, 0f, local.z);
        if (flat.sqrMagnitude < 1f || Mathf.Abs(local.y) < HalfHeight)
        {
            autoAttitude = Quaternion.identity; // Level with it - nothing to gain by tilting.
            return autoAttitude;
        }

        // Tilting about this axis turns the deck towards (positive angle: away from) the target's bearing.
        Vector3 axis = Vector3.Cross(Vector3.up, flat.normalized);
        float bestScore = float.MinValue;
        Quaternion best = Quaternion.identity;

        foreach (float angle in AutoCandidates)
        {
            if (Mathf.Abs(angle) > MaxAutoAngle) continue;
            Quaternion candidate = Quaternion.AngleAxis(angle, axis);
            float score = 0f;
            foreach ((TurretController _, Vector3 up, Vector3 position, float weight) in guns)
            {
                Vector3 gunUp = candidate * up;
                Vector3 toTarget = (local - candidate * position).normalized;
                // In this gun's arc: between its lowest and highest barrel elevation (TurretController limits).
                float elevation = 90f - Vector3.Angle(gunUp, toTarget);
                if (elevation <= TurretController.MaxElevationUp && elevation >= -TurretController.MaxElevationDown) score += weight;
            }
            score -= Mathf.Abs(angle) * 0.01f; // Prefer the gentlest tilt among equals.

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        autoAttitude = best;
        return autoAttitude;
    }

    // What the ship is shooting at: its ordered target, else whatever most of its main guns are on.
    private Transform CurrentTarget()
    {
        if (hardpoints != null && hardpoints.OrderedTarget != null && hardpoints.OrderedTarget.gameObject.activeInHierarchy)
        {
            UnitHealthManager ordered = hardpoints.OrderedTarget.GetComponentInParent<UnitHealthManager>();
            if (ordered != null && !ordered.IsDead) return hardpoints.OrderedWholeShip ? ordered.transform : hardpoints.OrderedTarget; // A specific hardpoint: angle onto it.
        }

        foreach ((TurretController turret, Vector3 _, Vector3 _, float _) in guns)
        {
            if (turret == null || turret.target == null || !turret.target.gameObject.activeInHierarchy) continue;
            UnitHealthManager unit = turret.target.GetComponentInParent<UnitHealthManager>();
            if (unit != null && !unit.IsDead && unit.GetComponent<Squadron>() == null) return unit.transform;
        }
        return NearestEnemyInReach();
    }

    // The nearest enemy warship within gun range: what the ship should angle to bring its guns onto when none
    // of them has a target yet (guns don't take targets their barrels can't reach).
    private Transform NearestEnemyInReach()
    {
        float reach = 0f;
        LayerMask hostile = 0;
        foreach ((TurretController turret, Vector3 _, Vector3 _, float _) in guns)
        {
            if (turret == null) continue;
            reach = Mathf.Max(reach, turret.Range);
            hostile |= turret.targetLayer;
        }
        if (reach <= 0f || hostile == 0) return null;

        Transform best = null;
        float bestSqr = reach * reach * 1.1f;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead || ((1 << unit.gameObject.layer) & hostile) == 0) continue;
            if (unit.GetComponent<Squadron>() != null) continue;
            float d = (unit.transform.position - transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = unit.transform; }
        }
        return best;
    }

    // ---------------- Height marker ----------------

    private void UpdateMarkers()
    {
        bool show = Mathf.Abs(Altitude) > MarkerThreshold && !HiddenBySensors && !CinematicCamera.Active; // A gameplay aid - not in cinematics.
        SetMarkers(show);
        if (!show) return;

        Vector3 top = transform.position;
        Vector3 bottom = new Vector3(top.x, PlaneY, top.z);
        stalk.SetPosition(0, top);
        stalk.SetPosition(1, bottom);

        float radius = Mathf.Max(6f, Radius * 0.8f);
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * Mathf.PI * 2f / RingSegments;
            ring.SetPosition(i, bottom + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
        }
    }

    private void SetMarkers(bool visible)
    {
        if (visible && stalk == null) CreateMarkers();
        if (stalk != null && stalk.enabled != visible) stalk.enabled = visible;
        if (ring != null && ring.enabled != visible) ring.enabled = visible;
    }

    private static Material markerMaterial;
    private static Transform markerRoot;

    private void OnDestroy()
    {
        if (stalk != null) Destroy(stalk.gameObject);
        if (ring != null) Destroy(ring.gameObject);
    }

    private void CreateMarkers()
    {
        if (markerMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            markerMaterial = new Material(shader);
        }

        Color color = playerSide ? new Color(0.35f, 0.8f, 1f, 0.45f) : new Color(1f, 0.4f, 0.35f, 0.45f);
        stalk = MakeLine("AltitudeStalk", 2, false, color);
        ring = MakeLine("AltitudeRing", RingSegments, true, color);
    }

    private LineRenderer MakeLine(string name, int points, bool loop, Color color)
    {
        // Kept outside the ship so destruction effects never treat the marker as part of the hull.
        if (markerRoot == null) markerRoot = new GameObject("AltitudeMarkers").transform;
        GameObject go = new GameObject(name);
        go.transform.SetParent(markerRoot, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = points;
        line.widthMultiplier = 0.8f;
        line.sharedMaterial = markerMaterial;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }
}
