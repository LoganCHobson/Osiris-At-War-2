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

        if (iconImage == null || ship.icon == null) return;

        Image sourceImage = ship.icon.GetComponentInChildren<Image>();
        if (sourceImage != null && sourceImage.sprite != null)
        {
            iconImage.sprite = sourceImage.sprite;
            return;
        }

        RawImage sourceRawImage = ship.icon.GetComponentInChildren<RawImage>();
        if (sourceRawImage != null && sourceRawImage.texture is Texture2D texture2D)
        {
            iconImage.sprite = Sprite.Create(texture2D, new Rect(0, 0, texture2D.width, texture2D.height), new Vector2(0.5f, 0.5f));
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
