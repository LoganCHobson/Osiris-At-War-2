using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class IconShipRef : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Ship ship;

    private RectTransform rect;
    private Canvas canvas;
    private Image iconImage;
    private Transform originalParent;
    private Vector3 originalPosition;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
        iconImage = GetComponentInChildren<Image>();
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

        PlacementManager.Instance?.BeginPreview(ship);
    }

    public void OnDrag(PointerEventData eventData)
    {
        rect.position = eventData.position;
        PlacementManager.Instance?.UpdatePreview(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (iconImage != null)
        {
            iconImage.raycastTarget = true;
        }

        if (PlacementManager.Instance != null && PlacementManager.Instance.TryPlace(ship, eventData.position, gameObject))
        {
            return; // Placed successfully - PlacementManager destroyed this icon.
        }

        // Didn't land anywhere valid - snap back to the reinforcement list.
        rect.SetParent(originalParent, true);
        rect.position = originalPosition;
    }
}
