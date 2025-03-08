using SolarStudios;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public class PlayerUnit : MonoBehaviour
{
    [HideInInspector]
    public NavMeshAgent agent;
    [HideInInspector]
    public PlayerUnitMoveState moveState;
    [HideInInspector]
    public PlayerUnitStateMachine stateMachine;

    public UnityEvent onSelected;
    public UnityEvent onDeSelected;

    public bool isSelected = false;


    void Start()
    {
        GameManager.Instance.allFriendlyUnits.Add(this);
        agent = GetComponent<NavMeshAgent>();
        moveState = GetComponentInChildren<PlayerUnitMoveState>();
        stateMachine = GetComponentInChildren<PlayerUnitStateMachine>();
    }

    public void ToggleSelect(bool isSelected)
    {
        if (isSelected)
        {
            onSelected.Invoke();
        }
        else
        {
            onDeSelected.Invoke();
        }
    }



}
