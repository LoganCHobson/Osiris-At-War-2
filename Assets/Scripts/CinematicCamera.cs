using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CinematicCamera : MonoBehaviour
{
    public static bool Active { get; private set; }

    [Header("Input")]
    public KeyCode toggleKey = KeyCode.C;
    public KeyCode exitKey = KeyCode.Escape;
    [Tooltip("Wide overhead view of the whole battle (press again for the normal shots).")]
    public KeyCode overviewKey = KeyCode.V;

    [Header("References")]
    public Camera viewCamera;
    public PlayerSpaceManager spaceManager;
    public Behaviour[] disableWhileActive;
    public Renderer[] hideWhileActive;

    [Header("Direction")]
    public Vector2 shotDuration = new Vector2(4.5f, 8f);
    public float positionSmoothing = 0.35f;
    public float rotationSharpness = 5f;
    public float fovSharpness = 3f;
    public float deathLinger = 6f;
    public float orbitSpeed = 9f;
    public float squadronFraming = 10f;

    [Header("Look")]
    public Volume cinematicVolume;
    public float lookBlendSpeed = 2.5f;
    [Range(1f, 3f)] public float letterboxAspect = 2.35f;
    public float focusFalloff = 6f;

    private enum Shot { Chase, Broadside, LowBow, Orbit, Bridge, Flyby, Duel, Overview }

    private static readonly Shot[] Rotation = { Shot.Chase, Shot.Broadside, Shot.LowBow, Shot.Orbit, Shot.Bridge, Shot.Flyby, Shot.Duel, Shot.Overview };
    private bool overviewLocked;

    private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    private readonly List<Renderer> hiddenLines = new List<Renderer>();
    private readonly List<SpaceUnit> hiddenSelection = new List<SpaceUnit>();
    private readonly List<Behaviour> disabled = new List<Behaviour>();

    private UnitHealthManager target;
    private HullDamage targetHull;
    private Squadron targetSquadron;
    private Transform follow;
    private float followSize;
    private float followYaw;
    private Vector3 followCenter;
    private Vector3 followLead;
    private float lostAt = -1f;

    private Shot shot;
    private float shotStartedAt;
    private float shotEndsAt;
    private float side = 1f;
    private float orbitAngle;
    private Vector3 anchor;
    private bool snap;
    private Vector3 moveVelocity;

    private Transform camTransform;
    private Vector3 savedLocalPosition;
    private Quaternion savedLocalRotation;
    private float savedFov;
    private float nextScanAt;
    private float lookBlend;
    private DepthOfField depthOfField;

    public UnitHealthManager Target => target;
    public float LookWeight { get; private set; }
    public bool IsWatchingSquadron => targetSquadron != null;

    private bool TargetAlive => target != null && !target.IsDead;

    private void Update()
    {
        if (Input.GetKeyDown(overviewKey))
        {
            if (!Active)
            {
                Enter();
                overviewLocked = Active;
            }
            else
            {
                overviewLocked = !overviewLocked;
            }

            if (Active)
            {
                if (overviewLocked) BeginShot(Shot.Overview);
                else NextShot();
            }
            return;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            if (Active) Exit();
            else Enter();
            return;
        }

        if (!Active) return;

        if (Input.GetKeyDown(exitKey))
        {
            Exit();
            return;
        }

        if (Input.GetMouseButtonDown(0)) Cycle(-1);
        else if (Input.GetMouseButtonDown(1)) Cycle(1);

        if (Time.unscaledTime >= nextScanAt) HideInterface();
    }

    private void LateUpdate()
    {
        if (Active)
        {
            TrackTarget();
            if (Active)
            {
                bool watchingDeath = lostAt >= 0f || (target != null && target.IsDead);
                if (!watchingDeath && Time.unscaledTime >= shotEndsAt) NextShot();
                Frame();
            }
        }

        UpdateLook();
    }

    private void UpdateLook()
    {
        float goal = Active ? 1f : 0f;
        if (lookBlend == goal && !Active) return;

        lookBlend = Mathf.MoveTowards(lookBlend, goal, lookBlendSpeed * Time.unscaledDeltaTime);
        float eased = lookBlend * lookBlend * (3f - 2f * lookBlend);
        LookWeight = eased;

        if (cinematicVolume != null)
        {
            cinematicVolume.weight = eased;
            if (depthOfField == null) cinematicVolume.profile.TryGet(out depthOfField);
            if (depthOfField != null) depthOfField.active = targetSquadron == null;
            if (depthOfField != null && camTransform != null && Active)
            {
                float focus = Vector3.Distance(camTransform.position, followCenter);
                depthOfField.gaussianStart.value = focus + followSize * 1.5f;
                depthOfField.gaussianEnd.value = focus + followSize * focusFalloff;
            }
        }

        if (viewCamera != null)
        {
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float bar = Mathf.Max(0f, (1f - screenAspect / letterboxAspect) * 0.5f) * eased;
            viewCamera.rect = new Rect(0f, bar, 1f, 1f - bar * 2f);
        }
    }

    public void Enter()
    {
        if (Active) return;
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) return;

        UnitHealthManager first = PickInitialTarget();
        if (first == null) return;

        Active = true;
        camTransform = viewCamera.transform;
        savedLocalPosition = camTransform.localPosition;
        savedLocalRotation = camTransform.localRotation;
        savedFov = viewCamera.fieldOfView;

        foreach (Behaviour behaviour in disableWhileActive)
        {
            if (behaviour != null && behaviour.enabled)
            {
                behaviour.enabled = false;
                disabled.Add(behaviour);
            }
        }

        if (spaceManager != null)
        {
            foreach (SpaceUnit unit in spaceManager.selectedUnits)
            {
                if (unit == null) continue;
                unit.ToggleSelect(false);
                hiddenSelection.Add(unit);
            }
        }

        if (hideWhileActive != null)
        {
            foreach (Renderer scenery in hideWhileActive)
            {
                if (scenery == null || !scenery.enabled) continue;
                scenery.enabled = false;
                hiddenLines.Add(scenery);
            }
        }

        HideInterface();
        SetTarget(first);
    }

    public void Exit()
    {
        if (!Active) return;
        Active = false;
        overviewLocked = false;

        if (camTransform != null)
        {
            camTransform.localPosition = savedLocalPosition;
            camTransform.localRotation = savedLocalRotation;
        }
        if (viewCamera != null) viewCamera.fieldOfView = savedFov;

        foreach (Behaviour behaviour in disabled)
        {
            if (behaviour != null) behaviour.enabled = true;
        }
        disabled.Clear();

        foreach (Canvas canvas in hiddenCanvases)
        {
            if (canvas != null) canvas.enabled = true;
        }
        hiddenCanvases.Clear();

        foreach (Renderer line in hiddenLines)
        {
            if (line != null) line.enabled = true;
        }
        hiddenLines.Clear();

        foreach (SpaceUnit unit in hiddenSelection)
        {
            if (unit != null && spaceManager != null && spaceManager.selectedUnits.Contains(unit)) unit.ToggleSelect(true);
        }
        hiddenSelection.Clear();

        target = null;
        targetHull = null;
        targetSquadron = null;
        follow = null;
    }

    public void Spectate(UnitHealthManager unit)
    {
        if (!Active || !IsWatchable(unit)) return;
        SetTarget(unit);
    }

    public void Cycle(int direction)
    {
        List<UnitHealthManager> units = Targets();
        if (units.Count == 0) return;

        int index = units.IndexOf(target);
        if (index < 0) index = direction > 0 ? -1 : 0;
        SetTarget(units[(index + direction + units.Count) % units.Count]);
    }

    private void HideInterface()
    {
        nextScanAt = Time.unscaledTime;

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.enabled) continue;
            canvas.enabled = false;
            hiddenCanvases.Add(canvas);
        }

        foreach (PathVisualization path in FindObjectsByType<PathVisualization>(FindObjectsSortMode.None))
        {
            if (!path.TryGetComponent(out LineRenderer line) || !line.enabled) continue;
            line.enabled = false;
            hiddenLines.Add(line);
        }
    }

    private static bool IsWatchable(UnitHealthManager unit)
    {
        if (unit == null || unit.IsDead) return false;
        if (unit.TryGetComponent(out HullDamage _)) return true;
        return unit.TryGetComponent(out Squadron squadron) && LeadFighter(squadron) != null;
    }

    private List<UnitHealthManager> Targets()
    {
        List<UnitHealthManager> units = new List<UnitHealthManager>();
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (IsWatchable(unit)) units.Add(unit);
        }
        units.Sort((a, b) => a.gameObject.layer != b.gameObject.layer
            ? a.gameObject.layer.CompareTo(b.gameObject.layer)
            : a.GetInstanceID().CompareTo(b.GetInstanceID()));
        return units;
    }

    private UnitHealthManager PickInitialTarget()
    {
        if (spaceManager != null)
        {
            foreach (SpaceUnit unit in spaceManager.selectedUnits)
            {
                if (unit != null && unit.TryGetComponent(out UnitHealthManager selected) && IsWatchable(selected)) return selected;
            }
        }

        UnitHealthManager best = null;
        float bestScore = float.MaxValue;
        foreach (UnitHealthManager unit in Targets())
        {
            Vector3 viewport = viewCamera.WorldToViewportPoint(unit.transform.position);
            float score = viewport.z > 0f ? new Vector2(viewport.x - 0.5f, viewport.y - 0.5f).sqrMagnitude : 100f + viewport.z * viewport.z;
            if (score < bestScore)
            {
                bestScore = score;
                best = unit;
            }
        }
        return best;
    }

    private void SetTarget(UnitHealthManager unit)
    {
        target = unit;
        targetHull = unit.GetComponent<HullDamage>();
        targetSquadron = targetHull == null ? unit.GetComponent<Squadron>() : null;
        follow = unit.transform;
        lostAt = -1f;
        Measure();
        NextShot();
    }

    private void Measure()
    {
        if (targetHull != null)
        {
            followCenter = targetHull.transform.TransformPoint(targetHull.LocalBounds.center);
            followLead = followCenter;
            followYaw = targetHull.transform.eulerAngles.y;
            followSize = Mathf.Max(8f, targetHull.Size);
            return;
        }

        if (targetSquadron != null)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            float spread = 0f;
            foreach (Fighter fighter in targetSquadron.fighters)
            {
                if (fighter == null || !fighter.Alive) continue;
                sum += fighter.transform.position;
                count++;
            }

            Fighter lead = LeadFighter(targetSquadron);
            if (count > 0)
            {
                followCenter = sum / count;
                foreach (Fighter fighter in targetSquadron.fighters)
                {
                    if (fighter != null && fighter.Alive) spread = Mathf.Max(spread, Vector3.Distance(fighter.transform.position, followCenter));
                }
            }
            else
            {
                followCenter = targetSquadron.transform.position;
            }

            followLead = lead != null ? lead.transform.position : followCenter;
            followYaw = lead != null ? lead.transform.eulerAngles.y : targetSquadron.transform.eulerAngles.y;
            followSize = Mathf.Max(squadronFraming, spread * 1.2f);
            return;
        }

        followCenter = target.transform.position;
        followLead = followCenter;
        followYaw = target.transform.eulerAngles.y;
    }

    private static Fighter LeadFighter(Squadron squadron)
    {
        foreach (Fighter fighter in squadron.fighters)
        {
            if (fighter != null && fighter.Alive) return fighter;
        }
        return null;
    }

    private void TrackTarget()
    {
        if (target != null)
        {
            Measure();

            if (target.IsDead && shotEndsAt < float.PositiveInfinity)
            {
                BeginShot(Shot.Orbit);
                shotEndsAt = float.PositiveInfinity;
                Vector3 toTarget = followCenter - camTransform.position;
                orbitAngle = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                snap = false;
            }
            return;
        }

        if (lostAt < 0f)
        {
            lostAt = Time.unscaledTime;
            follow = LargestWreckNear(followCenter, followSize);
        }

        if (follow != null) followCenter = follow.position;
        followLead = followCenter;

        if (Time.unscaledTime - lostAt < deathLinger) return;

        List<UnitHealthManager> units = Targets();
        if (units.Count == 0)
        {
            Exit();
            return;
        }

        UnitHealthManager nearest = units[0];
        float nearestDistance = float.MaxValue;
        foreach (UnitHealthManager unit in units)
        {
            float distance = (unit.transform.position - followCenter).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = unit;
            }
        }
        SetTarget(nearest);
    }

    private static Transform LargestWreckNear(Vector3 point, float range)
    {
        Transform best = null;
        float bestSize = 0f;
        foreach (WreckChunk chunk in FindObjectsByType<WreckChunk>(FindObjectsSortMode.None))
        {
            if (!chunk.IsPersistent || (chunk.transform.position - point).sqrMagnitude > range * range * 4f) continue;
            float size = 0f;
            foreach (Renderer renderer in chunk.GetComponentsInChildren<MeshRenderer>()) size += renderer.bounds.size.sqrMagnitude;
            if (size > bestSize)
            {
                bestSize = size;
                best = chunk.transform;
            }
        }
        return best;
    }

    private void NextShot()
    {
        if (overviewLocked)
        {
            BeginShot(Shot.Overview);
            return;
        }

        Shot next = shot;
        for (int attempt = 0; attempt < 8 && (next == shot || (next == Shot.Duel && Opponent() == null)); attempt++)
        {
            next = Rotation[Random.Range(0, Rotation.Length)];
        }
        BeginShot(next);
    }

    private void BeginShot(Shot next)
    {
        shot = next;
        shotStartedAt = Time.unscaledTime;
        shotEndsAt = Time.unscaledTime + Random.Range(shotDuration.x, shotDuration.y);
        if (shot == Shot.Overview && overviewLocked) shotEndsAt = float.PositiveInfinity; // Hold until V again.
        side = Random.value < 0.5f ? -1f : 1f;
        orbitAngle = followYaw + Random.Range(0f, 360f);
        snap = true;

        if (shot == Shot.Flyby)
        {
            Vector3 heading = Quaternion.Euler(0f, followYaw, 0f) * Vector3.forward;
            if (target != null && target.TryGetComponent(out NavMeshAgent agent) && agent.velocity.sqrMagnitude > 0.5f)
            {
                heading = agent.velocity.normalized;
            }
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            anchor = followCenter + heading * followSize * 1.1f + right * side * followSize * 0.4f + Vector3.up * followSize * 0.08f;
        }
    }

    private Vector3? Opponent()
    {
        if (target == null) return null;

        if (targetSquadron != null && targetSquadron.CurrentTarget != null && !targetSquadron.CurrentTarget.IsDead)
        {
            return targetSquadron.CurrentTarget.transform.position;
        }

        Transform best = null;
        float bestDistance = float.MaxValue;
        foreach (HullDamage ship in HullDamage.Active)
        {
            if (ship == null || !ship.IsAlive || ship.gameObject.layer == target.gameObject.layer) continue;
            float distance = (ship.transform.position - target.transform.position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = ship.transform;
            }
        }
        return best != null ? best.position : (Vector3?)null;
    }

    private void Frame()
    {
        float dt = Time.unscaledDeltaTime;
        float L = followSize;
        Vector3 c = followCenter;
        Vector3 lead = followLead;
        Quaternion yaw = Quaternion.Euler(0f, followYaw, 0f);
        float t = Mathf.Clamp01((Time.unscaledTime - shotStartedAt) / Mathf.Max(0.1f, shotEndsAt - shotStartedAt));

        Vector3 position;
        Vector3 look;
        float fov;

        switch (shot)
        {
            case Shot.Chase:
                position = lead + yaw * new Vector3(side * 0.08f, 0.2f, -0.85f) * L;
                look = lead + yaw * Vector3.forward * 0.45f * L;
                fov = 45f;
                break;
            case Shot.Broadside:
                position = lead + yaw * new Vector3(side * 0.75f, 0.06f, Mathf.Lerp(-0.35f, 0.35f, t)) * L;
                look = lead;
                fov = 40f;
                break;
            case Shot.LowBow:
                position = lead + yaw * new Vector3(side * 0.3f, -0.2f, 0.8f) * L;
                look = lead + Vector3.up * 0.05f * L;
                fov = 50f;
                break;
            case Shot.Bridge:
                position = lead + yaw * new Vector3(side * 0.12f, 0.35f, -0.4f) * L;
                look = lead + yaw * new Vector3(0f, 0.1f, 2.5f) * L;
                fov = 55f;
                break;
            case Shot.Flyby:
                position = anchor;
                look = c;
                fov = 35f;
                break;
            case Shot.Duel:
                Vector3? enemy = Opponent();
                if (enemy == null) goto default;
                Vector3 away = c - enemy.Value;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : yaw * Vector3.back;
                position = c + away * 0.9f * L + Vector3.up * 0.3f * L + Vector3.Cross(Vector3.up, away) * side * 0.3f * L;
                look = Vector3.Lerp(c, enemy.Value, 0.35f);
                fov = 50f;
                break;
            case Shot.Overview:
                BattleSpread(out Vector3 battleCentre, out float battleRadius);
                orbitAngle += orbitSpeed * 0.35f * dt;
                float overviewDistance = Mathf.Max(battleRadius * 1.6f, 220f);
                Quaternion high = Quaternion.Euler(52f, orbitAngle, 0f);
                position = battleCentre - high * Vector3.forward * overviewDistance;
                look = battleCentre;
                fov = 50f;
                break;
            default:
                bool death = lostAt >= 0f || (target != null && target.IsDead);
                orbitAngle += orbitSpeed * (death ? 0.6f : 1f) * dt;
                Quaternion around = Quaternion.Euler(death ? 28f : 22f, orbitAngle, 0f);
                position = c - around * Vector3.forward * (death ? 1.4f : 1.1f) * L;
                look = c;
                fov = 45f;
                break;
        }

        if (targetHull != null && shot != Shot.Bridge && shot != Shot.Chase)
        {
            Vector3 fromCenter = position - c;
            float minimum = L * 0.9f;
            if (fromCenter.sqrMagnitude < minimum * minimum)
            {
                position = c + (fromCenter.sqrMagnitude > 0.0001f ? fromCenter.normalized : Vector3.up) * minimum;
            }
        }

        position = AvoidRocks(position);

        Vector3 direction = look - position;
        Quaternion rotation = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction, Vector3.up) : camTransform.rotation;

        if (snap)
        {
            snap = false;
            moveVelocity = Vector3.zero;
            camTransform.SetPositionAndRotation(position, rotation);
            viewCamera.fieldOfView = fov;
            return;
        }

        float smoothing = targetSquadron != null ? positionSmoothing * 0.6f : positionSmoothing;
        camTransform.position = Vector3.SmoothDamp(camTransform.position, position, ref moveVelocity, smoothing, Mathf.Infinity, dt);
        camTransform.rotation = Quaternion.Slerp(camTransform.rotation, rotation, 1f - Mathf.Exp(-rotationSharpness * dt));
        viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, fov, 1f - Mathf.Exp(-fovSharpness * dt));
    }

    // Middle of everything still fighting, and how far the fighting spreads from it.
    private void BattleSpread(out Vector3 centre, out float radius)
    {
        centre = Vector3.zero;
        int count = 0;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead) continue;
            centre += unit.transform.position;
            count++;
        }

        if (count == 0)
        {
            centre = followCenter;
            radius = followSize;
            return;
        }

        centre /= count;
        radius = 0f;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead) continue;
            radius = Mathf.Max(radius, Vector3.Distance(unit.transform.position, centre));
        }
        radius = Mathf.Min(radius, 900f);
    }

    private static readonly Collider[] rockHits = new Collider[16];

    // Never park the camera inside an asteroid - push it out past the rock's visible size.
    private static Vector3 AvoidRocks(Vector3 position)
    {
        int mask = BattleTerrain.BlockingMask;
        if (mask == 0) return position;

        int count = Physics.OverlapSphereNonAlloc(position, 30f, rockHits, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!(rockHits[i] is SphereCollider rock)) continue;

            Vector3 centre = rock.transform.TransformPoint(rock.center);
            float scale = Mathf.Max(rock.transform.lossyScale.x, rock.transform.lossyScale.y, rock.transform.lossyScale.z);
            float clearance = rock.radius * scale * 1.6f + 4f; // Rocks look bigger than their colliders.
            Vector3 away = position - centre;
            if (away.sqrMagnitude >= clearance * clearance) continue;

            position = centre + (away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.up) * clearance;
        }
        return position;
    }

    private void OnDisable()
    {
        Exit();
    }
}
