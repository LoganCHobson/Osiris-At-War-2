using UnityEngine;

// A small personal shield on an Avanti fighter (fighters only - not interceptors or bombers). It soaks damage
// before the hull takes any, flashes as a soft blue ring when struck, and recharges once the craft has been out
// of fire for a few seconds. A hit from a warship's guns or point defence (normally lethal in two) knocks the
// shield down instead of hurting the craft, so a shielded fighter takes one extra heavy hit to kill.
[DisallowMultipleComponent]
public class FighterShield : MonoBehaviour
{
    public const float CapacityFraction = 0.6f; // Of the craft's own health.
    public const float RechargeDelay = 4f;      // Seconds out of fire before it comes back.
    public const float RechargeTime = 3f;       // Seconds from empty to full once recharging.
    private const float FlashTime = 0.3f;
    private static readonly Color Tint = new Color(0.35f, 0.65f, 1f);

    private HardpointHealth health;
    private float capacity;
    private float current;
    private float lastHitAt = float.NegativeInfinity;

    private Transform flash;
    private MeshRenderer flashRenderer;
    private MaterialPropertyBlock block;
    private float flashStartedAt = float.NegativeInfinity;
    private float flashStrength;
    private float flashSize = 1f;

    private static Material flashMaterial;
    private static Mesh quad;

    public bool Up => current > 0.01f;

    public static void Fit(Fighter fighter)
    {
        if (fighter.GetComponent<FighterShield>() == null) fighter.gameObject.AddComponent<FighterShield>();
    }

    private void Start()
    {
        health = GetComponent<HardpointHealth>();
        capacity = health != null ? Mathf.Max(1f, health.maxHealth * CapacityFraction) : 5f;
        current = capacity;
        Renderer body = GetComponentInChildren<Renderer>();
        flashSize = body != null ? Mathf.Clamp(body.bounds.extents.magnitude * 2.4f, 0.6f, 6f) : 1.5f;
    }

    // Takes what it can of a hit; returns what gets through to the hull.
    public float Absorb(float damage)
    {
        lastHitAt = Time.time;
        if (!Up) return damage;
        float taken = Mathf.Min(current, damage);
        current -= taken;
        Flash(current <= 0.01f ? 1.6f : 1f);
        return damage - taken;
    }

    // A warship hit: the shield takes it and goes down. False if there was no shield left to take it.
    public bool TakeShipHit()
    {
        lastHitAt = Time.time;
        if (!Up) return false;
        current = 0f;
        Flash(1.6f);
        return true;
    }

    private void Update()
    {
        if (current < capacity && Time.time - lastHitAt > RechargeDelay)
            current = Mathf.Min(capacity, current + capacity / RechargeTime * Time.deltaTime);

        if (flashRenderer == null || !flashRenderer.enabled) return;
        float t = (Time.time - flashStartedAt) / FlashTime;
        if (t >= 1f) { flashRenderer.enabled = false; return; }
        Camera cam = Camera.main;
        if (cam != null) flash.rotation = Quaternion.LookRotation(flash.position - cam.transform.position, cam.transform.up);
        flash.localScale = Vector3.one * flashSize * Mathf.Lerp(0.85f, 1.1f, t) / Mathf.Max(0.001f, transform.lossyScale.x);
        block.SetColor("_Color", Tint * (flashStrength * (1f - t) * (1f - t) * 2.5f));
        flashRenderer.SetPropertyBlock(block);
    }

    private void Flash(float strength)
    {
        if (!EnsureFlash()) return;
        flashStartedAt = Time.time;
        flashStrength = strength;
        flashRenderer.enabled = true;
    }

    private bool EnsureFlash()
    {
        if (flashRenderer != null) return true;
        if (flashMaterial == null)
        {
            Material source = Resources.Load<Material>("FTL/FTLGlow");
            if (source == null) return false;
            flashMaterial = new Material(source) { name = "FighterShieldFlash" };
            flashMaterial.SetTexture("_MainTex", RingTexture());
            flashMaterial.SetFloat("_Intensity", 1f);
        }
        if (quad == null)
        {
            quad = new Mesh { name = "FighterShieldQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.RecalculateBounds();
        }
        GameObject go = new GameObject("ShieldFlash");
        go.layer = gameObject.layer;
        flash = go.transform;
        flash.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        flashRenderer = go.AddComponent<MeshRenderer>();
        flashRenderer.sharedMaterial = flashMaterial;
        flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flashRenderer.receiveShadows = false;
        flashRenderer.enabled = false;
        block = new MaterialPropertyBlock();
        return true;
    }

    // A soft ring with a faint fill: reads as a bubble around the craft.
    private static Texture2D RingTexture()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.09f, 2f));
                float fill = r < 0.82f ? 0.12f * r : 0f;
                float a = Mathf.Clamp01(ring + fill) * Mathf.Clamp01((1f - r) * 8f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
