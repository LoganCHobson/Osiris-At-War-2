using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EffectPool
{
    private static readonly Dictionary<string, Stack<PooledEffect>> available = new Dictionary<string, Stack<PooledEffect>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        available.Clear();
        emitters.Clear();
        budgets.Clear();
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        available.Clear();
        emitters.Clear();
        budgets.Clear();
    }

    // ---------------- Cheap repeats ----------------
    // Switching a pooled effect on costs its whole hierarchy waking up (a few milliseconds for the bigger
    // explosions). For small effects played over and over - a burning fighter's trail of puffs - one copy of
    // the effect stays live and just emits a few more particles where it's wanted: no objects switched on.
    private static readonly Dictionary<string, ParticleSystem[]> emitters = new Dictionary<string, ParticleSystem[]>();

    public static void Puff(string key, ParticleSystem template, Vector3 position, float scale, int perSystem = 2)
    {
        if (template == null) return;
        if (!emitters.TryGetValue(key, out ParticleSystem[] systems) || systems == null || systems.Length == 0 || systems[0] == null)
        {
            ParticleSystem root = Object.Instantiate(template);
            root.name = key + " (puffs)";
            foreach (CFX_AutoDestructShuriken autoDestruct in root.GetComponentsInChildren<CFX_AutoDestructShuriken>(true)) Object.Destroy(autoDestruct);
            foreach (Light light in root.GetComponentsInChildren<Light>(true)) light.enabled = false; // a light per puff was never wanted
            root.transform.localScale = template.transform.localScale * scale;
            systems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.MainModule main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.loop = true;
                main.stopAction = ParticleSystemStopAction.None;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                ParticleSystem.EmissionModule emission = system.emission;
                emission.enabled = false;
                system.Play(false);
            }
            emitters[key] = systems;
        }
        ParticleSystem.EmitParams at = new ParticleSystem.EmitParams { position = position, applyShapeToPosition = true };
        foreach (ParticleSystem system in systems)
        {
            if (system != null) system.Emit(at, perSystem);
        }
    }

    // How many full plays of an effect a frame gets (0 = no limit). Past it, Play falls back to a Puff.
    private static readonly Dictionary<string, (int frame, int count)> budgets = new Dictionary<string, (int, int)>();

    public static bool Budget(string key, int perFrame)
    {
        budgets.TryGetValue(key, out (int frame, int count) b);
        if (b.frame != Time.frameCount) b = (Time.frameCount, 0);
        b.count++;
        budgets[key] = b;
        return b.count <= perFrame;
    }

    public static void Play(string key, ParticleSystem template, Vector3 position, Quaternion rotation, float lifetime)
    {
        Play(key, template, position, rotation, lifetime, template.transform.localScale);
    }

    public const int MaxPlaysPerFrame = 12;
    private static int playsFrame = -1, playsThisFrame;

    private static readonly Dictionary<string, Unity.Profiling.ProfilerMarker> markers = new Dictionary<string, Unity.Profiling.ProfilerMarker>();

    public static void Play(string key, ParticleSystem template, Vector3 position, Quaternion rotation, float lifetime, Vector3 scale)
    {
        if (!markers.TryGetValue(key, out Unity.Profiling.ProfilerMarker marker)) markers[key] = marker = new Unity.Profiling.ProfilerMarker("Effect " + key);
        using var effectTiming = marker.Auto();
        // Switching an effect on wakes its whole hierarchy (a fighter's death alone was ~4 ms a frame in a big
        // fight). Past MaxPlaysPerFrame in one frame, across every kind of effect, the rest are emitted as puffs
        // from the one live copy instead - no objects switched on.
        if (playsFrame != Time.frameCount) { playsFrame = Time.frameCount; playsThisFrame = 0; }
        if (++playsThisFrame > MaxPlaysPerFrame)
        {
            float templateScale = Mathf.Max(1e-4f, Mathf.Abs(template.transform.localScale.x));
            Puff(key, template, position, Mathf.Abs(scale.x) / templateScale, 3);
            return;
        }
        PooledEffect effect = Take(key, template);
        effect.transform.SetPositionAndRotation(position, rotation);
        effect.transform.localScale = scale;
        effect.gameObject.SetActive(true);
        effect.Begin(lifetime);
    }

    internal static void Return(PooledEffect effect)
    {
        effect.gameObject.SetActive(false);

        if (!available.TryGetValue(effect.Key, out Stack<PooledEffect> stack))
        {
            stack = new Stack<PooledEffect>();
            available[effect.Key] = stack;
        }
        stack.Push(effect);
    }

    private static PooledEffect Take(string key, ParticleSystem template)
    {
        if (available.TryGetValue(key, out Stack<PooledEffect> stack))
        {
            while (stack.Count > 0)
            {
                PooledEffect pooled = stack.Pop();
                if (pooled != null) return pooled;
            }
        }

        ParticleSystem instance = Object.Instantiate(template);
        foreach (CFX_AutoDestructShuriken autoDestruct in instance.GetComponentsInChildren<CFX_AutoDestructShuriken>(true))
        {
            Object.Destroy(autoDestruct);
        }
        PooledEffect effect = instance.gameObject.AddComponent<PooledEffect>();
        effect.Initialize(key, instance);
        return effect;
    }
}
