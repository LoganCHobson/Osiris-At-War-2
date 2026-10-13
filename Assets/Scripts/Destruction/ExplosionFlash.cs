using UnityEngine;

public class ExplosionFlash : MonoBehaviour
{
    private Light flashLight;
    private float peak;
    private float duration;
    private float startedAt;

    public void Begin(Light light, float lifetime)
    {
        flashLight = light;
        peak = light.intensity;
        duration = Mathf.Max(0.05f, lifetime);
        startedAt = Time.time;
    }

    private void Update()
    {
        float t = (Time.time - startedAt) / duration;
        if (t >= 1f || flashLight == null)
        {
            Destroy(gameObject);
            return;
        }

        float rise = Mathf.Clamp01(t * 8f);
        float fall = 1f - t;
        flashLight.intensity = peak * rise * fall * fall;
    }
}
