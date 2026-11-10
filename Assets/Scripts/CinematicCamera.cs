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
    // In a fight the camera follows one pilot of the squadron, keeping whoever he's fighting in frame.
    private Fighter featured;
    private float featuredSince;
    private Vector3 featuredVelocity;
    private Vector3 lastSquadronCenter = Vector3.positiveInfinity;
    private float squadronYaw = float.NaN;
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
        if (Keybinds.Down(KeyAction.Overview))
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

        if (Keybinds.Down(KeyAction.CinematicCamera))
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

        if (PresentationClock.Now >= nextScanAt) HideInterface();
    }

    private void LateUpdate()
    {
        if (Active)
        {
            TrackTarget();
            if (Active)
            {
                bool watchingDeath = lostAt >= 0f || (target != null && target.IsDead);
                if (!watchingDeath && PresentationClock.Now >= shotEndsAt) NextShot();
                Frame();
            }
        }

        UpdateLook();
    }

    private void UpdateLook()
    {
        float goal = Active ? 1f : 0f;
        if (lookBlend == goal && !Active) return;

        lookBlend = Mathf.MoveTowards(lookBlend, goal, lookBlendSpeed * PresentationClock.Delta);
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
            UpdateBarFill(bar > 0.0005f);
        }
    }

    // The letterbox narrows the camera's viewport, and nothing draws outside it - in a build the bars would
    // show stale garbage. A camera that renders nothing, behind everything, clears the whole screen to black.
    private Camera barFill;

    private void UpdateBarFill(bool needed)
    {
        if (barFill == null)
        {
            if (!needed) return;
            barFill = new GameObject("LetterboxFill").AddComponent<Camera>();
            barFill.transform.SetParent(transform, false);
            barFill.clearFlags = CameraClearFlags.SolidColor;
            barFill.backgroundColor = Color.black;
            barFill.cullingMask = 0;
            barFill.depth = viewCamera.depth - 50f;
            barFill.useOcclusionCulling = false;
            barFill.allowHDR = false;
            barFill.allowMSAA = false;
        }
        if (barFill.enabled != needed) barFill.enabled = needed;
    }

    private float savedFarClip;
    private float savedLodBias = 1f;

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

        // The RTS view's draw distance and LOD settings cull ships that are small or far on screen; cinematic
        // shots are wider and lower, so open both up while directing (restored on Exit).
        savedFarClip = viewCamera.farClipPlane;
        savedLodBias = QualitySettings.lodBias;
        viewCamera.farClipPlane = Mathf.Max(savedFarClip, 6000f);
        QualitySettings.lodBias = Mathf.Max(savedLodBias, 4f);

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
        if (viewCamera != null)
        {
            viewCamera.fieldOfView = savedFov;
            viewCamera.farClipPlane = savedFarClip;
        }
        QualitySettings.lodBias = savedLodBias;

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
        overviewLocked = false;
        SetTarget(unit);
    }

    // Scripted direction (recordings): watch this unit from a slow orbit (good for watching something happen to it).
    public void SpectateOrbit(UnitHealthManager unit)
    {
        Spectate(unit);
        if (target == unit) BeginShot(Shot.Orbit);
    }

    // Scripted direction (recordings): hold the wide view of the whole battle until the next Spectate.
    public void ShowOverview()
    {
        if (!Active) Enter();
        if (!Active) return;
        overviewLocked = true;
        BeginShot(Shot.Overview);
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
        nextScanAt = PresentationClock.Now + 0.25f; // A full scene search: a few times a second catches new UI without costing every frame.

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
        // Hold the subject at full detail: hull scorch marks and breaches are built on the LOD0 model, and a
        // lower LOD's slightly different surface hides them - flipping LODs as the camera moves made damaged
        // hulls flicker between charred and clean.
        ForceDetail(target, false);
        ForceDetail(unit, true);
        target = unit;
        targetHull = unit.GetComponent<HullDamage>();
        targetSquadron = targetHull == null ? unit.GetComponent<Squadron>() : null;
        lastSquadronCenter = Vector3.positiveInfinity;
        squadronYaw = float.NaN;
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
            if (count > 0 && lead != null)
            {
                // The formation around the lead: craft that have strayed far off (dogfighting, going down)
                // would otherwise blow the frame up until every fighter is a dot.
                Vector3 leadAt = lead.transform.position;
                Vector3 close = Vector3.zero;
                int near = 0;
                foreach (Fighter fighter in targetSquadron.fighters)
                {
                    if (fighter == null || !fighter.Alive || Vector3.Distance(fighter.transform.position, leadAt) > FormationReach) continue;
                    close += fighter.transform.position;
                    near++;
                }
                followCenter = near > 0 ? close / near : leadAt;
                foreach (Fighter fighter in targetSquadron.fighters)
                {
                    if (fighter == null || !fighter.Alive) continue;
                    float d = Vector3.Distance(fighter.transform.position, followCenter);
                    if (d <= FormationReach) spread = Mathf.Max(spread, d);
                }
            }
            else if (count > 0)
            {
                followCenter = sum / count;
            }
            else
            {
                followCenter = targetSquadron.transform.position;
            }

            // Frame the whole squadron, not the lead fighter: fighters jink constantly, so the camera follows
            // the group's smoothed direction of travel and keeps a wider, steadier frame.
            followLead = followCenter;
            Vector3 travel = followCenter - lastSquadronCenter;
            travel.y = 0f;
            float dt = Mathf.Max(0.0001f, PresentationClock.Delta);
            if (lastSquadronCenter != Vector3.positiveInfinity && travel.sqrMagnitude > 0.0001f && travel.magnitude / dt < 400f)
            {
                float rawYaw = Mathf.Atan2(travel.x, travel.z) * Mathf.Rad2Deg;
                squadronYaw = float.IsNaN(squadronYaw) ? rawYaw : Mathf.LerpAngle(squadronYaw, rawYaw, 1f - Mathf.Exp(-0.7f * dt));
            }
            else if (float.IsNaN(squadronYaw))
            {
                squadronYaw = lead != null ? lead.transform.eulerAngles.y : targetSquadron.transform.eulerAngles.y;
            }
            lastSquadronCenter = followCenter;
            followYaw = squadronYaw;
            followSize = Mathf.Max(squadronFraming * 1.2f, spread * 1.3f);
            return;
        }

        followCenter = target.transform.position;
        followLead = followCenter;
        followYaw = target.transform.eulerAngles.y;
    }

    private const float FormationReach = 45f;

    private static void ForceDetail(UnitHealthManager unit, bool full)
    {
        if (unit == null || (!full && unit.IsDead)) return;
        foreach (LODGroup group in unit.GetComponentsInChildren<LODGroup>(true)) group.ForceLOD(full ? 0 : -1);
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
            lostAt = PresentationClock.Now;
            follow = LargestWreckNear(followCenter, followSize);
        }

        if (follow != null) followCenter = follow.position;
        followLead = followCenter;

        if (PresentationClock.Now - lostAt < deathLinger) return;

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

        // The overhead wide is a rare change of pace, not one angle in eight: close angles carry the action.
        Shot next = shot;
        for (int attempt = 0; attempt < 8 && (next == shot || (next == Shot.Duel && Opponent() == null) || (next == Shot.Overview && Random.value > 0.25f)); attempt++)
        {
            next = Rotation[Random.Range(0, Rotation.Length)];
        }
        BeginShot(next);
    }

    private void BeginShot(Shot next)
    {
        shot = next;
        shotStartedAt = PresentationClock.Now;
        shotEndsAt = PresentationClock.Now + Random.Range(shotDuration.x, shotDuration.y);
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

    // A squadron in a fight (not just flying somewhere, and not bombers on a run, where the formation is the
    // show): follow one pilot. Keep him while he's alive or going down in flames; then pick the busiest.
    private Fighter Featured()
    {
        if (targetSquadron == null || targetSquadron.IsMoving || targetSquadron.FlyingBombingRuns || !targetSquadron.Engaging)
        {
            featured = null;
            return null;
        }
        bool keep = featured != null && featured.gameObject.activeInHierarchy && featured.transform.IsChildOf(targetSquadron.transform);
        if (keep && !featured.Alive && !featured.IsDying) keep = false;
        if (keep && PresentationClock.Now - featuredSince > 12f && featured.Alive && featured.Interest < 1f) keep = false; // Gone quiet: find the action.
        if (!keep)
        {
            Fighter best = null;
            float bestScore = float.MinValue;
            foreach (Fighter fighter in targetSquadron.fighters)
            {
                if (fighter == null || !fighter.Alive) continue;
                float score = fighter.Interest + Random.value * 0.5f;
                if (score > bestScore) { bestScore = score; best = fighter; }
            }
            if (best != featured) { featuredSince = PresentationClock.Now; snap = featured == null; }
            featured = best;
        }
        return featured;
    }

    // Behind the pilot and a little above, pulled back far enough that whoever he's fighting - the bandit
    // he's chasing, the one on his tail, the ship he's strafing - is in the shot too.
    private void FrameFighter(Fighter pilot, float dt, out Vector3 position, out Vector3 look, out float fov)
    {
        Vector3 here = pilot.transform.position;
        Vector3 velocity = pilot.Velocity.sqrMagnitude > 1f ? pilot.Velocity : pilot.transform.forward * 20f;
        featuredVelocity = Vector3.Lerp(featuredVelocity.sqrMagnitude > 0.01f ? featuredVelocity : velocity, velocity, 1f - Mathf.Exp(-2.5f * dt));
        Vector3 heading = featuredVelocity.normalized;

        Vector3? other = null;
        if (pilot.Pursuer != null && pilot.Pursuer.gameObject.activeInHierarchy) other = pilot.Pursuer.transform.position;
        else if (pilot.Prey != null && pilot.Prey.gameObject.activeInHierarchy) other = pilot.Prey.transform.position;
        else if (pilot.ShipAim != null) other = pilot.ShipAim.position;

        // Only bring the other party into the shot when it's close - pulling back to fit a distant one shrinks
        // the pilot to a speck.
        if (other.HasValue && Vector3.Distance(here, other.Value) > 55f) other = null;
        float spread = other.HasValue ? Vector3.Distance(here, other.Value) : 0f;
        float back = Mathf.Clamp(6f + spread * 0.3f, 6f, 20f);
        Vector3 side = Vector3.Cross(Vector3.up, heading);
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        side.Normalize();

        position = here - heading * back + Vector3.up * back * 0.28f + side * this.side * back * 0.18f;
        // Look between the two, weighted toward the pilot so he stays the subject.
        look = other.HasValue ? Vector3.Lerp(here, other.Value, 0.38f) : here + heading * 12f;
        fov = Mathf.Lerp(48f, 60f, Mathf.InverseLerp(0f, 55f, spread));
    }

    private void Frame()
    {
        float dt = PresentationClock.Delta;
        float L = followSize;
        Vector3 c = followCenter;
        Vector3 lead = followLead;
        Quaternion yaw = Quaternion.Euler(0f, followYaw, 0f);
        float t = Mathf.Clamp01((PresentationClock.Now - shotStartedAt) / Mathf.Max(0.1f, shotEndsAt - shotStartedAt));

        Vector3 position;
        Vector3 look;
        float fov;

        Fighter pilot = Featured();
        if (pilot != null)
        {
            FrameFighter(pilot, dt, out position, out look, out fov);
            goto Apply;
        }

        // Close angles (bow, bridge, broadside) don't work on a swarm of jinking fighters: use the wider ones.
        Shot framing = shot;
        if (targetSquadron != null && (framing == Shot.LowBow || framing == Shot.Bridge || framing == Shot.Broadside)) framing = Shot.Chase;

        switch (framing)
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

        Apply:
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

        // A featured pilot is chased tightly - fighters move 30-50 units a second and turn hard, so a lazy camera
        // loses them out of the frame; a formation is framed a little more lazily.
        float smoothing = pilot != null ? positionSmoothing * 0.18f : targetSquadron != null ? positionSmoothing * 0.9f : positionSmoothing;
        float sharpness = pilot != null ? rotationSharpness * 2.5f : targetSquadron != null ? rotationSharpness * 1.6f : rotationSharpness;
        camTransform.position = Vector3.SmoothDamp(camTransform.position, position, ref moveVelocity, smoothing, Mathf.Infinity, dt);
        camTransform.rotation = Quaternion.Slerp(camTransform.rotation, rotation, 1f - Mathf.Exp(-sharpness * dt));
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
