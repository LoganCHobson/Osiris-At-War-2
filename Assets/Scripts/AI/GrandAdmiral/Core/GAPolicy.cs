using System;

namespace Osiris.GrandAdmiral
{
    public sealed class GAChoice
    {
        public int Action;
        public double Probability;  // of the action taken, under the distribution it was drawn from
        public bool[] Mask;         // allowed actions (stance / commit)
        public double[] Distribution;
        public double[][] Contacts; // target choices only (rounded, as uploaded)
    }

    // One evaluation of the network for one battle state.
    public sealed class GAState
    {
        public double[] Features;   // rounded, as uploaded
        public double[] Output;
        public double[] Hidden;
        public double Value => Output[GASpec.OutValue];
    }

    // Turns network outputs into decisions, exactly as osiris_ml/policy.py describes them: logits /
    // temperature -> softmax over allowed actions -> mixed with epsilon uniform exploration. The
    // probability of the action taken is logged so the trainer can learn off-policy (and spot fakes).
    public sealed class GAPolicy
    {
        private readonly GAModel model;
        private readonly GAAdaptation adaptation;
        private readonly Random random;

        public double Temperature { get; }
        public double Exploration { get; }
        public GAModel Model => model;

        public GAPolicy(GAModel model, GAAdaptation adaptation, int seed, double? explorationOverride = null)
        {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.adaptation = adaptation;
            random = new Random(seed);
            Temperature = model.Knob(GASpec.Knob.Temperature);
            Exploration = explorationOverride.HasValue
                ? GASpec.ClampKnob(GASpec.Knob.Exploration, explorationOverride.Value)
                : model.Knob(GASpec.Knob.Exploration);
        }

        public GAState Evaluate(double[] features)
        {
            double[] x = new double[GASpec.NFeatures];
            for (int i = 0; i < x.Length; i++) x[i] = GASpec.Round(GASpec.Clamp(features[i], -10, 10));
            double[] hidden = new double[model.Hidden];
            return new GAState { Features = x, Hidden = hidden, Output = model.Forward(x, hidden, adaptation) };
        }

        public static double[] MaskedSoftmax(double[] logits, int offset, int count, bool[] mask, double temperature, double epsilon)
        {
            double max = double.NegativeInfinity;
            int allowed = 0;
            for (int i = 0; i < count; i++)
            {
                if (mask != null && !mask[i]) continue;
                allowed++;
                max = Math.Max(max, logits[offset + i] / temperature);
            }
            double[] p = new double[count];
            if (allowed == 0) return p;
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                if (mask != null && !mask[i]) continue;
                p[i] = Math.Exp(logits[offset + i] / temperature - max);
                sum += p[i];
            }
            for (int i = 0; i < count; i++)
            {
                if (mask != null && !mask[i]) continue;
                p[i] = (1 - epsilon) * p[i] / sum + epsilon / allowed;
            }
            return p;
        }

        public static double RetreatProbability(double logit, double temperature, double epsilon)
        {
            double p = 1.0 / (1.0 + Math.Exp(-logit / temperature));
            return (1 - epsilon) * p + epsilon * 0.5;
        }

        private int Sample(double[] distribution)
        {
            double roll = random.NextDouble();
            double running = 0;
            int last = -1;
            for (int i = 0; i < distribution.Length; i++)
            {
                if (distribution[i] <= 0) continue;
                last = i;
                running += distribution[i];
                if (roll < running) return i;
            }
            return last;
        }

        private GAChoice Categorical(GAState state, int offset, bool[] mask)
        {
            double[] p = MaskedSoftmax(state.Output, offset, 3, mask, Temperature, Exploration);
            int a = Sample(p);
            return new GAChoice { Action = a, Probability = p[a], Mask = (bool[])mask.Clone(), Distribution = p };
        }

        public GAChoice ChooseStance(GAState state, bool canFallBack)
        {
            return Categorical(state, GASpec.OutStance, new[] { true, true, canFallBack });
        }

        public GAChoice ChooseCommit(GAState state)
        {
            return Categorical(state, GASpec.OutCommit, new[] { true, true, true });
        }

        public GAChoice ChooseRetreat(GAState state)
        {
            double p1 = RetreatProbability(state.Output[GASpec.OutRetreat], Temperature, Exploration);
            int a = random.NextDouble() < p1 ? 1 : 0;
            return new GAChoice { Action = a, Probability = a == 1 ? p1 : 1 - p1, Distribution = new[] { 1 - p1, p1 } };
        }

        // contacts: up to MaxContacts rows of NContact features, best heuristic priority first.
        public GAChoice ChooseTarget(GAState state, double[][] contacts)
        {
            int n = Math.Min(contacts.Length, GASpec.MaxContacts);
            double[][] rounded = new double[n][];
            double[] scores = new double[n];
            for (int i = 0; i < n; i++)
            {
                rounded[i] = new double[GASpec.NContact];
                double score = 0;
                for (int k = 0; k < GASpec.NContact; k++)
                {
                    rounded[i][k] = GASpec.Round(GASpec.Clamp(contacts[i][k], -10, 10));
                    score += rounded[i][k] * state.Output[GASpec.OutTarget + k];
                }
                scores[i] = score;
            }
            double[] p = MaskedSoftmax(scores, 0, n, null, Temperature, Exploration);
            int a = Sample(p);
            return new GAChoice { Action = a, Probability = p[a], Distribution = p, Contacts = rounded };
        }
    }
}
