using System.Collections.Generic;
using UnityEngine;

// Battlefield features beyond asteroids, rolled with the terrain:
//   Nebula     - shields can't hold inside: they drain away and won't recharge until the ship leaves.
//                (Hurts the shielded Avanti, a haven for Human ships.)
//   Ion storm  - strike craft can't fight inside and take steady damage; their electronics are fried.
// Zones are vertical columns (like the asteroid fields), so height doesn't get you out of them.
public class BattleHazards : MonoBehaviour
{
    public enum Kind { Nebula, IonStorm }

    public class Zone
    {
        public Kind kind;
        public Vector2 centre;
        public float radius;
    }

    public const float IonDamagePerSecond = 0.06f; // Share of a fighter's health.
    public const float NebulaDrainPerSecond = 0.35f; // Share of shield capacity.

    public static BattleHazards Instance { get; private set; }
    // Dev recordings: at least this many nebulae on the next battle map (one-shot).
    public static int ForceNebulae;
    public readonly List<Zone> zones = new List<Zone>();

    private float nextIonTickAt;

    public static bool InNebula(Vector3 point) => Instance != null && Instance.Inside(point, Kind.Nebula);
    public static bool InIonStorm(Vector3 point) => Instance != null && Instance.Inside(point, Kind.IonStorm);

    private bool Inside(Vector3 point, Kind kind)
    {
        Vector2 flat = new Vector2(point.x, point.z);
        foreach (Zone zone in zones)
        {
            if (zone.kind == kind && (flat - zone.centre).sqrMagnitude <= zone.radius * zone.radius) return true;
        }
        return false;
    }

    public static BattleHazards Generate(Transform parent, int seed, Rect map, Vector2 attacker, Vector2 defender, IReadOnlyList<AsteroidField> fields)
    {
        if (Instance != null) Destroy(Instance.gameObject);
        BattleHazards hazards = new GameObject("BattleHazards").AddComponent<BattleHazards>();
        hazards.transform.SetParent(parent, false);
        hazards.Roll(seed, map, attacker, defender, fields);
        return hazards;
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Roll(int seed, Rect map, Vector2 attacker, Vector2 defender, IReadOnlyList<AsteroidField> fields)
    {
        System.Random random = new System.Random(seed * 7919 + 17);
        float Next() => (float)random.NextDouble();

        Rect inner = new Rect(map.x + map.width * 0.12f, map.y + map.height * 0.12f, map.width * 0.76f, map.height * 0.76f);
        List<(Vector2 centre, float radius)> taken = new List<(Vector2, float)>();
        foreach (AsteroidField field in fields)
        {
            if (field == null) continue;
            foreach (AsteroidField.Blob blob in field.blobs) taken.Add((blob.centre, blob.radius * 0.6f));
        }
        taken.Add((attacker, 230f));
        taken.Add((defender, 260f));

        bool TryPlace(float radius, out Vector2 spot, Vector2? near = null)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                spot = near.HasValue
                    ? near.Value + new Vector2(Next() - 0.5f, Next() - 0.5f) * 160f
                    : new Vector2(Mathf.Lerp(inner.xMin, inner.xMax, Next()), Mathf.Lerp(inner.yMin, inner.yMax, Next()));
                if (!inner.Contains(spot)) continue;
                bool clear = true;
                foreach ((Vector2 centre, float r) in taken)
                {
                    if ((spot - centre).sqrMagnitude < (r + radius) * (r + radius)) { clear = false; break; }
                }
                if (!clear) continue;
                taken.Add((spot, radius));
                return true;
            }
            spot = default;
            return false;
        }

        int nebulae = Next() < 0.55f ? (Next() < 0.35f ? 2 : 1) : 0;
        nebulae = Mathf.Max(nebulae, ForceNebulae);
        ForceNebulae = 0;
        for (int i = 0; i < nebulae; i++)
        {
            float radius = Mathf.Lerp(110f, 170f, Next());
            if (TryPlace(radius, out Vector2 spot)) AddZone(Kind.Nebula, spot, radius);
        }

        if (Next() < 0.35f)
        {
            float radius = Mathf.Lerp(90f, 140f, Next());
            if (TryPlace(radius, out Vector2 spot)) AddZone(Kind.IonStorm, spot, radius);
        }
    }

    private void AddZone(Kind kind, Vector2 centre, float radius)
    {
        zones.Add(new Zone { kind = kind, centre = centre, radius = radius });

        NebulaCloud.Build(transform, kind, centre, radius); // EAW-style billowing gas (see NebulaCloud).
    }

    private void Update()
    {
        if (Time.time < nextIonTickAt) return;
        nextIonTickAt = Time.time + 0.5f;
        if (!zones.Exists(z => z.kind == Kind.IonStorm)) return;

        // Ion storms eat strike craft.
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead || !unit.TryGetComponent(out Squadron squadron)) continue;
            foreach (Fighter fighter in squadron.fighters)
            {
                if (fighter == null || !fighter.Alive || !InIonStorm(fighter.transform.position)) continue;
                if (fighter.TryGetComponent(out HardpointHealth health))
                {
                    DamageLedger.Begin(null, DamageLedger.Weapon.IonStorm);
                    health.DealDamage(health.maxHealth * IonDamagePerSecond * 0.5f);
                    DamageLedger.End();
                }
            }
        }
    }
}
