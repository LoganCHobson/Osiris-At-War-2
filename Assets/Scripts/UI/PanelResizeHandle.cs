using UnityEngine;
using UnityEngine.EventSystems;

public class PanelResizeHandle : MonoBehaviour, IDragHandler, IEndDragHandler
{
    public ReinforcementPanel panel;

    private Canvas canvas;

    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnDrag(PointerEventData eventData)
    {
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        panel.Resize(eventData.delta.x / scale);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        panel.SaveSize();
    }
}
