using UnityEngine;

public class PooledEffect : MonoBehaviour
{
    public string Key { get; private set; }

    private ParticleSystem system;
    private float returnAt;

    public void Initialize(string key, ParticleSystem particleSystem)
    {
        Key = key;
        system = particleSystem;
    }

    public void Begin(float lifetime)
    {
        returnAt = Time.time + lifetime;
        system.Clear(true);
        system.Play(true);
    }

    private void Update()
    {
        if (Time.time >= returnAt)
        {
            EffectPool.Return(this);
        }
    }
}
