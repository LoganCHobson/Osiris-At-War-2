using SolarStudios;
using UnityEngine;
using UnityEngine.AI;

public class PlayerSpaceManager : MonoBehaviour
{
    private Camera cam;

    public NavMeshAgent agent;

    public LayerMask groundLayer;
    public LayerMask friendlyUnitLayer;

    private PlayerUnitMoveState moveState;
    private PlayerUnitStateMachine stateMachine;
    void Start()
    {
        cam = Camera.main;
    }


    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            RaycastHit hit;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer))
            {
                agent = hit.collider.GetComponentInParent<NavMeshAgent>();
                moveState = agent.GetComponentInChildren<PlayerUnitMoveState>();
                stateMachine = agent.GetComponentInChildren<PlayerUnitStateMachine>();
                Debug.Log("Unit Selected");
            }
            else if (agent != null && Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
            {
                moveState.destinations.Add(hit.point);
                stateMachine.SetState(agent.GetComponentInChildren<PlayerUnitMoveState>());
                Debug.Log("Location selected");
            }


        }

        if (Input.GetMouseButton(1))
        {
            agent = null;
        }
    }
}
