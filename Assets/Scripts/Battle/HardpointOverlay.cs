using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Screen-space hardpoint icons for picking targets in a 3D battle. For the enemy ship under the pointer, and
// any ship your selection is attacking, each live hardpoint gets a flat icon drawn over everything (hulls
// can't hide it): weapons a diamond, engines a triangle, the shield generator a circle, anything else a
// square, coloured by its health. With ships selected, the icon nearest the pointer (within SnapPixels) is
// the one a click will hit: it grows, and a label beside the pointer names it ("ENGINE 62%"). Hardpoints your
// selected ships are shooting at wear red brackets, with faint lines from the ships. PlayerSpaceManager
// drives it each frame and asks for HoveredHardpoint / FocusShip.
public class HardpointOverlay : MonoBehaviour
{
    public float SnapPixels = 30f;
    public float iconSize = 18f, hoveredSize = 28f, lockSize = 40f;
    public float stickySeconds = 0.8f; // Keep showing a ship's icons briefly after the pointer leaves its hull.

    public HardpointHealth HoveredHardpoint { get; private set; }
    public HardpointManager FocusShip { get; private set; }

    public enum Kind { Weapon, PointDefence, Engine, Shield, System }

    private RectTransform root;
    private TMP_Text label;
    private readonly List<Image> icons = new List<Image>();
    private readonly List<Image> locks = new List<Image>();
    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private Sprite diamond, triangle, circle, square, bracket;
    private Material lineMaterial;
    private HardpointManager hoveredShip;
    private float hoveredShipUntil;
    private readonly HashSet<HardpointManager> shown = new HashSet<HardpointManager>();
    private readonly HashSet<Transform> lockedTargets = new HashSet<Transform>();

    private void Awake()
    {
        Canvas canvas = UIKit.Canvas("HardpointOverlay", 40, transform);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        root = (RectTransform)canvas.transform;
        foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
        GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null) raycaster.enabled = false; // Icons never swallow clicks; PlayerSpaceManager snaps to them.

        diamond = MakeSprite(Shape.Diamond);
        triangle = MakeSprite(Shape.Triangle);
        circle = MakeSprite(Shape.Circle);
        square = MakeSprite(Shape.Square);
        bracket = MakeSprite(Shape.Bracket);

        GameObject labelObject = new GameObject("HardpointLabel", typeof(RectTransform));
        labelObject.transform.SetParent(root, false);
        label = labelObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = 18f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.outlineWidth = 0.25f;
        label.outlineColor = new Color32(0, 0, 0, 255);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.rectTransform.pivot = new Vector2(0f, 1f);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = Vector2.zero;
        label.rectTransform.sizeDelta = new Vector2(320f, 30f);
        label.enabled = false;
    }

    public void UpdateOverlay(Camera cam, List<SpaceUnit> selected, LayerMask enemyLayer, bool pointerFree)
    {
        HoveredHardpoint = null;
        FocusShip = null;
        if (cam == null) { HideAll(); return; }
        Vector2 mouse = Input.mousePosition;

        // Which enemy ship the pointer is on (sticky for a moment, so the pointer can move out to its icons).
        if (pointerFree && Physics.Raycast(cam.ScreenPointToRay(mouse), out RaycastHit hit, Mathf.Infinity, enemyLayer))
        {
            HardpointManager ship = hit.collider.GetComponentInParent<HardpointManager>();
            if (ship != null) { hoveredShip = ship; hoveredShipUntil = Time.unscaledTime + stickySeconds; }
        }
        if (hoveredShip != null && (Time.unscaledTime > hoveredShipUntil || !Alive(hoveredShip))) hoveredShip = null;

        // What the selection is shooting at.
        lockedTargets.Clear();
        shown.Clear();
        foreach (SpaceUnit unit in selected)
        {
            if (unit == null || !unit.TryGetComponent(out HardpointManager own) || own.OrderedTarget == null) continue;
            HardpointManager target = own.OrderedTarget.GetComponentInParent<HardpointManager>();
            if (target == null || !Alive(target) || !Visible(target)) continue;
            lockedTargets.Add(own.OrderedTarget);
            shown.Add(target);
        }
        if (hoveredShip != null && Visible(hoveredShip)) shown.Add(hoveredShip);

        // Icons, and the one nearest the pointer.
        int used = 0;
        float best = SnapPixels;
        Image bestIcon = null;
        bool selecting = selected.Count > 0 && pointerFree;
        foreach (HardpointManager ship in shown)
        {
            foreach (HardpointHealth hardpoint in ship.hardpoints)
            {
                if (hardpoint == null || hardpoint.Destroyed || hardpoint.currentHealth <= 0f) continue;
                Vector3 screen = cam.WorldToScreenPoint(hardpoint.transform.position);
                if (screen.z <= 0f) continue;

                Image icon = Pooled(icons, used++, "HardpointIcon");
                icon.sprite = SpriteFor(KindOf(hardpoint));
                float health = hardpoint.maxHealth > 0f ? Mathf.Clamp01(hardpoint.currentHealth / hardpoint.maxHealth) : 1f;
                icon.color = health > 0.5f ? Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.35f, 1f, 0.45f), (health - 0.5f) * 2f)
                                           : Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(1f, 0.85f, 0.2f), health * 2f);
                icon.rectTransform.position = new Vector3(screen.x, screen.y, 0f); // Overlay canvas: world = screen pixels.
                icon.rectTransform.sizeDelta = Vector2.one * iconSize;
                icon.enabled = true;

                float distance = Vector2.Distance(mouse, screen);
                if (selecting && distance < best)
                {
                    best = distance;
                    bestIcon = icon;
                    HoveredHardpoint = hardpoint;
                    FocusShip = ship;
                }
            }
        }
        for (int i = used; i < icons.Count; i++) icons[i].enabled = false;
        if (FocusShip == null) FocusShip = hoveredShip;

        if (bestIcon != null)
        {
            bestIcon.rectTransform.sizeDelta = Vector2.one * hoveredSize;
            bestIcon.transform.SetAsLastSibling();
            float health = HoveredHardpoint.maxHealth > 0f ? HoveredHardpoint.currentHealth / HoveredHardpoint.maxHealth : 1f;
            label.text = $"{Name(KindOf(HoveredHardpoint))}  {health:P0}";
            label.rectTransform.position = new Vector3(mouse.x + 22f, mouse.y - 18f, 0f);
            label.enabled = true;
        }
        else
        {
            label.enabled = false;
        }

        // Target locks: red brackets on what the selection is shooting at, and faint lines from the ships.
        int lockUsed = 0;
        foreach (Transform target in lockedTargets)
        {
            Vector3 screen = cam.WorldToScreenPoint(target.position);
            if (screen.z <= 0f) continue;
            Image mark = Pooled(locks, lockUsed++, "TargetLock");
            mark.sprite = bracket;
            mark.color = new Color(1f, 0.3f, 0.25f, 0.95f);
            mark.rectTransform.position = new Vector3(screen.x, screen.y, 0f);
            mark.rectTransform.sizeDelta = Vector2.one * lockSize;
            mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * 45f % 90f);
            mark.enabled = true;
        }
        for (int i = lockUsed; i < locks.Count; i++) locks[i].enabled = false;

        int lineUsed = 0;
        foreach (SpaceUnit unit in selected)
        {
            if (unit == null || !unit.TryGetComponent(out HardpointManager own) || own.OrderedTarget == null) continue;
            if (!lockedTargets.Contains(own.OrderedTarget)) continue;
            LineRenderer line = Line(lineUsed++);
            line.SetPosition(0, unit.transform.position);
            line.SetPosition(1, own.OrderedTarget.position);
            line.enabled = true;
        }
        for (int i = lineUsed; i < lines.Count; i++) lines[i].enabled = false;
    }

    public void HideAll()
    {
        foreach (Image image in icons) image.enabled = false;
        foreach (Image image in locks) image.enabled = false;
        foreach (LineRenderer line in lines) line.enabled = false;
        if (label != null) label.enabled = false;
        HoveredHardpoint = null;
        FocusShip = null;
    }

    // Engines first, then the shield generator, then weapons, then the rest: Tab's cycling order.
    public static List<HardpointHealth> CycleOrder(HardpointManager ship)
    {
        List<HardpointHealth> live = ship.hardpoints.FindAll(h => h != null && !h.Destroyed && h.currentHealth > 0f);
        live.Sort((a, b) =>
        {
            int order = Rank(KindOf(a)).CompareTo(Rank(KindOf(b)));
            return order != 0 ? order : a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
        });
        return live;
    }

    private static int Rank(Kind kind) => kind switch { Kind.Engine => 0, Kind.Shield => 1, Kind.Weapon => 2, Kind.PointDefence => 3, _ => 4 };

    public static Kind KindOf(HardpointHealth hardpoint)
    {
        if (hardpoint.GetComponent<EngineHardpoint>() != null) return Kind.Engine;
        Transform ship = hardpoint.GetComponentInParent<HardpointManager>() != null ? hardpoint.GetComponentInParent<HardpointManager>().transform : null;
        if (ship != null && ShieldGenerator.LiveGenerator(ship.gameObject) == hardpoint) return Kind.Shield;
        if (hardpoint.TryGetComponent(out TurretController turret)) return turret.gatling || turret.beam ? Kind.PointDefence : Kind.Weapon;
        return Kind.System;
    }

    public static string Name(Kind kind) => kind switch
    {
        Kind.Engine => "ENGINE",
        Kind.Shield => "SHIELD GENERATOR",
        Kind.Weapon => "WEAPON",
        Kind.PointDefence => "POINT DEFENCE",
        _ => "SYSTEM"
    };

    private Sprite SpriteFor(Kind kind) => kind switch
    {
        Kind.Engine => triangle,
        Kind.Shield => circle,
        Kind.Weapon => diamond,
        Kind.PointDefence => diamond,
        _ => square
    };

    private static bool Alive(HardpointManager ship)
    {
        UnitHealthManager health = ship != null ? ship.GetComponent<UnitHealthManager>() : null;
        return ship != null && (health == null || !health.IsDead);
    }

    private static bool Visible(HardpointManager ship)
    {
        UnitHealthManager health = ship.GetComponent<UnitHealthManager>();
        return health == null || BattleSensors.PlayerCanSee(health);
    }

    private Image Pooled(List<Image> pool, int index, string name)
    {
        while (pool.Count <= index)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            Image image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.zero;
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.enabled = false;
            pool.Add(image);
        }
        return pool[index];
    }

    private LineRenderer Line(int index)
    {
        while (lines.Count <= index)
        {
            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
            GameObject go = new GameObject("TargetLine");
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.material = lineMaterial;
            line.positionCount = 2;
            line.widthMultiplier = 0.5f;
            line.startColor = new Color(1f, 0.3f, 0.25f, 0.05f);
            line.endColor = new Color(1f, 0.3f, 0.25f, 0.45f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            lines.Add(line);
        }
        return lines[index];
    }

    // ---- icon shapes: white fill with a dark outline, so Image.color tints the fill only ----

    private enum Shape { Diamond, Triangle, Circle, Square, Bracket }

    private static Sprite MakeSprite(Shape shape)
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[size * size];
        Color fill = Color.white, edge = new Color(0.03f, 0.04f, 0.06f, 1f);
        const float stroke = 6f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f; // -1..1, v up
                float d = Distance(shape, u, v) * size / 2f; // signed, pixels: < 0 inside
                Color c = Color.clear;
                float inner = shape == Shape.Bracket ? 2.5f : stroke; // The bracket's band is thin: a slimmer outline.
                if (d <= -inner) c = fill;
                else if (d <= 0f) c = edge;
                pixels[y * size + x] = c;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // Signed distance (in -1..1 units) from the shape's edge: negative inside.
    private static float Distance(Shape shape, float u, float v)
    {
        switch (shape)
        {
            case Shape.Diamond: return (Mathf.Abs(u) + Mathf.Abs(v) - 0.95f) * 0.7071f;
            case Shape.Circle: return Mathf.Sqrt(u * u + v * v) - 0.9f;
            case Shape.Square: return Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) - 0.78f;
            case Shape.Triangle:
            {
                // Pointing up: inside the three half-planes.
                float a = -v - 0.75f;
                float b = (v * 0.5f + u * 0.866f) - 0.55f;
                float c = (v * 0.5f - u * 0.866f) - 0.55f;
                return Mathf.Max(a, Mathf.Max(b, c));
            }
            case Shape.Bracket:
            {
                // Four corner brackets of a square ring.
                float ring = Mathf.Abs(Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) - 0.8f) - 0.14f;
                bool corner = Mathf.Abs(u) > 0.35f && Mathf.Abs(v) > 0.35f;
                return corner ? ring : 1f;
            }
        }
        return 1f;
    }
}
