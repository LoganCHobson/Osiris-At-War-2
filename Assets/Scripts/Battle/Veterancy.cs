using System.Collections.Generic;
using UnityEngine;

// Ship veterancy: a ship that comes through campaign battles earns experience and climbs the ranks, and
// each rank makes it a little better. Kept deliberately small (at most +12% damage and 20% tighter aim, at
// Legendary) so a veteran fleet is an edge, not a steamroller. Nothing else carries between battles:
// damage is repaired as before.
//
// Experience per campaign battle, for every ship that was deployed and is still with its fleet afterwards
// (retreated ships count, destroyed ones don't):
//   +10 for coming through it, +5 more if its side won,
//   +1 per 10 points of enemy combat power its own guns destroyed (a destroyer is ~60-90).
// Skirmish battles award nothing.
public static class Veterancy
{
    public enum Rank { Green, Regular, Veteran, Elite, Legendary }

    private static readonly int[] Thresholds = { 0, 20, 60, 140, 300 };
    public const float DamagePerRank = 0.03f;   // +3% gun damage per rank
    public const float SpreadPerRank = 0.05f;   // 5% tighter shot spread per rank
    public const float PowerPerRank = 0.03f;    // counts 3% stronger per rank when the AI weighs fleets
    public const int SurvivedXp = 10, WonXp = 5;
    public const float XpPerPowerDestroyed = 0.1f;

    public static Rank RankOf(int xp)
    {
        for (int r = Thresholds.Length - 1; r > 0; r--)
        {
            if (xp >= Thresholds[r]) return (Rank)r;
        }
        return Rank.Green;
    }

    public static Rank RankOf(ShipEntry ship) => ship != null ? RankOf(ship.xp) : Rank.Green;

    // Experience still needed for the next rank (0 at Legendary).
    public static int ToNextRank(int xp)
    {
        Rank rank = RankOf(xp);
        return rank == Rank.Legendary ? 0 : Thresholds[(int)rank + 1] - xp;
    }

    public static string Title(Rank rank) => rank.ToString();

    public static float DamageMultiplier(ShipEntry ship) => 1f + DamagePerRank * (int)RankOf(ship);
    public static float SpreadMultiplier(ShipEntry ship) => 1f - SpreadPerRank * (int)RankOf(ship);
    public static float PowerMultiplier(int xp) => 1f + PowerPerRank * (int)RankOf(xp);

    // ---- during a battle: enemy combat power each ship's guns destroyed ----

    private static readonly Dictionary<ShipEntry, float> destroyed = new Dictionary<ShipEntry, float>();
    private static readonly List<string> promotions = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        destroyed.Clear();
        promotions.Clear();
    }

    public static void BattleStarted() => destroyed.Clear();

    // Called as hull damage lands (UnitHealthManager.SubtractHealth); credits whoever's shot it was
    // (DamageLedger's current source) with the share of the target's combat power it took away.
    public static void HullDamage(UnitHealthManager target, float amount)
    {
        UnitHealthManager from = DamageLedger.CurrentSource;
        if (from == null || from.entry == null || target == null || from.isAttackerSide == target.isAttackerSide) return;
        if (target.maxHealth <= 0f || target.sourceShip == null || amount <= 0f) return;
        float share = Mathf.Min(amount, Mathf.Max(0f, target.currentHealth)) / target.maxHealth;
        destroyed[from.entry] = (destroyed.TryGetValue(from.entry, out float sum) ? sum : 0f) + share * target.sourceShip.combatPower;
    }

    // ---- after a battle ----

    // Awards experience to both sides' surviving ships (campaign battles only) and queues promotion
    // messages for the player's. Call once the battle's losses and retreats are settled, before Lock().
    public static void Award(BattleContext context, bool attackerLost)
    {
        if (context == null || !context.hasPendingBattle || context.isSkirmish) return;
        AwardSide(context.attackerTokens, won: !attackerLost, player: Strength.IsPlayer(context.attackerFaction));
        AwardSide(context.defenderTokens, won: attackerLost, player: Strength.IsPlayer(context.defenderFaction));
        destroyed.Clear();
    }

    private static void AwardSide(List<BattleContext.ShipToken> tokens, bool won, bool player)
    {
        foreach (BattleContext.ShipToken token in tokens)
        {
            ShipEntry ship = token.ship;
            if (ship == null || !token.claimed || token.lost) continue; // Never fought, or didn't come back.
            Rank before = RankOf(ship.xp);
            int gain = SurvivedXp + (won ? WonXp : 0)
                       + Mathf.RoundToInt((destroyed.TryGetValue(ship, out float power) ? power : 0f) * XpPerPowerDestroyed);
            ship.xp += gain;
            Rank after = RankOf(ship.xp);
            if (player && after > before) promotions.Add($"{ship.shipName} promoted to {Title(after)}");
        }
    }

    // The player's promotions from the last battle, once (the galactic map shows them on return).
    public static List<string> TakePromotions()
    {
        List<string> taken = new List<string>(promotions);
        promotions.Clear();
        return taken;
    }
}
