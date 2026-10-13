using UnityEngine;

[DefaultExecutionOrder(100)]
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    public float maxAngle = 2.5f;
    public float frequency = 18f;
    public float recovery = 1.6f;
    public float range = 250f;

    private float trauma;
    private Quaternion applied = Quaternion.identity;
    private Quaternion shakenRotation;
    private float seed;

    public static void Shake(Vector3 position, float strength)
    {
        if (strength <= 0f) return;

        if (Instance == null)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Instance = cam.GetComponent<CameraShake>();
            if (Instance == null) Instance = cam.gameObject.AddComponent<CameraShake>();
        }

        float distance = Vector3.Distance(Instance.transform.position, position);
        float falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(1f, Instance.range));
        Instance.trauma = Mathf.Clamp01(Instance.trauma + strength * falloff);
    }

    private void Awake()
    {
        Instance = this;
        seed = Random.value * 100f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        if (transform.localRotation == shakenRotation)
        {
            transform.localRotation = transform.localRotation * Quaternion.Inverse(applied);
        }
        applied = Quaternion.identity;
        shakenRotation = transform.localRotation;

        if (trauma <= 0f) return;

        trauma = Mathf.Max(0f, trauma - recovery * Time.unscaledDeltaTime);
        if (Time.timeScale <= 0f) return;

        float amount = trauma * trauma * maxAngle;
        float time = Time.time * frequency;
        applied = Quaternion.Euler(
            (Mathf.PerlinNoise(seed, time) * 2f - 1f) * amount,
            (Mathf.PerlinNoise(seed + 10f, time) * 2f - 1f) * amount,
            (Mathf.PerlinNoise(seed + 20f, time) * 2f - 1f) * amount * 0.5f);
        transform.localRotation = transform.localRotation * applied;
        shakenRotation = transform.localRotation;
    }
}
