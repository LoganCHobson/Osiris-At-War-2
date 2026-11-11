using System;

namespace Osiris.GrandAdmiral
{
    // The Grand Admiral contract. Twin of GrandAdmiral/ml/osiris_ml/spec.py: change one, change
    // both, and bump FeatureSchema. A build only accepts models for the schema it was compiled with,
    // so an old build keeps its last good model rather than misreading a new one.
    public static class GASpec
    {
        public const int FeatureSchema = 2;
        public const int ModelFormat = 1;
        public const int BundleFormat = 1;
        public const int RecordSchema = 2;

        // Battle-state features (see spec.py for what each means and how it's scaled).
        public const int LogTotalRatio = 0, LogFieldRatio = 1, RatioTrend = 2, OurHealth = 3, EnemyHealth = 4,
            OurReserveShare = 5, EnemyUnseenShare = 6, OurLosses = 7, EnemyLosses = 8, LogDpsRatio = 9,
            LogRangeRatio = 10, CenterDistance = 11, OurStrikeShare = 12, EnemyStrikeShare = 13,
            EnemyCapitalShare = 14, PopHeadroom = 15, Time = 16, IsAttacker = 17, FriendlyDefenses = 18,
            EnemyDefenses = 19, EnemyRetreating = 20, StancePress = 21, StanceFallback = 22, VsPlayer = 23;
        // Schema 2: shields, missiles, craft, gunnery, terrain, tempo, flagship, current orders.
        public const int OurShield = 24, ShieldTrend = 25, LowShieldShare = 26, MissilePressure = 27,
            PdIdleShare = 28, EnemyCraftRatio = 29, BombersOnCapitals = 30, BowAlignment = 31,
            LosBlockedShare = 32, CoverBetween = 33, CoverNear = 34, EnemyWaves = 35, SinceEnemyWave = 36,
            FlagshipHealth = 37, FlagshipUnderFire = 38, Kited = 39, ClosingRate = 40, OurSpread = 41,
            EnemySpread = 42, LogSpeedRatio = 43, DamageExchange = 44, FormationTight = 45, FormationLoose = 46,
            EscortScreen = 47, EscortHunt = 48, ManeuverFlank = 49, ManeuverEnvelop = 50, TempoRotate = 51,
            TempoPulse = 52;
        public const int NFeatures = 53;

        // Per-contact features for choosing the focus target.
        public const int CHeuristicPriority = 0, CLogDps = 1, CLogPower = 2, CHealth = 3, CDistance = 4,
            CIsDefense = 5, CIsCapital = 6, CIsStrike = 7, CIsCarrier = 8;
        public const int NContact = 9;
        public const int MaxContacts = 10;

        // Network outputs.
        public const int OutStance = 0;   // 3: engage, press, fall back
        public const int OutRetreat = 3;  // 1
        public const int OutCommit = 4;   // 3: hold, match, surge
        public const int OutTarget = 7;   // NContact weights
        public const int OutFormation = 16; // 3: standard, tight, loose
        public const int OutEscort = 19;    // 3: line, screen, hunt
        public const int OutManeuver = 22;  // 3: line, flank, envelop
        public const int OutTempo = 25;     // 3: steady, rotate, pulse
        public const int OutValue = 28;
        public const int NOut = OutValue + 1;

        public const int StanceEngage = 0, StancePressAction = 1, StanceFallBack = 2;
        public const int CommitHold = 0, CommitMatch = 1, CommitSurge = 2;
        public const int FormationStandard = 0, FormationTightAction = 1, FormationLooseAction = 2;
        public const int EscortLine = 0, EscortScreenAction = 1, EscortHuntAction = 2;
        public const int ManeuverLine = 0, ManeuverFlankAction = 1, ManeuverEnvelopAction = 2;
        public const int TempoSteady = 0, TempoRotateAction = 1, TempoPulseAction = 2;

        // Safety rails applied before the network gets a say.
        public const double RetreatNeverAbove = 1.2;
        public const double RetreatForcedBelow = 0.12;

        public static readonly byte[] SignatureContext = { (byte)'O', (byte)'G', (byte)'A', (byte)'B', (byte)'-', (byte)'S',
            (byte)'I', (byte)'G', (byte)'-', (byte)'v', (byte)'1', 0 };

        // Knobs the server can retune without a game update. Every value is clamped here, so even a
        // validly signed model can't push the AI outside these bounds.
        public enum Knob : ushort
        {
            DecisionInterval = 1,
            ReinforcementDelayScale = 2,
            ThinkIntervalScale = 3,
            AttackMarginScale = 4,
            RetreatJudgement = 5,
            TickSeconds = 6,
            Exploration = 7,
            Temperature = 8,
            AdaptLearningRate = 9,
            FormationTightScale = 10,
            FormationLooseScale = 11,
            FlankShare = 12,
            FlankWidth = 13,
            RotateOutBelow = 14,
            RotateBackAbove = 15,
            PulseDepth = 16,
            ScreenRadius = 17
        }

        public static bool IsKnown(ushort id) => id >= 1 && id <= 17;

        public static double KnobDefault(Knob knob)
        {
            switch (knob)
            {
                case Knob.DecisionInterval: return 0.5;
                case Knob.ReinforcementDelayScale: return 0.7;
                case Knob.ThinkIntervalScale: return 0.75;
                case Knob.AttackMarginScale: return 1.1;
                case Knob.RetreatJudgement: return 1.0;
                case Knob.TickSeconds: return 3.0;
                case Knob.Exploration: return 0.05;
                case Knob.Temperature: return 1.0;
                case Knob.AdaptLearningRate: return 0.01;
                case Knob.FormationTightScale: return 0.6;
                case Knob.FormationLooseScale: return 1.8;
                case Knob.FlankShare: return 0.3;
                case Knob.FlankWidth: return 1.0;
                case Knob.RotateOutBelow: return 0.25;
                case Knob.RotateBackAbove: return 0.7;
                case Knob.PulseDepth: return 0.4;
                case Knob.ScreenRadius: return 1.5;
                default: throw new ArgumentOutOfRangeException(nameof(knob));
            }
        }

        public static double ClampKnob(Knob knob, double value)
        {
            double lo, hi;
            switch (knob)
            {
                case Knob.DecisionInterval: lo = 0.3; hi = 2.0; break;
                case Knob.ReinforcementDelayScale: lo = 0.3; hi = 2.0; break;
                case Knob.ThinkIntervalScale: lo = 0.5; hi = 2.0; break;
                case Knob.AttackMarginScale: lo = 0.8; hi = 1.6; break;
                case Knob.RetreatJudgement: lo = 0.5; hi = 1.5; break;
                case Knob.TickSeconds: lo = 1.0; hi = 10.0; break;
                case Knob.Exploration: lo = 0.0; hi = 0.2; break;
                case Knob.Temperature: lo = 0.25; hi = 2.0; break;
                case Knob.AdaptLearningRate: lo = 0.0; hi = 0.05; break;
                case Knob.FormationTightScale: lo = 0.4; hi = 0.9; break;
                case Knob.FormationLooseScale: lo = 1.2; hi = 2.5; break;
                case Knob.FlankShare: lo = 0.15; hi = 0.5; break;
                case Knob.FlankWidth: lo = 0.5; hi = 2.0; break;
                case Knob.RotateOutBelow: lo = 0.1; hi = 0.5; break;
                case Knob.RotateBackAbove: lo = 0.4; hi = 0.95; break;
                case Knob.PulseDepth: lo = 0.2; hi = 0.8; break;
                case Knob.ScreenRadius: lo = 0.8; hi = 3.0; break;
                default: throw new ArgumentOutOfRangeException(nameof(knob));
            }
            if (double.IsNaN(value)) return KnobDefault(knob);
            return Math.Min(hi, Math.Max(lo, value));
        }

        // Features are rounded before the network sees them, exactly as they're uploaded, so the
        // trainer recomputes the same probabilities from the record.
        public static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

        public static double Clamp(double value, double lo, double hi)
        {
            if (double.IsNaN(value)) return 0.0;
            return value < lo ? lo : value > hi ? hi : value;
        }
    }
}
