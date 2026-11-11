using System;
using System.Collections.Generic;
using System.IO;
using Osiris.GrandAdmiral;
using UnityEngine;

// Grand Admiral: the learned difficulty. This owns the model the AI plays with.
//
// Where the model comes from, in order:
//   1. the newest model downloaded from the Grand Admiral server (persistentDataPath/grand-admiral),
//   2. the baseline shipped in the build (Resources/GrandAdmiral/baseline.bytes).
// Both are signed bundles, checked against GATrustedKeys every time they're loaded. Anything that
// doesn't verify is ignored. If nothing verifies, Grand Admiral plays exactly like Admiral.
//
// New models are only data (weights and tuning numbers), never code, so the AI improves for every
// player without a game update; GrandAdmiralService fetches them quietly in the background.
public static class GrandAdmiral
{
    public const string DefaultServer = "https://ga.solarnebulae.com";
    private const string ModelFile = "model.bundle";
    private const string AdaptationFile = "adaptation.bin";

    public static GAModel Model { get; private set; }
    public static GAAdaptation Adaptation { get; private set; }
    public static string LastError { get; private set; }
    public static bool Available => Model != null;
    public static event Action ModelChanged;

    public static string Folder
    {
        get
        {
            string folder = Path.Combine(Application.persistentDataPath, "grand-admiral");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string Server
    {
        get
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-gaServer" && GAAuth.IsAllowedServer(args[i + 1])) return args[i + 1].TrimEnd('/');
            }
            return DefaultServer;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        LoadModel();
        GrandAdmiralService.Ensure();
    }

    public static void LoadModel()
    {
        GAModel best = TryLoad(ReadFile(ModelFile), "downloaded");
        TextAsset baseline = Resources.Load<TextAsset>("GrandAdmiral/baseline");
        GAModel shipped = baseline != null ? TryLoad(baseline.bytes, "baseline") : null;
        if (shipped != null && (best == null || shipped.Version > best.Version)) best = shipped;
        SetModel(best);
    }

    private static GAModel TryLoad(byte[] bundle, string source)
    {
        if (bundle == null) return null;
        try
        {
            return GABundle.Load(bundle, GATrustedKeys.All);
        }
        catch (Exception e)
        {
            LastError = $"{source} model rejected: {e.Message}";
            Debug.LogWarning($"[GrandAdmiral] {LastError}");
            return null;
        }
    }

    private static void SetModel(GAModel model)
    {
        bool changed = Model?.Version != model?.Version;
        Model = model;
        if (model != null && (Adaptation == null || Adaptation.ModelVersion != model.Version))
        {
            Adaptation = GAAdaptation.Restore(model, ReadFile(AdaptationFile)); // Fresh if it was for another model.
        }
        Difficulty.Invalidate();
        if (changed)
        {
            Debug.Log(model != null ? $"[GrandAdmiral] Using model v{model.Version}" : "[GrandAdmiral] No model; Grand Admiral plays as Admiral");
            ModelChanged?.Invoke();
        }
    }

    // A bundle fresh from the server. Takes effect from the next battle; battles in progress keep the
    // model they started with. Returns false (and keeps the current model) unless it verifies and is newer.
    public static bool Install(byte[] bundle)
    {
        GAModel incoming = TryLoad(bundle, "downloaded");
        if (incoming == null || (Model != null && incoming.Version <= Model.Version)) return false;
        WriteFileAtomic(ModelFile, bundle);
        SetModel(incoming);
        return true;
    }

    // Self-play only: use this model for the coming battles whatever its version (a trial can be newer than
    // anything released, and the next run may be told to play an older release again). Not saved.
    public static bool UseForSelfPlay(byte[] bundle)
    {
        GAModel incoming = TryLoad(bundle, "self-play");
        if (incoming == null) return false;
        SetModel(incoming);
        return true;
    }

    // After a Grand Admiral battle: learn from it locally, and queue it for the global model.
    public static void Complete(GARecord record, IReadOnlyList<GAAdaptation.Step> steps, bool learnLocally)
    {
        if (record == null) return;
        if (learnLocally && Adaptation != null && Model != null && Adaptation.ModelVersion == record.ModelVersion)
        {
            Adaptation.Learn(steps, record.Return(), Model.Knob(GASpec.Knob.AdaptLearningRate), record.Exploration);
            WriteFileAtomic(AdaptationFile, Adaptation.ToBytes());
        }
        if (record.Uploadable && GameSettings.ShareBattleData)
        {
            GrandAdmiralService.Enqueue(record);
        }
    }

    public static byte[] ReadFile(string name)
    {
        try
        {
            string path = Path.Combine(Folder, name);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GrandAdmiral] Couldn't read {name}: {e.Message}");
            return null;
        }
    }

    public static void WriteFileAtomic(string name, byte[] data)
    {
        try
        {
            string path = Path.Combine(Folder, name);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GrandAdmiral] Couldn't write {name}: {e.Message}");
        }
    }
}
