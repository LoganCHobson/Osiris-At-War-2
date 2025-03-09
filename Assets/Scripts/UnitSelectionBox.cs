using UnityEngine;

public class UnitSelectionBox : MonoBehaviour
{
    Camera cam;


    public RectTransform boxVisual;

    Rect selectionBox;

    Vector2 startPosition;
    Vector2 endPosition;

    public PlayerSpaceManager spaceManager;
    private void Start()
    {
        cam = Camera.main;
        startPosition = Vector2.zero;
        endPosition = Vector2.zero;
        DrawVisual();
    }

    private void Update()
    {
        //Clicked
        if (Input.GetMouseButtonDown(0))
        {
            startPosition = Input.mousePosition;

            // For selection the Units
            selectionBox = new Rect();
        }

        //Dragging
        if (Input.GetMouseButton(0))
        {

            if (boxVisual.rect.width > 0.1 || boxVisual.rect.height > 0.1)
            {
                spaceManager.DeselectAllUnits();

                SelectUnits();
            }

            endPosition = Input.mousePosition;
            DrawVisual();
            DrawSelection();
        }

        //Releasing
        if (Input.GetMouseButtonUp(0))
        {
            SelectUnits();

            startPosition = Vector2.zero;
            endPosition = Vector2.zero;
            DrawVisual();
        }
    }

    void DrawVisual()
    {
        //Calculate the starting and ending positions of the selection box.
        Vector2 boxStart = startPosition;
        Vector2 boxEnd = endPosition;

        //Calculate the center of the selection box.
        Vector2 boxCenter = (boxStart + boxEnd) / 2;

        //Set the position of the visual selection box based on its center.
        boxVisual.position = boxCenter;

        //Calculate the size of the selection box in both width and height.
        Vector2 boxSize = new Vector2(Mathf.Abs(boxStart.x - boxEnd.x), Mathf.Abs(boxStart.y - boxEnd.y));

        //Set the size of the visual selection box based on its calculated size.
        boxVisual.sizeDelta = boxSize;
    }

    void DrawSelection()
    {
        if (Input.mousePosition.x < startPosition.x)
        {
            selectionBox.xMin = Input.mousePosition.x;
            selectionBox.xMax = startPosition.x;
        }
        else
        {
            selectionBox.xMin = startPosition.x;
            selectionBox.xMax = Input.mousePosition.x;
        }


        if (Input.mousePosition.y < startPosition.y)
        {
            selectionBox.yMin = Input.mousePosition.y;
            selectionBox.yMax = startPosition.y;
        }
        else
        {
            selectionBox.yMin = startPosition.y;
            selectionBox.yMax = Input.mousePosition.y;
        }
    }

    void SelectUnits()
    {
        foreach (PlayerUnit unit in GameManager.Instance.allFriendlyUnits)
        {
            if (selectionBox.Contains(cam.WorldToScreenPoint(unit.transform.position)))
            {
                spaceManager.DragSelect(unit);
            }
        }
    }
}