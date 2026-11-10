using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One entry in the battle's reinforcement list. Ships of the same class share a single icon with an "xN"
// badge; dragging it onto the battlefield deploys one (hold Shift when dropping to deploy as many as the
// population cap allows). Hovering the icon slides in the ship's info blade so you know what it is.
public class IconShipRef : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    // The ship the next drag deploys (the top of the stack). Kept as a field for older callers.
    public Ship ship;

    private readonly List<ShipEntry> stack = new List<ShipEntry>();

    private RectTransform rect;
    private Canvas canvas;
    private Image iconImage;
    private Transform originalParent;
    private int originalSibling;
    private Vector3 originalPosition;
    private TMP_Text badge;
    private bool hovering;

    public int Count => stack.Count;
    public IReadOnlyList<ShipEntry> Ships => stack;
    public ShipEntry Top => stack.Count > 0 ? stack[0] : null;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
        iconImage = GetComponentInChildren<Image>();
    }

    private void Start()
    {
        if (stack.Count == 0 && ship != null) stack.Add(ShipEntry.New(ship));
        PlacementManager.Instance?.Prewarm(ship); // Build its placement hologram before the first drag.
        RefreshBadge();
    }

    // Same class = same prefab.
    // Same class, and neither is a hero (a hero's flagship always gets its own icon).
    public bool Accepts(ShipEntry other)
    {
        ShipEntry top = Top;
        return other != null && top != null && !other.IsHero && !top.IsHero && other.prefab == top.prefab;
    }

    public void Add(ShipEntry added)
    {
        if (added == null) return;
        stack.Add(added);
        ship = stack[0].type;
        RefreshBadge();
    }

    // Called by PlacementManager when one ship from this stack has been deployed.
    public void Consume(ShipEntry deployed)
    {
        if (!stack.Remove(deployed) && stack.Count > 0) stack.RemoveAt(0);
        if (stack.Count == 0)
        {
            if (hovering) ShipBlade.Hide();
            Destroy(gameObject);
            return;
        }

        ship = stack[0].type;
        RefreshBadge();
    }

    private void RefreshBadge()
    {
        if (rect == null) return;

        if (badge == null)
        {
            RectTransform holder = UIKit.Rect("CountBadge", rect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-2f, 2f), new Vector2(48f, 22f));
            UIKit.Image(holder, new Color(0f, 0f, 0f, 0.65f));
            badge = UIKit.Text(holder, "", 16f, UIKit.Accent, TextAlignmentOptions.Center);
            badge.fontStyle = FontStyles.Bold;
            badge.raycastTarget = false;
        }

        badge.transform.parent.gameObject.SetActive(stack.Count > 1);
        badge.text = $"x{stack.Count}";
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (eventData.dragging || ship == null) return;
        hovering = true;
        ShipBlade.Show(ship);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!hovering) return;
        hovering = false;
        ShipBlade.Hide();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        originalParent = rect.parent;
        originalSibling = rect.GetSiblingIndex();
        originalPosition = rect.position;
        rect.SetParent(canvas.transform, true);
        rect.SetAsLastSibling();

        if (iconImage != null)
        {
            iconImage.raycastTarget = false;
        }

        PlacementManager.Instance?.BeginPreview(ship);
    }

    public void OnDrag(PointerEventData eventData)
    {
        rect.position = eventData.position;
        PlacementManager.Instance?.UpdatePreview(eventData.position);
        ShowHeightLabel(true);
    }

    private TMP_Text heightLabel;

    // "+40" / "-25" over the dragged icon: the height it will drop in at (mouse wheel to change).
    private void ShowHeightLabel(bool visible)
    {
        if (visible && heightLabel == null)
        {
            RectTransform holder = UIKit.Rect("DropHeight", rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(110f, 24f));
            UIKit.Image(holder, new Color(0f, 0f, 0f, 0.7f));
            heightLabel = UIKit.Text(holder, "", 15f, new Color(0.45f, 0.9f, 1f), TextAlignmentOptions.Center);
            heightLabel.raycastTarget = false;
        }
        if (heightLabel == null) return;

        heightLabel.transform.parent.gameObject.SetActive(visible);
        float height = PlacementManager.DropHeight;
        heightLabel.text = Mathf.Abs(height) < 1f ? "PLANE  (wheel: height)" : $"HEIGHT {height:+0;-0}";
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (iconImage != null)
        {
            iconImage.raycastTarget = true;
        }
        ShowHeightLabel(false);

        bool all = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        PlacementManager placement = PlacementManager.Instance;
        bool placed = placement != null && (all
            ? placement.TryPlaceStack(this, eventData.position) > 0
            : placement.TryPlace(Top, eventData.position, gameObject));

        if (placed && stack.Count == 0) return; // Whole stack deployed - the icon is gone.

        // Ships left (or it didn't land anywhere valid) - back into the reinforcement list where it was.
        rect.SetParent(originalParent, true);
        rect.SetSiblingIndex(originalSibling);
        rect.position = originalPosition;
    }
}
