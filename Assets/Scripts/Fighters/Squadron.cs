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

    public UnitHealthManager Health { get; private set; }
    public SpaceUnit Unit { get; private set; }
    public UnitHealthManager CurrentTarget { get; private set; }
    public Squadron EnemySquadron { get; private set; }

    public bool IsMoving => Unit != null && Unit.stateMachine != null && Unit.moveState != null
        && (Object)Unit.stateMachine.currentState == Unit.moveState && Unit.moveState.destinations.Count > 0;

    public bool Retreating => RetreatManager.Instance != null && Health != null && RetreatManager.Instance.IsRetreating(Health.isAttackerSide);

    public bool Engaging => CurrentTarget != null && !IsMoving && !Retreating && !GameManager.CombatOver;

    private Transform orderedAim;
    private UnitHealthManager orderedTarget;
    private float nextThinkAt;

    private void Awake()
    {
        Unit = GetComponent<SpaceUnit>();
        Health = GetComponent<UnitHealthManager>();

        GetComponentsInChildren(true, fighters);
        for (int i = 0; i < fighters.Count; i++)
        {
            fighters[i].Bind(this, FormationOffset(i));
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

        orderedAim = aim;
        orderedTarget = target;
        CurrentTarget = target;
        EnemySquadron = target.GetComponent<Squadron>();
    }

    private void Update()
    {
        if (Time.time < nextThinkAt || Health == null || Health.IsDead) return;
        nextThinkAt = Time.time + thinkInterval * Random.Range(0.85f, 1.15f);

        if (orderedTarget != null && (orderedTarget.IsDead || !orderedTarget.gameObject.activeInHierarchy))
        {
            orderedTarget = null;
            orderedAim = null;
        }

        UnitHealthManager target = orderedTarget != null ? orderedTarget : AcquireNearest();
        if (target != null && Vector3.Distance(target.transform.position, transform.position) > leashRadius + 60f && target != orderedTarget)
        {
            target = null;
        }

        CurrentTarget = target;
        EnemySquadron = target != null ? target.GetComponent<Squadron>() : null;
    }

    private UnitHealthManager AcquireNearest()
    {
        UnitHealthManager best = null;
        float bestScore = float.MaxValue;
        Vector3 origin = transform.position;

        foreach (UnitHealthManager candidate in UnitHealthManager.Active)
        {
            if (candidate == null || candidate == Health || candidate.IsDead || !IsHostile(candidate)) continue;

            bool isSquadron = candidate.TryGetComponent(out Squadron _);
            float priority = isSquadron ? fighterPriority : shipPriority;
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
