using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerSpaceManager : MonoBehaviour
{
    private Camera cam;



    public LayerMask groundLayer;
    public LayerMask friendlyUnitLayer;
    public LayerMask enemyUnitLayer;

    public LayerMask uiLayer = 5;

    public Animator selectionAnim;

    public List<SpaceUnit> selectedUnits = new List<SpaceUnit>();

    private HardpointManager lastHighlight;

    void Start()
    {
        cam = Camera.main;
    }


    void Update()
    {
        ShipHeathHighlighter();
        CursorSelector();

        if (Input.GetMouseButtonDown(0) && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            RaycastHit hit;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out hit, Mathf.Infinity, uiLayer))
            {
                if (selectedUnits.Count > 0)
                {
                    TargetSelection(hit);
                }
            }
            else if (Physics.Raycast(ray, out hit, Mathf.Infinity, enemyUnitLayer))
            {
                if (selectedUnits.Count > 0)
                {
                    TargetSelection(hit);
                }
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
            Transform targetTransform = hit.transform.parent.parent;
            foreach (SpaceUnit unit in selectedUnits)
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
            foreach (SpaceUnit unit in selectedUnits)
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
            foreach (SpaceUnit unit in selectedUnits)
            {
                HardpointManager targetManager = hit.collider.gameObject.GetComponentInParent<HardpointManager>();
                Transform randomHardpoint = targetManager != null ? targetManager.GetRandomHardpoint() : null;
                if (randomHardpoint == null) continue; // Target has no hardpoints left (or none at all) - nothing to attack there.

                unit.gameObject.GetComponent<HardpointManager>().AssignTarget(randomHardpoint);
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
        SpaceUnit unit = hit.collider.GetComponentInParent<SpaceUnit>();
        if (unit == null) return;

        if (Input.GetKey(KeyCode.LeftShift)) //Multi selection
        {
            if (!selectedUnits.Contains(unit))
            {
                SelectUnit(unit);
            }
        }
        else //Single selection
        {
            DeselectAllUnits();
            SelectUnit(unit);
            unit.ToggleSelect(true);
        }
    }

    private void PlayerLocation(RaycastHit hit)
    {
        if (Input.GetKey(KeyCode.LeftShift)) //Multi selection
        {
            foreach (SpaceUnit unit in selectedUnits)
            {
                unit.moveState.AddDestination(hit.point);

                if ((Object)unit.stateMachine.currentState != unit.moveState)
                {
                    unit.stateMachine.SetState(unit.moveState);
                }
            }
            selectionAnim.gameObject.transform.localPosition = hit.point;
            selectionAnim.Play("GroundMarker");
        }
        else
        {
            foreach (SpaceUnit unit in selectedUnits) //Single location selection.
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
        }
    }

    private void ShipHeathHighlighter()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;


        if (Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer))
        {

            if (hit.collider.gameObject.transform.root.TryGetComponent(out HardpointManager manager))
            {

                lastHighlight = manager;
                manager.ToggleHighlight(true);
            }
        }
        else if (Physics.Raycast(ray, out hit, Mathf.Infinity, enemyUnitLayer))
        {

            if (hit.collider.gameObject.transform.root.TryGetComponent(out HardpointManager manager))
            {

                lastHighlight = manager;
                manager.ToggleHighlight(true);
            }

        }
        else
        {
            if (lastHighlight != null && !lastHighlight.gameObject.GetComponent<SpaceUnit>().isSelected)
            {
                lastHighlight.ToggleHighlight(false);

                lastHighlight = null;
            }

        }
    }

    private void CursorSelector()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
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
        foreach (SpaceUnit unit in selectedUnits)
        {
            unit.gameObject.GetComponent<HardpointManager>().ToggleHighlight(false);
            unit.isSelected = false;
            unit.ToggleSelect(false);
        }
        selectedUnits.Clear();
    }
    public void DeselectUnit(SpaceUnit unit)
    {
        unit.gameObject.GetComponent<HardpointManager>().ToggleHighlight(false);
        unit.isSelected = false;
        unit.ToggleSelect(false);
        selectedUnits.Remove(unit);
    }

    public void SelectUnit(SpaceUnit unit)
    {
        unit.isSelected = true;
        selectedUnits.Add(unit);
        unit.ToggleSelect(true);
    }

    public void DragSelect(SpaceUnit unit)
    {
        if (!selectedUnits.Contains(unit))
        {
            unit.isSelected = true;
            unit.ToggleSelect(true);
            selectedUnits.Add(unit);
            //Debug.Log("Added Unit");
        }
    }


}
