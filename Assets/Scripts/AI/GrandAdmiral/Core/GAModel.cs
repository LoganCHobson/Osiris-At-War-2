using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Osiris.GrandAdmiral
{
    public class GAFormatException : Exception
    {
        public GAFormatException(string message) : base(message) { }
    }

    // The Grand Admiral network: inputs -> tanh hidden layer -> policy heads + value. Pure data: a
    // model file is weights and numbers, never code. Format documented in osiris_ml/model.py.
    public sealed class GAModel
    {
        public const int MaxHidden = 256;
        private const int HeaderSize = 30;

        public int Version { get; private set; }
        public long CreatedUnix { get; private set; }
        public int Hidden { get; private set; }

        private double[] inMean, inStd, w1, b1, w2, b2;
        private readonly Dictionary<ushort, double> knobs = new Dictionary<ushort, double>();

        public static GAModel FromBytes(byte[] data)
        {
            if (data == null || data.Length < HeaderSize) throw new GAFormatException("truncated header");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(data, false), Encoding.ASCII))
            {
                if (reader.ReadByte() != 'O' || reader.ReadByte() != 'G' || reader.ReadByte() != 'A' || reader.ReadByte() != 'M')
                    throw new GAFormatException("bad magic");
                int format = reader.ReadUInt16();
                int schema = reader.ReadUInt16();
                if (format != GASpec.ModelFormat || schema != GASpec.FeatureSchema)
                    throw new GAFormatException($"unsupported format {format}/schema {schema}");

                GAModel model = new GAModel
                {
                    Version = (int)Math.Min(reader.ReadUInt32(), int.MaxValue),
                    CreatedUnix = reader.ReadInt64()
                };
                int nIn = reader.ReadUInt16();
                int nHidden = reader.ReadUInt16();
                int nOut = reader.ReadUInt16();
                int nContact = reader.ReadUInt16();
                int nKnobs = reader.ReadUInt16();
                if (nIn != GASpec.NFeatures || nOut != GASpec.NOut || nContact != GASpec.NContact)
                    throw new GAFormatException("shape does not match the feature schema");
                if (nHidden < 1 || nHidden > MaxHidden || nKnobs > 64) throw new GAFormatException("implausible dimensions");

                long floats = 2L * nIn + (long)nHidden * nIn + nHidden + (long)nOut * nHidden + nOut;
                long expected = HeaderSize + floats * 4 + nKnobs * 6L;
                if (data.Length != expected) throw new GAFormatException($"length {data.Length} != {expected}");

                model.Hidden = nHidden;
                model.inMean = ReadFloats(reader, nIn);
                model.inStd = ReadFloats(reader, nIn);
                model.w1 = ReadFloats(reader, nHidden * nIn);
                model.b1 = ReadFloats(reader, nHidden);
                model.w2 = ReadFloats(reader, nOut * nHidden);
                model.b2 = ReadFloats(reader, nOut);
                foreach (double s in model.inStd)
                {
                    if (!(s > 0)) throw new GAFormatException("non-positive input scale");
                }

                for (int i = 0; i < nKnobs; i++)
                {
                    ushort id = reader.ReadUInt16();
                    float value = reader.ReadSingle();
                    if (GASpec.IsKnown(id)) model.knobs[id] = GASpec.ClampKnob((GASpec.Knob)id, value);
                }
                return model;
            }
        }

        private static double[] ReadFloats(BinaryReader reader, int count)
        {
            double[] values = new double[count];
            for (int i = 0; i < count; i++)
            {
                float f = reader.ReadSingle();
                if (float.IsNaN(f) || float.IsInfinity(f)) throw new GAFormatException("non-finite weights");
                values[i] = f;
            }
            return values;
        }

        public double Knob(GASpec.Knob knob)
        {
            return knobs.TryGetValue((ushort)knob, out double value) ? value : GASpec.KnobDefault(knob);
        }

        // Forward pass. `hidden` (length Hidden) receives the hidden activations, for local adaptation.
        public double[] Forward(double[] x, double[] hidden = null, GAAdaptation adaptation = null)
        {
            if (x == null || x.Length != GASpec.NFeatures) throw new ArgumentException("feature vector has the wrong length");
            int nIn = GASpec.NFeatures;
            double[] z = new double[nIn];
            for (int i = 0; i < nIn; i++) z[i] = (x[i] - inMean[i]) / inStd[i];

            double[] h = hidden ?? new double[Hidden];
            for (int j = 0; j < Hidden; j++)
            {
                double sum = b1[j];
                int row = j * nIn;
                for (int i = 0; i < nIn; i++) sum += w1[row + i] * z[i];
                h[j] = Math.Tanh(sum);
            }

            double[] output = new double[GASpec.NOut];
            for (int k = 0; k < GASpec.NOut; k++)
            {
                double sum = b2[k];
                int row = k * Hidden;
                for (int j = 0; j < Hidden; j++) sum += w2[row + j] * h[j];
                output[k] = sum;
            }
            adaptation?.Apply(h, output);
            return output;
        }
    }
}
