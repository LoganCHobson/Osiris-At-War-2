using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A ship (or a stack of identical ships) in the fleet breakdown. Drag to move one ship, Shift+drag to move
// the whole stack. The lead slot is the ship that deploys first when the fleet attacks.
public class ShipIconDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    public const float Width = 96f;
    public const float Height = 92f;

    private static readonly Color Idle = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color LeadIdle = new Color(1f, 0.74f, 0.32f, 0.1f);
    private static readonly Color DropHighlight = new Color(0.55f, 0.9f, 1f, 0.3f);
    private static readonly Color InvalidDrop = new Color(1f, 0.35f, 0.3f, 0.3f);

    public Ship Ship { get; private set; }
    public FleetRowUI SourceRow { get; private set; }
    public bool IsLead { get; private set; }
    public int Count => indices.Count;
    public int FirstIndex => indices.Count > 0 ? indices[0] : -1;

    private readonly List<int> indices = new List<int>();
    private RectTransform rect;
    private Canvas canvas;
    private Image background;
    private Transform originalParent;
    private Vector3 originalPosition;
    private int originalSiblingIndex;

    public static ShipIconDragHandler Create(Transform parent, Ship ship, FleetRowUI sourceRow, List<int> rosterIndices, bool isLead)
    {
        RectTransform tile = UIKit.Rect(ship.name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(Width, Height));
        ShipIconDragHandler handler = tile.gameObject.AddComponent<ShipIconDragHandler>();
        handler.Bind(ship, sourceRow, rosterIndices, isLead);
        return handler;
    }

    public void Bind(Ship ship, FleetRowUI sourceRow, List<int> rosterIndices, bool isLead)
    {
        Ship = ship;
        SourceRow = sourceRow;
        IsLead = isLead;
        indices.Clear();
        indices.AddRange(rosterIndices);
        rect = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>().rootCanvas;
        Build();
    }

    // The roster positions a drag should move: the whole stack, or just one ship from the back of it.
    public List<int> TakeIndices(bool wholeStack)
    {
        return wholeStack ? new List<int>(indices) : new List<int> { indices[indices.Count - 1] };
    }

    private void Build()
    {
        background = UIKit.Image(rect, IsLead ? LeadIdle : Idle, true);

        RectTransform iconRect = UIKit.Rect("Icon", rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(-16f, 56f));
        Image icon = UIKit.Image(iconRect, Color.white);
        icon.sprite = Ship.IconSprite;
        icon.preserveAspect = true;
        icon.enabled = icon.sprite != null;

        TMP_Text name = UIKit.Text(rect, ShipInfo.Name(Ship), 13f, UIKit.TextColor, TextAlignmentOptions.Bottom);
        name.rectTransform.offsetMin = new Vector2(4f, 6f);
        name.rectTransform.offsetMax = new Vector2(-4f, 0f);
        name.overflowMode = TextOverflowModes.Ellipsis;

        if (Count > 1)
        {
            RectTransform badge = UIKit.Rect("Count", rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -4f), new Vector2(34f, 22f));
            UIKit.Image(badge, new Color(0f, 0f, 0f, 0.65f));
            TMP_Text count = UIKit.Text(badge, $"×{Count}", 15f, UIKit.TextColor, TextAlignmentOptions.Center);
            count.fontStyle = FontStyles.Bold;
        }

        if (!IsLead) return;

        // Amber frame and tag: this ship leads the fleet into battle.
        Frame(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 2f));
        Frame(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 2f));
        Frame(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(2f, 0f));
        Frame(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(2f, 0f));
        TMP_Text tag = UIKit.Text(rect, "LEAD", 12f, UIKit.Accent, TextAlignmentOptions.TopLeft);
        tag.fontStyle = FontStyles.Bold;
        tag.characterSpacing = 4f;
        tag.rectTransform.offsetMin = new Vector2(6f, 0f);
        tag.rectTransform.offsetMax = new Vector2(0f, -4f);
    }

    private void Frame(Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
    {
        RectTransform edge = UIKit.Rect("Frame", rect, anchorMin, anchorMax, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        UIKit.Image(edge, UIKit.Accent);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        originalParent = rect.parent;
        originalPosition = rect.position;
        originalSiblingIndex = rect.GetSiblingIndex();
        rect.SetParent(canvas.transform, true);
        rect.SetAsLastSibling();
        background.raycastTarget = false; // Let the drop land on whatever is under the pointer.
    }

    public void OnDrag(PointerEventData eventData)
    {
        rect.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (this == null) return;
        if (originalParent == null) // The drop rebuilt the panel; this dragged copy is left over.
        {
            Destroy(gameObject);
            return;
        }

        rect.SetParent(originalParent, true);
        rect.SetSiblingIndex(originalSiblingIndex);
        rect.position = originalPosition;
        background.raycastTarget = true;
    }

    public void OnDrop(PointerEventData eventData)
    {
        SetHighlight(null);

        ShipIconDragHandler dragged = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<ShipIconDragHandler>() : null;
        if (dragged == null || dragged == this || SourceRow == null) return;

        SourceRow.PlaceShips(dragged, this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!eventData.dragging || eventData.pointerDrag == null) return;

        ShipIconDragHandler dragged = eventData.pointerDrag.GetComponent<ShipIconDragHandler>();
        if (dragged == null || dragged == this) return;

        bool allowed = !IsLead || dragged.Ship.CanLead;
        SetHighlight(allowed ? DropHighlight : InvalidDrop);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetHighlight(null);
    }

    private void SetHighlight(Color? color)
    {
        if (background == null) return;
        background.color = color ?? (IsLead ? LeadIdle : Idle);
    }
}
