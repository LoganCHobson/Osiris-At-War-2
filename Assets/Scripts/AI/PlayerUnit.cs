using SolarStudios;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using static UnityEngine.GraphicsBuffer;

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

    public bool testing = false;

    public float maxRange;
    void Start()
    {
        GetMaxRange();

        if(gameObject.layer == 7 || testing == true)
        {
            GameManager.Instance.allFriendlyUnits.Add(this);
        }
        
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

    public void GetMaxRange()
    {
        float largestRange = 0f;
        HardpointManager hardpointManager = GetComponent<HardpointManager>();
        List<TurretController> turrets = hardpointManager.GetSpecificHardpoints<TurretController>();

        if (turrets.Count > 0)
        {
            foreach(TurretController turret in turrets)
            {
                float turretRange = turret.range;  

                
                if (turretRange > largestRange)
                {
                    largestRange = turretRange;
                   
                }
            }
            maxRange = largestRange;
        }
    }


}
