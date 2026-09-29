using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ShipIconDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Image iconImage;

    public Ship Ship { get; private set; }
    public FleetRowUI SourceRow { get; private set; }

    private RectTransform rect;
    private Canvas canvas;
    private Transform originalParent;
    private Vector3 originalPosition;

    public void Bind(Ship ship, FleetRowUI sourceRow)
    {
        Ship = ship;
        SourceRow = sourceRow;

        if (iconImage == null) return;

        Sprite sprite = ship.IconSprite;
        if (sprite != null)
        {
            iconImage.sprite = sprite;
        }
    }

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        originalParent = rect.parent;
        originalPosition = rect.position;
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
        rect.SetParent(originalParent, true);
        rect.position = originalPosition;

        if (iconImage != null)
        {
            iconImage.raycastTarget = true;
        }
    }
}
