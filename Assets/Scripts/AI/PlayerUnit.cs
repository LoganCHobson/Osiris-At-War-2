using SolarStudios;
using UnityEngine;
using UnityEngine.AI;

public class PlayerUnit : MonoBehaviour
{
    [HideInInspector]
    public NavMeshAgent agent;
    [HideInInspector]
    public PlayerUnitMoveState moveState;
    [HideInInspector]
    public PlayerUnitStateMachine stateMachine;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();   
        moveState = GetComponentInChildren<PlayerUnitMoveState>();
        stateMachine = GetComponentInChildren<PlayerUnitStateMachine>();
    }

   
}
