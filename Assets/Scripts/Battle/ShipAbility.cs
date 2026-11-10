using UnityEngine;

// Each warship class has one active ability (B for your selected ships; the AI uses theirs too):
//   Battleship (Human)       Overcharge Batteries - +50% gun damage for 8s.
//   Capital (Avanti/shields) Shield Surge         - shields back up by half and recharge 3x for 8s.
//   Cruiser                  Targeting Uplink     - much tighter aim and +20% range for 10s.
//   Destroyer                Afterburners         - +70% speed for 6s.
//   Corvette                 Evasive Manoeuvres   - takes half damage for 6s.
//   Carrier                  Scramble Squadrons   - launches every missing squadron at once.
// Hero AbilityCooldown buffs shorten the recharge. Effects are read by the systems they change
// (TurretController, ShipSpeed via the hero ticker, HardpointHealth, ShieldGenerator).
public class ShipAbility : MonoBehaviour
{
    public enum Kind { None, Overcharge, ShieldSurge, Uplink, Afterburners, Evasive, Scramble }

    public Kind kind;
    public float duration;
    public float cooldown;

    public float ReadyAt { get; private set; }
    public float ActiveUntil { get; private set; } = float.NegativeInfinity;
    public bool Active => Time.time < ActiveUntil;
    public bool Ready => kind != Kind.None && Time.time >= ReadyAt && (health == null || !health.IsDead);
    public float CooldownLeft => Mathf.Max(0f, ReadyAt - Time.time);

    // Effect multipliers while active (1 = no effect).
    public float DamageMultiplier => Active && kind == Kind.Overcharge ? 1.5f : 1f;
    public float SpreadMultiplier => Active && kind == Kind.Uplink ? 0.45f : 1f;
    public float RangeMultiplier => Active && kind == Kind.Uplink ? 1.2f : 1f;
    public float SpeedMultiplier => Active && kind == Kind.Afterburners ? 1.7f : 1f;
    public float DamageTakenMultiplier => Active && kind == Kind.Evasive ? 0.5f : 1f;
    public float ShieldRegenMultiplier => Active && kind == Kind.ShieldSurge ? 3f : 1f;

    private UnitHealthManager health;

    public string Name
    {
        get
        {
            switch (kind)
            {
                case Kind.Overcharge: return "Overcharge Batteries";
                case Kind.ShieldSurge: return "Shield Surge";
                case Kind.Uplink: return "Targeting Uplink";
                case Kind.Afterburners: return "Afterburners";
                case Kind.Evasive: return "Evasive Manoeuvres";
                case Kind.Scramble: return "Scramble Squadrons";
                default: return "";
            }
        }
    }

    public static void Fit(GameObject ship)
    {
        if (!ship.TryGetComponent(out SpaceUnit unit) || ship.GetComponent<Squadron>() != null) return;

        Kind kind;
        float duration, cooldown;
        bool shielded = ship.GetComponent<ShieldGenerator>() != null;
        switch (unit.shipType)
        {
            case ShipType.Battleship:
                kind = shielded ? Kind.ShieldSurge : Kind.Overcharge; duration = 8f; cooldown = 45f; break;
            case ShipType.Cruiser:
                kind = Kind.Uplink; duration = 10f; cooldown = 40f; break;
            case ShipType.Destroyer:
                kind = Kind.Afterburners; duration = 6f; cooldown = 30f; break;
            case ShipType.Corvette:
                kind = Kind.Evasive; duration = 6f; cooldown = 35f; break;
            case ShipType.Carrier:
                kind = Kind.Scramble; duration = 0f; cooldown = 60f; break;
            default:
                return;
        }

        if (!ship.TryGetComponent(out ShipAbility ability)) ability = ship.AddComponent<ShipAbility>();
        ability.kind = kind;
        ability.duration = duration;
        ability.cooldown = cooldown;
        ability.ReadyAt = Time.time + 5f; // Not straight out of the jump.
    }

    public static ShipAbility Of(Component part)
    {
        if (part == null) return null;
        UnitHealthManager unit = part.GetComponentInParent<UnitHealthManager>();
        return unit != null ? unit.GetComponent<ShipAbility>() : null;
    }

    private void Awake()
    {
        health = GetComponent<UnitHealthManager>();
    }

    private bool appliedSpeed;

    private void Update()
    {
        // Afterburners change the ship's pace the moment they fire and when they wear off.
        bool burning = Active && kind == Kind.Afterburners;
        if (burning == appliedSpeed) return;
        appliedSpeed = burning;
        if (TryGetComponent(out UnityEngine.AI.NavMeshAgent agent)) ShipSpeed.For(agent)?.SetAbilityFactor(SpeedMultiplier);
    }

    public bool TryActivate()
    {
        if (!Ready) return false;

        float recharge = cooldown * (1f - (health != null ? HeroForces.Buff(health.isAttackerSide, HeroBuff.AbilityCooldown) : 0f));
        ReadyAt = Time.time + recharge;
        ActiveUntil = Time.time + duration;

        switch (kind)
        {
            case Kind.ShieldSurge:
                if (TryGetComponent(out ShieldGenerator shield)) shield.Surge(0.5f);
                break;
            case Kind.Scramble:
                foreach (HangarHardpoint hangar in GetComponentsInChildren<HangarHardpoint>()) hangar.ScrambleAll();
                break;
        }

        Sfx.Play(Sfx.Cue.HitShield, transform.position, 0.7f, 0.6f);
        return true;
    }
}
