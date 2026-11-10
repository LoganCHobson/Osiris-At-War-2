using System.Collections.Generic;
using UnityEngine;

// Battle-side view of heroes: which hero flagships are on the field for each side, and the buffs they give
// to every ship on that side while they live. Several heroes on one side stack, up to a cap.
public static class HeroForces
{
    public const float MaxStack = 0.6f;

    private class Present
    {
        public UnitHealthManager unit;
        public HeroDefinition hero;
        public bool attackerSide;
    }

    private static readonly List<Present> present = new List<Present>();

    public static void Register(GameObject spawned, ShipEntry ship, bool attackerSide)
    {
        if (ship == null || !ship.IsHero) return;
        HeroDefinition hero = ship.Hero;
        UnitHealthManager unit = spawned != null ? spawned.GetComponent<UnitHealthManager>() : null;
        if (hero == null || unit == null) return;

        present.RemoveAll(p => p.unit == null);
        present.Add(new Present { unit = unit, hero = hero, attackerSide = attackerSide });
    }

    // The total buff for a side from its living heroes on the field (0 = none).
    public static float Buff(bool attackerSide, HeroBuff buff)
    {
        float total = 0f;
        for (int i = present.Count - 1; i >= 0; i--)
        {
            Present p = present[i];
            if (p.unit == null)
            {
                present.RemoveAt(i);
                continue;
            }
            if (p.unit.IsDead || p.attackerSide != attackerSide) continue;
            total += p.hero.Buff(buff);
        }
        return Mathf.Min(total, MaxStack);
    }

    // Convenience: the buff for whatever side this unit is on.
    public static float BuffFor(Component unit, HeroBuff buff)
    {
        UnitHealthManager health = unit != null ? unit.GetComponentInParent<UnitHealthManager>() : null;
        return health != null ? Buff(health.isAttackerSide, buff) : 0f;
    }

    public static IEnumerable<(HeroDefinition hero, UnitHealthManager unit, bool attackerSide)> OnField()
    {
        foreach (Present p in present)
        {
            if (p.unit != null && !p.unit.IsDead) yield return (p.hero, p.unit, p.attackerSide);
        }
    }

    public static void Clear() => present.Clear();
}
