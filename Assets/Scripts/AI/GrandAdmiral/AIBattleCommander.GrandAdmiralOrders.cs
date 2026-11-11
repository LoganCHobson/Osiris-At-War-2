using System.Collections.Generic;
using System.Linq;
using Osiris.GrandAdmiral;
using UnityEngine;

// Grand Admiral's schema 2 senses and orders. The senses are battle-state numbers the network reads
// (shields, missiles, strike craft, gunnery, terrain, tempo, the flagship). The orders are four
// three-way calls the heuristic commander then carries out:
//   formation  standard / tight / loose          how far apart the battle line flies
//   escort     line / screen / hunt              escorts in the line, guarding capitals, or chasing craft
//   manoeuvre  line / flank / envelop            one line, or a detachment swinging round one or both wings
//   tempo      steady / rotate / pulse           shield rhythm: hold, swap drained ships out, or step back
// How far each goes (spacing, flank share and width, shield thresholds, pulse depth, screen radius)
// is a server knob, clamped by the client. Nothing here runs unless Grand Admiral is commanding.
public partial class AIBattleCommander
{
    private const float BowArcDegrees = 30f;
    private const float WaveGapSeconds = 10f;      // A new enemy ship this long after the last one starts a new wave.
    private const float MaxRotatedShare = 0.4f;    // Never rotate more of the line than this out at once.
    private const int FlankMinWarships = 4, EnvelopMinWarships = 7;

    private int gaFormation, gaEscort, gaManeuver, gaTempo;
    private double gaLastShield = double.NaN;
    private double gaLastCenterDistance = double.NaN;
    private double gaLastDealt = double.NaN, gaLastTaken = double.NaN;
    private int gaEnemyWaves;
    private float gaLastEnemyArrival = float.NegativeInfinity;
    private readonly HashSet<Unit> gaRotated = new HashSet<Unit>();
    private readonly HashSet<Unit> gaFlankers = new HashSet<Unit>();
    private readonly Dictionary<UnitHealthManager, List<TurretController>> gaTurrets = new Dictionary<UnitHealthManager, List<TurretController>>();

    // ---- senses ----

    private List<TurretController> Turrets(UnitHealthManager health, HardpointManager hardpoints)
    {
        if (health == null || hardpoints == null) return new List<TurretController>();
        if (!gaTurrets.TryGetValue(health, out List<TurretController> turrets))
        {
            turrets = hardpoints.GetSpecificHardpoints<TurretController>();
            gaTurrets[health] = turrets;
        }
        turrets.RemoveAll(t => t == null);
        return turrets;
    }

    private static bool IsPointDefence(TurretController turret) => turret.gatling || turret.beam;

    private List<Unit> Warships() => units.FindAll(u => u.squadron == null && u.health != null && !u.health.IsDead);

    private static bool IsEscort(Unit unit) =>
        unit.squadron == null && unit.spaceUnit != null
        && (unit.spaceUnit.shipType == ShipType.Corvette || unit.spaceUnit.shipType == ShipType.Destroyer);

    private static bool IsCapitalUnit(Unit unit) => unit.squadron == null && unit.ship != null && unit.ship.CanLead && !IsEscort(unit);

    private Unit Flagship()
    {
        Unit best = null;
        float bestPower = 0f;
        foreach (Unit unit in units)
        {
            if (!IsCapitalUnit(unit) || unit.health == null || unit.health.IsDead) continue;
            float power = BasePower(unit.health);
            if (power > bestPower) { bestPower = power; best = unit; }
        }
        return best;
    }

    private static float ShieldOf(Unit unit, out bool shielded)
    {
        ShieldGenerator shield = unit.health != null ? unit.health.GetComponent<ShieldGenerator>() : null;
        shielded = shield != null && !shield.GeneratorDestroyed;
        return shielded ? shield.Fraction : 0f;
    }

    // Called from TrackEnemies when a ship is seen for the first time.
    private void NoteEnemyArrival()
    {
        if (Time.time - gaLastEnemyArrival > WaveGapSeconds) gaEnemyWaves++;
        gaLastEnemyArrival = Time.time;
    }

    private static double Spread(IEnumerable<Vector3> points, Vector3 centre, float range)
    {
        int n = 0;
        double sum = 0;
        foreach (Vector3 p in points)
        {
            Vector3 d = p - centre;
            d.y = 0f;
            sum += d.magnitude;
            n++;
        }
        return n > 0 ? GASpec.Clamp(sum / n / Mathf.Max(1f, range), 0, 3) : 0;
    }

    private static float SpeedOf(Component component)
    {
        SpaceUnit unit = component != null ? component.GetComponent<SpaceUnit>() : null;
        return unit != null && unit.agent != null ? unit.agent.speed : 0f;
    }

    private static bool InAsteroids(Vector3 point) => BattleTerrain.Instance != null && BattleTerrain.Instance.InAsteroids(point);

    private bool AsteroidsWithin(Vector3 point, float distance)
    {
        if (BattleTerrain.Instance == null) return false;
        foreach (AsteroidField field in BattleTerrain.Instance.Fields)
        {
            if (field == null) continue;
            foreach (AsteroidField.Blob blob in field.blobs)
            {
                if (Vector2.Distance(blob.centre, new Vector2(point.x, point.z)) - blob.radius <= distance) return true;
            }
        }
        return false;
    }

    private void SchemaTwoFeatures(double[] x)
    {
        List<Unit> warships = Warships();

        // Shields.
        int shieldedShips = 0, lowShields = 0;
        double shieldSum = 0;
        float rotateBelow = (float)gaPolicy.Model.Knob(GASpec.Knob.RotateOutBelow);
        foreach (Unit unit in warships)
        {
            float fraction = ShieldOf(unit, out bool shielded);
            if (!shielded) continue;
            shieldedShips++;
            shieldSum += fraction;
            if (fraction < rotateBelow) lowShields++;
        }
        double shield = shieldedShips > 0 ? shieldSum / shieldedShips : 0;
        x[GASpec.OurShield] = shield;
        x[GASpec.ShieldTrend] = double.IsNaN(gaLastShield) ? 0 : GASpec.Clamp(shield - gaLastShield, -1, 1);
        gaLastShield = shield;
        x[GASpec.LowShieldShare] = shieldedShips > 0 ? (double)lowShields / shieldedShips : 0;

        // Missiles at us, and how much of our point defence is free to meet them.
        int incoming = 0;
        foreach (Missile missile in Missile.Active)
        {
            if (missile != null && missile.InFlight && missile.TargetShip != null && missile.TargetShip.isAttackerSide == IsAttackerSide) incoming++;
        }
        x[GASpec.MissilePressure] = GASpec.Clamp(incoming / (double)Mathf.Max(1, warships.Count), 0, 3);

        int pd = 0, pdIdle = 0, aimed = 0, blocked = 0, aligned = 0, withTarget = 0;
        foreach (Unit unit in warships)
        {
            foreach (TurretController turret in Turrets(unit.health, unit.hardpoints))
            {
                if (IsPointDefence(turret))
                {
                    pd++;
                    if (turret.target == null) pdIdle++;
                }
                else if (turret.target != null)
                {
                    aimed++;
                    if (!turret.CanFire) blocked++;
                }
            }
            if (unit.target != null)
            {
                withTarget++;
                Vector3 to = unit.target.transform.position - unit.health.transform.position;
                Vector3 forward = unit.health.transform.forward;
                to.y = 0f; forward.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(forward, to) <= BowArcDegrees) aligned++;
            }
        }
        x[GASpec.PdIdleShare] = pd > 0 ? (double)pdIdle / pd : 0;
        x[GASpec.BowAlignment] = withTarget > 0 ? (double)aligned / withTarget : 0;
        x[GASpec.LosBlockedShare] = aimed > 0 ? (double)blocked / aimed : 0;

        // Enemy strike craft.
        HashSet<UnitHealthManager> capitals = new HashSet<UnitHealthManager>(units.Where(IsCapitalUnit).Select(u => u.health));
        int craft = 0, bombers = 0, bombersOnCapitals = 0;
        foreach (Contact contact in enemies)
        {
            Squadron squadron = contact.health.GetComponent<Squadron>();
            if (squadron == null) continue;
            craft++;
            if (squadron.CraftRole != Squadron.Role.Bomber) continue;
            bombers++;
            if (squadron.CurrentTarget != null && capitals.Contains(squadron.CurrentTarget)) bombersOnCapitals++;
        }
        x[GASpec.EnemyCraftRatio] = GASpec.Clamp(craft / (double)Mathf.Max(1, warships.Count), 0, 3);
        x[GASpec.BombersOnCapitals] = bombers > 0 ? (double)bombersOnCapitals / bombers : 0;

        // Terrain between and around us.
        int inside = 0;
        const int samples = 12;
        for (int i = 1; i <= samples; i++)
        {
            if (InAsteroids(Vector3.Lerp(ourCenter, enemyCenter, i / (samples + 1f)))) inside++;
        }
        x[GASpec.CoverBetween] = (double)inside / samples;
        x[GASpec.CoverNear] = AsteroidsWithin(ourCenter, ourRange) ? 1 : 0;

        // Enemy waves.
        x[GASpec.EnemyWaves] = GASpec.Clamp(gaEnemyWaves / 5.0, 0, 2);
        x[GASpec.SinceEnemyWave] = float.IsNegativeInfinity(gaLastEnemyArrival) ? 2 : GASpec.Clamp((Time.time - gaLastEnemyArrival) / 120.0, 0, 2);

        // The flagship, and how much of the enemy's fire is on it.
        Unit flagship = Flagship();
        x[GASpec.FlagshipHealth] = flagship != null ? HealthFraction(flagship.health) : 0;
        if (flagship != null)
        {
            int enemyGuns = 0, onFlagship = 0;
            Transform flag = flagship.health.transform;
            foreach (Contact contact in enemies)
            {
                foreach (TurretController turret in Turrets(contact.health, contact.hardpoints))
                {
                    if (turret.target == null || IsPointDefence(turret)) continue;
                    enemyGuns++;
                    if (turret.target == flag || turret.target.IsChildOf(flag)) onFlagship++;
                }
            }
            x[GASpec.FlagshipUnderFire] = enemyGuns > 0 ? (double)onFlagship / enemyGuns : 0;
        }

        // Tempo of the fight.
        x[GASpec.Kited] = Kited ? 1 : 0;
        double distance = x[GASpec.CenterDistance];
        x[GASpec.ClosingRate] = double.IsNaN(gaLastCenterDistance) ? 0 : GASpec.Clamp(distance - gaLastCenterDistance, -1, 1);
        gaLastCenterDistance = distance;
        x[GASpec.OurSpread] = Spread(units.Where(u => u.health != null).Select(u => u.health.transform.position), ourCenter, ourRange);
        x[GASpec.EnemySpread] = Spread(enemies.Where(c => !c.health.IsDefense).Select(c => c.health.transform.position), enemyCenter, ourRange);
        float ourSpeed = warships.Count > 0 ? warships.Average(u => SpeedOf(u.health)) : 0f;
        List<Contact> enemyShips = enemies.Where(c => !c.health.IsDefense && c.health.GetComponent<Squadron>() == null).ToList();
        float theirSpeed = enemyShips.Count > 0 ? enemyShips.Average(c => SpeedOf(c.health)) : 0f;
        x[GASpec.LogSpeedRatio] = ourSpeed > 0 && theirSpeed > 0 ? GASpec.Clamp(System.Math.Log(ourSpeed / theirSpeed), -2, 2) : 0;

        double dealt = EnemyDamage();
        double taken = System.Math.Max(0, gaRecord.StartPower - (FieldPower() + ReservePower - DefensePower()));
        x[GASpec.DamageExchange] = double.IsNaN(gaLastDealt) ? 0
            : GASpec.Clamp(System.Math.Log((System.Math.Max(0, dealt - gaLastDealt) + 1) / (System.Math.Max(0, taken - gaLastTaken) + 1)), -3, 3);
        gaLastDealt = dealt;
        gaLastTaken = taken;

        // The orders standing right now.
        x[GASpec.FormationTight] = gaFormation == GASpec.FormationTightAction ? 1 : 0;
        x[GASpec.FormationLoose] = gaFormation == GASpec.FormationLooseAction ? 1 : 0;
        x[GASpec.EscortScreen] = gaEscort == GASpec.EscortScreenAction ? 1 : 0;
        x[GASpec.EscortHunt] = gaEscort == GASpec.EscortHuntAction ? 1 : 0;
        x[GASpec.ManeuverFlank] = gaManeuver == GASpec.ManeuverFlankAction ? 1 : 0;
        x[GASpec.ManeuverEnvelop] = gaManeuver == GASpec.ManeuverEnvelopAction ? 1 : 0;
        x[GASpec.TempoRotate] = gaTempo == GASpec.TempoRotateAction ? 1 : 0;
        x[GASpec.TempoPulse] = gaTempo == GASpec.TempoPulseAction ? 1 : 0;
    }

    // ---- deciding the orders (from GrandAdmiralTick) ----

    private void ChooseOrders(GAState state, GARecord.Decision decision)
    {
        List<Unit> warships = Warships();
        int escorts = warships.Count(IsEscort);
        bool capitals = warships.Exists(IsCapitalUnit);
        bool enemyCraft = enemies.Exists(c => c.health.GetComponent<Squadron>() != null);
        int lineShips = warships.Count(u => !IsCarrier(u) && !InAmbush(u) && !InProbe(u));
        bool fallingBack = gaStance == Stance.FallBack;
        bool shielded = warships.Exists(u => { ShieldOf(u, out bool s); return s; });

        decision.Formation = gaPolicy.ChooseOrder(state, GASpec.OutFormation, new[] { true, true, true });
        gaFormation = decision.Formation != null ? decision.Formation.Action : GASpec.FormationStandard;

        decision.Escort = gaPolicy.ChooseOrder(state, GASpec.OutEscort, new[] { true, escorts > 0 && capitals, escorts > 0 && enemyCraft });
        gaEscort = decision.Escort != null ? decision.Escort.Action : GASpec.EscortLine;

        decision.Maneuver = gaPolicy.ChooseOrder(state, GASpec.OutManeuver,
            new[] { true, !fallingBack && lineShips >= FlankMinWarships, !fallingBack && lineShips >= EnvelopMinWarships });
        int maneuver = decision.Maneuver != null ? decision.Maneuver.Action : GASpec.ManeuverLine;
        if (maneuver != gaManeuver) gaFlankers.Clear(); // A new manoeuvre picks its own detachment.
        gaManeuver = maneuver;

        decision.Tempo = gaPolicy.ChooseOrder(state, GASpec.OutTempo, new[] { true, shielded, shielded });
        gaTempo = decision.Tempo != null ? decision.Tempo.Action : GASpec.TempoSteady;
        if (gaTempo != GASpec.TempoRotateAction) gaRotated.Clear();

        string orders = $"{gaStance} / {Formations[gaFormation]} / {Escorts[gaEscort]} / {Maneuvers[gaManeuver]} / {Tempos[gaTempo]}";
        if (orders != gaLastOrders)
        {
            gaLastOrders = orders;
            Debug.Log($"[GA] {Faction?.name} t={Time.time - gaStartTime:F0}s orders: {orders}");
        }
    }

    private static readonly string[] Formations = { "standard", "tight", "loose" };
    private static readonly string[] Escorts = { "escorts in line", "escorts screen", "escorts hunt" };
    private static readonly string[] Maneuvers = { "one line", "flank", "envelop" };
    private static readonly string[] Tempos = { "steady", "rotate", "pulse" };
    private string gaLastOrders;

    // ---- carrying them out (hooks from Maneuver / FieldSpacing) ----

    private float GrandAdmiralSpacingScale()
    {
        if (!GrandAdmiralActive) return 1f;
        if (gaFormation == GASpec.FormationTightAction) return (float)gaPolicy.Model.Knob(GASpec.Knob.FormationTightScale);
        if (gaFormation == GASpec.FormationLooseAction) return (float)gaPolicy.Model.Knob(GASpec.Knob.FormationLooseScale);
        return 1f;
    }

    // Pulse: the whole line steps back out of reach so shields recharge, then comes back.
    private Vector3 GrandAdmiralFront(Vector3 front)
    {
        if (!GrandAdmiralActive || gaTempo != GASpec.TempoPulseAction || CurrentStance == Stance.FallBack) return front;
        return front - toEnemy * ourRange * (float)gaPolicy.Model.Knob(GASpec.Knob.PulseDepth);
    }

    // Takes ships out of the battle line for the orders that need them elsewhere, sends them there, and
    // returns what's left of the line for the usual placement.
    private List<Unit> GrandAdmiralDetach(List<Unit> line, Vector3 front, float spacing)
    {
        if (!GrandAdmiralActive || CurrentStance == Stance.FallBack) return line;
        List<Unit> remaining = new List<Unit>(line);

        // Tempo rotate: drained ships drop behind the line until their shields are back.
        if (gaTempo == GASpec.TempoRotateAction)
        {
            float outBelow = (float)gaPolicy.Model.Knob(GASpec.Knob.RotateOutBelow);
            float backAbove = (float)gaPolicy.Model.Knob(GASpec.Knob.RotateBackAbove);
            gaRotated.RemoveWhere(u => u.health == null || u.health.IsDead || ShieldOf(u, out _) >= backAbove);
            int limit = Mathf.FloorToInt(line.Count * MaxRotatedShare);
            foreach (Unit unit in line.OrderBy(u => ShieldOf(u, out _)))
            {
                if (gaRotated.Count >= limit) break;
                float fraction = ShieldOf(unit, out bool shielded);
                if (shielded && fraction < outBelow) gaRotated.Add(unit);
            }
            List<Unit> resting = remaining.FindAll(gaRotated.Contains);
            remaining.RemoveAll(gaRotated.Contains);
            if (resting.Count > 0) PlaceRow(resting, front - toEnemy * (ourRange * 0.6f + spacing), spacing, false, false);
        }

        // Escorts: guard the capitals, or go after the enemy's strike craft.
        if (gaEscort != GASpec.EscortLine)
        {
            List<Unit> escorts = remaining.FindAll(IsEscort);
            List<Unit> capitals = remaining.FindAll(IsCapitalUnit);
            List<Contact> craft = enemies.FindAll(c => c.health.GetComponent<Squadron>() != null);
            if (escorts.Count > 0 && (gaEscort == GASpec.EscortScreenAction ? capitals.Count > 0 : craft.Count > 0))
            {
                remaining.RemoveAll(IsEscort);
                float radius = spacing * (float)gaPolicy.Model.Knob(GASpec.Knob.ScreenRadius);
                for (int i = 0; i < escorts.Count; i++)
                {
                    Unit escort = escorts[i];
                    Vector3 destination;
                    if (gaEscort == GASpec.EscortScreenAction)
                    {
                        // Each capital gets escorts in turn, placed between it and the nearest enemy craft.
                        Unit capital = capitals[i % capitals.Count];
                        Vector3 at = capital.health.transform.position;
                        Contact threat = craft.Count > 0 ? craft.OrderBy(c => (c.health.transform.position - at).sqrMagnitude).First() : null;
                        Vector3 toward = threat != null ? threat.health.transform.position - at : toEnemy;
                        toward.y = 0f;
                        toward = toward.sqrMagnitude > 0.01f ? toward.normalized : toEnemy;
                        float side = (i / capitals.Count) % 2 == 0 ? 1f : -1f;
                        destination = at + toward * radius + Vector3.Cross(Vector3.up, toward) * side * spacing * 0.5f * (i / capitals.Count);
                    }
                    else
                    {
                        Vector3 from = escort.health.transform.position;
                        destination = craft.OrderBy(c => (c.health.transform.position - from).sqrMagnitude).First().health.transform.position;
                    }
                    MoveTo(escort, SnapToNavMesh(destination));
                }
            }
        }

        // Manoeuvre: a detachment of the fastest line ships swings round one wing (flank) or both (envelop).
        if (gaManeuver != GASpec.ManeuverLine && remaining.Count >= FlankMinWarships)
        {
            gaFlankers.RemoveWhere(u => u.health == null || u.health.IsDead || !remaining.Contains(u));
            int want = Mathf.Clamp(Mathf.RoundToInt(remaining.Count * (float)gaPolicy.Model.Knob(GASpec.Knob.FlankShare)),
                gaManeuver == GASpec.ManeuverEnvelopAction ? 2 : 1, remaining.Count - 2);
            foreach (Unit unit in remaining.OrderByDescending(u => SpeedOf(u.health)))
            {
                if (gaFlankers.Count >= want) break;
                if (!IsCapitalUnit(unit) || remaining.Count(IsCapitalUnit) > 1) gaFlankers.Add(unit);
            }
            List<Unit> wing = remaining.FindAll(gaFlankers.Contains);
            remaining.RemoveAll(gaFlankers.Contains);

            float width = ourRange * (float)gaPolicy.Model.Knob(GASpec.Knob.FlankWidth);
            Vector3 beside = enemyCenter - toEnemy * ourRange * 0.3f;
            if (gaManeuver == GASpec.ManeuverEnvelopAction)
            {
                List<Unit> left = wing.Where((_, i) => i % 2 == 0).ToList();
                List<Unit> right = wing.Where((_, i) => i % 2 == 1).ToList();
                if (left.Count > 0) PlaceRow(left, beside + lateral * width, spacing, false);
                if (right.Count > 0) PlaceRow(right, beside - lateral * width, spacing, false);
            }
            else if (wing.Count > 0)
            {
                // The wing goes round whichever side has fewer enemy ships on it.
                float leftSide = enemies.Count(c => Vector3.Dot(c.health.transform.position - enemyCenter, lateral) > 0f);
                float sign = leftSide <= enemies.Count - leftSide ? 1f : -1f;
                PlaceRow(wing, beside + lateral * sign * width, spacing, false);
            }
        }

        return remaining;
    }
}
