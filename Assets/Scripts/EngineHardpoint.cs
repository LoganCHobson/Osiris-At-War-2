using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public class EngineHardpoint : MonoBehaviour
{
    [Range(0.05f, 1f)] public float destroyedSpeedMultiplier = 0.25f;
    [Range(0.05f, 1f)] public float destroyedAccelerationMultiplier = 0.4f;
    [Range(0.05f, 1f)] public float destroyedTurnMultiplier = 0.5f;

    [Header("Effects")]
    public ParticleSystem[] destroyedBursts;
    public ParticleSystem[] damagedLoops;
    public AudioSource explosionAudio;

    [Header("Thrusters")]
    public ParticleSystem[] thrusterFlames;
    public bool sputterThrusters = true;
    public Vector2 sputterGap = new Vector2(0.4f, 1.8f);
    public Vector2 sputterLength = new Vector2(0.08f, 0.3f);

    public UnityEvent onEnginesDestroyed;

    public bool IsDestroyed { get; private set; }

    private NavMeshAgent agent;
    private float baseSpeed;
    private float baseAcceleration;
    private float baseAngularSpeed;

    private void Awake()
    {
        agent = GetComponentInParent<NavMeshAgent>();
        if (agent != null)
        {
            baseSpeed = agent.speed;
            baseAcceleration = agent.acceleration;
            baseAngularSpeed = agent.angularSpeed;
        }
    }

    public void OnEngineDestroyed()
    {
        if (IsDestroyed) return;

        IsDestroyed = true;
        ApplyEngineDamage();
        PlayEffects();
        onEnginesDestroyed.Invoke();
    }

    private void PlayEffects()
    {
        foreach (ParticleSystem burst in destroyedBursts)
        {
            if (burst != null) burst.Play(true);
        }

        foreach (ParticleSystem loop in damagedLoops)
        {
            if (loop != null) loop.Play(true);
        }

        if (explosionAudio != null)
        {
            explosionAudio.Play();
        }

        foreach (ParticleSystem flame in thrusterFlames)
        {
            if (flame != null) flame.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        if (sputterThrusters && thrusterFlames.Length > 0)
        {
            StartCoroutine(Sputter());
        }
    }

    private IEnumerator Sputter()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(sputterGap.x, sputterGap.y));

            ParticleSystem flame = thrusterFlames[Random.Range(0, thrusterFlames.Length)];
            if (flame == null) continue;

            flame.Play(true);
            yield return new WaitForSeconds(Random.Range(sputterLength.x, sputterLength.y));
            if (flame != null) flame.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void ApplyEngineDamage()
    {
        if (agent == null) return;

        EngineHardpoint[] engines = agent.GetComponentsInChildren<EngineHardpoint>(true);
        int destroyed = 0;
        foreach (EngineHardpoint engine in engines)
        {
            if (engine.IsDestroyed) destroyed++;
        }

        float lost = engines.Length > 0 ? destroyed / (float)engines.Length : 0f;
        agent.speed = baseSpeed * Mathf.Lerp(1f, destroyedSpeedMultiplier, lost);
        agent.acceleration = baseAcceleration * Mathf.Lerp(1f, destroyedAccelerationMultiplier, lost);
        agent.angularSpeed = baseAngularSpeed * Mathf.Lerp(1f, destroyedTurnMultiplier, lost);
    }

    public static bool EnginesDisabled(GameObject ship)
    {
        if (ship == null) return false;

        EngineHardpoint[] engines = ship.GetComponentsInChildren<EngineHardpoint>(true);
        if (engines.Length == 0) return false;

        foreach (EngineHardpoint engine in engines)
        {
            if (!engine.IsDestroyed) return false;
        }
        return true;
    }
}
