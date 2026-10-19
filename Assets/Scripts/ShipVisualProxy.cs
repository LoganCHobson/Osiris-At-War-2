using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Turns a copy of a ship into a look-only model: same meshes and materials, but no scripts, physics, AI, UI
// or audio. Used for the reinforcement placement hologram.
public static class ShipVisualProxy
{
    private static readonly Dictionary<System.Type, System.Type[]> RequirementsByType = new Dictionary<System.Type, System.Type[]>();

    public static void Strip(GameObject model)
    {
        foreach (Canvas canvas in model.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas != null) Object.DestroyImmediate(canvas.gameObject);
        }

        StripBehaviours(model);

        StripAll<NavMeshAgent>(model);
        StripAll<NavMeshObstacle>(model);
        StripAll<Rigidbody>(model);
        StripAll<Collider>(model);
        StripAll<AudioSource>(model);
        StripAll<Light>(model);

        foreach (ParticleSystem particles in model.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
        }

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            bool isMesh = renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
            renderer.enabled = isMesh && renderer.enabled;
            renderer.forceRenderingOff = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    // Removes every script, dependents first - Unity refuses to remove a component another one
    // [RequireComponent]s (e.g. ShipDestruction -> HullDamage -> UnitHealthManager).
    private static void StripBehaviours(GameObject model)
    {
        List<MonoBehaviour> remaining = new List<MonoBehaviour>(model.GetComponentsInChildren<MonoBehaviour>(true));
        bool progress = true;

        while (remaining.Count > 0 && progress)
        {
            progress = false;
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                MonoBehaviour behaviour = remaining[i];
                if (behaviour != null && IsRequiredByAnother(behaviour, remaining)) continue;

                if (behaviour != null) Object.DestroyImmediate(behaviour);
                remaining.RemoveAt(i);
                progress = true;
            }
        }
    }

    private static bool IsRequiredByAnother(MonoBehaviour target, List<MonoBehaviour> remaining)
    {
        System.Type targetType = target.GetType();

        foreach (MonoBehaviour other in remaining)
        {
            if (other == null || other == target || other.gameObject != target.gameObject) continue;

            foreach (System.Type required in RequirementsOf(other.GetType()))
            {
                if (required.IsAssignableFrom(targetType)) return true;
            }
        }
        return false;
    }

    // Reflection is slow, so each script type's [RequireComponent] list is looked up once.
    private static System.Type[] RequirementsOf(System.Type type)
    {
        if (RequirementsByType.TryGetValue(type, out System.Type[] cached)) return cached;

        List<System.Type> required = new List<System.Type>();
        foreach (RequireComponent requirement in type.GetCustomAttributes(typeof(RequireComponent), true))
        {
            if (requirement.m_Type0 != null) required.Add(requirement.m_Type0);
            if (requirement.m_Type1 != null) required.Add(requirement.m_Type1);
            if (requirement.m_Type2 != null) required.Add(requirement.m_Type2);
        }

        cached = required.ToArray();
        RequirementsByType[type] = cached;
        return cached;
    }

    private static void StripAll<T>(GameObject model) where T : Component
    {
        foreach (T component in model.GetComponentsInChildren<T>(true))
        {
            if (component != null) Object.DestroyImmediate(component);
        }
    }
}
