using SolarStudios;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSpaceManager : MonoBehaviour
{
    private Camera cam;



    public LayerMask groundLayer;
    public LayerMask friendlyUnitLayer;



    public List<PlayerUnit> selectedUnits = new List<PlayerUnit>();
    void Start()
    {
        cam = Camera.main;
    }


    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            RaycastHit hit;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer))
            {
                PlayerUnit unit = hit.collider.GetComponentInParent<PlayerUnit>();
                if (unit == null) return;

                if (Input.GetKey(KeyCode.LeftShift)) //Multi selection
                {
                    if (!selectedUnits.Contains(unit))
                    {
                        selectedUnits.Add(unit);
                        Debug.Log("Unit Selected");
                    }
                }
                else //Single selection
                {
                    selectedUnits.Clear();
                    selectedUnits.Add(unit);
                    Debug.Log("Single Unit Selected");
                }
            }
            else if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer) && selectedUnits.Count > 0) //Location selection
            {
                if (Input.GetKey(KeyCode.LeftShift)) //Multi selection
                {
                    foreach (PlayerUnit unit in selectedUnits)
                    {
                        unit.moveState.destinations.Add(hit.point);

                        unit.stateMachine.SetState(unit.GetComponentInChildren<PlayerUnitMoveState>());
                    }
                    Debug.Log("Location Selected");
                }
                else
                {
                    foreach (PlayerUnit unit in selectedUnits) //Single selection.
                    {
                        unit.agent.isStopped = true;
                        unit.moveState.destinations.Clear();
                        unit.moveState.destinations.Add(hit.point);
                        unit.agent.isStopped = false;
                        unit.stateMachine.SetState(unit.GetComponentInChildren<PlayerUnitMoveState>());
                    }
                    Debug.Log("Location Selected");
                }

            }
        }
        else if (Input.GetMouseButtonDown(1)) // Clear selection
        {
            selectedUnits.Clear();
        }
    }

    public void DragSelect(PlayerUnit unit)
    {
        if(!selectedUnits.Contains(unit))
        {
            selectedUnits.Add(unit);
            Debug.Log("Added Unit");
        }
    }

}
