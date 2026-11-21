using System;
using System.Collections.Generic;

namespace Osiris.GrandAdmiral
{
    // A test commander for "do the AI's decisions change who wins?" (the farm's -probe flag, see
    // DifficultySim). The released model keeps making every call except the ones a probe overrides:
    // those are made uniformly at random among the allowed options, or fixed to one option.
    // While a probe is active nothing is uploaded, so the battles never reach training or the arena.
    public sealed class GAProbe
    {
        public static GAProbe Active;

        public string Name { get; private set; }
        private readonly HashSet<string> randomHeads = new HashSet<string>();
        private readonly Dictionary<string, int> fixedHeads = new Dictionary<string, int>();
        private readonly Random random = new Random(Environment.TickCount);

        public static readonly string[] Heads = { "stance", "commit", "target", "formation", "escort", "maneuver", "tempo" };

        // model | random | random-<head> | press | fallback | never-retreat. Null for anything else.
        public static GAProbe Parse(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return null;
            GAProbe probe = new GAProbe { Name = mode };
            if (mode == "model") return probe;
            if (mode == "random")
            {
                foreach (string head in Heads) probe.randomHeads.Add(head);
                return probe;
            }
            if (mode.StartsWith("random-") && Array.IndexOf(Heads, mode.Substring(7)) >= 0)
            {
                probe.randomHeads.Add(mode.Substring(7));
                return probe;
            }
            switch (mode)
            {
                case "press": probe.fixedHeads["stance"] = 1; return probe;
                case "fallback": probe.fixedHeads["stance"] = 2; return probe;
                case "never-retreat": probe.fixedHeads["retreat"] = 0; return probe;
            }
            return null;
        }

        public static string HeadAt(int offset) => offset switch
        {
            GASpec.OutStance => "stance",
            GASpec.OutCommit => "commit",
            GASpec.OutFormation => "formation",
            GASpec.OutEscort => "escort",
            GASpec.OutManeuver => "maneuver",
            GASpec.OutTempo => "tempo",
            _ => null
        };

        // The action this probe takes instead of `chosen`, given what's allowed (null mask: all of count).
        public int Override(string head, int chosen, bool[] mask, int count)
        {
            if (head == null) return chosen;
            if (fixedHeads.TryGetValue(head, out int forced))
            {
                return forced < count && (mask == null || mask[forced]) ? forced : chosen;
            }
            if (!randomHeads.Contains(head)) return chosen;
            List<int> allowed = new List<int>();
            for (int i = 0; i < count; i++) if (mask == null || mask[i]) allowed.Add(i);
            return allowed.Count == 0 ? chosen : allowed[random.Next(allowed.Count)];
        }
    }
}
