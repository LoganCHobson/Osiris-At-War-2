using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Osiris.GrandAdmiral;
using Xunit;

public class Fixtures
{
    public static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "vectors");
    public static readonly JsonElement Vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "vectors.json"))).RootElement;
    public static byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(Dir, name));
    public static GAModel Model() => GAModel.FromBytes(Bytes("model.bin"));

    public static Dictionary<ushort, RSAParameters> Keys() => new Dictionary<ushort, RSAParameters>
    {
        { (ushort)Vectors.GetProperty("key_id").GetInt32(),
          GABundle.PublicKey(Vectors.GetProperty("modulus").GetString(), Vectors.GetProperty("exponent").GetString()) }
    };

    public static double[] Doubles(JsonElement e) => e.EnumerateArray().Select(v => v.GetDouble()).ToArray();
    public static IEnumerable<JsonElement> Cases => Vectors.GetProperty("cases").EnumerateArray();
}

public class ModelTests
{
    [Fact]
    public void ParsesHeader()
    {
        GAModel model = Fixtures.Model();
        Assert.Equal(Fixtures.Vectors.GetProperty("version").GetInt32(), model.Version);
        Assert.Equal(Fixtures.Vectors.GetProperty("hidden").GetInt32(), model.Hidden);
        Assert.Equal(0.7, model.Knob(GASpec.Knob.Temperature), 6);
        Assert.Equal(0.1, model.Knob(GASpec.Knob.Exploration), 6);
        Assert.Equal(GASpec.KnobDefault(GASpec.Knob.TickSeconds), model.Knob(GASpec.Knob.TickSeconds), 6);
    }

    [Fact]
    public void ForwardMatchesPython()
    {
        GAModel model = Fixtures.Model();
        foreach (JsonElement c in Fixtures.Cases)
        {
            double[] output = model.Forward(Fixtures.Doubles(c.GetProperty("x")));
            double[] expected = Fixtures.Doubles(c.GetProperty("output"));
            for (int k = 0; k < GASpec.NOut; k++) Assert.Equal(expected[k], output[k], 9);
        }
    }

    [Fact]
    public void RejectsTruncatedAndPadded()
    {
        byte[] data = Fixtures.Bytes("model.bin");
        Assert.Throws<GAFormatException>(() => GAModel.FromBytes(data.Take(data.Length - 1).ToArray()));
        Assert.Throws<GAFormatException>(() => GAModel.FromBytes(data.Concat(new byte[] { 0 }).ToArray()));
        Assert.Throws<GAFormatException>(() => GAModel.FromBytes(data.Take(20).ToArray()));
        Assert.Throws<GAFormatException>(() => GAModel.FromBytes(null));
    }

    [Fact]
    public void RejectsBadMagicAndSchema()
    {
        byte[] data = Fixtures.Bytes("model.bin");
        byte[] magic = (byte[])data.Clone(); magic[0] = (byte)'X';
        Assert.Throws<GAFormatException>(() => GAModel.FromBytes(magic));
        byte[] schema = (byte[])data.Clone(); schema[6] = 99;
        Assert.Throws<GAFormatException>(() => GAModel.FromBytes(schema));
    }

    [Fact]
    public void RejectsNonFiniteWeights()
    {
        byte[] data = (byte[])Fixtures.Bytes("model.bin").Clone();
        BitConverter.GetBytes(float.NaN).CopyTo(data, 30 + GASpec.NFeatures * 8 + 8);
        Assert.Throws<GAFormatException>(() => GAModel.FromBytes(data));
        BitConverter.GetBytes(float.PositiveInfinity).CopyTo(data, 30 + GASpec.NFeatures * 8 + 8);
        Assert.Throws<GAFormatException>(() => GAModel.FromBytes(data));
    }

    [Fact]
    public void RejectsHugeHiddenLayer()
    {
        byte[] data = (byte[])Fixtures.Bytes("model.bin").Clone();
        BitConverter.GetBytes((ushort)60000).CopyTo(data, 22);
        Assert.Throws<GAFormatException>(() => GAModel.FromBytes(data));
    }

    [Fact]
    public void ForwardRejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(() => Fixtures.Model().Forward(new double[3]));
    }

    [Fact]
    public void KnobsAreClamped()
    {
        Assert.Equal(2.0, GASpec.ClampKnob(GASpec.Knob.Temperature, 99));
        Assert.Equal(0.0, GASpec.ClampKnob(GASpec.Knob.Exploration, -1));
        Assert.Equal(0.2, GASpec.ClampKnob(GASpec.Knob.Exploration, 0.9));
        Assert.Equal(GASpec.KnobDefault(GASpec.Knob.TickSeconds), GASpec.ClampKnob(GASpec.Knob.TickSeconds, double.NaN));
    }
}

public class BundleTests
{
    [Fact]
    public void VerifiesSignedBundle()
    {
        byte[] model = GABundle.Verify(Fixtures.Bytes("bundle.bin"), Fixtures.Keys());
        Assert.Equal(Fixtures.Bytes("model.bin"), model);
    }

    [Fact]
    public void RejectsTampered() =>
        Assert.Throws<GAFormatException>(() => GABundle.Verify(Fixtures.Bytes("bundle_tampered.bin"), Fixtures.Keys()));

    [Fact]
    public void RejectsOtherKey() =>
        Assert.Throws<GAFormatException>(() => GABundle.Verify(Fixtures.Bytes("bundle_other_key.bin"), Fixtures.Keys()));

    [Fact]
    public void RejectsWhenNoKeysTrusted() =>
        Assert.Throws<GAFormatException>(() => GABundle.Verify(Fixtures.Bytes("bundle.bin"), new Dictionary<ushort, RSAParameters>()));

    [Fact]
    public void RejectsEveryFlippedByte()
    {
        byte[] bundle = Fixtures.Bytes("bundle.bin");
        var keys = Fixtures.Keys();
        Random random = new Random(1);
        for (int i = 0; i < 60; i++)
        {
            byte[] copy = (byte[])bundle.Clone();
            copy[random.Next(copy.Length)] ^= (byte)(1 << random.Next(8));
            Assert.ThrowsAny<Exception>(() => GABundle.Verify(copy, keys));
        }
    }

    [Fact]
    public void RejectsTruncatedOrPadded()
    {
        byte[] bundle = Fixtures.Bytes("bundle.bin");
        var keys = Fixtures.Keys();
        Assert.Throws<GAFormatException>(() => GABundle.Verify(bundle.Take(bundle.Length - 1).ToArray(), keys));
        Assert.Throws<GAFormatException>(() => GABundle.Verify(bundle.Concat(new byte[] { 1 }).ToArray(), keys));
        Assert.Throws<GAFormatException>(() => GABundle.Verify(new byte[5], keys));
    }

    [Fact]
    public void RefusesDowngrade()
    {
        Assert.Equal(7, GABundle.Load(Fixtures.Bytes("bundle.bin"), Fixtures.Keys(), 7).Version);
        Assert.Throws<GAFormatException>(() => GABundle.Load(Fixtures.Bytes("bundle.bin"), Fixtures.Keys(), 8));
    }

    [Fact]
    public void RefusesWeakKeys()
    {
        using RSA weak = RSA.Create(2048);
        var keys = new Dictionary<ushort, RSAParameters> { { 3, weak.ExportParameters(false) } };
        Assert.Throws<GAFormatException>(() => GABundle.Verify(Fixtures.Bytes("bundle.bin"), keys));
    }

    [Fact]
    public void ShippedKeyListParses()
    {
        Assert.NotNull(GATrustedKeys.All);
    }
}

public class PolicyTests
{
    [Fact]
    public void DistributionsMatchPython()
    {
        GAModel model = Fixtures.Model();
        double t = model.Knob(GASpec.Knob.Temperature), e = model.Knob(GASpec.Knob.Exploration);
        foreach (JsonElement c in Fixtures.Cases)
        {
            double[] output = model.Forward(Fixtures.Doubles(c.GetProperty("x")));
            bool[] mask = c.GetProperty("stance_mask").EnumerateArray().Select(v => v.GetBoolean()).ToArray();

            double[] stance = GAPolicy.MaskedSoftmax(output, GASpec.OutStance, 3, mask, t, e);
            double[] expected = Fixtures.Doubles(c.GetProperty("stance"));
            for (int i = 0; i < 3; i++) Assert.Equal(expected[i], stance[i], 9);

            double[] commit = GAPolicy.MaskedSoftmax(output, GASpec.OutCommit, 3, null, t, e);
            expected = Fixtures.Doubles(c.GetProperty("commit"));
            for (int i = 0; i < 3; i++) Assert.Equal(expected[i], commit[i], 9);

            Assert.Equal(c.GetProperty("retreat").GetDouble(), GAPolicy.RetreatProbability(output[GASpec.OutRetreat], t, e), 9);

            GAPolicy policy = new GAPolicy(model, null, 1);
            GAState state = new GAState { Features = Fixtures.Doubles(c.GetProperty("x")), Output = output, Hidden = new double[model.Hidden] };
            double[][] contacts = c.GetProperty("contacts").EnumerateArray().Select(Fixtures.Doubles).ToArray();
            GAChoice target = policy.ChooseTarget(state, contacts);
            expected = Fixtures.Doubles(c.GetProperty("target"));
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], target.Distribution[i], 9);
            Assert.Equal(target.Distribution[target.Action], target.Probability);
        }
    }

    [Fact]
    public void MaskedActionsAreNeverChosen()
    {
        GAModel model = Fixtures.Model();
        GAPolicy policy = new GAPolicy(model, null, 42);
        GAState state = policy.Evaluate(new double[GASpec.NFeatures]);
        for (int i = 0; i < 2000; i++)
        {
            GAChoice choice = policy.ChooseStance(state, canFallBack: false);
            Assert.NotEqual(GASpec.StanceFallBack, choice.Action);
            Assert.False(choice.Mask[2]);
        }
    }

    [Fact]
    public void SamplingFollowsTheDistribution()
    {
        GAModel model = Fixtures.Model();
        GAPolicy policy = new GAPolicy(model, null, 7);
        GAState state = policy.Evaluate(Fixtures.Doubles(Fixtures.Cases.First().GetProperty("x")));
        int[] counts = new int[3];
        const int n = 30000;
        double[] p = null;
        for (int i = 0; i < n; i++)
        {
            GAChoice choice = policy.ChooseCommit(state);
            counts[choice.Action]++;
            p = choice.Distribution;
        }
        for (int i = 0; i < 3; i++) Assert.InRange(counts[i] / (double)n, p[i] - 0.015, p[i] + 0.015);
    }

    [Fact]
    public void ExplorationOverrideIsClamped()
    {
        Assert.Equal(0.2, new GAPolicy(Fixtures.Model(), null, 1, 0.9).Exploration);
    }

    [Fact]
    public void EvaluateRoundsAndClampsFeatures()
    {
        GAPolicy policy = new GAPolicy(Fixtures.Model(), null, 1);
        double[] x = new double[GASpec.NFeatures];
        x[0] = 0.123456789; x[1] = double.NaN; x[2] = 1e9;
        GAState state = policy.Evaluate(x);
        Assert.Equal(0.1235, state.Features[0]);
        Assert.Equal(0.0, state.Features[1]);
        Assert.Equal(10.0, state.Features[2]);
    }
}

public class AdaptationTests
{
    private static (GAPolicy, GAAdaptation, GAModel) Setup()
    {
        GAModel model = Fixtures.Model();
        GAAdaptation adaptation = new GAAdaptation(model);
        return (new GAPolicy(model, adaptation, 3), adaptation, model);
    }

    [Fact]
    public void StartsInactiveAndLeavesOutputsAlone()
    {
        var (policy, adaptation, model) = Setup();
        Assert.False(adaptation.IsActive);
        double[] x = Fixtures.Doubles(Fixtures.Cases.First().GetProperty("x"));
        Assert.Equal(model.Forward(x), policy.Evaluate(x).Output);
    }

    [Fact]
    public void RewardedChoicesBecomeMoreLikely()
    {
        var (policy, adaptation, _) = Setup();
        double[] x = Fixtures.Doubles(Fixtures.Cases.First().GetProperty("x"));
        double before = policy.Evaluate(x).Output[GASpec.OutCommit + GASpec.CommitSurge];
        for (int battle = 0; battle < 30; battle++)
        {
            GAState state = policy.Evaluate(x);
            double[] p = GAPolicy.MaskedSoftmax(state.Output, GASpec.OutCommit, 3, null, policy.Temperature, policy.Exploration);
            GAChoice surge = new GAChoice { Action = GASpec.CommitSurge, Probability = p[2], Mask = new[] { true, true, true } };
            adaptation.Learn(new[] { new GAAdaptation.Step { State = state, Commit = surge } }, reward: 1.5, learningRate: 0.05, epsilon: policy.Exploration);
        }
        Assert.True(adaptation.IsActive);
        Assert.True(policy.Evaluate(x).Output[GASpec.OutCommit + GASpec.CommitSurge] > before);
    }

    [Fact]
    public void OffsetIsAlwaysCapped()
    {
        var (policy, adaptation, model) = Setup();
        Random random = new Random(5);
        double worst = 0;
        foreach (JsonElement c in Fixtures.Cases)
        {
            double[] x = Fixtures.Doubles(c.GetProperty("x"));
            for (int i = 0; i < 50; i++)
            {
                GAState state = policy.Evaluate(x);
                GAChoice stance = policy.ChooseStance(state, true);
                GAChoice retreat = policy.ChooseRetreat(state);
                adaptation.Learn(new[] { new GAAdaptation.Step { State = state, Stance = stance, Retreat = retreat } },
                    reward: random.NextDouble() < 0.5 ? 1.5 : -1.5, learningRate: 5.0, epsilon: policy.Exploration);
            }
            double[] baseOut = model.Forward(x);
            double[] adapted = policy.Evaluate(x).Output;
            for (int k = GASpec.OutStance; k < GASpec.OutTarget; k++) worst = Math.Max(worst, Math.Abs(adapted[k] - baseOut[k]));
            for (int k = GASpec.OutTarget; k < GASpec.NOut; k++) Assert.Equal(baseOut[k], adapted[k]); // target + value never adapt
        }
        Assert.True(worst <= 0.6 * policy.Temperature + 1e-9, $"logit moved {worst}");
        Assert.All(adaptation.RowNorms(), n => Assert.True(n <= adaptation.MaxRowNorm + 1e-9));
    }

    [Fact]
    public void SurvivesSaveAndLoad()
    {
        var (policy, adaptation, model) = Setup();
        GAState state = policy.Evaluate(new double[GASpec.NFeatures]);
        adaptation.Learn(new[] { new GAAdaptation.Step { State = state, Stance = policy.ChooseStance(state, true) } }, 1.0, 0.05, policy.Exploration);
        GAAdaptation restored = GAAdaptation.Restore(model, adaptation.ToBytes());
        Assert.Equal(adaptation.RowNorms().Select(v => Math.Round(v, 5)), restored.RowNorms().Select(v => Math.Round(v, 5)));
        Assert.Equal(1, restored.BattlesLearned);
    }

    [Fact]
    public void ResetsForAnotherModelOrJunk()
    {
        var (policy, adaptation, model) = Setup();
        GAState state = policy.Evaluate(new double[GASpec.NFeatures]);
        adaptation.Learn(new[] { new GAAdaptation.Step { State = state, Stance = policy.ChooseStance(state, true) } }, 1.0, 0.05, policy.Exploration);
        byte[] saved = adaptation.ToBytes();
        BitConverter.GetBytes(model.Version + 1).CopyTo(saved, 4);
        Assert.False(GAAdaptation.Restore(model, saved).IsActive);
        Assert.False(GAAdaptation.Restore(model, new byte[] { 1, 2, 3 }).IsActive);
        Assert.False(GAAdaptation.Restore(model, null).IsActive);
        Assert.False(GAAdaptation.Restore(model, adaptation.ToBytes().Take(20).ToArray()).IsActive);
    }
}

public class AuthTests
{
    [Fact]
    public void SignatureMatchesServer()
    {
        JsonElement h = Fixtures.Vectors.GetProperty("hmac");
        string signature = GAAuth.Sign(h.GetProperty("token").GetString(), h.GetProperty("method").GetString(),
            h.GetProperty("path").GetString(), h.GetProperty("time").GetInt64(), Encoding.UTF8.GetBytes(h.GetProperty("body").GetString()));
        Assert.Equal(h.GetProperty("signature").GetString(), signature);
    }

    [Fact]
    public void SolvesProofOfWork()
    {
        string nonce = GAAuth.SolveChallenge("123.abc.def", 12);
        using SHA256 sha = SHA256.Create();
        Assert.True(GAAuth.LeadingZeroBits(sha.ComputeHash(Encoding.UTF8.GetBytes("123.abc.def:" + nonce))) >= 12);
    }

    [Fact]
    public void RefusesAbsurdWork() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GAAuth.SolveChallenge("x", 40));

    [Fact]
    public void CountsZeroBits()
    {
        Assert.Equal(0, GAAuth.LeadingZeroBits(new byte[] { 0x80 }));
        Assert.Equal(7, GAAuth.LeadingZeroBits(new byte[] { 0x01 }));
        Assert.Equal(12, GAAuth.LeadingZeroBits(new byte[] { 0x00, 0x0F }));
    }

    [Theory]
    [InlineData("https://ga.solarnebulae.com", true)]
    [InlineData("http://localhost:8000", true)]
    [InlineData("http://127.0.0.1:8000", true)]
    [InlineData("http://ga.solarnebulae.com", false)]
    [InlineData("http://grand-admiral-api.grand-admiral.svc.cluster.local:8080", true)]
    [InlineData("http://evil.svc.cluster.local.example.com", false)]
    [InlineData("https://user:pass@ga.solarnebulae.com", false)]
    [InlineData("ftp://ga.solarnebulae.com", false)]
    [InlineData("not a url", false)]
    public void OnlyHttpsServers(string url, bool allowed) => Assert.Equal(allowed, GAAuth.IsAllowedServer(url));
}

public class RecordTests
{
    // Plays a few fake battles through the real policy and writes them out for the Python side to
    // validate (ml/tests/test_csharp_parity.py): the C# client must produce records the trainer accepts.
    [Fact]
    public void ProducesValidRecords()
    {
        GAModel model = Fixtures.Model();
        string outDir = Path.Combine(Fixtures.Dir, "..", "..", "..", "..", "out");
        Directory.CreateDirectory(outDir);
        StringBuilder lines = new StringBuilder();
        Random random = new Random(11);
        for (int b = 0; b < 12; b++)
        {
            GAAdaptation adaptation = b % 2 == 0 ? null : new GAAdaptation(model);
            GAPolicy policy = new GAPolicy(model, adaptation, b);
            GARecord record = new GARecord
            {
                ModelVersion = model.Version, ClientVersion = "1.2.3-test<>\"", Adapted = adaptation != null, VsPlayer = b % 3 == 0,
                Attacker = b % 2 == 0, Temperature = policy.Temperature, Exploration = policy.Exploration,
                Duration = 120 + b, StartPower = 1000, EndPower = 400, EnemyStartPower = 900, EnemyEndPower = b % 2 == 0 ? 0 : 300,
                Won = b % 2 == 0
            };
            List<GAAdaptation.Step> steps = new List<GAAdaptation.Step>();
            for (int t = 0; t < 8; t++)
            {
                double[] x = new double[GASpec.NFeatures];
                for (int i = 0; i < x.Length; i++) x[i] = random.NextDouble() * 4 - 2;
                GAState state = policy.Evaluate(x);
                double[][] contacts = Enumerable.Range(0, 2 + t).Select(_ => Enumerable.Range(0, GASpec.NContact).Select(__ => random.NextDouble()).ToArray()).ToArray();
                var decision = new GARecord.Decision
                {
                    Time = t * 3.0, State = state, Stance = policy.ChooseStance(state, t % 2 == 0),
                    Retreat = policy.ChooseRetreat(state), Commit = policy.ChooseCommit(state), Target = policy.ChooseTarget(state, contacts)
                };
                record.Add(decision);
                steps.Add(new GAAdaptation.Step { State = state, Stance = decision.Stance, Retreat = decision.Retreat, Commit = decision.Commit });
            }
            adaptation?.Learn(steps, record.Return(), 0.05, policy.Exploration);
            string json = record.ToJson();
            using JsonDocument doc = JsonDocument.Parse(json);  // well-formed
            Assert.DoesNotContain("NaN", json);
            Assert.Equal("1.2.3-test", doc.RootElement.GetProperty("client_version").GetString());
            lines.AppendLine(json);
        }
        File.WriteAllText(Path.Combine(outDir, "csharp_records.jsonl"), lines.ToString());
    }

    [Fact]
    public void CapsDecisionsAndIgnoresEmptyOnes()
    {
        GAModel model = Fixtures.Model();
        GAPolicy policy = new GAPolicy(model, null, 1);
        GARecord record = new GARecord();
        GAState state = policy.Evaluate(new double[GASpec.NFeatures]);
        record.Add(new GARecord.Decision { State = state });
        Assert.Empty(record.Decisions);
        for (int i = 0; i < 500; i++) record.Add(new GARecord.Decision { Time = i, State = state, Commit = policy.ChooseCommit(state) });
        Assert.Equal(GARecord.MaxDecisions, record.Decisions.Count);
    }

    [Fact]
    public void ReturnMatchesTrainer()
    {
        GARecord record = new GARecord { StartPower = 1000, EndPower = 500, EnemyStartPower = 800, EnemyEndPower = 0, Won = true };
        Assert.Equal(1.0, record.Return(), 9);  // 0.5 - 0 + 0.5
        record = new GARecord { StartPower = 1000, EndPower = 0, EnemyStartPower = 800, EnemyEndPower = 800, Won = false };
        Assert.Equal(-1.5, record.Return(), 9);
    }

    [Fact]
    public void NeverClaimsAWinByRetreating()
    {
        GAModel model = Fixtures.Model();
        GAPolicy policy = new GAPolicy(model, null, 1);
        GARecord record = new GARecord { Won = true, Retreated = true, Duration = 1, StartPower = 1, EnemyStartPower = 1 };
        GAState state = policy.Evaluate(new double[GASpec.NFeatures]);
        record.Add(new GARecord.Decision { State = state, Commit = policy.ChooseCommit(state) });
        Assert.False(record.Uploadable);
        Assert.Contains("\"won\":false", record.ToJson());
    }
}
