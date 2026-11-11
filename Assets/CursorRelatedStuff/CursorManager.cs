using System.Collections.Generic;
using UnityEngine;

// The battle cursor. The real mouse pointer always stays visible (a hardware cursor is drawn over the game,
// so bloom and explosions can't wash it out) and changes shape with what's under it: a plain arrow, an arrow
// with a cyan ring over something selectable, a green ring for a move, a red crosshair for an attack, a grey
// "no" sign where an order can't go. Every shape has a dark outline. Its size is a setting (Battle Cursor Size).
//
// The marker on the battle plane (the prefabs below) stays as a depth cue: it shows where the pointer meets
// the plane, and when the pointer is over a ship a thin line runs from the plane up (or down) to it.
public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); // Menus and the map get the normal pointer back.
        }
    }

    private GameObject markerInstance;  // The plane marker for the current state
    bool isMarkerActive = false;
    float markerHeight = 0.0f;  // The battle plane

    [Header("Plane markers (depth cue under the pointer)")]
    public GameObject walkableCursor;
    public GameObject selectableCursor;
    public GameObject attackableCursor;
    public GameObject unAvailableCursor;

    [Header("Pointer")]
    public Color outline = new Color(0.02f, 0.03f, 0.05f, 1f);
    public Color arrowFill = Color.white;
    public Color selectColor = new Color(0.35f, 0.85f, 1f);
    public Color moveColor = new Color(0.4f, 1f, 0.5f);
    public Color attackColor = new Color(1f, 0.25f, 0.2f);
    public Color blockedColor = new Color(0.6f, 0.6f, 0.65f);

    CursorType currentCursor;
    private readonly Dictionary<(CursorType, int), (Texture2D texture, Vector2 hotspot)> cursors = new Dictionary<(CursorType, int), (Texture2D, Vector2)>();
    private LineRenderer heightLine;

    public enum CursorType
    {
        None,
        Walkable,
        UnAvailable,
        Selectable,
        Attackable
    }

    private void Start() => ReapplyCursor();

    void Update()
    {
        Cursor.visible = true;

        // Menus and UI get the plain pointer and no plane marker.
        bool overInterface = UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        if (GameSpeed.MenuPaused || SettingsPanel.IsOpen || overInterface)
        {
            if (markerInstance != null) markerInstance.SetActive(false);
            ShowHeightLine(false, Vector3.zero, Vector3.zero);
            Apply(CursorType.None);
            return;
        }
        Apply(currentCursor);

        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, markerHeight, 0));

        if (isMarkerActive && markerInstance != null && groundPlane.Raycast(ray, out float rayDistance))
        {
            markerInstance.SetActive(true);
            markerInstance.transform.position = ray.GetPoint(rayDistance);
        }
        else if (markerInstance != null)
        {
            markerInstance.SetActive(false);
        }

        // Height line: from the plane straight up/down to the ship under the pointer.
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity) && hit.collider.GetComponentInParent<UnitHealthManager>() is UnitHealthManager unit && unit != null)
        {
            Vector3 top = unit.transform.position;
            Vector3 bottom = new Vector3(top.x, markerHeight, top.z);
            ShowHeightLine(Mathf.Abs(top.y - markerHeight) > 1f, bottom, top);
        }
        else
        {
            ShowHeightLine(false, Vector3.zero, Vector3.zero);
        }
    }

    public void SetMarkerType(CursorType type)
    {
        if (type == currentCursor) return;

        currentCursor = type;
        markerInstance?.SetActive(false);
        isMarkerActive = type != CursorType.None;
        markerInstance = type switch
        {
            CursorType.Walkable => walkableCursor,
            CursorType.Selectable => selectableCursor,
            CursorType.Attackable => attackableCursor,
            CursorType.UnAvailable => unAvailableCursor,
            _ => null
        };
        Apply(type);
    }

    // Put the current pointer back (after the camera's edge-scroll arrows, or a size change in Settings).
    public void ReapplyCursor()
    {
        applied = (CursorType)(-1);
        Apply(currentCursor);
    }

    private CursorType applied = (CursorType)(-1);
    private int appliedSize = -1;

    private void Apply(CursorType type)
    {
        int size = GameSettings.CursorSize;
        if (type == applied && size == appliedSize) return;
        applied = type;
        appliedSize = size;
        (Texture2D texture, Vector2 hotspot) = Pointer(type, size);
        Cursor.SetCursor(texture, hotspot, CursorMode.Auto);
    }

    private void ShowHeightLine(bool show, Vector3 from, Vector3 to)
    {
        if (heightLine == null)
        {
            if (!show) return;
            GameObject go = new GameObject("CursorHeightLine");
            go.transform.SetParent(transform, false);
            heightLine = go.AddComponent<LineRenderer>();
            heightLine.material = new Material(Shader.Find("Sprites/Default"));
            heightLine.positionCount = 2;
            heightLine.widthMultiplier = 0.6f;
            heightLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            heightLine.receiveShadows = false;
            heightLine.startColor = new Color(1f, 1f, 1f, 0.15f);
            heightLine.endColor = new Color(1f, 1f, 1f, 0.7f);
        }
        heightLine.enabled = show;
        if (!show) return;
        heightLine.SetPosition(0, from);
        heightLine.SetPosition(1, to);
    }

    // ---------------- Drawing the pointers ----------------

    private (Texture2D, Vector2) Pointer(CursorType type, int sizeSetting)
    {
        if (cursors.TryGetValue((type, sizeSetting), out (Texture2D, Vector2) made)) return made;

        int size = sizeSetting == 0 ? 32 : sizeSetting == 1 ? 48 : 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[size * size];
        float stroke = Mathf.Max(1.5f, size / 20f);
        Vector2 hotspot;

        if (type == CursorType.Attackable)
        {
            hotspot = new Vector2(size / 2f, size / 2f);
            DrawCrosshair(pixels, size, stroke, attackColor);
        }
        else if (type == CursorType.UnAvailable)
        {
            hotspot = new Vector2(size / 2f, size / 2f);
            DrawNoSign(pixels, size, stroke, blockedColor);
        }
        else
        {
            hotspot = new Vector2(1f, 1f);
            DrawArrow(pixels, size, stroke);
            if (type == CursorType.Selectable) DrawBadgeRing(pixels, size, stroke, selectColor);
            if (type == CursorType.Walkable) DrawBadgeRing(pixels, size, stroke, moveColor);
        }

        texture.SetPixels(pixels);
        texture.Apply();
        cursors[(type, sizeSetting)] = (texture, hotspot);
        return (texture, hotspot);
    }

    // Texture rows run bottom-up; these helpers work in top-down screen coordinates (y down) and flip.
    private static void Plot(Color[] pixels, int size, int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= size || y >= size) return;
        int index = (size - 1 - y) * size + x;
        Color under = pixels[index];
        float a = color.a + under.a * (1f - color.a);
        pixels[index] = a <= 0f ? Color.clear
            : new Color((color.r * color.a + under.r * under.a * (1f - color.a)) / a,
                        (color.g * color.a + under.g * under.a * (1f - color.a)) / a,
                        (color.b * color.a + under.b * under.a * (1f - color.a)) / a, a);
    }

    // The classic pointer: a white arrow with a dark outline, tip at the top-left (the hotspot).
    private void DrawArrow(Color[] pixels, int size, float stroke)
    {
        float s = size * 0.62f;
        Vector2[] shape =
        {
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.27f, 0.76f), new Vector2(0.45f, 1.08f),
            new Vector2(0.6f, 1.0f), new Vector2(0.43f, 0.69f), new Vector2(0.75f, 0.69f)
        };
        for (int i = 0; i < shape.Length; i++) shape[i] = shape[i] * s + new Vector2(1.5f, 1.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                bool inside = Inside(shape, p);
                float edge = EdgeDistance(shape, p);
                if (inside && edge > stroke) Plot(pixels, size, x, y, arrowFill);
                else if (inside || edge <= stroke * 0.8f) Plot(pixels, size, x, y, outline);
            }
        }
    }

    // A small coloured ring at the lower right of the arrow: what clicking will do.
    private void DrawBadgeRing(Color[] pixels, int size, float stroke, Color color)
    {
        Vector2 centre = new Vector2(size * 0.72f, size * 0.72f);
        float radius = size * 0.2f;
        Ring(pixels, size, centre, radius, stroke * 2.2f, outline);
        Ring(pixels, size, centre, radius, stroke, color);
    }

    private void DrawCrosshair(Color[] pixels, int size, float stroke, Color color)
    {
        Vector2 centre = new Vector2(size / 2f, size / 2f);
        float radius = size * 0.3f;
        Ring(pixels, size, centre, radius, stroke * 2.2f, outline);
        Ring(pixels, size, centre, radius, stroke, color);
        float gap = size * 0.12f, reach = size * 0.48f;
        foreach (Vector2 dir in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
        {
            Segment(pixels, size, centre + dir * gap, centre + dir * reach, stroke * 2.2f, outline);
            Segment(pixels, size, centre + dir * gap, centre + dir * reach, stroke, color);
        }
        Ring(pixels, size, centre, stroke * 0.6f, stroke * 1.6f, outline);
        Ring(pixels, size, centre, 0.1f, stroke * 1.1f, color);
    }

    private void DrawNoSign(Color[] pixels, int size, float stroke, Color color)
    {
        Vector2 centre = new Vector2(size / 2f, size / 2f);
        float radius = size * 0.32f;
        Ring(pixels, size, centre, radius, stroke * 2.4f, outline);
        Ring(pixels, size, centre, radius, stroke * 1.2f, color);
        Vector2 diagonal = new Vector2(1f, 1f).normalized * radius;
        Segment(pixels, size, centre - diagonal, centre + diagonal, stroke * 2.4f, outline);
        Segment(pixels, size, centre - diagonal, centre + diagonal, stroke * 1.2f, color);
    }

    private static void Ring(Color[] pixels, int size, Vector2 centre, float radius, float width, Color color)
    {
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Abs(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre) - radius);
                if (d <= width * 0.5f) Plot(pixels, size, x, y, color);
            }
    }

    private static void Segment(Color[] pixels, int size, Vector2 a, Vector2 b, float width, Color color)
    {
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (DistanceToSegment(new Vector2(x + 0.5f, y + 0.5f), a, b) <= width * 0.5f) Plot(pixels, size, x, y, color);
            }
    }

    private static bool Inside(Vector2[] polygon, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if ((polygon[i].y > p.y) != (polygon[j].y > p.y)
                && p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                inside = !inside;
        }
        return inside;
    }

    private static float EdgeDistance(Vector2[] polygon, Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++) best = Mathf.Min(best, DistanceToSegment(p, polygon[j], polygon[i]));
        return best;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }
}
