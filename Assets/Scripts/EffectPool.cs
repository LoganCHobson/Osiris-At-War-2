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
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        available.Clear();
    }

    public static void Play(string key, ParticleSystem template, Vector3 position, Quaternion rotation, float lifetime)
    {
        PooledEffect effect = Take(key, template);
        effect.transform.SetPositionAndRotation(position, rotation);
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
        PooledEffect effect = instance.gameObject.AddComponent<PooledEffect>();
        effect.Initialize(key, instance);
        return effect;
    }
}
