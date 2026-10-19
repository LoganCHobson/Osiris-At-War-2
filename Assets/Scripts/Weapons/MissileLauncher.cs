using System.Collections;
using UnityEngine;

// Missile tubes on destroyers and corvettes. Fires a salvo at whatever ship its guns are shooting at (or
// the nearest enemy ship in range), then reloads. Shield-equipped (Avanti) ships carry plasma torpedoes;
// everyone else carries conventional missiles. Fitted by GameManager.ApplyAllegiance when a ship enters a
// battle, so the ship prefabs don't need rebuilding.
public class MissileLauncher : MonoBehaviour
{
    private const float SearchInterval = 0.5f;

    public MissileArsenal.Warhead warhead;
    public int salvo;
    public float range;
    public LayerMask hostileMask;

    private UnitHealthManager health;
    private HullDamage hull;
    private TurretController[] guns;
    private float nextSalvoAt;
    private float nextSearchAt;
    private bool firing;
    private int side = 1;

    public static void Fit(GameObject ship, LayerMask hostileMask)
    {
        if (!ship.TryGetComponent(out SpaceUnit unit) || !ship.TryGetComponent(out UnitHealthManager _)) return;

        MissileArsenal arsenal = MissileArsenal.Instance;
        MissileArsenal.Warhead warhead = ship.GetComponent<ShieldGenerator>() != null ? arsenal.plasma : arsenal.conventional;
        int salvo = warhead.SalvoFor(unit.shipType);
        if (salvo <= 0) return;

        if (!ship.TryGetComponent(out MissileLauncher launcher)) launcher = ship.AddComponent<MissileLauncher>();
        launcher.warhead = warhead;
        launcher.salvo = salvo;
        launcher.hostileMask = hostileMask;
    }

    private void Start()
    {
        health = GetComponent<UnitHealthManager>();
        hull = GetComponent<HullDamage>();
        guns = System.Array.FindAll(GetComponentsInChildren<TurretController>(true), gun => gun.targetFilter != TurretController.TargetFilter.FightersOnly);

        float gunRange = 0f;
        foreach (TurretController gun in guns) gunRange = Mathf.Max(gunRange, gun.range);
        if (range <= 0f) range = (gunRange > 0f ? gunRange : 120f) * MissileArsenal.Instance.rangeMultiplier;

        // Tubes start part-loaded so a fleet's first volleys don't all go off together.
        nextSalvoAt = Time.time + warhead.reload * Random.Range(0.3f, 0.7f);
    }

    private void Update()
    {
        if (firing || warhead == null || Time.time < nextSalvoAt || Time.time < nextSearchAt) return;
        if (GameManager.CombatOver || health == null || health.IsDead) return;

        nextSearchAt = Time.time + SearchInterval * Random.Range(0.8f, 1.2f);
        UnitHealthManager target = PickTarget();
        if (target != null) StartCoroutine(Salvo(target));
    }

    private UnitHealthManager PickTarget()
    {
        foreach (TurretController gun in guns)
        {
            if (gun == null || gun.target == null) continue;
            UnitHealthManager ship = gun.target.GetComponentInParent<UnitHealthManager>();
            if (IsTarget(ship)) return ship;
        }

        UnitHealthManager nearest = null;
        float best = float.MaxValue;
        foreach (UnitHealthManager ship in UnitHealthManager.Active)
        {
            if (!IsTarget(ship)) continue;
            float distance = (ship.transform.position - transform.position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = ship;
            }
        }
        return nearest;
    }

    // An enemy ship or station (not a fighter squadron) within range.
    private bool IsTarget(UnitHealthManager ship)
    {
        if (ship == null || ship.IsDead || ((1 << ship.gameObject.layer) & hostileMask) == 0) return false;
        if (ship.TryGetComponent(out Squadron _)) return false;
        return (ship.transform.position - transform.position).sqrMagnitude <= range * range;
    }

    private IEnumerator Salvo(UnitHealthManager target)
    {
        firing = true;
        float lifetime = range * 1.6f / Mathf.Max(1f, warhead.speed);

        for (int i = 0; i < salvo; i++)
        {
            if (GameManager.CombatOver || health.IsDead || target == null || target.IsDead) break;

            side = -side;
            Missile.Fire(warhead, LaunchPoint(side), LaunchDirection(side, target.transform.position), target, gameObject.layer, hostileMask, lifetime);
            if (i < salvo - 1) yield return new WaitForSeconds(warhead.salvoSpacing);
        }

        nextSalvoAt = Time.time + warhead.reload;
        firing = false;
    }

    // Vertical launch cells along the spine, alternating port and starboard.
    private Vector3 LaunchPoint(int portOrStarboard)
    {
        Bounds bounds = hull != null ? hull.LocalBounds : new Bounds(Vector3.zero, Vector3.one * 6f);
        Vector3 local = bounds.center
            + Vector3.up * bounds.extents.y * 0.5f
            + Vector3.right * portOrStarboard * bounds.extents.x * 0.35f
            + Vector3.forward * bounds.extents.z * Random.Range(-0.3f, 0.3f);
        return transform.TransformPoint(local);
    }

    // Pops up out of the cell, already leaning toward the target, so it doesn't loft high over the fight.
    private Vector3 LaunchDirection(int portOrStarboard, Vector3 targetPosition)
    {
        Vector3 toward = targetPosition - transform.position;
        toward.y = 0f;
        toward = toward.sqrMagnitude > 0.01f ? toward.normalized : transform.forward;
        return (transform.up * 0.6f + toward + transform.right * portOrStarboard * 0.35f).normalized;
    }
}
