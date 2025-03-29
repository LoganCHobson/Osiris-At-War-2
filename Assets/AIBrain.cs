using NUnit.Framework;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class AIBrain : MonoBehaviour
{
    public static AIBrain Instance;

    public StationController station;
    public StationController playerStation;
    public List<AITask> toDoList = new List<AITask>();
    private List<AIUnitBrain> assignedUnits = new List<AIUnitBrain>();
    private UnitHealthManager health;

    private float lastHealth;
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        else
        {
            Destroy(gameObject);
        }

    }

    private void Start()
    {
        health = station.GetComponentInParent<UnitHealthManager>();
        lastHealth = health.currentHealth;
    }

    private void Update()
    {
        Assess();
        ExecuteTasks();
    }

    private void Assess()
    {
        if(health.currentHealth < lastHealth)
        {
            Debug.Log("Adding a defend station task. . ");
            lastHealth = health.currentHealth;
            AddTask(new DefendStationTask());
        }
        else
        {
            lastHealth = health.currentHealth;
        }
        if (GameManager.Instance.enemyCash >= 100 && !station.cooling)
        {
            Debug.Log("Making a ship. . ");
            station.MakeShip();


            //AddTask(new BuyShipTask());
        }

        if (GameManager.Instance.allFriendlyUnits.Count == 0)
        {
            Debug.Log("Adding an attack station task. .");
            AddTask(new AttackStationTask());
        }
        if (toDoList.Count == 0)
        {
            
            AddDefaultTasks();
        }
    }

    private void ExecuteTasks()
    {
        for (int i = toDoList.Count - 1; i >= 0; i--)
        {
            var task = toDoList[i];
            if (!task.IsAssigned)
            {
                AIUnitBrain availableUnit = FindAvailableUnit();
                if (availableUnit != null)
                {
                    availableUnit.AssignTask(task);
                    Debug.Log("Task: " + task + " was assigned to: " + availableUnit.gameObject.name);
                    task.IsAssigned = true;
                    assignedUnits.Add(availableUnit);
                    task.Execute(availableUnit);

                    
                    toDoList.RemoveAt(i);
                }
            }
        }
    }

    private AIUnitBrain FindAvailableUnit()
    {
        foreach (SpaceUnit unit in GameManager.Instance.allEnemyUnits)
        {
            if (!unit.GetComponent<AIUnitBrain>().HasTask())
            {
                Debug.Log(unit.gameObject.name + " Can preform a task.");
                return unit.GetComponent<AIUnitBrain>();
            }
        }
        return null; 
    }

    private void AddDefaultTasks()
    {
        foreach (AstroidMiner astroidMiner in GameManager.Instance.allAstroidMiners)
        {
            if (astroidMiner.team != 8)
            {
                Debug.Log("Adding capture astroid tasks. .");
                AddTask(new CaptureAsteroidTask());
            }
        }
        AddTask(new HuntTask());


    }

    private void AddTask(AITask task)
    {
        int maxTasks = GameManager.Instance.allEnemyUnits.Count + 2;

        if (toDoList.Count < maxTasks)
        {
            toDoList.Add(task);
        }
        else
        {
            Debug.Log("Task limit reached! Not adding more tasks.");
        }
    }
}

public abstract class AITask
{
    public bool IsAssigned { get; set; } = false;

    public abstract void Execute(AIUnitBrain unit);
}

public class HuntTask : AITask
{
    public override void Execute(AIUnitBrain unit)
    {
        unit.HuntTask();
    }
}

public class CaptureAsteroidTask : AITask
{
    public override void Execute(AIUnitBrain unit)
    {
        unit.MoveToAsteroid();
    }
}

public class AttackStationTask : AITask
{
    public override void Execute(AIUnitBrain unit)
    {
        unit.AttackStation();
    }
}

public class BuyShipTask : AITask
{
    public override void Execute(AIUnitBrain unit)
    {
        //Wont be needing this.
        unit.BuyShip();
    }
}

public class DefendStationTask : AITask
{
    public override void Execute(AIUnitBrain unit)
    {
       
        unit.DefendStation();
    }
}


