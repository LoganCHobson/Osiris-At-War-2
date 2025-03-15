using UnityEngine;
using UnityEngine.EventSystems;

public class ShipIconDrag : MonoBehaviour, IDragHandler, IEndDragHandler, IBeginDragHandler
{
    private Vector2 lastPosition;
    private RectTransform rectTransform;
    private Canvas canvas;
    private IconDropZone currentDropZone;
    private MapManager mapManager;
    private void Awake()
    {
        mapManager = FindAnyObjectByType<MapManager>();
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
        lastPosition = rectTransform.anchoredPosition; // Save the initial position
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Move the dragged item based on the pointer position
        Vector2 localPointerPosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            canvas.worldCamera,
            out localPointerPosition);

        rectTransform.anchoredPosition = localPointerPosition;

        
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (currentDropZone != null)
        {
            // If dropped into a valid drop zone, snap to that zone
            if (eventData.pointerEnter != null && eventData.pointerEnter.CompareTag("DropZone"))
            {
                rectTransform.anchoredPosition = currentDropZone.GetDropPosition();
            }
        }
        else
        {
            // Otherwise, snap back to the last known position
            rectTransform.anchoredPosition = lastPosition;
        }
        mapManager.ToggleAllPlanetsFleetSlots();
    }

    public void SetCurrentDropZone(IconDropZone dropZone)
    {
        currentDropZone = dropZone;
    }

    public void ResetDropZone()
    {
        currentDropZone = null;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        mapManager.ToggleAllPlanetsFleetSlots();
    }
}
