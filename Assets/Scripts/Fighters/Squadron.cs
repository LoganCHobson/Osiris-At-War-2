using System.Collections.Generic;
using SolarStudios;
using UnityEngine;

public class Squadron : MonoBehaviour
{
    [Header("Engagement")]
    public float engageRadius = 140f;
    public float dogfightRadius = 90f;
    public float leashRadius = 170f;
    public float thinkInterval = 0.4f;

    [Header("Role")]
    [Min(0f)] public float shipPriority = 1f;
    [Min(0f)] public float fighterPriority = 1.5f;

    [Header("Formation")]
    public float formationSpacing = 8f;
    public float formationDepth = 7f;

    [Header("Allegiance")]
    public LayerMask targetLayer;
    public GameObject projectilePrefab;

    [Header("Visuals")]
    public SquadronIcon icon;

    public readonly List<Fighter> fighters = new List<Fighter>();

    // What kind of craft these are (from the prefab's name): fighters and interceptors dogfight and strafe;
    // bombers fly formed-up bombing runs. Avanti craft fire lasers; Human craft fire tracer rounds.
    public enum Role { Fighter, Interceptor, Bomber }
    public Role CraftRole { get; private set; }
    public bool Avanti { get; private set; }

    public UnitHealthManager Health { get; private set; }
    public SpaceUnit Unit { get; private set; }
    public UnitHealthManager CurrentTarget { get; private set; }
    public Squadron EnemySquadron { get; private set; }

    public bool IsMoving => Unit != null && Unit.stateMachine != null && Unit.moveState != null
        && (Object)Unit.stateMachine.currentState == Unit.moveState && Unit.moveState.destinations.Count > 0;

    public bool Retreating => RetreatManager.Instance != null && Health != null && RetreatManager.Instance.IsRetreating(Health.isAttackerSide);

    // Fighters attack when the squadron has a target and isn't just flying somewhere. An attack order counts
    // as soon as the ordered target is within reach, even while the squadron is still sliding into place -
    // otherwise any repositioning near the target puts the whole squadron back into cruise formation.
    public bool Engaging => CurrentTarget != null && !Retreating && !GameManager.CombatOver && (Health == null || !Health.HoldFire) && !BattleHazards.InIonStorm(transform.position)
        && (!IsMoving || Escorting != null || (CurrentTarget == orderedTarget && Vector3.Distance(CurrentTarget.transform.position, transform.position) <= engageRadius));

    public float AnchorSpeed { get; private set; }

    // The height fighters keep to: their target's while fighting (it may be far above or below the
    // squadron in a 3D battle), the bombers' while escorting, otherwise the squadron's own.
    public float FlightHeight => Engaging && CurrentTarget != null ? CurrentTarget.transform.position.y
        : Escorting != null ? Escorting.FlightHeight : transform.position.y;

    // Where the fighters' leash is tied: the squadron, or the bombers it's escorting.
    public Vector3 LeashCentre
    {
        get
        {
            if (Escorting == null) return transform.position;
            Fighter lead = Escorting.LeadCraft();
            return lead != null ? lead.transform.position : Escorting.transform.position;
        }
    }

    public static readonly List<Squadron> Active = new List<Squadron>();

    private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }

    private void OnDisable()
    {
        Active.Remove(this);
        EndEscort();
        if (EscortedBy != null) EscortedBy.EndEscort();
    }

    // The first craft still flying: the formation lead.
    public Fighter LeadCraft()
    {
        foreach (Fighter f in fighters) if (f != null && f.Alive) return f;
        return null;
    }

    // ---------------- Two-ship elements ----------------
    // Human fighters and interceptors often fight in pairs - a lead and a wingman - but not always: each time
    // the squadron goes into a fight, neighbours in the formation pair up with PairChance. A pair broken by a
    // loss is made good from the other singletons where possible. Avanti pilots fly alone.
    public bool FliesPairs => !Avanti && CraftRole != Role.Bomber;
    public const float PairChance = 0.7f;
    private bool wasEngaging;
    private float disengagedSince = -100f;

    private void FormElements(bool reroll)
    {
        if (!FliesPairs) return;
        List<Fighter> loose = new List<Fighter>();
        foreach (Fighter f in fighters)
        {
            if (f == null) continue;
            if (!f.Alive) { f.LeaveElement(); continue; }
            if (!reroll && f.Partner != null && f.Partner.Alive && f.Partner.Partner == f) continue;
            f.LeaveElement();
            loose.Add(f);
        }
        for (int i = 0; i + 1 < loose.Count; i += 2)
        {
            if (Random.value < PairChance) Fighter.Pair(loose[i], loose[i + 1]);
        }
    }

    private void KeepElements()
    {
        if (!FliesPairs) return;
        // A fresh roll of who pairs with whom only for a new fight, not every brief gap between targets.
        bool engaging = Engaging || Escorting != null;
        if (engaging) { if (!wasEngaging && Time.time - disengagedSince > 5f) FormElements(true); }
        else if (wasEngaging) disengagedSince = Time.time;
        wasEngaging = engaging;
        foreach (Fighter f in fighters)
        {
            if (f == null || f.Partner == null) continue;
            if (!f.Partner.Alive || !f.Alive) { FormElements(false); return; }
        }
    }

    // ---------------- Escort ----------------
    // Fighters and interceptors that have nothing particular to do sometimes fly cover for a friendly bomber
    // squadron on its runs: they keep loose formation around the bombers, and when enemy craft come in on
    // them they break off and take them on, then come back to the bombers. A specific order ends it.
    public Squadron Escorting { get; private set; }
    public Squadron EscortedBy { get; private set; }
    public const float EscortChance = 0.45f;
    public const float EscortThreatRadius = 90f;
    private float nextEscortRollAt;
    private float escortIdleSince = -1f;
    private bool escortCommanded;

    // Each tasking pass (the AI commander's, or an idle squadron's own): keep up an escort, or now and then
    // start one. True while escorting.
    public bool ConsiderEscort(bool commanded)
    {
        if (Escorting != null)
        {
            if (EscortStillWanted()) return true;
            EndEscort();
            nextEscortRollAt = Time.time + Random.Range(8f, 14f);
        }
        if (CraftRole == Role.Bomber || Retreating || GameManager.CombatOver || Time.time < nextEscortRollAt) return false;
        nextEscortRollAt = Time.time + Random.Range(12f, 20f);
        if (Random.value > EscortChance) return false;
        Squadron bombers = BombersToEscort(commanded ? 500f : 230f);
        if (bombers == null) return false;

        orderedTarget = null;
        orderedAim = null;
        CurrentTarget = null;
        EnemySquadron = null;
        Escorting = bombers;
        bombers.EscortedBy = this;
        escortCommanded = commanded;
        escortIdleSince = -1f;
        FormElements(true);
        return true;
    }

    public void EndEscort()
    {
        if (Escorting == null) return;
        if (Escorting.EscortedBy == this) Escorting.EscortedBy = null;
        Escorting = null;
        CurrentTarget = null; // Whatever it was fighting was the escort's business.
        EnemySquadron = null;
    }

    private bool EscortStillWanted()
    {
        Squadron b = Escorting;
        if (b == null || !b.isActiveAndEnabled || b.Health == null || b.Health.IsDead || b.LeadCraft() == null) return false;
        if (Retreating || GameManager.CombatOver || (!escortCommanded && IsMoving)) return false;
        if (b.Engaging) { escortIdleSince = -1f; return true; }
        if (escortIdleSince < 0f) escortIdleSince = Time.time;
        return Time.time - escortIdleSince < 8f;
    }

    // A friendly bomber squadron out on bombing runs within reach that nobody is escorting yet.
    private Squadron BombersToEscort(float reach)
    {
        Squadron best = null;
        float bestDistance = reach * reach;
        foreach (Squadron other in Active)
        {
            if (other == null || other == this || other.CraftRole != Role.Bomber || other.gameObject.layer != gameObject.layer) continue;
            if (other.EscortedBy != null || !other.FlyingBombingRuns || other.Health == null || other.Health.IsDead) continue;
            Fighter lead = other.LeadCraft();
            if (lead == null) continue;
            float d = (lead.transform.position - transform.position).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = other; }
        }
        return best;
    }

    // Enemy craft coming in on the bombers: within EscortThreatRadius of them, or with the bombers as their target.
    private Squadron EscortThreat()
    {
        Fighter lead = Escorting.LeadCraft();
        if (lead == null) return null;
        Vector3 at = lead.transform.position;
        Squadron best = null;
        float bestDistance = float.MaxValue;
        foreach (Squadron enemy in Active)
        {
            if (enemy == null || enemy.CraftRole == Role.Bomber || enemy.Health == null || enemy.Health.IsDead || !IsHostile(enemy.Health)) continue;
            bool targeting = enemy.CurrentTarget == Escorting.Health || enemy.CurrentTarget == Health;
            float reach = EnemySquadron == enemy ? EscortThreatRadius * 1.6f : targeting ? EscortThreatRadius * 1.5f : EscortThreatRadius;
            foreach (Fighter f in enemy.fighters)
            {
                if (f == null || !f.Alive) continue;
                float d = Vector3.Distance(f.transform.position, at);
                if (d < reach && d < bestDistance) { bestDistance = d; best = enemy; }
            }
        }
        return best;
    }

    // Place of each escort in the box around the bombers: wingmen fly on their leads, so only leads and
    // singletons take places.
    public int EscortSlot(Fighter fighter)
    {
        int slot = 0;
        foreach (Fighter f in fighters)
        {
            if (f == fighter) return slot;
            if (f != null && f.Alive && !f.IsWingman) slot++;
        }
        return slot;
    }

    // ---------------- Formations on the lead ----------------
    // A bomber's place relative to the lead bomber while flying a run: the Human V (vic), or Avanti line astern
    // (one behind another, each a little lower so it isn't flying through the wake ahead).
    public const float LineAsternSpacing = 10f;

    public Vector3 FormationSlot(Fighter fighter, Fighter lead, Vector3 heading)
    {
        int rank = 0;
        foreach (Fighter f in fighters)
        {
            if (f == fighter) break;
            if (f != null && f.Alive) rank++;
        }
        Vector3 offset = Avanti
            ? new Vector3(0f, -rank * 0.9f, -rank * LineAsternSpacing)
            : (FormationOffset(rank) - FormationOffset(0)) * 0.6f;
        Quaternion frame = Quaternion.LookRotation(heading.sqrMagnitude > 0.01f ? heading : Vector3.forward);
        return lead.transform.position + frame * offset;
    }

    // Where the run is headed: the lead bomber's aim point (all the formation drops on it).
    public Vector3 RunAimPoint()
    {
        Fighter lead = LeadCraft();
        if (lead != null && lead.AimPoint != null && lead.AimPoint.gameObject.activeInHierarchy) return lead.AimPoint.position;
        return CurrentTarget != null ? CurrentTarget.transform.position : transform.position;
    }

    // A formation flying together right now: a bombing run, or a squadron cruising with nothing to fight.
    public bool InFormation => FlyingBombingRuns || (!Engaging && Escorting == null);

    private Transform orderedAim;
    private UnitHealthManager orderedTarget;
    private float nextThinkAt;
    private Vector3 lastAnchorPosition;
    private bool hasAnchorPosition;

    private void Awake()
    {
        Unit = GetComponent<SpaceUnit>();
        Health = GetComponent<UnitHealthManager>();
        string kind = name.ToLowerInvariant();
        CraftRole = kind.Contains("bomber") ? Role.Bomber : kind.Contains("interceptor") ? Role.Interceptor : Role.Fighter;
        Avanti = kind.Contains("avanti");

        GetComponentsInChildren(true, fighters);
        for (int i = 0; i < fighters.Count; i++)
        {
            fighters[i].Bind(this, FormationOffset(i));
            if (Avanti && CraftRole == Role.Fighter) FighterShield.Fit(fighters[i]); // Avanti fighters carry shields.
            if (!Avanti && CraftRole == Role.Interceptor) CraftEvasion.Fit(fighters[i]); // Human interceptors jink.
        }

        PlayerUnitMoveState moveState = GetComponentInChildren<PlayerUnitMoveState>(true);
        if (moveState != null) moveState.airborne = true;

        if (Unit != null && icon != null)
        {
            Unit.onSelected.AddListener(() => icon.SetSelected(true));
            Unit.onDeSelected.AddListener(() => icon.SetSelected(false));
        }
    }

    public void SetAllegiance(LayerMask hostileLayer, GameObject projectile, bool playerSide)
    {
        targetLayer = hostileLayer;
        if (projectile != null) projectilePrefab = projectile;
        icon?.SetSide(playerSide);
    }

    public Vector3 FormationOffset(int index)
    {
        int lastRow = Mathf.Max(1, fighters.Count / 2);
        float lead = lastRow * formationDepth * 0.5f;
        if (index == 0) return new Vector3(0f, 0f, lead);

        int row = (index + 1) / 2;
        float side = index % 2 == 1 ? -1f : 1f;
        return new Vector3(side * row * formationSpacing, 0f, lead - row * formationDepth);
    }

    public void ClearTarget()
    {
        EndEscort();
        nextEscortRollAt = Time.time + 20f; // Just given an order: don't wander off to escort straight away.
        orderedTarget = null;
        orderedAim = null;
        CurrentTarget = null;
        EnemySquadron = null;
        nextThinkAt = Time.time + thinkInterval;
    }

    public void AssignTarget(Transform aim)
    {
        UnitHealthManager target = aim != null ? aim.GetComponentInParent<UnitHealthManager>() : null;
        if (target == null || target == Health || !IsHostile(target)) return;

        EndEscort();
        orderedAim = aim;
        orderedTarget = target;
        CurrentTarget = target;
        EnemySquadron = target.GetComponent<Squadron>();

        // Go to the target's height so the fighters stay within their leash of the squadron.
        if (TryGetComponent(out ShipAltitude altitude)) altitude.SetTargetAltitude(ShipAltitude.AltitudeOf(target.transform));
    }

    // ---------------- Bombing runs ----------------
    // A bomber squadron attacking a ship works as one: it forms up off to one side of the target, runs in
    // together - a tight V for the Humans, line astern for the Avanti - each bomber releasing as it reaches
    // the drop point, breaks away together, then swings round to come in again from a fresh angle.
    public enum RunPhase { FormUp, Run, Egress }
    public RunPhase BomberPhase { get; private set; } = RunPhase.FormUp;
    public Vector3 RunDirection { get; private set; } = Vector3.forward; // From the form-up point toward the target.
    private float runPhaseEndsAt;
    private UnitHealthManager runTarget;
    public const float RunStandoff = 95f;
    public const float RunDropDistance = 34f;

    public bool FlyingBombingRuns => CraftRole == Role.Bomber && Engaging && CurrentTarget != null && EnemySquadron == null;

    private void UpdateBomberRun()
    {
        if (!FlyingBombingRuns)
        {
            runTarget = null;
            BomberPhase = RunPhase.FormUp;
            return;
        }

        Vector3 target = CurrentTarget.transform.position;
        if (runTarget != CurrentTarget)
        {
            runTarget = CurrentTarget;
            Vector3 from = transform.position - target;
            from.y = 0f;
            RunDirection = from.sqrMagnitude > 1f ? -from.normalized : transform.forward;
            BomberPhase = RunPhase.FormUp;
            runPhaseEndsAt = Time.time + 9f;
        }

        Vector3 lead = LeadPosition();
        switch (BomberPhase)
        {
            case RunPhase.FormUp:
                // Formed up (or waited long enough): go.
                if (Time.time >= runPhaseEndsAt || FormedUp(FormUpPoint(), 14f))
                {
                    BomberPhase = RunPhase.Run;
                    runPhaseEndsAt = Time.time + 14f;
                }
                break;
            case RunPhase.Run:
                Vector3 toTarget = target - lead;
                if (Time.time >= runPhaseEndsAt || Vector3.Dot(toTarget, RunDirection) < -6f || toTarget.magnitude < RunDropDistance * 0.4f)
                {
                    BomberPhase = RunPhase.Egress;
                    runPhaseEndsAt = Time.time + Random.Range(3.5f, 5f);
                }
                break;
            case RunPhase.Egress:
                if (Time.time >= runPhaseEndsAt)
                {
                    // Come round for another pass from a different angle.
                    RunDirection = Quaternion.Euler(0f, Random.Range(50f, 110f) * (Random.value < 0.5f ? -1f : 1f), 0f) * RunDirection;
                    BomberPhase = RunPhase.FormUp;
                    runPhaseEndsAt = Time.time + 9f;
                }
                break;
        }
    }

    public Vector3 FormUpPoint()
    {
        Vector3 target = CurrentTarget != null ? CurrentTarget.transform.position : transform.position;
        return target - RunDirection * RunStandoff + Vector3.up * 8f;
    }

    public Vector3 EgressPoint()
    {
        Vector3 target = CurrentTarget != null ? CurrentTarget.transform.position : transform.position;
        Vector3 away = Quaternion.Euler(0f, 55f, 0f) * RunDirection;
        return target + away * RunStandoff * 0.9f + Vector3.up * 22f;
    }

    // Each bomber's place in the run's V, given the point the formation is heading for.
    public Vector3 RunSlot(int index, Vector3 formationPoint, Vector3 heading)
    {
        Vector3 offset = FormationOffset(index) * 0.6f; // Tighter than cruise formation.
        Quaternion frame = Quaternion.LookRotation(heading.sqrMagnitude > 0.01f ? heading : Vector3.forward);
        return formationPoint + frame * offset;
    }

    // The lead is at the form-up point and the others are on station on him.
    private bool FormedUp(Vector3 point, float tolerance)
    {
        Fighter lead = LeadCraft();
        if (lead == null) return true;
        if (Vector3.Distance(lead.transform.position, point) > tolerance * 2.5f) return false;
        Vector3 heading = lead.Velocity.sqrMagnitude > 1f ? lead.Velocity.normalized : lead.transform.forward;
        foreach (Fighter f in fighters)
        {
            if (f == null || !f.Alive || f == lead) continue;
            if (Vector3.Distance(f.transform.position, FormationSlot(f, lead, heading)) > tolerance) return false;
        }
        return true;
    }

    private Vector3 LeadPosition()
    {
        foreach (Fighter f in fighters) if (f != null && f.Alive) return f.transform.position;
        return transform.position;
    }

    // ---------------- Weapons ----------------

    private static readonly Dictionary<GameObject, GameObject> craftLasers = new Dictionary<GameObject, GameObject>();

    // Avanti craft: a smaller, quicker bolt than the warships' (a scaled-down copy of their laser, pooled).
    public void FireCraftLaser(Vector3 origin, Vector3 direction, float damage, float speed, Missile intercept = null)
    {
        if (projectilePrefab == null || GameManager.CombatOver) return;
        if (Health != null) Health.LastFiredAt = Time.time;
        GameObject template = CraftLaserTemplate(projectilePrefab);
        ObjectPool pool = ObjectPool.GetPoolFor(template);
        Quaternion rotation = Quaternion.LookRotation(direction);
        GameObject shot = pool != null ? pool.Spawn(origin, rotation) : null;
        if (shot == null) return;
        Laser laser = shot.GetComponent<Laser>();
        if (laser == null) return;
        laser.damage = damage;
        laser.layer = targetLayer;
        laser.fromWarship = false;
        laser.timeOut = Mathf.Max(0.4f, leashRadius * 0.6f / Mathf.Max(1f, speed));
        laser.SetSourcePool(pool);
        laser.source = Health;
        laser.weapon = DamageLedger.Weapon.CraftGuns;
        laser.intercept = intercept;
        DamageLedger.Fired(Health, DamageLedger.Weapon.CraftGuns);
        laser.Launch(direction * speed);
    }

    private static GameObject CraftLaserTemplate(GameObject prefab)
    {
        if (craftLasers.TryGetValue(prefab, out GameObject template) && template != null) return template;
        template = Instantiate(prefab);
        template.name = prefab.name + "_Craft";
        template.SetActive(false);
        template.transform.localScale = prefab.transform.localScale * 0.45f;
        craftLasers[prefab] = template;

        GameObject host = new GameObject(template.name + "Pool");
        host.SetActive(false);
        ObjectPool pool = host.AddComponent<ObjectPool>();
        pool.prefab = template;
        pool.poolSize = 40;
        pool.canGrow = true;
        // Only serialized pools get their events created for them.
        pool.onSpawn = new UnityEngine.Events.UnityEvent();
        pool.onRecycle = new UnityEngine.Events.UnityEvent();
        pool.onRecycleAll = new UnityEngine.Events.UnityEvent();
        pool.onInitalize = new UnityEngine.Events.UnityEvent();
        host.SetActive(true); // Registers the pool for this template.
        return template;
    }

    // Human craft: a burst of tracer rounds (drawn and hit-tested by CIWSTracers) at a fighter or hardpoint.
    public void FireTracers(Vector3 origin, Vector3 direction, float damagePerRound, HardpointHealth victim, float speed, float range, Missile missile = null)
    {
        if (GameManager.CombatOver) return;
        if (Health != null) Health.LastFiredAt = Time.time;
        CIWSTracers.Fire(origin, direction * speed, range / speed * 1.2f, damagePerRound, missile, victim, Health, DamageLedger.Weapon.CraftGuns);
        DamageLedger.Fired(Health, DamageLedger.Weapon.CraftGuns);
    }

    public void FireMissile(MissileArsenal.Warhead warhead, Vector3 origin, Vector3 direction, UnitHealthManager target, float range)
    {
        if (warhead == null || target == null || GameManager.CombatOver) return;
        if (Health != null) Health.LastFiredAt = Time.time;
        Missile fired = Missile.Fire(warhead, origin, direction, target, gameObject.layer, targetLayer, range * 1.8f / Mathf.Max(1f, warhead.speed));
        if (fired != null) { fired.source = Health; DamageLedger.Fired(Health, fired.LedgerWeapon); }
    }

    private void Update()
    {
        TrackAnchor();
        UpdateBomberRun();
        KeepElements();

        if (Time.time < nextThinkAt || Health == null || Health.IsDead) return;
        nextThinkAt = Time.time + thinkInterval * Random.Range(0.85f, 1.15f);

        if (orderedTarget != null && (orderedTarget.IsDead || !orderedTarget.gameObject.activeInHierarchy))
        {
            orderedTarget = null;
            orderedAim = null;
        }

        // Escorting: fight only what comes in on the bombers. An idle squadron with no orders may take up
        // an escort of its own accord (the AI commander tasks its squadrons through ConsiderEscort too).
        if (Escorting != null || (orderedTarget == null && !IsMoving && CraftRole != Role.Bomber))
        {
            if (ConsiderEscort(escortCommanded && Escorting != null))
            {
                Squadron threat = EscortThreat();
                CurrentTarget = threat != null ? threat.Health : null;
                EnemySquadron = threat;
                return;
            }
        }

        UnitHealthManager target = orderedTarget != null ? orderedTarget : AcquireNearest();
        if (target != null && Vector3.Distance(target.transform.position, transform.position) > leashRadius + 60f && target != orderedTarget)
        {
            target = null;
        }

        CurrentTarget = target;
        EnemySquadron = target != null ? target.GetComponent<Squadron>() : null;
    }

    private void TrackAnchor()
    {
        float dt = Time.deltaTime;
        Vector3 position = transform.position;
        if (hasAnchorPosition && dt > 0f)
        {
            float speed = (position - lastAnchorPosition).magnitude / dt;
            AnchorSpeed = Mathf.Lerp(AnchorSpeed, speed, Mathf.Clamp01(dt * 6f));
        }
        lastAnchorPosition = position;
        hasAnchorPosition = true;
    }

    private UnitHealthManager AcquireNearest()
    {
        UnitHealthManager best = null;
        float bestScore = float.MaxValue;
        Vector3 origin = transform.position;

        foreach (UnitHealthManager candidate in UnitHealthManager.Active)
        {
            if (candidate == null || candidate == Health || candidate.IsDead || !IsHostile(candidate)) continue;
            if (!BattleSensors.CanSee(this, candidate)) continue;

            bool isSquadron = candidate.TryGetComponent(out Squadron _);
            float priority = isSquadron ? fighterPriority : shipPriority;
            // Bombers only take on corvettes - point-defence boats that shred bomber wings - when nothing else is near.
            if (CraftRole == Role.Bomber && candidate.TryGetComponent(out SpaceUnit su) && su.shipType == ShipType.Corvette) priority *= 0.15f;
            float reach = isSquadron ? dogfightRadius : engageRadius;
            if (priority <= 0f) continue;

            float distance = Vector3.Distance(candidate.transform.position, origin);
            if (distance > reach) continue;

            float score = distance / priority;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    public bool IsHostile(UnitHealthManager other)
    {
        return other != null && (targetLayer.value & (1 << other.gameObject.layer)) != 0;
    }

    public Transform PickAimPoint()
    {
        if (CurrentTarget == null) return null;

        HardpointManager hardpoints = CurrentTarget.GetComponent<HardpointManager>();
        if (CurrentTarget == orderedTarget && orderedAim != null && orderedAim.gameObject.activeInHierarchy)
        {
            HardpointHealth ordered = orderedAim.GetComponentInParent<HardpointHealth>();
            if (ordered == null || hardpoints == null || hardpoints.hardpoints.Contains(ordered)) return orderedAim;
        }

        return hardpoints != null ? hardpoints.GetRandomHardpoint() : CurrentTarget.transform;
    }

    public Fighter PickEnemyFighter(Vector3 from)
    {
        if (EnemySquadron == null) return null;

        Fighter best = null;
        float bestDistance = float.MaxValue;
        foreach (Fighter fighter in EnemySquadron.fighters)
        {
            if (fighter == null || !fighter.Alive) continue;

            float distance = (fighter.transform.position - from).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = fighter;
            }
        }
        return best;
    }

    public void FireLaser(Vector3 origin, Vector3 direction, float damage, float speed)
    {
        if (projectilePrefab == null || GameManager.CombatOver) return;
        if (Health != null) Health.LastFiredAt = Time.time;

        Quaternion rotation = Quaternion.LookRotation(direction);
        ObjectPool pool = ObjectPool.GetPoolFor(projectilePrefab);
        GameObject shot = pool != null ? pool.Spawn(origin, rotation) : Instantiate(projectilePrefab, origin, rotation);
        if (shot == null) return;

        Laser laser = shot.GetComponent<Laser>();
        if (laser == null) return;

        laser.damage = damage;
        laser.layer = targetLayer;
        laser.timeOut = Mathf.Max(0.5f, leashRadius / Mathf.Max(1f, speed));
        laser.SetSourcePool(pool);
        laser.source = Health;
        laser.weapon = DamageLedger.Weapon.CraftGuns;
        DamageLedger.Fired(Health, DamageLedger.Weapon.CraftGuns);
        laser.Launch(direction * speed);
    }

    public float Dps()
    {
        float dps = 0f;
        foreach (Fighter fighter in fighters)
        {
            if (fighter != null && fighter.Alive) dps += fighter.shipDamage / Mathf.Max(0.05f, fighter.fireRate);
        }
        return dps;
    }
}
