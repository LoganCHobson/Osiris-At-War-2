using UnityEngine;
using UnityEngine.UI;

public class SquadronIcon : MonoBehaviour
{
    public Image image;
    public Image healthFill;
    public Color healthyColor = new Color(0.3f, 1f, 0.35f, 1f);
    public Color criticalColor = new Color(1f, 0.25f, 0.2f, 1f);
    public float pixelSize = 34f;
    public float canvasSize = 10f;
    public float heightOffset = 9f;

    public Color friendlyColor = new Color(0.35f, 0.75f, 1f, 0.9f);
    public Color enemyColor = new Color(1f, 0.35f, 0.3f, 0.9f);
    public Color selectedColor = new Color(0.5f, 1f, 0.5f, 1f);

    private Camera cam;
    private bool selected;
    private bool friendly = true;
    private UnitHealthManager health;
    private float shownHealth = -1f;

    // Where the icon sits before any clumping (see SquadronIconLayout), and which side it's on.
    public Vector3 HomePosition { get; private set; }
    public bool Friendly => friendly;
    public FleetRole Role { get; private set; } = FleetRole.Fighter;
    public float PixelSize => pixelSize;

    private void OnEnable()
    {
        SquadronIconLayout.Register(this);
    }

    private void OnDisable()
    {
        SquadronIconLayout.Unregister(this);
    }

    private void Start()
    {
        health = GetComponentInParent<UnitHealthManager>();
        ApplyGlyph();
        Refresh();
    }

    // Each kind of strike craft gets its own shape so they can be told apart (colour still shows the side).
    private void ApplyGlyph()
    {
        Ship ship = health != null ? health.sourceShip : null;
        if (ship == null) ship = GetComponentInParent<Ship>();
        if (ship != null) Role = ship.fleetRole;

        if (image == null) return;
        image.sprite = SquadronGlyphs.For(Role);
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
    }

    // Used by SquadronIconLayout to slot this icon into a grid with its neighbours.
    public void PlaceAt(Vector3 worldPosition)
    {
        transform.position = worldPosition;
    }

    private void UpdateHealth()
    {
        if (healthFill == null || health == null || health.maxHealth <= 0f) return;

        float fraction = Mathf.Clamp01(health.currentHealth / health.maxHealth);
        if (Mathf.Approximately(fraction, shownHealth)) return;

        shownHealth = fraction;
        healthFill.fillAmount = fraction;
        healthFill.color = Color.Lerp(criticalColor, healthyColor, fraction);
    }

    public void SetSide(bool playerSide)
    {
        friendly = playerSide;
        Refresh();
    }

    public void SetSelected(bool value)
    {
        selected = value;
        Refresh();
    }

    private void Refresh()
    {
        if (image != null)
        {
            image.color = selected ? selectedColor : friendly ? friendlyColor : enemyColor;
        }
    }

    // Hidden until the squadron's craft are actually in sight (not on the frame it spawns, nor while it's still
    // jumping in), so the icon never pops up on its own.
    private CanvasGroup group;
    private Squadron squadron;

    private bool CraftVisible()
    {
        if (squadron == null) squadron = GetComponentInParent<Squadron>();
        if (squadron == null) return true;
        foreach (Fighter fighter in squadron.fighters)
        {
            if (fighter == null || !fighter.Alive) continue;
            Renderer renderer = fighter.GetComponentInChildren<Renderer>();
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy) return true;
        }
        return false;
    }

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
    }

    private void LateUpdate()
    {
        UpdateHealth();
        if (group != null) group.alpha = !CinematicCamera.Active && CraftVisible() ? 1f : 0f; // Never in the director's shots.

        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        Transform anchor = transform.parent != null ? transform.parent : transform;
        HomePosition = anchor.position + Vector3.up * heightOffset;
        transform.position = HomePosition;
        transform.rotation = cam.transform.rotation;

        float distance = Vector3.Distance(cam.transform.position, transform.position);
        float worldPerPixel = cam.orthographic
            ? cam.orthographicSize * 2f / Screen.height
            : 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;

        float scale = pixelSize * worldPerPixel / Mathf.Max(0.01f, canvasSize);
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(scale / parentScale.x, scale / parentScale.y, scale / parentScale.z);
    }
}
