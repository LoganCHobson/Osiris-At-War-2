using System;
using System.Collections.Generic;
using UnityEngine;

public class AIUnitBrain : MonoBehaviour
{
    public AITask currentTask;
    private GameObject states;

    private float timer;
    public float maxWait = 25;
    private void Start()
    {
        timer = maxWait;
        states = GetComponentInChildren<AIUnitStateMachine>().gameObject;
    }

    private void Update()
    {
        if(currentTask != null)
        {
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                Debug.LogWarning("TASK FAILED TO COMPLETE INTIME. AUTO COMPLETING");
                CompleteTask();
                timer = maxWait;
            }
        }
    }
    public void AssignTask(AITask task)
    {
        currentTask = task;
    }

    public bool HasTask()
    {
        return currentTask != null;
    }

    public void CompleteTask()
    {
        currentTask = null;
        Debug.Log("Task completed");
    }


    public void MoveToAsteroid()
    {
        List<AstroidMiner> availableMiners = new List<AstroidMiner>();
        AstroidMiner closestMiner = null;
        float closestDistance = Mathf.Infinity;

        foreach (AstroidMiner astroidMiner in GameManager.Instance.allAstroidMiners)
        {
            if (astroidMiner.team != 8)
            {
                availableMiners.Add(astroidMiner);
            }
        }

        if (availableMiners.Count == 0)
        {
            Debug.LogWarning("No available miners to move to.");
            return;
        }

        foreach (AstroidMiner asteroidMiner in availableMiners)
        {
            float distanceToMiner = Vector3.Distance(transform.position, asteroidMiner.transform.position);

            if (distanceToMiner < closestDistance)
            {
                closestDistance = distanceToMiner;
                closestMiner = asteroidMiner;
            }
        }

        if (closestMiner != null)
        {
            AIUnitMoveState moveState = states.GetComponent<AIUnitMoveState>();
            if (moveState == null)
            {
                Debug.LogError("Move state is missing! AI cannot move.");
                return;
            }

            moveState.destination = closestMiner.transform.position;
            Debug.Log($"Moving to asteroid miner at {moveState.destination}");

            AIUnitStateMachine stateMachine = states.GetComponent<AIUnitStateMachine>();
            if (stateMachine != null)
            {
                stateMachine.SetState(moveState);
            }
            else
            {
                Debug.LogError("State machine is missing! AI cannot change states.");
            }
        }
    }
    public void AttackStation()
    {
        AIUnitMoveState moveState = states.GetComponent<AIUnitMoveState>();
        moveState.MoveWithinRangeOfTarget(AIBrain.Instance.playerStation.transform.position);
        states.GetComponent<AIUnitStateMachine>().SetState(moveState);

    }
    public void BuyShip()
    {
        //Dont need this. Clean later.
    }
    public void DefendStation()
    {
        AIUnitMoveState moveState = states.GetComponent<AIUnitMoveState>();
        moveState.MoveWithinRangeOfTarget(AIBrain.Instance.station.transform.root.position);
        states.GetComponent<AIUnitStateMachine>().SetState(moveState);
        
    }

    internal void HuntTask()
    {
        AIUnitMoveState moveState = states.GetComponent<AIUnitMoveState>();
        
        states.GetComponent<AIUnitStateMachine>().SetState(moveState);
    }
}
