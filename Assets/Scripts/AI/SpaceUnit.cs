using SolarStudios;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using static UnityEngine.GraphicsBuffer;

public class SpaceUnit : MonoBehaviour
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

    public ShipType shipType;

    public float maxRange;
    void Start()
    {
        GetMaxRange();
        if (shipType != ShipType.Station)
        {
            if (gameObject.layer == 7 || testing == true)
            {
                GameManager.Instance.allFriendlyUnits.Add(this);
            }
            else
            {
                GameManager.Instance.allEnemyUnits.Add(this);
            }
        }
       if(TryGetComponent(out NavMeshAgent _agent))
        {
            agent = _agent;
        }
        else
        {
            Debug.LogWarning(gameObject.name + " Does not have a navmesh agent. Was that intended?");
        }

        if(GetComponentInChildren<PlayerUnitStateMachine>())
        {
            moveState = GetComponentInChildren<PlayerUnitMoveState>();
            stateMachine = GetComponentInChildren<PlayerUnitStateMachine>();
        }
        else
        {
            Debug.LogWarning(gameObject.name + " Does not have a statemachine. Was that intended?");
        }
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

public enum ShipType { Station, Battleship, Carrier, Cruiser, Destroyer, Corvette, Fighter }
