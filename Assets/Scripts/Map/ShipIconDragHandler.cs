using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A ship (or a stack of identical ships) in the fleet breakdown. Drag to move one ship, Shift+drag to move
// the whole stack. The lead slot is the ship that deploys first when the fleet attacks.
public class ShipIconDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Image iconImage;

    [Header("Markers")]
    public Color leadColor = new Color(1f, 0.78f, 0.25f);
    public Color dropHighlightColor = new Color(0.55f, 0.9f, 1f);
    public Color invalidDropColor = new Color(1f, 0.35f, 0.3f);
    public float leadOutlineWidth = 4f;

    public Ship Ship { get; private set; }
    public FleetRowUI SourceRow { get; private set; }
    public bool IsLead { get; private set; }
    public int Count => indices.Count;
    public int FirstIndex => indices.Count > 0 ? indices[0] : -1;

    private readonly List<int> indices = new List<int>();
    private RectTransform rect;
    private Canvas canvas;
    private Transform originalParent;
    private Vector3 originalPosition;
    private int originalSiblingIndex;
    private Color baseColor = Color.white;

    public void Bind(Ship ship, FleetRowUI sourceRow, List<int> rosterIndices, bool isLead)
    {
        Ship = ship;
        SourceRow = sourceRow;
        IsLead = isLead;
        indices.Clear();
        indices.AddRange(rosterIndices);

        if (iconImage != null)
        {
            Sprite sprite = ship.IconSprite;
            if (sprite != null)
            {
                iconImage.sprite = sprite;
            }
            baseColor = iconImage.color;
        }

        BuildMarkers();
    }

    // The roster positions a drag should move: the whole stack, or just one ship from the back of it.
    public List<int> TakeIndices(bool wholeStack)
    {
        return wholeStack ? new List<int>(indices) : new List<int> { indices[indices.Count - 1] };
    }

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    private void BuildMarkers()
    {
        if (Count > 1)
        {
            // A dark plate behind the count keeps it readable over any ship art. (No TMP outline: the label
            // can be built while the panel is still inactive, before TMP has a material to instance.)
            GameObject plate = new GameObject("StackCount", typeof(RectTransform), typeof(Image));
            plate.transform.SetParent(transform, false);
            RectTransform plateRect = (RectTransform)plate.transform;
            plateRect.anchorMin = plateRect.anchorMax = plateRect.pivot = new Vector2(1f, 0f);
            plateRect.anchoredPosition = new Vector2(-2f, 2f);
            plateRect.sizeDelta = new Vector2(38f, 24f);
            Image plateImage = plate.GetComponent<Image>();
            plateImage.color = new Color(0f, 0f, 0f, 0.7f);
            plateImage.raycastTarget = false;

            TMP_Text countLabel = CreateLabel("Count", TextAlignmentOptions.Center, 18f, plate.transform);
            countLabel.text = $"<b>x{Count}</b>";
            countLabel.color = Color.white;
        }

        if (!IsLead) return;

        TMP_Text leadLabel = CreateLabel("LeadTag", TextAlignmentOptions.TopLeft, 16f);
        leadLabel.text = "<b>LEAD</b>";
        leadLabel.color = leadColor;

        if (iconImage != null)
        {
            Outline leadOutline = iconImage.gameObject.AddComponent<Outline>();
            leadOutline.effectColor = leadColor;
            leadOutline.effectDistance = new Vector2(leadOutlineWidth, -leadOutlineWidth);
        }
    }

    private TMP_Text CreateLabel(string objectName, TextAlignmentOptions alignment, float size, Transform parent = null)
    {
        GameObject labelObject = new GameObject(objectName, typeof(RectTransform));
        labelObject.transform.SetParent(parent != null ? parent : transform, false);

        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        Vector2 inset = parent != null ? Vector2.zero : new Vector2(4f, 2f);
        labelRect.offsetMin = inset;
        labelRect.offsetMax = -inset;

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = size;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        originalParent = rect.parent;
        originalPosition = rect.position;
        originalSiblingIndex = rect.GetSiblingIndex();
        rect.SetParent(canvas.transform, true);
        rect.SetAsLastSibling();

        if (iconImage != null)
        {
            iconImage.raycastTarget = false;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        rect.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (this == null || originalParent == null) return; // The drop refreshed the panel and this icon is gone.

        rect.SetParent(originalParent, true);
        rect.SetSiblingIndex(originalSiblingIndex);
        rect.position = originalPosition;

        if (iconImage != null)
        {
            iconImage.raycastTarget = true;
        }
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
        SetHighlight(allowed ? dropHighlightColor : invalidDropColor);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetHighlight(null);
    }

    private void SetHighlight(Color? color)
    {
        if (iconImage == null) return;
        iconImage.color = color ?? baseColor;
    }
}
