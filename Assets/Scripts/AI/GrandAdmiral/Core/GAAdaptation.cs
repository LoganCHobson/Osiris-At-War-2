using System;
using System.Collections.Generic;
using System.IO;

namespace Osiris.GrandAdmiral
{
    // Learning from the battles it fights, on the player's own machine, right away.
    //
    // After every Grand Admiral battle the AI nudges its stance, retreat and reinforcement heads
    // toward choices that did better than its critic expected against *this* player, and away from
    // ones that did worse (on-policy REINFORCE with the value head as baseline). It's a small learned
    // offset on top of the global model, never a replacement:
    //   * each head's offset is capped so it can shift a logit by at most 0.6 x temperature, so a
    //     probability moves at most about 1.8x from the global model (and uploads stay believable
    //     to the trainer's deep check);
    //   * it decays a little every battle, so old lessons fade;
    //   * it resets whenever a new global model arrives (the base it was learned against changed).
    public sealed class GAAdaptation
    {
        private const int Rows = 7; // stance (3), retreat (1), commit (3)
        private const double Decay = 0.98;
        private const double MaxLogitShift = 0.6;

        public int ModelVersion { get; private set; }
        public int Hidden { get; }
        public int BattlesLearned { get; private set; }
        private readonly double[][] delta;
        private readonly double temperature;

        public GAAdaptation(GAModel model)
        {
            ModelVersion = model.Version;
            Hidden = model.Hidden;
            temperature = model.Knob(GASpec.Knob.Temperature);
            delta = new double[Rows][];
            for (int r = 0; r < Rows; r++) delta[r] = new double[Hidden + 1];
        }

        public bool IsActive
        {
            get
            {
                foreach (double[] row in delta)
                    foreach (double v in row)
                        if (v != 0) return true;
                return false;
            }
        }

        public double MaxRowNorm => MaxLogitShift * temperature / Math.Sqrt(Hidden + 1);

        public void Apply(double[] hidden, double[] output)
        {
            for (int r = 0; r < Rows; r++)
            {
                double[] row = delta[r];
                double sum = row[Hidden];
                for (int j = 0; j < Hidden; j++) sum += row[j] * hidden[j];
                output[GASpec.OutStance + r] += sum;
            }
        }

        public sealed class Step
        {
            public GAState State;
            public GAChoice Stance, Retreat, Commit;
        }

        // reward: the battle return (see osiris_ml/dataset.py battle_return).
        public void Learn(IReadOnlyList<Step> steps, double reward, double learningRate, double epsilon)
        {
            for (int r = 0; r < Rows; r++)
                for (int j = 0; j <= Hidden; j++)
                    delta[r][j] *= Decay;

            if (steps == null || steps.Count == 0 || learningRate <= 0) return;
            double scale = learningRate / steps.Count;
            double[] grad = new double[Rows];

            foreach (Step step in steps)
            {
                double advantage = GASpec.Clamp(reward - step.State.Value, -2, 2);
                Array.Clear(grad, 0, Rows);
                if (step.Stance != null) Categorical(step.Stance, step.State.Output, GASpec.OutStance, grad, 0, epsilon);
                if (step.Commit != null) Categorical(step.Commit, step.State.Output, GASpec.OutCommit, grad, 4, epsilon);
                if (step.Retreat != null)
                {
                    double p1 = 1.0 / (1.0 + Math.Exp(-step.State.Output[GASpec.OutRetreat] / temperature));
                    double mixed = step.Retreat.Probability;
                    grad[3] = (1 - epsilon) * p1 * (1 - p1) / temperature / mixed * (step.Retreat.Action == 1 ? 1 : -1);
                }
                for (int r = 0; r < Rows; r++)
                {
                    if (grad[r] == 0) continue;
                    double g = scale * advantage * grad[r];
                    for (int j = 0; j < Hidden; j++) delta[r][j] += g * step.State.Hidden[j];
                    delta[r][Hidden] += g;
                }
            }

            double cap = MaxRowNorm;
            foreach (double[] row in delta)
            {
                double norm = 0;
                foreach (double v in row) norm += v * v;
                norm = Math.Sqrt(norm);
                if (norm > cap)
                    for (int j = 0; j < row.Length; j++) row[j] *= cap / norm;
            }
            BattlesLearned++;
        }

        // d log(mixed p_a) / d logit for a masked, tempered, epsilon-mixed softmax.
        private void Categorical(GAChoice choice, double[] output, int offset, double[] grad, int row, double epsilon)
        {
            double[] raw = GAPolicy.MaskedSoftmax(output, offset, 3, choice.Mask, temperature, 0);
            double pa = raw[choice.Action];
            for (int i = 0; i < 3; i++)
            {
                if (!choice.Mask[i]) continue;
                double indicator = i == choice.Action ? 1 : 0;
                grad[row + i] = (1 - epsilon) * pa * (indicator - raw[i]) / temperature / choice.Probability;
            }
        }

        public double[] RowNorms()
        {
            double[] norms = new double[Rows];
            for (int r = 0; r < Rows; r++)
            {
                double sum = 0;
                foreach (double v in delta[r]) sum += v * v;
                norms[r] = Math.Sqrt(sum);
            }
            return norms;
        }

        // ---- persistence ----

        public byte[] ToBytes()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { (byte)'O', (byte)'G', (byte)'A', (byte)'A' });
                writer.Write(ModelVersion);
                writer.Write((ushort)Hidden);
                writer.Write(BattlesLearned);
                foreach (double[] row in delta)
                    foreach (double v in row)
                        writer.Write((float)v);
                writer.Flush();
                return stream.ToArray();
            }
        }

        // Restores saved adaptation if it belongs to this model; otherwise starts fresh.
        public static GAAdaptation Restore(GAModel model, byte[] data)
        {
            GAAdaptation fresh = new GAAdaptation(model);
            if (data == null) return fresh;
            try
            {
                using (BinaryReader reader = new BinaryReader(new MemoryStream(data, false)))
                {
                    if (reader.ReadByte() != 'O' || reader.ReadByte() != 'G' || reader.ReadByte() != 'A' || reader.ReadByte() != 'A') return fresh;
                    if (reader.ReadInt32() != model.Version || reader.ReadUInt16() != model.Hidden) return fresh;
                    int learned = reader.ReadInt32();
                    if (data.Length != 14 + Rows * (model.Hidden + 1) * 4) return fresh;
                    for (int r = 0; r < Rows; r++)
                        for (int j = 0; j <= model.Hidden; j++)
                        {
                            float v = reader.ReadSingle();
                            fresh.delta[r][j] = float.IsNaN(v) || float.IsInfinity(v) ? 0 : v;
                        }
                    fresh.BattlesLearned = learned;
                    fresh.EnforceCap();
                    return fresh;
                }
            }
            catch (EndOfStreamException)
            {
                return new GAAdaptation(model);
            }
        }

        private void EnforceCap()
        {
            double cap = MaxRowNorm;
            foreach (double[] row in delta)
            {
                double norm = 0;
                foreach (double v in row) norm += v * v;
                norm = Math.Sqrt(norm);
                if (norm > cap)
                    for (int j = 0; j < row.Length; j++) row[j] *= cap / norm;
            }
        }
    }
}
