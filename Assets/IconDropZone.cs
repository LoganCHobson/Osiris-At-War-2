using UnityEngine;
using UnityEngine.EventSystems;

public class IconDropZone : MonoBehaviour, IDropHandler
{
    private ShipIconDrag currentItem;


    private void Start()
    {
        if(gameObject.transform.childCount > 0 && gameObject.transform.GetChild(0).TryGetComponent<ShipIconDrag>(out ShipIconDrag item))
        {
            gameObject.SetActive(true);
            currentItem = item;
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        ShipIconDrag draggableItem = eventData.pointerDrag.GetComponent<ShipIconDrag>();

        if (draggableItem != null)
        {
            if (currentItem != null)
            {
                currentItem.ResetDropZone();
                currentItem.transform.position = currentItem.GetComponent<RectTransform>().anchoredPosition;
            }

            

            currentItem = draggableItem;
            currentItem.SetCurrentDropZone(this);
            currentItem.transform.position = transform.position; 
        }
    }

    public Vector2 GetDropPosition()
    {
        return transform.position; 
    }

}
