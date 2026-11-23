using UnityEngine;

public class Fighter : MonoBehaviour
{
    [Header("Flight")]
    public float speed = 30f;
    public float formationSpeedBoost = 1.4f;
    public float turnRate = 150f;
    public float bankAmount = 0.7f;
    public float maxBank = 65f;
    public float altitudeBand = 22f;

    [Header("Guns")]
    public float shipDamage = 0.5f;
    public float fighterDamage = 0.8f;
    public float fireRate = 0.45f;
    public float fireRange = 55f;
    public float fireCone = 10f;
    public float projectileSpeed = 110f;
    public float spread = 0.015f;

    [Header("Attack Runs")]
    public float breakDistance = 16f;
    public float breakTime = 1.8f;
    public float breakRadius = 55f;
    public float breakClimb = 18f;
    public float dogfightOvershoot = 8f;

    [Header("Holding Formation")]
    public float holdRadius = 14f;
    public float holdSettleTime = 0.9f;
    public float holdAnchorSpeed = 2f;

    [Header("Death")]
    public ParticleSystem deathEffect;
    public float deathEffectLifetime = 3f;

    // Hits from a warship's guns or point defence it takes to destroy one.
    public const int ShipHitsToKill = 2;

    // A hit from a warship weapon: lethal in ShipHitsToKill hits regardless of the shot's damage.
    public static void TakeShipHit(HardpointHealth health)
    {
        if (health == null || health.currentHealth <= 0f) return;
        if (health.TryGetComponent(out CraftEvasion evasion) && evasion.Dodges()) return;     // Human interceptors jink.
        if (health.TryGetComponent(out FighterShield shield) && shield.TakeShipHit()) return; // Avanti fighters' shields.
        health.DealDamage(health.maxHealth / ShipHitsToKill + 0.01f);
    }

    public bool Alive => gameObject.activeInHierarchy && health != null && health.currentHealth > 0f;
    public Vector3 Velocity => holding ? holdVelocity : transform.forward * currentSpeed;
    public bool IsHolding => holding;

    // Approach/Break: a strafing pass on a ship. Pursue/Evade/Extend: dogfighting. Run phases come from the
    // squadron (bombers).
    private enum Phase { Approach, Break, Pursue, Evade, Extend }

    private Squadron squadron;
    private HardpointHealth health;
    private Vector3 formationOffset;
    private Phase phase;
    private Vector3 breakPoint;
    private float phaseEndsAt;
    private float nextShotAt;
    private Transform aim;
    private Fighter prey;
    private float bank;
    private float currentSpeed;
    private float wobbleSeed;
    private bool hasPose;
    private Vector3 worldPosition;
    private Quaternion worldRotation;
    private bool holding;
    private Vector3 holdVelocity;
    private int index;
    private float rollSpin;          // Barrel roll in progress (degrees left to roll).
    private float evadeSide = 1f;
    private float nextThreatCheckAt;
    private float burstEndsAt;
    private float nextMissileAt;
    private bool droppedThisRun;
    private Squadron.RunPhase lastRunPhase;
    private float speedJitter = 1f;

    // Weapons. Guns fire in bursts when the nose is on the target; missiles and bombs as the role allows.
    private const float GunCone = 5f;
    private const float BurstLength = 0.55f;
    private const float TracerSpeed = 180f;
    private const float LaserBoltSpeed = 140f;
    private const float MissileRange = 95f;
    // Share of the time the guns are actually firing (burst, then a pause): per-shot damage is scaled up by
    // this so a fighter's damage per second is what it always was.
    private const float BurstDuty = 0.4f;

    // Flight feel. Craft used to snap onto a new heading at their full turn rate in a single frame and reverse
    // in a second; now they roll into a turn, the turn builds up and eases off, and speed changes take a
    // moment. Every craft turns at TurnScale of its authored turnRate (fighters 150 -> 108 deg/s, interceptors
    // 200 -> 144, bombers 95 -> 68; missile chases are flown at the old full rate), and attack runs are longer to suit the wider turns.
    private const float TurnScale = 0.72f;
    private const float TurnBuildUp = 2.6f;   // Full turn rate reached in ~0.4s.
    private const float TurnGain = 2.4f;      // Turn rate eases off over the last ~40 degrees.
    private const float BankRate = 150f;      // Degrees of roll per second.
    private const float Acceleration = 0.75f; // Share of cruise speed gained or shed per second.
    private const float RunLength = 1.25f;     // Strafing passes: break further out and extend further.
    private float turnSpeed;

    // Two-ship elements (Human fighters and interceptors, some of the time; see Squadron.FormElements): a
    // lead and a wingman flying fighting wing off the lead's quarter, covering his six.
    public Fighter Lead { get; private set; }
    public Fighter Wingman { get; private set; }
    public bool IsWingman => Lead != null && Lead.Alive;
    public bool IsElementLead => Wingman != null && Wingman.Alive;
    public Fighter Partner => Lead != null ? Lead : Wingman;
    public Fighter Tail => tail != null && tail.Alive ? tail : null; // A bandit on my six right now.
    private Fighter tail;
    private float wingSide = 1f;
    private float supportUntil;
    private float nextRetargetAt;
    private float evadeReadyAt;
    private float fightThrottle = 1f;
    private const float WingTrail = 9f;
    private const float WingSpread = 8f;

    public static void Pair(Fighter lead, Fighter wingman)
    {
        lead.Lead = null;
        lead.Wingman = wingman;
        wingman.Wingman = null;
        wingman.Lead = lead;
        wingman.wingSide = Random.value < 0.5f ? -1f : 1f;
    }

    public void LeaveElement()
    {
        Lead = null;
        Wingman = null;
    }

    public Transform AimPoint => aim;
    public float CurrentSpeed => currentSpeed;

    private void Awake()
    {
        health = GetComponent<HardpointHealth>();
        if (health != null) health.die.AddListener(Die);
        wobbleSeed = Random.value * 100f;
    }

    public void Bind(Squadron owner, Vector3 offset)
    {
        squadron = owner;
        formationOffset = offset;
        currentSpeed = speed;
        index = owner.fighters.IndexOf(this);
        speedJitter = Random.Range(0.92f, 1.08f);
        nextMissileAt = Time.time + Random.Range(3f, 10f);
    }

    // For the cinematic camera: what this pilot is up to.
    public Fighter Prey => prey;
    public Fighter Pursuer => phase == Phase.Evade ? pursuer : null;
    public Transform ShipAim => squadron != null && squadron.EnemySquadron == null ? aim : null;
    public bool IsDying => dying;
    public bool IsFiring => Time.time < burstEndsAt;
    public float Interest => (dying ? 3f : 0f) + (phase == Phase.Evade ? 2f : 0f) + (IsFiring ? 1.5f : 0f) + (InCombat ? 1f : 0f) + (IsElementLead ? 0.75f : 0f);
    private Fighter pursuer;

    // Actually in the thick of it: someone close in front of the guns or on the tail, or a ship run under way.
    public bool InCombat
    {
        get
        {
            if (dying || !Alive) return false;
            if (Tail != null) return true;
            if (prey != null && prey.Alive && (prey.transform.position - transform.position).sqrMagnitude < 75f * 75f) return true;
            return aim != null && squadron != null && squadron.EnemySquadron == null && (aim.position - transform.position).sqrMagnitude < 110f * 110f;
        }
    }

    private float HealthFraction => health != null && health.maxHealth > 0f ? Mathf.Clamp01(health.currentHealth / health.maxHealth) : 1f;

    private void LateUpdate()
    {
        if (squadron == null) return;

        if (!hasPose)
        {
            worldPosition = transform.position;
            worldRotation = transform.rotation;
            hasPose = true;
        }
        transform.SetPositionAndRotation(worldPosition, worldRotation);

        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            if (dying) DeathFlight(dt);
            else Fly(dt);
        }

        worldPosition = transform.position;
        worldRotation = transform.rotation;
    }

    private void Fly(float dt)
    {
        if (InterceptMissile(dt)) return;

        Vector3 target;
        float throttle = 1f;

        if (squadron.Engaging && WithinLeash())
        {
            ReleaseHold();
            fightThrottle = 1f;
            target = FightPoint();
            throttle = fightThrottle;
        }
        else if (squadron.Escorting != null && EscortPoint(out target, out throttle))
        {
            ReleaseHold();
            aim = null;
            tail = null;
            phase = Phase.Approach;
        }
        else
        {
            aim = null;
            prey = null;
            tail = null;
            phase = Phase.Approach;
            if (HoldFormation(dt)) return;
            target = FormationPoint(out throttle);
        }

        Steer(target, throttle, dt);
    }

    private bool HoldFormation(float dt)
    {
        Vector3 slot = squadron.transform.TransformPoint(formationOffset);
        slot += Vector3.up * Mathf.Sin(Time.time * 0.8f + wobbleSeed) * 0.25f;
        float distance = Vector3.Distance(transform.position, slot);

        bool parked = !squadron.IsMoving && squadron.AnchorSpeed < holdAnchorSpeed;
        if (!parked || distance > (holding ? HoldCapture * 1.5f : HoldCapture))
        {
            ReleaseHold();
            return false;
        }

        if (!holding)
        {
            holding = true;
            holdVelocity = Velocity;
        }

        // Settling in from further out (HoldCapture) takes a little longer.
        float settle = holdSettleTime * Mathf.Lerp(1f, 2.2f, Mathf.InverseLerp(holdRadius, HoldCapture, distance));
        transform.position = Vector3.SmoothDamp(transform.position, slot, ref holdVelocity, settle, speed, dt);
        currentSpeed = holdVelocity.magnitude;

        bank = Mathf.Lerp(bank, 0f, dt * 3f);
        Vector3 heading = Flat(squadron.transform.forward);
        if (heading.sqrMagnitude < 0.001f) heading = Flat(transform.forward);
        // Still coasting in: nose along the way it's going, not sliding sideways into the slot.
        Vector3 drift = Flat(holdVelocity);
        if (drift.sqrMagnitude > 1f && heading.sqrMagnitude > 0.001f)
            heading = Vector3.Slerp(heading.normalized, drift.normalized, Mathf.Clamp01(drift.magnitude / (speed * 0.5f)));
        if (heading.sqrMagnitude > 0.001f)
        {
            Quaternion settled = Quaternion.LookRotation(heading.normalized) * Quaternion.Euler(0f, 0f, bank);
            transform.rotation = Quaternion.Slerp(transform.rotation, settled, 1f - Mathf.Exp(-2.5f * dt));
        }
        return true;
    }

    private void ReleaseHold()
    {
        if (!holding) return;
        holding = false;
        currentSpeed = Mathf.Max(currentSpeed, speed * 0.35f);
    }

    private bool WithinLeash()
    {
        return (transform.position - squadron.LeashCentre).sqrMagnitude < squadron.leashRadius * squadron.leashRadius;
    }

    // ---- Station keeping (wingmen, escorts, bombers in formation) ----
    // Fly to a slot that moves with someone else: aim a little ahead of it so the craft settles in flying
    // parallel, and set the throttle from how far ahead or behind the slot is.
    private Vector3 StationKeep(Vector3 slot, Vector3 heading, float referenceSpeed, out float throttle)
    {
        Vector3 to = slot - transform.position;
        float distance = to.magnitude;
        float along = Vector3.Dot(to, heading);
        float wanted = distance > 45f ? speed * formationSpeedBoost : referenceSpeed + along * 0.9f;
        throttle = Mathf.Clamp(wanted / Mathf.Max(1f, speed * speedJitter), 0.4f, formationSpeedBoost);
        return slot + heading * Mathf.Clamp(distance * 0.8f, 8f, 28f);
    }

    // Something in gun range and near enough the nose that a wingman can swing onto it without leaving the lead.
    private bool Bears(Vector3 point, float range)
    {
        return range < fireRange && Vector3.Angle(transform.forward, point - transform.position) < 30f;
    }

    private static Vector3 HeadingOf(Fighter craft)
    {
        Vector3 v = craft.Velocity;
        if (v.sqrMagnitude < 1f) v = craft.transform.forward;
        return v.normalized;
    }

    // Fighting wing: behind the lead and out to one side, a little high, keeping his tail in view.
    private Vector3 WingPoint(out float throttle)
    {
        Vector3 heading = HeadingOf(Lead);
        Vector3 right = Vector3.Cross(Vector3.up, heading);
        if (right.sqrMagnitude < 0.01f) right = Lead.transform.right;
        right.Normalize();
        Vector3 slot = Lead.transform.position - heading * WingTrail + right * wingSide * WingSpread + Vector3.up * 1.5f;
        return StationKeep(slot, heading, Lead.currentSpeed, out throttle);
    }

    // ---- Escort ----
    // Flying cover for a bomber squadron: a loose box above and out to the sides of the bombers (Avanti
    // escorts drift about in theirs). The squadron itself spots enemy craft coming in and turns the escort on
    // them (Squadron.UpdateEscort); this is just station keeping.
    private Vector3 escortJitter;
    private float nextJitterAt;

    private bool EscortPoint(out Vector3 target, out float throttle)
    {
        target = default;
        throttle = 1f;
        Fighter leadBomber = squadron.Escorting.LeadCraft();
        if (leadBomber == null) return false;

        if (IsWingman)
        {
            prey = null;
            target = WingPoint(out throttle);
            return true;
        }

        Vector3 heading = Flat(HeadingOf(leadBomber));
        if (heading.sqrMagnitude < 0.01f) heading = Flat(squadron.Escorting.transform.forward);
        if (heading.sqrMagnitude < 0.01f) heading = Vector3.forward;
        heading.Normalize();

        int slot = squadron.EscortSlot(this);
        float side = slot % 2 == 0 ? 1f : -1f;
        int row = slot / 2;
        Vector3 offset = new Vector3(side * (22f + row * 10f), 9f + (row % 2) * 7f, 6f - row * 14f);
        if (squadron.Avanti)
        {
            if (Time.time >= nextJitterAt)
            {
                nextJitterAt = Time.time + Random.Range(2f, 4f);
                escortJitter = Random.insideUnitSphere * 9f;
            }
            offset += escortJitter;
        }
        Vector3 at = leadBomber.transform.position + Quaternion.LookRotation(heading) * offset;
        target = StationKeep(at, heading, leadBomber.currentSpeed, out throttle);
        return true;
    }

    private Vector3 FormationPoint(out float throttle)
    {
        Vector3 slot = squadron.transform.TransformPoint(formationOffset);
        slot += Vector3.up * Mathf.Sin(Time.time * 1.3f + wobbleSeed) * 0.6f;

        float distance = Vector3.Distance(transform.position, slot);

        // Parked (and further out than the hold capture): fly to the slot; HoldFormation takes over from
        // HoldCapture, which is wider than the turning circle, so nobody circles the slot.
        bool parked = !squadron.IsMoving && squadron.AnchorSpeed < holdAnchorSpeed;
        if (parked)
        {
            throttle = Mathf.Clamp(distance / 20f, 0.45f, formationSpeedBoost);
            return slot;
        }

        // On the move: keep station. The steering point is always ahead of the craft along the squadron's
        // heading, so a craft that has drifted past its slot eases off and lets it come back to him instead of
        // looping round to it; the throttle does the rest.
        Vector3 heading = squadron.transform.forward;
        heading.y = 0f;
        if (heading.sqrMagnitude < 0.01f) heading = Flat(transform.forward);
        heading = heading.sqrMagnitude > 0.01f ? heading.normalized : Vector3.forward;
        Vector3 to = slot - transform.position;
        float along = Vector3.Dot(to, heading);
        float wanted = distance > 60f ? speed * formationSpeedBoost : squadron.AnchorSpeed + along * 0.7f;
        throttle = Mathf.Clamp(wanted / Mathf.Max(1f, speed * speedJitter), 0.25f, formationSpeedBoost);
        if (distance < 1.5f)
        {
            transform.position = Vector3.Lerp(transform.position, slot, 0.2f);
            return transform.position + heading * 10f;
        }
        float ahead = Mathf.Clamp(distance * 0.8f, 10f, 30f) + Mathf.Max(0f, -along);
        return slot + heading * ahead;
    }

    private float HoldCapture => Mathf.Max(holdRadius, 32f);

    private Vector3 FightPoint()
    {
        if (squadron.EnemySquadron != null)
        {
            return DogfightPoint();
        }
        tail = null;
        if (squadron.FlyingBombingRuns) return BombingRunPoint();

        // A wingman on a strafing pass follows his lead in, guns on the same spot, and breaks with him.
        if (IsWingman && Lead.aim != null && Lead.aim.gameObject.activeInHierarchy)
        {
            aim = Lead.aim;
            phase = Phase.Approach;
            Vector3 onAim = aim.position;
            float range = Vector3.Distance(transform.position, onAim);
            TryFire(onAim, range, shipDamage, aim.GetComponentInParent<HardpointHealth>());
            TryMissile(squadron.CurrentTarget, range);
            Vector3 wing = WingPoint(out fightThrottle);
            return Bears(onAim, range) ? onAim : wing; // On the firing pass, nose onto the target.
        }

        if (aim == null || !aim.gameObject.activeInHierarchy)
        {
            aim = squadron.PickAimPoint();
            phase = Phase.Approach;
        }

        if (aim == null) return squadron.transform.position;

        Vector3 aimPosition = aim.position;
        float distance = Vector3.Distance(transform.position, aimPosition);

        if (phase != Phase.Approach && phase != Phase.Break) phase = Phase.Approach;
        if (phase == Phase.Approach)
        {
            TryFire(aimPosition, distance, shipDamage, aim.GetComponentInParent<HardpointHealth>());
            TryMissile(squadron.CurrentTarget, distance);

            // Wider turns: pull off a little earlier, and carry on further out before coming round again.
            if (distance < breakDistance * 1.25f)
            {
                phase = Phase.Break;
                phaseEndsAt = Time.time + breakTime * RunLength * Random.Range(0.85f, 1.2f);
                Vector3 away = Flat(transform.forward);
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                away = Quaternion.Euler(0f, Random.Range(-45f, 45f), 0f) * away.normalized;
                breakPoint = aimPosition + away * breakRadius * RunLength + Vector3.up * Random.Range(-0.3f, 1f) * breakClimb;
            }
            return aimPosition;
        }

        if (Time.time >= phaseEndsAt || Vector3.Distance(transform.position, breakPoint) < 6f)
        {
            phase = Phase.Approach;
            if (Random.value < 0.35f) aim = squadron.PickAimPoint();
        }
        return breakPoint;
    }

    // A dogfight, flown fighter by fighter: chase a bandit and shoot when the nose is on him; if someone
    // gets on your tail, break hard and roll (more readily the more damaged you are); after a firing pass
    // that overshoots, extend away and come back round. Some pilots peel off to clear a wingman's tail.
    //
    // Human two-ship elements fly it the way an air force does (loose deuce / fighting wing): the wingman holds
    // station off the lead's quarter, watching his six, and takes shots at the lead's bandit when one crosses
    // his nose. Whoever of the two gets a bandit on his tail breaks; the other one goes after that bandit.
    private Vector3 DogfightPoint()
    {
        if (Time.time >= nextThreatCheckAt)
        {
            nextThreatCheckAt = Time.time + Random.Range(0.45f, 0.7f);
            tail = Threat();
            // A break is a big, committed manoeuvre, not a twitch: not again straight after the last one.
            if (tail != null && phase != Phase.Evade && Time.time >= evadeReadyAt)
            {
                float chance = 0.4f + 0.5f * (1f - HealthFraction);
                if (Random.value < chance) BeginEvade(tail);
            }
        }

        if (phase == Phase.Evade)
        {
            if (Time.time >= phaseEndsAt) { phase = Phase.Pursue; evadeReadyAt = Time.time + Random.Range(2f, 3.5f); }
            else return breakPoint;
        }
        if (phase == Phase.Extend)
        {
            if (Time.time >= phaseEndsAt) phase = Phase.Pursue;
            else return breakPoint;
        }
        phase = Phase.Pursue;

        if (IsWingman)
        {
            // Clear the lead's six first; otherwise stay on his wing.
            Fighter onLead = Lead.Tail;
            if (onLead != null && onLead.squadron == squadron.EnemySquadron)
            {
                prey = onLead;
                supportUntil = Time.time + 5f;
            }
            if (prey == null || !prey.Alive || Time.time >= supportUntil)
            {
                prey = Lead.prey != null && Lead.prey.Alive ? Lead.prey : null;
                Vector3 wing = WingPoint(out fightThrottle);
                if (prey != null)
                {
                    float range = Vector3.Distance(transform.position, prey.transform.position);
                    Vector3 shot = prey.transform.position + prey.Velocity * (range / Mathf.Max(1f, squadron.Avanti ? LaserBoltSpeed : TracerSpeed));
                    TryFire(shot, range, fighterDamage, prey.health);
                    if (Bears(shot, range)) return shot; // The lead's bandit across the nose: take the shot too.
                }
                return wing;
            }
            return AttackPoint();
        }

        // A lead (or a singleton): his wingman's attacker first, then his own pick.
        if (IsElementLead && Wingman.Tail != null && Wingman.Tail.squadron == squadron.EnemySquadron)
        {
            prey = Wingman.Tail;
            nextRetargetAt = Time.time + 4f;
        }
        if (prey == null || !prey.Alive || Time.time >= nextRetargetAt)
        {
            prey = PickPrey();
            nextRetargetAt = Time.time + Random.Range(6f, 10f);
        }
        if (prey == null) return squadron.transform.position;
        return AttackPoint();
    }

    // Chase the current prey: lead him, shoot when the nose is on him, and extend away after an overshoot.
    private Vector3 AttackPoint()
    {
        Vector3 preyPosition = prey.transform.position;
        float distance = Vector3.Distance(transform.position, preyPosition);
        float leadTime = distance / Mathf.Max(1f, squadron.Avanti ? LaserBoltSpeed : TracerSpeed);
        Vector3 lead = preyPosition + prey.Velocity * leadTime;

        TryFire(lead, distance, fighterDamage, prey.health);
        TryMissile(squadron.EnemySquadron != null ? squadron.EnemySquadron.Health : null, distance);

        // Overshot: zoom past and extend, then turn back in.
        if (distance < dogfightOvershoot || (distance < dogfightOvershoot * 2.5f && Vector3.Dot(transform.forward, preyPosition - transform.position) < 0f))
        {
            phase = Phase.Extend;
            phaseEndsAt = Time.time + Random.Range(1.8f, 2.8f);
            breakPoint = transform.position + transform.forward * 65f + Random.insideUnitSphere * 12f;
        }
        return lead;
    }

    // A bandit sitting behind me with his nose on me.
    private Fighter Threat()
    {
        Squadron enemies = squadron.EnemySquadron;
        if (enemies == null) return null;
        foreach (Fighter enemy in enemies.fighters)
        {
            if (enemy == null || !enemy.Alive) continue;
            Vector3 fromEnemy = transform.position - enemy.transform.position;
            float distance = fromEnemy.magnitude;
            if (distance > 45f || distance < 0.5f) continue;
            bool behindMe = Vector3.Dot(transform.forward, -fromEnemy) < -0.3f * distance;
            bool onMe = Vector3.Angle(enemy.transform.forward, fromEnemy) < 25f;
            if (behindMe && onMe) return enemy;
        }
        return null;
    }

    private void BeginEvade(Fighter tail)
    {
        pursuer = tail;
        phase = Phase.Evade;
        phaseEndsAt = Time.time + Random.Range(1.6f, 2.8f);
        evadeSide = Random.value < 0.5f ? -1f : 1f;
        Vector3 side = Vector3.Cross(Vector3.up, transform.forward).normalized * evadeSide;
        Vector3 vertical = Vector3.up * Random.Range(-1f, 1f);
        breakPoint = transform.position + (side * 1.2f + vertical * 0.6f + transform.forward * 0.5f).normalized * 55f;
        rollSpin = Random.value < 0.45f ? 360f * evadeSide : 0f; // Often a barrel roll into the break.
    }

    // Someone chasing a squadron-mate gets attention first (clearing a wingman's tail); otherwise the
    // nearest bandit roughly in front.
    private Fighter PickPrey()
    {
        Squadron enemies = squadron.EnemySquadron;
        if (enemies == null) return null;
        Fighter best = null;
        float bestScore = float.MaxValue;
        foreach (Fighter enemy in enemies.fighters)
        {
            if (enemy == null || !enemy.Alive) continue;
            Vector3 to = enemy.transform.position - transform.position;
            float score = to.magnitude * (Vector3.Dot(transform.forward, to) > 0f ? 1f : 1.8f);
            if (enemy.prey != null && enemy.prey.squadron == squadron && Random.value < 0.6f) score *= 0.4f; // He's on a friend.
            if (enemy.prey != null && squadron.Escorting != null && enemy.prey.squadron == squadron.Escorting) score *= 0.3f; // He's on our bombers.
            if (score < bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }
        return best;
    }

    // ---- Bombing runs ----
    // Form up in the V off to one side, run in together, release at the drop point, break away together.
    private Vector3 BombingRunPoint()
    {
        Squadron.RunPhase run = squadron.BomberPhase;
        if (run != lastRunPhase)
        {
            if (run == Squadron.RunPhase.Run) droppedThisRun = false;
            lastRunPhase = run;
        }

        Vector3 heading = squadron.RunDirection;
        UnitHealthManager target = squadron.CurrentTarget;
        Vector3 targetPoint = target.transform.position;
        Fighter leadBomber = squadron.LeadCraft();
        bool leading = leadBomber == null || leadBomber == this;

        // The rest of the formation keeps station on the lead bomber (a V for the Humans, line astern for the
        // Avanti); the lead flies the run itself.
        if (!leading)
        {
            if (run == Squadron.RunPhase.Run)
            {
                if (aim == null || !aim.gameObject.activeInHierarchy) aim = squadron.PickAimPoint();
                Vector3 aimAt = aim != null ? aim.position : targetPoint;
                float range = Vector3.Distance(transform.position, aimAt);
                if (!droppedThisRun && Vector3.Distance(transform.position, squadron.RunAimPoint()) < Squadron.RunDropDistance)
                {
                    droppedThisRun = true;
                    ReleaseBombs(target, aimAt);
                }
                TryFire(aimAt, range, shipDamage, aim != null ? aim.GetComponentInParent<HardpointHealth>() : null);
            }
            Vector3 leadHeading = HeadingOf(leadBomber);
            Vector3 slot = squadron.FormationSlot(this, leadBomber, leadHeading);
            return StationKeep(slot, leadHeading, leadBomber.currentSpeed, out fightThrottle);
        }

        switch (run)
        {
            case Squadron.RunPhase.FormUp:
            {
                Vector3 point = squadron.FormUpPoint();
                // Ease off near the form-up point so the others can join up.
                if (Vector3.Distance(transform.position, point) < 40f) fightThrottle = 0.8f;
                return point;
            }
            case Squadron.RunPhase.Run:
            {
                if (aim == null || !aim.gameObject.activeInHierarchy) aim = squadron.PickAimPoint();
                Vector3 aimPoint = aim != null ? aim.position : targetPoint;
                float distance = Vector3.Distance(transform.position, aimPoint);
                if (!droppedThisRun && distance < Squadron.RunDropDistance)
                {
                    droppedThisRun = true;
                    ReleaseBombs(target, aimPoint);
                }
                // Guns on the way in too.
                TryFire(aimPoint, distance, shipDamage, aim != null ? aim.GetComponentInParent<HardpointHealth>() : null);
                fightThrottle = 0.9f; // A touch under full so the formation holds together.
                return aimPoint + heading * 20f; // Aim a little past it so the run carries straight through.
            }
            default:
                return squadron.EgressPoint();
        }
    }

    private void ReleaseBombs(UnitHealthManager target, Vector3 aimPoint)
    {
        if (target == null || MissileArsenal.Instance == null) return;
        MissileArsenal.Warhead bomb = MissileArsenal.Instance.BombFor(squadron.Avanti);
        Vector3 toward = (aimPoint - transform.position).normalized;
        squadron.FireMissile(bomb, transform.position - transform.up * 0.6f + transform.forward * 0.5f, toward, target, Squadron.RunDropDistance * 2f);
        // A second bomb a beat later, from the other rack.
        StartCoroutine(SecondBomb(bomb, target, aimPoint));
    }

    private System.Collections.IEnumerator SecondBomb(MissileArsenal.Warhead bomb, UnitHealthManager target, Vector3 aimPoint)
    {
        yield return new WaitForSeconds(0.22f);
        if (target == null || !Alive) yield break;
        squadron.FireMissile(bomb, transform.position - transform.up * 0.6f, (aimPoint - transform.position).normalized, target, Squadron.RunDropDistance * 2f);
    }

    // Guns: when the nose is within GunCone of the aim point and in range, fire a burst. Avanti craft fire
    // small laser bolts; Human craft hose tracer rounds. Damage per second is the same as before.
    private void TryFire(Vector3 point, float distance, float damage, HardpointHealth victim)
    {
        if (distance > fireRange) return;
        Vector3 toPoint = point - transform.position;
        bool onTarget = Vector3.Angle(transform.forward, toPoint) <= GunCone;
        if (onTarget && Time.time >= nextShotAt && Time.time >= burstEndsAt)
        {
            burstEndsAt = Time.time + BurstLength;
            nextShotAt = burstEndsAt + fireRate * Random.Range(1.4f, 2.2f);
        }
        if (Time.time >= burstEndsAt) return;

        Vector3 muzzle = transform.position + transform.forward * 1.2f;
        Vector3 direction = (toPoint.normalized + Random.insideUnitSphere * spread).normalized;
        if (squadron.Avanti)
        {
            // About four bolts a second through the burst.
            if (Time.frameCount % 2 == 0 && Random.value < Time.deltaTime * 8f)
                squadron.FireCraftLaser(muzzle, direction, damage / (4f * fireRate * BurstDuty), LaserBoltSpeed);
        }
        else
        {
            // ~24 rounds a second; the burst's damage matches the old single shot per fireRate.
            int rounds = Mathf.Max(1, Mathf.RoundToInt(24f * Time.deltaTime + Random.value - 0.5f));
            float perRound = damage / (24f * fireRate * BurstDuty);
            for (int i = 0; i < rounds; i++)
            {
                Vector3 jitter = (direction + Random.insideUnitSphere * spread * 0.8f).normalized;
                squadron.FireTracers(muzzle, jitter, perRound, victim, TracerSpeed, fireRange);
            }
        }
    }

    // Missile interception. Interceptors always - shooting down incoming missiles and torpedoes is part of
    // owning the sky - and fighters while they're on anti-air duty; bombers never break off their runs. A craft
    // goes after a hostile missile that comes within reach, leads it, and shoots it down with its guns; the
    // missile's own engagement claim keeps a whole squadron (and the ship's point defence) from piling onto one.
    private const float MissileInterceptReach = 60f;
    private Missile chasing;
    private float nextMissileScanAt;

    private bool InterceptMissile(float dt)
    {
        bool interceptor = squadron.CraftRole == Squadron.Role.Interceptor;
        bool antiAir = squadron.CraftRole == Squadron.Role.Fighter && squadron.EnemySquadron != null;
        if (!interceptor && !antiAir) { chasing = null; return false; }
        if (squadron.Retreating || GameManager.CombatOver) { chasing = null; return false; }

        if (chasing == null || !chasing.InFlight)
        {
            chasing = null;
            if (Time.time < nextMissileScanAt) return false;
            nextMissileScanAt = Time.time + 0.4f * Random.Range(0.8f, 1.2f);
            chasing = Missile.NearestHostile(transform.position, MissileInterceptReach, squadron.targetLayer, this);
            if (chasing == null) return false;
        }

        Vector3 at = chasing.transform.position;
        float distance = Vector3.Distance(transform.position, at);
        if (distance > MissileInterceptReach * 1.6f || !chasing.Engage(this)) { chasing = null; return false; }

        ReleaseHold();
        aim = null;
        float shotSpeed = squadron.Avanti ? LaserBoltSpeed : TracerSpeed;
        Vector3 lead = at + chasing.Velocity * (distance / shotSpeed);
        FireAtMissile(lead, distance);
        Steer(lead, 1f, dt, 1f / TurnScale); // Flown hard, at the old full turn rate: a missile won't wait for a lazy turn.
        return true;
    }

    private void FireAtMissile(Vector3 point, float distance)
    {
        if (distance > fireRange) return;
        Vector3 toPoint = point - transform.position;
        if (Vector3.Angle(transform.forward, toPoint) > GunCone * 2f) return; // A small target: fire when roughly on.
        if (Time.time >= burstEndsAt && Time.time >= nextShotAt)
        {
            burstEndsAt = Time.time + BurstLength;
            nextShotAt = burstEndsAt + fireRate * Random.Range(1.0f, 1.5f);
        }
        if (Time.time >= burstEndsAt) return;

        Vector3 muzzle = transform.position + transform.forward * 1.2f;
        Vector3 direction = (toPoint.normalized + Random.insideUnitSphere * spread).normalized;
        if (squadron.Avanti)
        {
            if (Time.frameCount % 2 == 0 && Random.value < Time.deltaTime * 8f)
                squadron.FireCraftLaser(muzzle, direction, fighterDamage / (4f * fireRate * BurstDuty), LaserBoltSpeed, chasing);
        }
        else
        {
            int rounds = Mathf.Max(1, Mathf.RoundToInt(24f * Time.deltaTime + Random.value - 0.5f));
            float perRound = fighterDamage / (24f * fireRate * BurstDuty);
            for (int i = 0; i < rounds; i++)
                squadron.FireTracers(muzzle, (direction + Random.insideUnitSphere * spread * 0.8f).normalized, perRound, null, TracerSpeed, fireRange, chasing);
        }
    }

    // Fighters and interceptors carry a few mini-missiles: one every so often at whatever they're attacking.
    private void TryMissile(UnitHealthManager target, float distance)
    {
        if (target == null || squadron.CraftRole == Squadron.Role.Bomber || Time.time < nextMissileAt) return;
        if (distance > MissileRange || distance < 15f || MissileArsenal.Instance == null) return;
        nextMissileAt = Time.time + Random.Range(9f, 16f);
        squadron.FireMissile(MissileArsenal.Instance.MiniMissileFor(squadron.Avanti), transform.position + transform.forward * 1f - transform.up * 0.3f, transform.forward, target, MissileRange);
    }

    private void Steer(Vector3 target, float throttle, float dt, float agility = 1f)
    {
        Vector3 toTarget = target - transform.position;

        float anchorHeight = squadron.FlightHeight;
        float offBand = transform.position.y - anchorHeight;
        if (Mathf.Abs(offBand) > altitudeBand)
        {
            toTarget.y -= offBand;
        }

        if (toTarget.sqrMagnitude < 0.0001f) toTarget = transform.forward;

        Vector3 forward = transform.forward;
        Vector3 desired = toTarget.normalized;
        Vector3 avoid = AvoidHulls(forward);
        bool avoiding = avoid.sqrMagnitude > 0.0001f || agility > 1.3f; // Hull ahead, or chasing a missile: turn hard.
        if (avoid.sqrMagnitude > 0.0001f) desired = (desired + avoid).normalized;
        if (phase == Phase.Evade) throttle = Mathf.Max(throttle, 1.25f);

        // Turning: the turn rate builds up and eases off rather than snapping, and a level turn needs the
        // craft rolled into it first - so it banks, then pulls round. Climbs and dives don't wait for the roll.
        float maxTurn = turnRate * TurnScale * agility * (phase == Phase.Evade ? 1.15f : 1f);
        float angle = Vector3.Angle(forward, desired);
        float flatAngle = Vector3.SignedAngle(Flat(forward), Flat(desired), Vector3.up);
        float level = angle > 0.01f ? Mathf.Clamp01(Mathf.Abs(flatAngle) / angle) : 0f;
        float wanted = Mathf.Min(maxTurn, angle * TurnGain);
        if (avoid.sqrMagnitude > 0.0001f) wanted = maxTurn * 1.2f; // Hull ahead: pull hard, now.
        turnSpeed = Mathf.MoveTowards(turnSpeed, wanted, maxTurn * TurnBuildUp * (avoiding ? 3f : 1f) * dt);
        float rolledIn = Mathf.Clamp01(Mathf.Abs(bank) / Mathf.Max(1f, maxBank * 0.55f));
        float gate = level * Mathf.Clamp01((Mathf.Abs(flatAngle) - 8f) / 20f); // Fine aiming corrections aren't held up.
        float allowed = avoiding ? turnSpeed : turnSpeed * Mathf.Lerp(1f, Mathf.Lerp(0.4f, 1f, rolledIn), gate);
        Vector3 newForward = Vector3.RotateTowards(forward, desired, allowed * Mathf.Deg2Rad * dt, 0f);

        float bankGoal = -Mathf.Sign(flatAngle) * maxBank * Mathf.Clamp01(Mathf.Abs(flatAngle) / 45f) * Mathf.Clamp01(bankAmount / 0.7f);
        bank = Mathf.MoveTowards(bank, Mathf.Clamp(bankGoal, -maxBank, maxBank), BankRate * dt);
        bank = Mathf.Lerp(bank, Mathf.Clamp(bankGoal, -maxBank, maxBank), dt * 1.5f);

        // A hard turn bleeds a little speed; speed itself changes over a second or so, not in a frame.
        float turnLoad = maxTurn > 0f ? Mathf.Clamp01(allowed / maxTurn) : 0f;
        float targetSpeed = speed * throttle * speedJitter * (1f - 0.12f * turnLoad);
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speed * Acceleration * dt);
        transform.position += newForward * currentSpeed * dt;

        float roll = 0f;
        if (Mathf.Abs(rollSpin) > 0.5f)
        {
            float step = Mathf.Sign(rollSpin) * Mathf.Min(Mathf.Abs(rollSpin), 330f * dt);
            rollSpin -= step;
            roll = rollSpin;
        }
        transform.rotation = Quaternion.LookRotation(newForward) * Quaternion.Euler(0f, 0f, bank + roll);
    }

    // Steer clear of warship hulls: each ship is treated as a capsule along its length; if the next ~1.2s of
    // flight would take us inside one, push the nose out away from its spine (harder the deeper we'd go).
    private static readonly Unity.Profiling.ProfilerMarker AvoidMarker = new Unity.Profiling.ProfilerMarker("Fighter.AvoidHulls");

    private Vector3 AvoidHulls(Vector3 forward)
    {
        using var avoidTiming = AvoidMarker.Auto();
        Vector3 push = Vector3.zero;
        Vector3 here = transform.position;
        Vector3 ahead = here + forward * Mathf.Max(currentSpeed, speed) * 1.2f;
        foreach (HullDamage ship in HullDamage.Active)
        {
            if (ship == null || !ship.IsAlive) continue;
            float reach = ship.Size * 0.6f + 6f;
            Vector3 centre = ship.transform.position;
            if ((centre - here).sqrMagnitude > reach * reach * 4f) continue;

            Bounds b = ship.LocalBounds;
            Vector3 lossy = ship.transform.lossyScale;
            float halfLength = b.extents.z * lossy.z;
            float radius = Mathf.Max(b.extents.x * lossy.x, b.extents.y * lossy.y) * 1.15f + 2f;
            Vector3 axisCentre = ship.transform.TransformPoint(b.center);
            Vector3 axis = ship.transform.forward;
            Vector3 a = axisCentre - axis * halfLength, c = axisCentre + axis * halfLength;

            Vector3 nearest = ClosestOnSegment(a, c, ahead);
            Vector3 away = ahead - nearest;
            float depth = radius - away.magnitude;
            if (depth <= 0f) continue;
            if (away.sqrMagnitude < 0.01f) away = Vector3.up;
            push += away.normalized * Mathf.Clamp01(depth / radius) * 2.5f;
        }
        return push;
    }

    private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return a + ab * t;
    }

    // ---------------- Death ----------------
    // A craft that's shot down goes one of three ways: blows apart on the spot; catches fire and corkscrews
    // away before blowing up; or spirals down in a fireball into the nearest ship or rock and explodes on
    // impact (scorching the hull it hits).
    private enum Fate { Burst, Spiral, Crash }

    private bool dying;
    private Fate fate;
    private float deathEndsAt;
    private float nextTrailAt;
    private float spiralAngle;
    private float spiralRate;
    private Vector3 deathVelocity;
    private Transform crashTarget;
    private Vector3 crashLocal;
    private GameObject fireTrail;

    private static DestructionProfile fxProfile;

    private static DestructionProfile FxProfile
    {
        get
        {
            if (fxProfile == null)
            {
                foreach (HullDamage hull in HullDamage.Active)
                {
                    if (hull != null && hull.profile != null) { fxProfile = hull.profile; break; }
                }
            }
            return fxProfile;
        }
    }

    private void Die()
    {
        if (dying) return;
        Sfx.Play(Sfx.Cue.ExplosionSmall, transform.position, 0.5f, 1.4f);

        float roll = Random.value;
        fate = roll < 0.35f ? Fate.Burst : roll < 0.7f ? Fate.Spiral : Fate.Crash;
        if (fate == Fate.Crash && !FindCrashSite()) fate = Fate.Spiral;
        if (fate == Fate.Burst)
        {
            Explode(1f);
            return;
        }

        // Out of the fight: no longer hittable, no longer flying the squadron's plan.
        dying = true;
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        deathVelocity = Velocity.sqrMagnitude > 1f ? Velocity : transform.forward * speed;
        spiralRate = Random.Range(380f, 720f) * (Random.value < 0.5f ? -1f : 1f);
        deathEndsAt = Time.time + (fate == Fate.Crash ? 6f : Random.Range(1.4f, 3f));
        nextTrailAt = Time.time;

        // A small blast where it was hit, then it burns.
        if (deathEffect != null)
        {
            if (EffectPool.Budget("FighterDeath", 3))
                EffectPool.Play("FighterDeath", deathEffect, transform.position, Quaternion.identity, deathEffectLifetime, deathEffect.transform.localScale * 0.45f);
            else EffectPool.Puff("FighterDeathHit", deathEffect, transform.position, 0.45f, 3);
        }
        DestructionProfile profile = FxProfile;
        if (profile != null && profile.hullFires != null && profile.hullFires.Length > 0 && EffectPool.Budget("FighterFire", 2))
        {
            fireTrail = DestructionFx.Attach(profile.hullFires[0], transform, transform.position, 0.35f);
        }
    }

    // The nearest ship hull or asteroid within reach to plough into.
    private bool FindCrashSite()
    {
        float best = 70f * 70f;
        crashTarget = null;
        foreach (HullDamage hull in HullDamage.Active)
        {
            if (hull == null || !hull.IsAlive) continue;
            Vector3 point = hull.RandomSurfacePoint(out _);
            float d = (point - transform.position).sqrMagnitude;
            if (d < best && Vector3.Dot(point - transform.position, transform.forward) > 0f)
            {
                best = d;
                crashTarget = hull.transform;
                crashLocal = hull.transform.InverseTransformPoint(point);
            }
        }
        int count = Physics.OverlapSphereNonAlloc(transform.position, 50f, crashBuffer, BattleTerrain.BlockingMask);
        for (int i = 0; i < count; i++)
        {
            Vector3 point = crashBuffer[i].ClosestPoint(transform.position);
            float d = (point - transform.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                crashTarget = crashBuffer[i].transform;
                crashLocal = crashTarget.InverseTransformPoint(point);
            }
        }
        return crashTarget != null;
    }

    private static readonly Collider[] crashBuffer = new Collider[8];
    private static readonly RaycastHit[] impactBuffer = new RaycastHit[4];

    private void DeathFlight(float dt)
    {
        // Heading: a crash dives at its target; a spiral keeps going, sagging off its line.
        Vector3 heading = deathVelocity.normalized;
        if (fate == Fate.Crash && crashTarget != null)
        {
            Vector3 toTarget = crashTarget.TransformPoint(crashLocal) - transform.position;
            heading = Vector3.RotateTowards(heading, toTarget.normalized, 140f * Mathf.Deg2Rad * dt, 0f);
        }
        else
        {
            heading = Vector3.RotateTowards(heading, (heading + Vector3.down * 0.4f + Random.insideUnitSphere * 0.3f).normalized, 40f * Mathf.Deg2Rad * dt, 0f);
        }
        float pace = Mathf.Max(speed * 0.8f, deathVelocity.magnitude);
        deathVelocity = heading * pace;

        // Corkscrew around the line of flight.
        spiralAngle += spiralRate * dt;
        Vector3 side = Vector3.Cross(heading, Vector3.up);
        if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(heading, Vector3.right);
        side.Normalize();
        Vector3 wobble = Quaternion.AngleAxis(spiralAngle, heading) * side * 1.2f;

        Vector3 from = transform.position;
        Vector3 to = from + deathVelocity * dt + (wobble - Quaternion.AngleAxis(spiralAngle - spiralRate * dt, heading) * side * 1.2f);

        // Hit something solid on the way: blow up against it.
        Vector3 step = to - from;
        // Solid things only: ships (either side), wrecks, rocks - not the battle plane, UI, lasers or shields.
        int mask = ~LayerMask.GetMask("Ground", "UI", "Ignore Raycast", "RedLaser", "BlueLaser", "FleetToken", "Shield");
        int hits = step.sqrMagnitude > 0.0001f ? Physics.RaycastNonAlloc(from, step.normalized, impactBuffer, step.magnitude + 0.5f, mask, QueryTriggerInteraction.Ignore) : 0;
        for (int i = 0; i < hits; i++)
        {
            Collider struck = impactBuffer[i].collider;
            if (struck == null || struck.transform.IsChildOf(transform) || struck.GetComponentInParent<Fighter>() != null || struck.gameObject.layer == ShieldGenerator.Layer) continue;
            transform.position = impactBuffer[i].point;
            HullScarring.Impact(struck, impactBuffer[i].point, step.normalized, 4f); // A scorch where it hit.
            Explode(1.3f);
            return;
        }

        transform.position = to;
        transform.rotation = Quaternion.LookRotation(heading) * Quaternion.Euler(0f, 0f, spiralAngle);

        // A trail of fire puffs behind the burning wreck.
        if (Time.time >= nextTrailAt && deathEffect != null)
        {
            nextTrailAt = Time.time + 0.09f;
            EffectPool.Puff("FighterDeathTrail", deathEffect, transform.position, 0.24f, 1); // emitted, not switched on: cheap
        }

        if (Time.time >= deathEndsAt) Explode(1.1f);
    }

    private void Explode(float scale)
    {
        if (deathEffect != null)
        {
            // The full blast for the first few a frame; in a mass of kills the rest burst from the live emitter.
            if (EffectPool.Budget("FighterDeath", 3))
                EffectPool.Play("FighterDeath", deathEffect, transform.position, Quaternion.identity, deathEffectLifetime, deathEffect.transform.localScale * scale);
            else EffectPool.Puff("FighterDeathBurst", deathEffect, transform.position, scale, 4);
        }
        if (dying) Sfx.Play(Sfx.Cue.ExplosionSmall, transform.position, 0.55f, 1.25f);
        if (fireTrail != null) Destroy(fireTrail);
        fireTrail = null;
        dying = false;
        gameObject.SetActive(false);
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }
}
