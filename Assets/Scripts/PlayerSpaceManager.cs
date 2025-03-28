using System.Collections.Generic;
using UnityEngine;

public class PlayerSpaceManager : MonoBehaviour
{
    private Camera cam;



    public LayerMask groundLayer;
    public LayerMask friendlyUnitLayer;
    public LayerMask enemyUnitLayer;

    public LayerMask uiLayer = 5;

    public Animator selectionAnim;

    public List<PlayerUnit> selectedUnits = new List<PlayerUnit>();

    private HardpointManager lastHighlight;

    void Start()
    {
        cam = Camera.main;
    }


    void Update()
    {
        ShipHeathHighlighter();
        CursorSelector();

        if (Input.GetMouseButtonDown(0))
        {
            RaycastHit hit;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out hit, Mathf.Infinity, uiLayer))
            {
                Debug.Log("UI HIT: " + hit.transform.gameObject + " On Layer " + hit.transform.gameObject.layer);
                TargetSelection(hit);
            }
            else if (Physics.Raycast(ray, out hit, Mathf.Infinity, enemyUnitLayer))
            {
                TargetSelection(hit);
            }
            else if (Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer))
            {
                PlayerSelection(hit);
            }
            else if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer) && selectedUnits.Count > 0) //Location selection
            {
                PlayerLocation(hit);
            }

        }
        else if (Input.GetMouseButtonDown(1)) // Clear selection
        {
            DeselectAllUnits();
        }
    }

    private void TargetSelection(RaycastHit hit)
    {
        if (hit.transform.gameObject.CompareTag("TargetUI"))
        {
            Debug.Log("UI TARGET");
            Transform targetTransform = hit.transform.parent.parent;
            foreach (PlayerUnit unit in selectedUnits)
            {
                unit.gameObject.GetComponent<HardpointManager>().AssignTarget(targetTransform);
                unit.agent.isStopped = true;
                unit.moveState.ClearDestinations();
                unit.moveState.MoveWithinRangeOfTarget(hit.point);
                unit.agent.isStopped = false;

                if ((Object)unit.stateMachine.currentState != unit.moveState)
                {
                    unit.stateMachine.SetState(unit.moveState);
                }
            }
        }
        else if (hit.collider.gameObject.CompareTag("Hardpoint"))
        {
            Debug.Log("MESH TARGET");
            foreach (PlayerUnit unit in selectedUnits)
            {
                unit.gameObject.GetComponent<HardpointManager>().AssignTarget(hit.collider.transform);
                unit.agent.isStopped = true;
                unit.moveState.ClearDestinations();
                unit.moveState.MoveWithinRangeOfTarget(hit.point);
                unit.agent.isStopped = false;

                if ((Object)unit.stateMachine.currentState != unit.moveState)
                {
                    unit.stateMachine.SetState(unit.moveState);
                }
            }
        }
        else
        {
            Debug.Log("RANDOM TARGET");
            foreach (PlayerUnit unit in selectedUnits)
            {
                unit.gameObject.GetComponent<HardpointManager>().AssignTarget(hit.collider.gameObject.GetComponentInParent<HardpointManager>().GetRandomHardpoint());
                unit.agent.isStopped = true;
                unit.moveState.ClearDestinations();
                unit.moveState.MoveWithinRangeOfTarget(hit.point);
                unit.agent.isStopped = false;
                if ((Object)unit.stateMachine.currentState != unit.moveState)
                {
                    unit.stateMachine.SetState(unit.moveState);
                }
            }
        }


    }

    private void PlayerSelection(RaycastHit hit)
    {
        PlayerUnit unit = hit.collider.GetComponentInParent<PlayerUnit>();
        if (unit == null) return;

        if (Input.GetKey(KeyCode.LeftShift)) //Multi selection
        {
            if (!selectedUnits.Contains(unit))
            {
                SelectUnit(unit);
                Debug.Log("Unit Selected");
            }
        }
        else //Single selection
        {
            DeselectAllUnits();
            SelectUnit(unit);
            unit.ToggleSelect(true);
            Debug.Log("Single Unit Selected");
        }
    }

    private void PlayerLocation(RaycastHit hit)
    {
        if (Input.GetKey(KeyCode.LeftShift)) //Multi selection
        {
            foreach (PlayerUnit unit in selectedUnits)
            {
                unit.moveState.AddDestination(hit.point);

                if ((Object)unit.stateMachine.currentState != unit.moveState)
                {
                    unit.stateMachine.SetState(unit.moveState);
                }
            }
            //selectionAnim.gameObject.SetActive(true);
            selectionAnim.gameObject.transform.localPosition = hit.point;
            //selectionAnim.gameObject.transform.position = new Vector3(0f, selectedUnits[0].agent.baseOffset, 0f) + hit.point;
            selectionAnim.Play("GroundMarker");
            Debug.Log("Location Selected");
        }
        else
        {
            foreach (PlayerUnit unit in selectedUnits) //Single location selection.
            {
                unit.agent.isStopped = true;
                unit.moveState.ClearDestinations();
                unit.moveState.AddDestination(hit.point);
                unit.agent.isStopped = false;
                if ((Object)unit.stateMachine.currentState != unit.moveState)
                {
                    unit.stateMachine.SetState(unit.moveState);
                }
            }
            selectionAnim.gameObject.transform.localPosition = hit.point;
            selectionAnim.Play("GroundMarker");
            Debug.Log("Location Selected");
        }
    }

    private void ShipHeathHighlighter()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;


        if (Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer))
        {
            Debug.Log("Friend");
            if (hit.collider.gameObject.transform.root.TryGetComponent(out HardpointManager manager))
            {
                Debug.Log("Turned on");
                lastHighlight = manager;
                manager.ToggleHighlight(true);
            }
        }
        else if (Physics.Raycast(ray, out hit, Mathf.Infinity, enemyUnitLayer))
        {
            Debug.Log("Foe");
            if (hit.collider.gameObject.transform.root.TryGetComponent(out HardpointManager manager))
            {
                Debug.Log("Turned on");
                lastHighlight = manager;
                manager.ToggleHighlight(true);
            }

        }
        else
        {
            Debug.Log("Else");
            if (lastHighlight != null && !lastHighlight.gameObject.GetComponent<PlayerUnit>().isSelected)
            {
                Debug.Log("Turned off");
                lastHighlight.ToggleHighlight(false);

                lastHighlight = null;
            }

        }
    }

    private void CursorSelector()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer))
        {
            CursorManager.Instance.SetMarkerType(CursorManager.CursorType.Selectable);
        }
        else if (Physics.Raycast(ray, out hit, Mathf.Infinity, enemyUnitLayer) && selectedUnits.Count > 0)
        {
            CursorManager.Instance.SetMarkerType(CursorManager.CursorType.Attackable);
        }
        else if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer) && selectedUnits.Count > 0)
        {
            CursorManager.Instance.SetMarkerType(CursorManager.CursorType.Walkable);
        }
        else
        {
            CursorManager.Instance.SetMarkerType(CursorManager.CursorType.None);
        }
    }

    public void DeselectAllUnits()
    {
        foreach (PlayerUnit unit in selectedUnits)
        {
            unit.gameObject.GetComponent<HardpointManager>().ToggleHighlight(false);
            unit.isSelected = false;
            unit.ToggleSelect(false);
        }
        selectedUnits.Clear();
    }
    public void DeselectUnit(PlayerUnit unit)
    {
        unit.gameObject.GetComponent<HardpointManager>().ToggleHighlight(false);
        unit.isSelected = false;
        unit.ToggleSelect(false);
        selectedUnits.Remove(unit);
    }

    public void SelectUnit(PlayerUnit unit)
    {
        unit.isSelected = true;
        selectedUnits.Add(unit);
        unit.ToggleSelect(true);
    }

    public void DragSelect(PlayerUnit unit)
    {
        if (!selectedUnits.Contains(unit))
        {
            unit.isSelected = true;
            unit.ToggleSelect(true);
            selectedUnits.Add(unit);
            Debug.Log("Added Unit");
        }
    }


}
