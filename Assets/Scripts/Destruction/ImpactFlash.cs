using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Volume))]
public class ImpactFlash : MonoBehaviour
{
    public static ImpactFlash Instance { get; private set; }

    public float decay = 2.2f;
    public float range = 400f;
    [Range(0f, 1f)] public float maxWeight = 0.7f;

    private Volume volume;
    private float energy;

    public static void Pulse(Vector3 position, float strength)
    {
        if (Instance == null || strength <= 0f) return;

        Camera cam = Camera.main;
        float distance = cam != null ? Vector3.Distance(cam.transform.position, position) : 0f;
        float falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(1f, Instance.range));
        Instance.energy = Mathf.Min(Instance.maxWeight, Instance.energy + strength * falloff * falloff);
    }

    private void Awake()
    {
        Instance = this;
        volume = GetComponent<Volume>();
        volume.weight = 0f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (energy <= 0f)
        {
            if (volume.weight != 0f) volume.weight = 0f;
            return;
        }

        energy = Mathf.MoveTowards(energy, 0f, decay * energy * Time.unscaledDeltaTime + 0.05f * Time.unscaledDeltaTime);
        volume.weight = energy;
    }
}
