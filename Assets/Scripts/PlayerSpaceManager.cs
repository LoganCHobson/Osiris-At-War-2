using System.Collections.Generic;
using UnityEngine;

public class PlayerSpaceManager : MonoBehaviour
{
    private Camera cam;



    public LayerMask groundLayer;
    public LayerMask friendlyUnitLayer;
    public LayerMask enemyUnitLayer;

    public Animator selectionAnim;

    public List<PlayerUnit> selectedUnits = new List<PlayerUnit>();
    void Start()
    {
        cam = Camera.main;
    }


    void Update()
    {
        CursorSelector();

        if (Input.GetMouseButtonDown(0))
        {
            RaycastHit hit;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out hit, Mathf.Infinity, enemyUnitLayer))
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
            foreach (PlayerUnit unit in selectedUnits)
            {
                unit.ToggleSelect(false);
            }
            selectedUnits.Clear();
        }
    }

    private void TargetSelection(RaycastHit hit)
    {
        if (hit.collider.gameObject.CompareTag("Hardpoint"))
        {
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
            selectionAnim.gameObject.transform.position = new Vector3(0f, selectedUnits[0].agent.baseOffset, 0f) + hit.point;
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
        foreach (PlayerUnit playerUnit in selectedUnits)
        {
            playerUnit.ToggleSelect(false);
        }
        selectedUnits.Clear();
    }
    public void DeselectUnit(PlayerUnit unit)
    {
        unit.ToggleSelect(false);
        selectedUnits.Remove(unit);
    }

    public void SelectUnit(PlayerUnit unit)
    {
        selectedUnits.Add(unit);
        unit.ToggleSelect(true);
    }

    public void DragSelect(PlayerUnit unit)
    {
        if (!selectedUnits.Contains(unit))
        {
            unit.ToggleSelect(true);
            selectedUnits.Add(unit);
            Debug.Log("Added Unit");
        }
    }


}
