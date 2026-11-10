using System.Collections.Generic;
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
        launcher.MountCells(); // Before the ship totals its health, so the launch block counts as a hardpoint.
    }

    // ---------------- Launch cells ----------------
    // The vertical launch blocks modelled on the Human hulls (a grid of hatches on the deck, measured off the
    // models: x across, z along, in the ship's local space). Missiles leave from these cells in turn. The block
    // is a hardpoint: knock it out and the launcher is finished, and whatever's still in the tubes cooks off.
    private static readonly Dictionary<string, Vector2[]> CellLayouts = new Dictionary<string, Vector2[]>
    {
        { "Human_Destroyer", new[] { new Vector2(-0.38f, -9.67f), new Vector2(0.38f, -9.67f), new Vector2(-0.38f, -10.33f), new Vector2(0.38f, -10.33f),
                                      new Vector2(-0.38f, -10.94f), new Vector2(0.38f, -10.94f), new Vector2(-0.38f, -11.6f), new Vector2(0.38f, -11.6f) } },
        { "Human_Corvette", new[] { new Vector2(-0.23f, 4.34f), new Vector2(0.23f, 4.34f), new Vector2(-0.23f, 3.53f), new Vector2(0.23f, 3.53f),
                                     new Vector2(-0.23f, 2.83f), new Vector2(0.23f, 2.83f) } },
    };

    private readonly List<Vector3> cells = new List<Vector3>(); // Local positions on the deck.
    private int nextCell;
    private HardpointHealth tubes;
    private bool tubesLost;

    private void MountCells()
    {
        if (cells.Count > 0) return;
        string kind = name.Replace("(Clone)", "").Trim();
        Vector2[] layout = null;
        foreach (KeyValuePair<string, Vector2[]> entry in CellLayouts)
        {
            if (kind.StartsWith(entry.Key)) { layout = entry.Value; break; }
        }
        if (layout == null) return;

        // Find the deck under each cell: straight down onto the ship's own hull colliders.
        Physics.SyncTransforms();
        MeshCollider[] hullColliders = GetComponentsInChildren<MeshCollider>();
        foreach (Vector2 cell in layout)
        {
            Vector3 above = transform.TransformPoint(new Vector3(cell.x, 60f, cell.y));
            Ray ray = new Ray(above, -transform.up);
            float best = float.MaxValue;
            Vector3 hitPoint = Vector3.zero;
            foreach (MeshCollider collider in hullColliders)
            {
                if (collider.GetComponentInParent<TurretController>() != null) continue;
                if (collider.Raycast(ray, out RaycastHit hit, 200f) && hit.distance < best)
                {
                    best = hit.distance;
                    hitPoint = hit.point;
                }
            }
            if (best < float.MaxValue) cells.Add(transform.InverseTransformPoint(hitPoint));
        }
        if (cells.Count == 0) return;

        Vector3 centre = Vector3.zero;
        foreach (Vector3 c in cells) centre += c;
        centre /= cells.Count;
        Bounds block = new Bounds(centre, Vector3.zero);
        foreach (Vector3 c in cells) block.Encapsulate(c);
        tubes = HardpointFactory.Create(gameObject, "MissileTubes", centre, block.size + new Vector3(0.7f, 0.4f, 0.7f));
        if (tubes != null) tubes.die.AddListener(OnTubesDestroyed);
    }

    private void OnTubesDestroyed()
    {
        if (tubesLost) return;
        tubesLost = true;
        StartCoroutine(CookOff());
    }

    // The loaded missiles going up in the tubes, one after another.
    private IEnumerator CookOff()
    {
        HullDamage hullDamage = GetComponent<HullDamage>();
        DestructionProfile profile = hullDamage != null ? hullDamage.profile : null;
        int blasts = Mathf.Max(3, cells.Count);
        for (int i = 0; i < blasts; i++)
        {
            Vector3 cell = transform.TransformPoint(cells[Random.Range(0, cells.Count)]);
            if (profile != null)
            {
                hullDamage.Explode(i == 0 ? profile.mediumExplosion : profile.smallExplosion, cell + transform.up * 0.3f, (i == 0 ? profile.mediumExplosionScale : profile.smallExplosionScale) * 0.35f);
                DestructionFx.Flash(cell, new Color(1f, 0.6f, 0.3f), 2.5f, 10f, 0.25f);
            }
            if (i == 0 && hullDamage != null) hullDamage.SpawnDamageEffect(cell, transform.up);
            yield return new WaitForSeconds(Random.Range(0.15f, 0.5f));
        }
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
        if (firing || warhead == null || tubesLost || Time.time < nextSalvoAt || Time.time < nextSearchAt) return;
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
            if (GameManager.CombatOver || health.IsDead || tubesLost || target == null || target.IsDead) break;

            side = -side;
            Missile fired = Missile.Fire(warhead, LaunchPoint(side), LaunchDirection(side, target.transform.position), target, gameObject.layer, hostileMask, lifetime);
            if (fired != null) { fired.source = health; DamageLedger.Fired(health, fired.LedgerWeapon); }
            if (i < salvo - 1) yield return new WaitForSeconds(warhead.salvoSpacing);
        }

        nextSalvoAt = Time.time + warhead.reload;
        firing = false;
    }

    // Out of the next launch cell in the block; ships without a modelled block use cells along the spine.
    private Vector3 LaunchPoint(int portOrStarboard)
    {
        if (cells.Count > 0)
        {
            Vector3 cell = cells[nextCell++ % cells.Count];
            return transform.TransformPoint(cell) + transform.up * 0.25f;
        }
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
        if (cells.Count > 0)
        {
            // Straight up out of the hatch, already starting to lean toward the target.
            Vector3 lean = targetPosition - transform.position;
            lean = Vector3.ProjectOnPlane(lean, transform.up);
            lean = lean.sqrMagnitude > 0.01f ? lean.normalized : transform.forward;
            return (transform.up * 1.4f + lean * 0.35f).normalized;
        }
        Vector3 toward = targetPosition - transform.position;
        toward.y = 0f;
        toward = toward.sqrMagnitude > 0.01f ? toward.normalized : transform.forward;
        return (transform.up * 0.6f + toward + transform.right * portOrStarboard * 0.35f).normalized;
    }
}
