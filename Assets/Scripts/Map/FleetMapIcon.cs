using TMPro;
using UnityEngine;

public enum PowerMarkerStyle { RingGauge, Pips, SlimChevrons }

// The galactic map marker for a fleet: a faction-coloured hex badge showing the ship count, with power-tier
// markers (a segmented gauge, pips or chevrons) so strength reads at a glance. Built entirely at runtime, so every
// fleet prefab picks it up without any setup. GalacticFleet adds it automatically.
[DisallowMultipleComponent]
public class FleetMapIcon : MonoBehaviour
{
    [Header("Look")]
    public float iconSize = 1.2f;
    public float hoverHeight = 0.4f;
    public bool hideShipModel = true;
    [Tooltip("Camera distance at which the icon is drawn at iconSize. Further out it grows (up to maxZoomScale) so it stays readable.")]
    public float referenceCameraDistance = 16f;
    public float maxZoomScale = 2.5f;
    public Color fillColor = new Color(0.05f, 0.07f, 0.11f, 0.92f);
    public Color powerColor = new Color(1f, 0.82f, 0.35f);
    public Color unlitPowerColor = new Color(1f, 1f, 1f, 0.18f);
    public Color selectedColor = new Color(1f, 1f, 1f, 0.9f);
    public Color dropTargetColor = new Color(1f, 0.78f, 0.25f, 1f);

    [Header("Power tiers")]
    public PowerMarkerStyle powerStyle = PowerMarkerStyle.RingGauge;
    [Tooltip("Total fleet combat power needed to light the 1st, 2nd, 3rd... power marker.")]
    public float[] powerThresholds = { 1f, 200f, 450f, 800f, 1300f };

    private const int MaxMarkers = 8;

    private GalacticFleet fleet;
    private Transform root;
    private SpriteRenderer ring;
    private SpriteRenderer fill;
    private SpriteRenderer selection;
    private TextMeshPro countText;
    private SpriteRenderer[] markers = new SpriteRenderer[0];
    private Transform markerRoot;
    private PowerMarkerStyle builtStyle;
    private Camera cam;

    private int shownCount = -1;
    private int shownTier = -1;
    private Color shownFaction;

    private static Sprite hexFillSprite;
    private static Sprite hexRingSprite;
    private static Sprite chevronSprite;
    private static Sprite pipSprite;
    private static Sprite arcSprite;
    private static Sprite circleRingSprite;
    private static Material spriteMaterial;

    public Transform Root => root;

    public static int TierFor(float power, float[] thresholds)
    {
        int tier = 0;
        for (int i = 0; i < thresholds.Length; i++)
        {
            if (power >= thresholds[i]) tier = i + 1;
        }
        return tier;
    }

    public bool IsIconPart(Transform part)
    {
        return root != null && part != null && part.IsChildOf(root);
    }

    private void Awake()
    {
        fleet = GetComponent<GalacticFleet>();
        BuildSharedAssets();
        Build();

        if (hideShipModel)
        {
            HideShipModel();
        }
    }

    private void Build()
    {
        root = new GameObject("MapIcon").transform;
        root.SetParent(transform, false);
        root.gameObject.layer = gameObject.layer;

        float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
        root.localScale = Vector3.one * (iconSize / parentScale);
        root.position = transform.position + Vector3.up * hoverHeight;

        selection = CreateSprite("Selection", circleRingSprite, 0, new Vector3(0f, 0f, 0.02f), 1.45f);
        selection.enabled = false;
        fill = CreateSprite("Fill", hexFillSprite, 1, Vector3.zero, 1f);
        fill.color = fillColor;
        ring = CreateSprite("Ring", hexRingSprite, 2, new Vector3(0f, 0f, -0.01f), 1f);

        BuildMarkers();

        GameObject textObject = new GameObject("Count");
        textObject.layer = gameObject.layer;
        textObject.transform.SetParent(root, false);
        textObject.transform.localPosition = new Vector3(0f, 0.02f, -0.02f);

        countText = textObject.AddComponent<TextMeshPro>();
        countText.alignment = TextAlignmentOptions.Center;
        countText.fontSize = 6f;
        countText.fontStyle = FontStyles.Bold;
        countText.color = Color.white;
        countText.textWrappingMode = TextWrappingModes.NoWrap;
        countText.rectTransform.sizeDelta = new Vector2(1.4f, 0.8f);
        countText.sortingOrder = 104; // Above the badge fill (sprites sort at 100+).

        // A generous click target - the old ship model's collider was tiny at map zoom.
        SphereCollider clickTarget = root.gameObject.AddComponent<SphereCollider>();
        clickTarget.radius = 0.55f;
        clickTarget.center = new Vector3(0f, -0.2f, 0f);
    }

    private void BuildMarkers()
    {
        if (markerRoot != null) Destroy(markerRoot.gameObject);

        markerRoot = new GameObject("PowerMarkers").transform;
        markerRoot.gameObject.layer = gameObject.layer;
        markerRoot.SetParent(root, false);
        builtStyle = powerStyle;

        int count = Mathf.Min(MaxMarkers, powerThresholds.Length);
        markers = new SpriteRenderer[count];

        for (int i = 0; i < count; i++)
        {
            switch (powerStyle)
            {
                case PowerMarkerStyle.RingGauge:
                    // Segments run clockwise around the badge, starting from the top.
                    markers[i] = CreateSprite($"Gauge{i + 1}", arcSprite, 3, new Vector3(0f, 0f, -0.005f), 1.2f, markerRoot);
                    markers[i].transform.localRotation = Quaternion.Euler(0f, 0f, -360f / count * i);
                    break;

                case PowerMarkerStyle.Pips:
                    float x = (i - (count - 1) * 0.5f) * 0.26f;
                    markers[i] = CreateSprite($"Pip{i + 1}", pipSprite, 3, new Vector3(x, -0.8f, -0.01f), 0.2f, markerRoot);
                    break;

                default:
                    markers[i] = CreateSprite($"Chevron{i + 1}", chevronSprite, 3, new Vector3(0f, -0.74f - i * 0.12f, -0.01f), 0.6f, markerRoot);
                    break;
            }
        }

        shownTier = -1;
    }

    private SpriteRenderer CreateSprite(string partName, Sprite sprite, int order, Vector3 localPosition, float scale, Transform parent = null)
    {
        GameObject part = new GameObject(partName);
        part.layer = gameObject.layer;
        part.transform.SetParent(parent != null ? parent : root, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = Vector3.one * scale;

        SpriteRenderer renderer = part.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = spriteMaterial;
        renderer.sortingOrder = 100 + order;
        return renderer;
    }

    private void HideShipModel()
    {
        foreach (MeshRenderer model in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsIconPart(model.transform)) continue;

            if (model.transform == transform)
            {
                model.forceRenderingOff = true;
            }
            else
            {
                model.gameObject.SetActive(false); // Stays off even when fog of war re-enables renderers.
            }
        }
    }

    private void LateUpdate()
    {
        if (root == null) return;

        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            root.rotation = cam.transform.rotation; // Billboard - always face the camera.
        }
        root.position = transform.position + Vector3.up * hoverHeight;

        float zoom = 1f;
        if (cam != null && referenceCameraDistance > 0f)
        {
            float distance = Vector3.Distance(cam.transform.position, root.position);
            zoom = Mathf.Clamp(distance / referenceCameraDistance, 1f, maxZoomScale);
        }
        root.localScale = Vector3.one * (iconSize * zoom / Mathf.Max(0.0001f, transform.lossyScale.x));

        if (powerStyle != builtStyle || markers.Length != Mathf.Min(MaxMarkers, powerThresholds.Length))
        {
            BuildMarkers(); // Inspector change while playing.
        }

        RefreshContents();
        RefreshHighlight();
    }

    private void RefreshContents()
    {
        if (fleet == null) return;

        int count = fleet.roster.Count;
        int tier = Mathf.Min(markers.Length, TierFor(Strength.Of(fleet), powerThresholds));
        Color factionColor = fleet.faction != null ? fleet.faction.color : Color.gray;

        // Fog of war toggles every renderer on reveal, so re-assert which markers show each frame.
        // Chevrons only draw the earned ones; the gauge and pips also show the unearned slots, dimmed.
        bool showUnlit = powerStyle != PowerMarkerStyle.SlimChevrons;
        for (int i = 0; i < markers.Length; i++)
        {
            markers[i].enabled = fleet.IsRevealed && (i < tier || showUnlit);
        }

        if (count == shownCount && tier == shownTier && factionColor == shownFaction) return;

        shownCount = count;
        shownTier = tier;
        shownFaction = factionColor;

        ring.color = Color.Lerp(factionColor, Color.white, 0.2f);
        countText.text = count.ToString();

        for (int i = 0; i < markers.Length; i++)
        {
            markers[i].color = i < tier ? powerColor : unlitPowerColor;
        }
    }

    private void RefreshHighlight()
    {
        GalacticMapManager map = GalacticMapManager.Instance;
        bool isDropTarget = map != null && map.DropTargetFleet == fleet;
        bool isSelected = map != null && map.IsSelected(fleet);

        bool show = fleet != null && fleet.IsRevealed && (isDropTarget || isSelected);
        selection.enabled = show;
        if (!show) return;

        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
        Color color = isDropTarget ? dropTargetColor : selectedColor;
        color.a *= isDropTarget ? 1f : pulse;
        selection.color = color;
        selection.transform.localScale = Vector3.one * (isDropTarget ? 1.6f : 1.45f);
    }

    private void OnValidate()
    {
        shownCount = -1; // Force a refresh after inspector tweaks.
    }

    // ---- Procedural sprites (generated once, shared by every fleet) ----

    private static void BuildSharedAssets()
    {
        if (spriteMaterial == null)
        {
            spriteMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        const int size = 128;
        if (hexFillSprite == null) hexFillSprite = MakeSprite(size, size, (x, y) => Coverage(Hex(x, y, 0.92f), size));
        if (hexRingSprite == null) hexRingSprite = MakeSprite(size, size, (x, y) => Coverage(Mathf.Abs(Hex(x, y, 0.84f)) - 0.09f, size));
        if (circleRingSprite == null) circleRingSprite = MakeSprite(size, size, (x, y) => Coverage(Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 0.9f) - 0.05f, size));
        if (chevronSprite == null) chevronSprite = MakeSprite(size, size / 2, (x, y) => Coverage(Chevron(x, y), size));
        if (pipSprite == null) pipSprite = MakeSprite(64, 64, (x, y) => Coverage((Mathf.Abs(x) + Mathf.Abs(y) - 0.8f) * 0.7f, 64));
        if (arcSprite == null) arcSprite = MakeSprite(size, size, (x, y) => Coverage(Arc(x, y, 0.88f, 0.06f, 27f), size));
    }

    // Signed distance to a pointy-top hexagon of the given radius (negative = inside).
    private static float Hex(float x, float y, float radius)
    {
        x = Mathf.Abs(x);
        y = Mathf.Abs(y);
        return Mathf.Max(x, x * 0.5f + y * 0.866f) - radius * 0.866f;
    }

    // Signed distance to a slim upward "^" chevron stroke.
    private static float Chevron(float x, float y)
    {
        Vector2 p = new Vector2(Mathf.Abs(x), y);
        Vector2 a = new Vector2(0f, 0.15f);
        Vector2 b = new Vector2(0.62f, -0.15f);
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t) - 0.075f;
    }

    // Signed distance to a ring segment centred on the top, spanning +/- halfSpanDegrees.
    private static float Arc(float x, float y, float radius, float halfThickness, float halfSpanDegrees)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        float ringDistance = Mathf.Abs(r - radius) - halfThickness;
        float angle = Mathf.Abs(Mathf.Atan2(x, y)) * Mathf.Rad2Deg;
        float spanDistance = (angle - halfSpanDegrees) * Mathf.Deg2Rad * radius;
        return Mathf.Max(ringDistance, spanDistance);
    }

    private static float Coverage(float distance, int size)
    {
        return Mathf.Clamp01(0.5f - distance * size * 0.5f);
    }

    private static Sprite MakeSprite(int width, int height, System.Func<float, float, float> alphaAt)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "FleetMapIcon"
        };

        Color32[] pixels = new Color32[width * height];
        float half = width * 0.5f;
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                float x = (px + 0.5f - half) / half;
                float y = (py + 0.5f - height * 0.5f) / half;
                byte alpha = (byte)(alphaAt(x, y) * 255f);
                pixels[py * width + px] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), width);
    }
}
