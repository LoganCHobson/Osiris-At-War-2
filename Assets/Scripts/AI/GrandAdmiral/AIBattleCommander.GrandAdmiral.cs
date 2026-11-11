using System.Collections.Generic;
using System.Linq;
using Osiris.GrandAdmiral;
using UnityEngine;

// Grand Admiral in battle. The heuristic commander still does all the flying: positioning, focus
// fire bookkeeping, drop zones, aiming. Every few seconds the network makes the four calls that
// decide fights: stance, whether to retreat, how hard to commit reserves, and what to focus.
// Hard safety rails stay outside the network (never flee a winning fight, always flee a hopeless
// one), and every call is recorded so the AI can learn from the battle afterwards.
public partial class AIBattleCommander
{
    [Tooltip("The other side is a human player (records 'vs player' and enables local adaptation).")]
    public bool OpponentIsPlayer;
    [Tooltip("Self-play: explore more, for the in-cluster trainer. Leave off for real games.")]
    public bool SelfPlay;
    public const double SelfPlayExploration = 0.2;

    public bool GrandAdmiralActive => gaPolicy != null;
    public GARecord GrandAdmiralRecord => gaRecord;

    private GAPolicy gaPolicy;
    private GARecord gaRecord;
    private readonly List<GAAdaptation.Step> gaSteps = new List<GAAdaptation.Step>();
    private readonly Dictionary<UnitHealthManager, float> gaEnemySeen = new Dictionary<UnitHealthManager, float>();
    private float gaNextTick;
    private float gaStartTime;
    private double gaLastLogRatio = double.NaN;
    private Stance gaStance = Stance.Engage;
    private int gaCommit = GASpec.CommitMatch;
    private bool gaWantsRetreat;
    private UnitHealthManager gaFocus;
    private bool gaFinished;

    private void InitGrandAdmiral(List<ShipEntry> roster)
    {
        if (Difficulty.level != DifficultyLevel.GrandAdmiral || !GrandAdmiral.Available) return;
        if (Faction == null || !Faction.CanBeGrandAdmiral) return; // Avanti only, never Human: no network, no record.

        GAModel model = GrandAdmiral.Model;
        GAAdaptation adaptation = OpponentIsPlayer && !SelfPlay ? GrandAdmiral.Adaptation : null;
        if (adaptation != null && adaptation.ModelVersion != model.Version) adaptation = null;
        gaPolicy = new GAPolicy(model, adaptation, Random.Range(int.MinValue, int.MaxValue),
            SelfPlay ? SelfPlayExploration : (double?)null);

        gaStartTime = Time.time;
        gaNextTick = Time.time;
        gaRecord = new GARecord
        {
            ModelVersion = model.Version,
            ClientVersion = Application.version,
            Adapted = adaptation != null && adaptation.IsActive,
            VsPlayer = OpponentIsPlayer,
            Attacker = IsAttackerSide,
            Temperature = gaPolicy.Temperature,
            Exploration = gaPolicy.Exploration,
            StartPower = Strength.Of(roster),
        };
        BattleContext context = BattleContext.Instance;
        if (context != null && context.hasPendingBattle)
        {
            gaRecord.EnemyStartPower = Strength.Of(IsAttackerSide ? context.defenderRoster : context.attackerRoster);
        }
    }

    // Called from Update after Scan + ComputeGeometry.
    private void GrandAdmiralTick()
    {
        TrackEnemies();
        if (gaFinished || Time.time < gaNextTick) return;
        gaNextTick = Time.time + (float)gaPolicy.Model.Knob(GASpec.Knob.TickSeconds);

        GAState state = gaPolicy.Evaluate(Features());
        GARecord.Decision decision = new GARecord.Decision { Time = Time.time - gaStartTime, State = state };

        decision.Stance = gaPolicy.ChooseStance(state, CanRegroup());
        gaStance = (Stance)decision.Stance.Action;

        double outlook = System.Math.Exp(state.Features[GASpec.LogTotalRatio]);
        gaWantsRetreat = false;
        if (RetreatIsOption() && outlook >= GASpec.RetreatForcedBelow && outlook <= GASpec.RetreatNeverAbove)
        {
            decision.Retreat = gaPolicy.ChooseRetreat(state);
            gaWantsRetreat = decision.Retreat.Action == 1;
        }

        if (reserve.Count > 0)
        {
            decision.Commit = gaPolicy.ChooseCommit(state);
            gaCommit = decision.Commit.Action;
        }

        ChooseOrders(state, decision); // Formation, escort, manoeuvre, tempo (AIBattleCommander.GrandAdmiralOrders.cs).

        List<Contact> ranked = RankedContacts();
        if (ranked.Count >= 2)
        {
            decision.Target = gaPolicy.ChooseTarget(state, ContactFeatures(ranked));
            gaFocus = ranked[decision.Target.Action].health;
        }
        else
        {
            gaFocus = ranked.Count == 1 ? ranked[0].health : null;
        }

        gaRecord.Add(decision);
        gaSteps.Add(new GAAdaptation.Step { State = state, Stance = decision.Stance, Retreat = decision.Retreat, Commit = decision.Commit });
    }

    private bool CanRegroup() => (reserve.Count > 0 || friendlyDefenses.Count > 0) && (!IsAttackerSide || Patient);

    private bool RetreatIsOption()
    {
        if (!personality.canRetreat || (Faction != null && Faction.isNeutral)) return false;
        return RetreatManager.Instance != null && RetreatManager.Instance.CanRetreat(IsAttackerSide);
    }

    // ---- what the network sees (scaling documented in osiris_ml/spec.py) ----

    private double[] Features()
    {
        double[] x = new double[GASpec.NFeatures];
        float field = FieldPower();
        float reservePower = ReservePower;
        float total = field + reservePower;
        float theirs = Mathf.Max(1f, EnemyFieldPower());

        double logTotal = GASpec.Clamp(System.Math.Log(Mathf.Max(1f, total) / theirs), -3, 3);
        x[GASpec.LogTotalRatio] = logTotal;
        x[GASpec.LogFieldRatio] = GASpec.Clamp(System.Math.Log(Mathf.Max(1f, field) / theirs), -3, 3);
        x[GASpec.RatioTrend] = double.IsNaN(gaLastLogRatio) ? 0 : GASpec.Clamp(logTotal - gaLastLogRatio, -1, 1);
        gaLastLogRatio = logTotal;

        x[GASpec.OurHealth] = units.Count > 0 ? units.Average(u => HealthFraction(u.health)) : 0;
        List<Contact> ships = enemies.Where(c => !c.health.IsDefense).ToList();
        x[GASpec.EnemyHealth] = ships.Count > 0 ? ships.Average(c => HealthFraction(c.health)) : 0;
        x[GASpec.OurReserveShare] = total > 0 ? reservePower / total : 0;

        float expected = gaRecord.EnemyStartPower > 0 ? (float)gaRecord.EnemyStartPower : 0f;
        float seenBase = gaEnemySeen.Values.Sum();
        x[GASpec.EnemyUnseenShare] = expected > 0 ? GASpec.Clamp((expected - seenBase) / expected, 0, 1) : 0;
        x[GASpec.OurLosses] = gaRecord.StartPower > 0 ? GASpec.Clamp(1 - (total - DefensePower()) / gaRecord.StartPower, 0, 1) : 0;
        x[GASpec.EnemyLosses] = EnemyStartForRecord() > 0 ? GASpec.Clamp(EnemyDamage() / EnemyStartForRecord(), 0, 1) : 0;

        float ourDps = units.Sum(u => u.dps), theirDps = enemies.Sum(c => c.dps);
        x[GASpec.LogDpsRatio] = GASpec.Clamp(System.Math.Log((ourDps + 1) / (theirDps + 1)), -3, 3);
        float enemyRange = enemies.Count > 0 ? enemies.Average(c => c.range) : defaultRange;
        x[GASpec.LogRangeRatio] = GASpec.Clamp(System.Math.Log(Mathf.Max(1f, ourRange) / Mathf.Max(1f, enemyRange)), -2, 2);
        x[GASpec.CenterDistance] = GASpec.Clamp(Vector3.Distance(ourCenter, enemyCenter) / Mathf.Max(1f, ourRange * 4f), 0, 3);

        float fieldShips = units.Sum(u => UnitPower(u));
        x[GASpec.OurStrikeShare] = fieldShips > 0 ? units.Where(u => IsStrike(u.ship)).Sum(u => UnitPower(u)) / fieldShips : 0;
        x[GASpec.EnemyStrikeShare] = enemies.Where(c => IsStrike(c.health.sourceShip)).Sum(c => c.power) / theirs;
        x[GASpec.EnemyCapitalShare] = enemies.Where(c => IsCapital(c.health.sourceShip)).Sum(c => c.power) / theirs;
        x[GASpec.PopHeadroom] = populationCap > 0 ? GASpec.Clamp((populationCap - FieldPopulation()) / (double)populationCap, 0, 1) : 0;
        x[GASpec.Time] = GASpec.Clamp((Time.time - gaStartTime) / 600.0, 0, 2);
        x[GASpec.IsAttacker] = IsAttackerSide ? 1 : 0;
        x[GASpec.FriendlyDefenses] = friendlyDefenses.Count > 0 ? 1 : 0;
        x[GASpec.EnemyDefenses] = enemies.Any(c => c.health.IsDefense) ? 1 : 0;
        x[GASpec.EnemyRetreating] = EnemyRetreating ? 1 : 0;
        x[GASpec.StancePress] = CurrentStance == Stance.Press ? 1 : 0;
        x[GASpec.StanceFallback] = CurrentStance == Stance.FallBack ? 1 : 0;
        x[GASpec.VsPlayer] = OpponentIsPlayer ? 1 : 0;
        SchemaTwoFeatures(x);
        return x;
    }

    private List<Contact> RankedContacts()
    {
        bool enemyRetreating = EnemyRetreating;
        foreach (Contact contact in enemies) contact.priority = HeuristicPriority(contact, enemyRetreating);
        return enemies.OrderByDescending(c => c.priority).Take(GASpec.MaxContacts).ToList();
    }

    private double[][] ContactFeatures(List<Contact> ranked)
    {
        float best = Mathf.Max(1e-9f, ranked[0].priority);
        double[][] rows = new double[ranked.Count][];
        for (int i = 0; i < ranked.Count; i++)
        {
            Contact c = ranked[i];
            Ship ship = c.health.sourceShip;
            rows[i] = new double[GASpec.NContact];
            rows[i][GASpec.CHeuristicPriority] = GASpec.Clamp(System.Math.Log(Mathf.Max(1e-9f, c.priority) / best), -5, 0);
            rows[i][GASpec.CLogDps] = System.Math.Log(1 + c.dps) / 6;
            rows[i][GASpec.CLogPower] = System.Math.Log(1 + c.power) / 7;
            rows[i][GASpec.CHealth] = HealthFraction(c.health);
            rows[i][GASpec.CDistance] = GASpec.Clamp(Vector3.Distance(c.health.transform.position, ourCenter) / Mathf.Max(1f, ourRange * 2f), 0, 4);
            rows[i][GASpec.CIsDefense] = c.health.IsDefense ? 1 : 0;
            rows[i][GASpec.CIsCapital] = IsCapital(ship) ? 1 : 0;
            rows[i][GASpec.CIsStrike] = IsStrike(ship) ? 1 : 0;
            rows[i][GASpec.CIsCarrier] = ship != null && ship.fleetRole == FleetRole.Carrier ? 1 : 0;
        }
        return rows;
    }

    private static bool IsStrike(Ship ship) => ship != null && (ship.fleetRole == FleetRole.Fighter || ship.fleetRole == FleetRole.Interceptor || ship.fleetRole == FleetRole.Bomber);
    private static bool IsCapital(Ship ship) => ship != null && ship.CanLead;

    private float DefensePower()
    {
        float power = 0f;
        foreach (UnitHealthManager defense in friendlyDefenses) power += BasePower(defense) * HealthFraction(defense);
        return power;
    }

    // Every enemy ship we've seen, at full strength, so losses can be counted even after it's gone.
    private void TrackEnemies()
    {
        foreach (Contact contact in enemies)
        {
            if (contact.health.IsDefense || gaEnemySeen.ContainsKey(contact.health)) continue;
            gaEnemySeen[contact.health] = BasePower(contact.health);
            NoteEnemyArrival();
        }
    }

    private float EnemyDamage()
    {
        float damage = 0f;
        foreach (KeyValuePair<UnitHealthManager, float> seen in gaEnemySeen)
        {
            UnitHealthManager h = seen.Key;
            float left = h == null || h.IsDead ? 0f : HealthFraction(h);
            damage += seen.Value * (1f - left);
        }
        return damage;
    }

    private double EnemyStartForRecord()
    {
        return System.Math.Max(gaRecord.EnemyStartPower, gaEnemySeen.Values.Sum());
    }

    // ---- hooks used by the main commander ----

    private bool GrandAdmiralStance(out Stance stance)
    {
        stance = gaStance == Stance.FallBack && !CanRegroup() ? Stance.Engage : gaStance;
        return GrandAdmiralActive;
    }

    // null = let the heuristic decide; true/false = Grand Admiral's call (inside the safety rails).
    private bool? GrandAdmiralRetreat(float outlook)
    {
        if (!GrandAdmiralActive) return null;
        if (outlook < GASpec.RetreatForcedBelow) return true;
        if (outlook > GASpec.RetreatNeverAbove) return false;
        return gaWantsRetreat;
    }

    private bool GrandAdmiralCommit(out List<ShipEntry> deploying)
    {
        deploying = null;
        if (!GrandAdmiralActive) return false;
        switch (gaCommit)
        {
            case GASpec.CommitHold:
                deploying = units.Count > 0 ? new List<ShipEntry>() : TakeUpTo(1f, null);
                return true;
            case GASpec.CommitSurge:
                deploying = TakeDeployable();
                return true;
            default:
                return false; // "match": the Admiral's own reinforcement logic.
        }
    }

    private void GrandAdmiralFocus()
    {
        if (!GrandAdmiralActive || gaFocus == null || gaFocus.IsDead) return;
        int index = enemies.FindIndex(c => c.health == gaFocus);
        if (index <= 0) return;
        Contact focus = enemies[index];
        enemies.RemoveAt(index);
        enemies.Insert(0, focus);
    }

    // ---- the end ----

    // Called once when the battle is decided (GameManager.ConcludeBattle, or the simulators).
    public void NotifyBattleEnded(bool weWon, bool weRetreated, bool enemyRetreated)
    {
        if (!GrandAdmiralActive || gaFinished) return;
        gaFinished = true;
        Scan();
        TrackEnemies();

        float survivors = 0f;
        foreach (Unit unit in units)
        {
            if (unit.health != null && !unit.health.IsDead) survivors += UnitPower(unit);
        }
        gaRecord.Duration = Mathf.Max(0.1f, Time.time - gaStartTime);
        gaRecord.EndPower = survivors + ReservePower;
        gaRecord.EnemyStartPower = EnemyStartForRecord();
        gaRecord.EnemyEndPower = System.Math.Max(0, gaRecord.EnemyStartPower - EnemyDamage());
        gaRecord.Won = weWon && !weRetreated;
        gaRecord.Retreated = weRetreated;
        gaRecord.EnemyRetreated = enemyRetreated;

        GrandAdmiral.Complete(gaRecord, gaSteps, learnLocally: OpponentIsPlayer && !SelfPlay);
    }
}
