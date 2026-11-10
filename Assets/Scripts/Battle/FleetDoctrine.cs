using System.Collections.Generic;
using UnityEngine;

// How each faction builds and fights, applied to every warship, station and shipyard as it spawns (strike
// craft are handled by their squadrons). Multipliers on the prefab values:
//
// AVANTI - masters of space, and they know it. Quality over quantity: every hull is a work of art, expensive and
// slow to build. Ships are glass behind shields - modest hull, a strong regenerating shield over it, and far
// more guns than a Human hull of the same class, at shorter range. The battleship is the pride of the navy and
// the best ship in it; cruisers are fine ships; the escorts (corvettes, destroyers) are built because someone
// must, and get the least care - still shielded, still doing their job (point defence, torpedoes), but poor
// value for what they cost. Few strike craft, little faith in bombing or missiles.
//
// HUMANS - established naval doctrine: range, missiles, air superiority. Ships are cheap, quick to build and
// hardier than they look, with fewer guns that reach further. Battleships hold the line and soak damage;
// cruisers are the workhorses (line, escort, picket - heavier CIWS); destroyers are agile missile boats;
// corvettes are point defence with missiles for capital ships. Carriers put up far more squadrons than the
// Avanti ever will - large bomber sorties, flexible fighters, and interceptors that are hard to hit.
//
// The capital shipyard is a construction yard, not a fortress: light security guns, a small patrol, and a
// weak structure. Its real defence is the squadron of ships it lends the battle (see GameManager).
public static class FleetDoctrine
{
    public struct Entry
    {
        public float health;        // Hardpoint (and so hull and shield) health.
        public float guns;          // Anti-ship gun damage.
        public float pointDefence;  // Point-defence damage.
        public Entry(float health, float guns, float pointDefence) { this.health = health; this.guns = guns; this.pointDefence = pointDefence; }
    }

    // Avanti batteries fire a little slower than they used to, each shot hitting harder (TurretController.
    // ApplyBatteryCadence). Their guns are many but few bear at once, so per shot they hit hard.
    public const float AvantiGuns = 1.8f;

    public static Entry For(bool avanti, ShipType type, bool shipyard)
    {
        if (shipyard) return avanti ? new Entry(1.1f, 0.2f * AvantiGuns, 1f) : new Entry(1.05f, 0.3f, 1f);
        if (avanti)
        {
            return type switch
            {
                ShipType.Battleship => new Entry(3.1f, AvantiGuns, 1f),  // The pride of the navy.
                ShipType.Cruiser => new Entry(2.2f, AvantiGuns, 1f),
                ShipType.Destroyer => new Entry(2.3f, AvantiGuns, 1f),
                ShipType.Corvette => new Entry(2.45f, AvantiGuns, 1f),
                ShipType.Carrier => new Entry(2.4f, AvantiGuns, 1f),
                ShipType.Station => new Entry(2.0f, AvantiGuns, 1f),
                _ => new Entry(2.3f, AvantiGuns, 1f),
            };
        }
        return type switch
        {
            ShipType.Battleship => new Entry(3.0f, 1f, 1f),
            ShipType.Cruiser => new Entry(2.6f, 1f, 3f),
            ShipType.Destroyer => new Entry(2.6f, 1f, 1f),
            ShipType.Corvette => new Entry(2.6f, 1f, 1f),
            ShipType.Carrier => new Entry(2.6f, 1f, 1f),
            ShipType.Station => new Entry(2.6f, 1f, 1f),
            _ => new Entry(2.6f, 1f, 1f),
        };
    }

    // Strike craft each carrier, station or shipyard hangar holds, by role (spread over its hangars).
    private static (int fighters, int interceptors, int bombers) Complement(bool avanti, ShipType type, bool shipyard)
    {
        if (shipyard) return (1, 1, 0);                    // A security patrol, no more.
        if (type == ShipType.Station) return avanti ? (2, 1, 1) : (3, 2, 2);
        return avanti ? (2, 1, 1) : (4, 3, 4);             // Carriers.
    }

    private static readonly HashSet<int> applied = new HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetApplied() => applied.Clear();

    public static void Apply(GameObject spawned, bool avanti, bool shipyard)
    {
        if (spawned.GetComponent<Squadron>() != null || spawned.GetComponent<UnitHealthManager>() == null) return;
        if (!applied.Add(spawned.GetInstanceID())) return; // Once per unit.

        SpaceUnit unit = spawned.GetComponent<SpaceUnit>();
        ShipType type = unit != null ? unit.shipType : ShipType.Station;
        Entry entry = For(avanti, type, shipyard);

        foreach (HardpointHealth hardpoint in spawned.GetComponentsInChildren<HardpointHealth>(true))
        {
            hardpoint.maxHealth *= entry.health;
            hardpoint.currentHealth = hardpoint.maxHealth;
        }
        foreach (TurretController turret in spawned.GetComponentsInChildren<TurretController>(true))
        {
            bool pd = turret.targetFilter == TurretController.TargetFilter.FightersOnly;
            turret.damage *= pd ? entry.pointDefence : entry.guns;
            // Avanti escorts hold the line: their batteries fire at ships and leave strike craft to the beams.
            // (Human escort guns stay flexible and will engage craft.)
            if (avanti && !pd && (type == ShipType.Corvette || type == ShipType.Destroyer)) turret.shipsOnly = true;
        }

        HangarHardpoint[] hangars = spawned.GetComponentsInChildren<HangarHardpoint>(true);
        if (hangars.Length > 0) Rearm(hangars, Complement(avanti, type, shipyard), shipyard);
    }

    // Refills the hangars to the doctrine's complement, using the squadron types they already carry.
    private static void Rearm(HangarHardpoint[] hangars, (int fighters, int interceptors, int bombers) want, bool shipyard)
    {
        Ship fighter = null, interceptor = null, bomber = null;
        foreach (HangarHardpoint hangar in hangars)
            foreach (HangarHardpoint.Bay bay in hangar.complement)
            {
                if (bay.squadron == null) continue;
                switch (bay.squadron.fleetRole)
                {
                    case FleetRole.Fighter: fighter ??= bay.squadron; break;
                    case FleetRole.Interceptor: interceptor ??= bay.squadron; break;
                    case FleetRole.Bomber: bomber ??= bay.squadron; break;
                }
            }

        foreach (HangarHardpoint hangar in hangars)
        {
            hangar.complement = new List<HangarHardpoint.Bay>();
            if (shipyard) hangar.launchInterval = Mathf.Max(hangar.launchInterval, 30f); // A patrol, not a wing.
        }
        // Deal the squadrons out across the hangars in turn, so losing one hangar loses part of each role.
        int next = 0;
        void Deal(Ship squadron, int count)
        {
            if (squadron == null) return;
            for (int i = 0; i < count; i++)
            {
                HangarHardpoint hangar = hangars[next++ % hangars.Length];
                HangarHardpoint.Bay bay = hangar.complement.Find(b => b.squadron == squadron);
                if (bay == null) hangar.complement.Add(new HangarHardpoint.Bay { squadron = squadron, count = 1 });
                else bay.count++;
            }
        }
        Deal(fighter, want.fighters);
        Deal(interceptor, want.interceptors);
        Deal(bomber, want.bombers);
    }
}
