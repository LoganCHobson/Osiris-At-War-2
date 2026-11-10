using System;

namespace Osiris.GrandAdmiral
{
    // The Grand Admiral contract. Twin of grand-admiral/ml/osiris_ml/spec.py: change one, change
    // both, and bump FeatureSchema. A build only accepts models for the schema it was compiled with,
    // so an old build keeps its last good model rather than misreading a new one.
    public static class GASpec
    {
        public const int FeatureSchema = 1;
        public const int ModelFormat = 1;
        public const int BundleFormat = 1;
        public const int RecordSchema = 1;

        // Battle-state features (see spec.py for what each means and how it's scaled).
        public const int LogTotalRatio = 0, LogFieldRatio = 1, RatioTrend = 2, OurHealth = 3, EnemyHealth = 4,
            OurReserveShare = 5, EnemyUnseenShare = 6, OurLosses = 7, EnemyLosses = 8, LogDpsRatio = 9,
            LogRangeRatio = 10, CenterDistance = 11, OurStrikeShare = 12, EnemyStrikeShare = 13,
            EnemyCapitalShare = 14, PopHeadroom = 15, Time = 16, IsAttacker = 17, FriendlyDefenses = 18,
            EnemyDefenses = 19, EnemyRetreating = 20, StancePress = 21, StanceFallback = 22, VsPlayer = 23;
        public const int NFeatures = 24;

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
        public const int OutValue = OutTarget + NContact;
        public const int NOut = OutValue + 1;

        public const int StanceEngage = 0, StancePressAction = 1, StanceFallBack = 2;
        public const int CommitHold = 0, CommitMatch = 1, CommitSurge = 2;

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
            AdaptLearningRate = 9
        }

        public static bool IsKnown(ushort id) => id >= 1 && id <= 9;

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
