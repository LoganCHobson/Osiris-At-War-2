using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Osiris.GrandAdmiral
{
    // Everything one Grand Admiral battle teaches: the AI's decisions and how the fight ended.
    // No player names, saves or machine details. Serialises to the schema in osiris_ml/records.py.
    public sealed class GARecord
    {
        public const int MaxDecisions = 160;

        public sealed class Decision
        {
            public double Time;
            public GAState State;
            public GAChoice Stance, Retreat, Commit, Target;
            public GAChoice Formation, Escort, Maneuver, Tempo; // schema 2
        }

        public string BattleId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        public int ModelVersion;
        public string ClientVersion = "0";
        public bool Adapted;
        public bool VsPlayer;
        public bool Attacker;
        public double Temperature, Exploration;
        public double Duration, StartPower, EndPower, EnemyStartPower, EnemyEndPower;
        public bool Won, Retreated, EnemyRetreated;
        public readonly List<Decision> Decisions = new List<Decision>();

        public bool Full => Decisions.Count >= MaxDecisions;

        public void Add(Decision decision)
        {
            if (Full) return;
            if (decision.Stance == null && decision.Retreat == null && decision.Commit == null && decision.Target == null
                && decision.Formation == null && decision.Escort == null && decision.Maneuver == null && decision.Tempo == null) return;
            Decisions.Add(decision);
        }

        public bool Uploadable =>
            Decisions.Count > 0 && Duration > 0 && StartPower > 0 && EnemyStartPower > 0 && !(Won && Retreated);

        // Battle return, as the trainer computes it (dataset.battle_return). Used for local adaptation.
        public double Return()
        {
            double ours = StartPower > 0 ? EndPower / StartPower : 0;
            double theirs = EnemyStartPower > 0 ? EnemyEndPower / EnemyStartPower : 0;
            return GASpec.Clamp(ours - theirs + (Won ? 0.5 : -0.5), -1.5, 1.5);
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder(16 * 1024);
            double end = Math.Max(Duration, 0.1);
            double start = Math.Max(StartPower, 1e-3), enemyStart = Math.Max(EnemyStartPower, 1e-3);
            sb.Append('{');
            Field(sb, "schema", GASpec.RecordSchema); sb.Append(',');
            Field(sb, "feature_schema", GASpec.FeatureSchema); sb.Append(',');
            Field(sb, "battle_id", BattleId); sb.Append(',');
            Field(sb, "model_version", ModelVersion); sb.Append(',');
            Field(sb, "client_version", SafeVersion(ClientVersion)); sb.Append(',');
            Field(sb, "adapted", Adapted); sb.Append(',');
            Field(sb, "opponent", VsPlayer ? "player" : "ai"); sb.Append(',');
            Field(sb, "side", Attacker ? "attacker" : "defender"); sb.Append(',');
            Field(sb, "duration", Math.Min(end, 7200)); sb.Append(',');
            Field(sb, "start_power", start); sb.Append(',');
            Field(sb, "end_power", GASpec.Clamp(EndPower, 0, start)); sb.Append(',');
            Field(sb, "enemy_start_power", enemyStart); sb.Append(',');
            Field(sb, "enemy_end_power", GASpec.Clamp(EnemyEndPower, 0, enemyStart)); sb.Append(',');
            Field(sb, "won", Won && !Retreated); sb.Append(',');
            Field(sb, "retreated", Retreated); sb.Append(',');
            Field(sb, "enemy_retreated", EnemyRetreated); sb.Append(',');
            Field(sb, "temperature", Temperature); sb.Append(',');
            Field(sb, "exploration", Exploration); sb.Append(',');
            sb.Append("\"decisions\":[");
            double lastTime = 0;
            for (int i = 0; i < Decisions.Count; i++)
            {
                Decision d = Decisions[i];
                if (i > 0) sb.Append(',');
                lastTime = Math.Max(lastTime, Math.Min(GASpec.Round(d.Time), end));
                sb.Append('{');
                Field(sb, "t", lastTime);
                sb.Append(",\"x\":");
                Array(sb, d.State.Features);
                if (d.Stance != null) { sb.Append(",\"s\":"); Choice(sb, d.Stance); }
                if (d.Retreat != null) { sb.Append(",\"r\":{"); Field(sb, "a", d.Retreat.Action); sb.Append(','); Field(sb, "p", d.Retreat.Probability); sb.Append('}'); }
                if (d.Commit != null) { sb.Append(",\"c\":"); Choice(sb, d.Commit); }
                if (d.Formation != null) { sb.Append(",\"f\":"); Choice(sb, d.Formation); }
                if (d.Escort != null) { sb.Append(",\"e\":"); Choice(sb, d.Escort); }
                if (d.Maneuver != null) { sb.Append(",\"n\":"); Choice(sb, d.Maneuver); }
                if (d.Tempo != null) { sb.Append(",\"w\":"); Choice(sb, d.Tempo); }
                if (d.Target != null)
                {
                    sb.Append(",\"g\":{\"c\":[");
                    for (int c = 0; c < d.Target.Contacts.Length; c++)
                    {
                        if (c > 0) sb.Append(',');
                        Array(sb, d.Target.Contacts[c]);
                    }
                    sb.Append("],");
                    Field(sb, "a", d.Target.Action); sb.Append(',');
                    Field(sb, "p", d.Target.Probability);
                    sb.Append('}');
                }
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void Choice(StringBuilder sb, GAChoice choice)
        {
            sb.Append('{');
            Field(sb, "a", choice.Action); sb.Append(',');
            Field(sb, "p", choice.Probability);
            sb.Append(",\"m\":[");
            for (int i = 0; i < choice.Mask.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(choice.Mask[i] ? "true" : "false");
            }
            sb.Append("]}");
        }

        private static void Array(StringBuilder sb, double[] values)
        {
            sb.Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                Number(sb, values[i]);
            }
            sb.Append(']');
        }

        private static void Field(StringBuilder sb, string name, double value) { sb.Append('"').Append(name).Append("\":"); Number(sb, value); }
        private static void Field(StringBuilder sb, string name, int value) { sb.Append('"').Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture)); }
        private static void Field(StringBuilder sb, string name, bool value) { sb.Append('"').Append(name).Append("\":").Append(value ? "true" : "false"); }
        private static void Field(StringBuilder sb, string name, string value) { sb.Append('"').Append(name).Append("\":\"").Append(value).Append('"'); }

        // JSON has no NaN/Infinity, and the API rejects them; never emit one.
        private static void Number(StringBuilder sb, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
            string text = value.ToString("R", CultureInfo.InvariantCulture);
            if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0) text += ".0";
            sb.Append(text);
        }

        // Matches the API's ^[0-9A-Za-z.\-+]{1,32}$; anything else is dropped rather than escaped.
        private static string SafeVersion(string version)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in version ?? "")
            {
                if (sb.Length >= 32) break;
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '.' || c == '-' || c == '+') sb.Append(c);
            }
            return sb.Length > 0 ? sb.ToString() : "0";
        }
    }
}
