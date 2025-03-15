using UnityEngine;
using UnityEngine.EventSystems;

public class DragMap : MonoBehaviour, IDragHandler, IScrollHandler
{
    private RectTransform rectTransform;
    public float dragSpeed = 1f;
    public float zoomSpeed = 0.1f;
    public float minScale = 0.5f;
    public float maxScale = 2f;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Move the map
        rectTransform.anchoredPosition += eventData.delta * dragSpeed;
    }

    public void OnScroll(PointerEventData eventData)
    {
        // Zoom in and out
        float scaleFactor = 1 + eventData.scrollDelta.y * zoomSpeed;
        float newScale = Mathf.Clamp(rectTransform.localScale.x * scaleFactor, minScale, maxScale);
        rectTransform.localScale = new Vector3(newScale, newScale, 1);
    }
}
