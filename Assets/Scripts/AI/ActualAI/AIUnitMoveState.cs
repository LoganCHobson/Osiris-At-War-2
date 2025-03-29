using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public class AIUnitMoveState : MonoBehaviour, IAIState
{
    public NavMeshAgent agent;

    public Vector3 destination;

    public float tiltMultiplier;
    public int maxTilt;
    private float currentTilt = 0f;
    private Quaternion lastRotation;

    public GameObject gfx;
    public void Enter(AIUnitStateMachine stateMachine)
    {
        agent = GetComponentInParent<NavMeshAgent>();
        lastRotation = agent.transform.rotation;
    }
    public void Run()
    {
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            AIUnitBrain brain = agent.GetComponent<AIUnitBrain>();

            if (brain.currentTask is DefendStationTask defendStationTask)
            {
                bool enemyNearby = GameManager.Instance.allEnemyUnits
                    .Any(enemy => Vector3.Distance(enemy.transform.position, AIBrain.Instance.station.transform.position) <= 70);

                if (!enemyNearby)
                {
                    brain.CompleteTask(); 
                }
            }
            if (brain.currentTask is HuntTask huntTask)
            {
                SpaceUnit closestEnemy = null;
                float closestDistance = Mathf.Infinity;

                foreach (SpaceUnit enemy in GameManager.Instance.allEnemyUnits)
                {
                    float distanceToEnemy = Vector3.Distance(transform.position, enemy.transform.position);
                    if (distanceToEnemy < closestDistance)
                    {
                        closestDistance = distanceToEnemy;
                        closestEnemy = enemy;
                    }
                }

                if (closestEnemy != null)
                {
                    destination = closestEnemy.transform.position;
                }
                else
                {
                    GetComponent<AIUnitStateMachine>().SetState(GetComponent<AIUnitIdleState>());
                }
            }


            
            GetComponent<AIUnitStateMachine>().SetState(GetComponent<AIUnitIdleState>());
        }

        Tilt();
        agent.SetDestination(destination);
        lastRotation = agent.transform.rotation;
    }
    public void Exit()
    {
        if (agent.gameObject.GetComponent<AIUnitBrain>().HasTask())
        {
            MarkTaskComplete(agent.gameObject.GetComponent<AIUnitBrain>());
        }
    }

    public void Tilt()
    {
        float turnAmount = Vector3.SignedAngle(lastRotation * Vector3.forward, agent.transform.forward, Vector3.up);


        if (Mathf.Abs(turnAmount) > 0.5f)
        {
            float targetTilt = Mathf.Clamp(Mathf.Abs(turnAmount) * tiltMultiplier, -maxTilt, maxTilt);

            currentTilt = Mathf.Lerp(currentTilt, targetTilt * -Mathf.Sign(turnAmount), Time.deltaTime * 5f);
        }
        else
        {
            currentTilt = Mathf.Lerp(currentTilt, 0, Time.deltaTime * 5f);
        }

        gfx.transform.localRotation = Quaternion.Euler(0, 0, currentTilt);
    }

    public void MoveWithinRangeOfTarget(Vector3 target)
    {
        SpaceUnit unit = GetComponentInParent<SpaceUnit>();

        float distanceToTarget = Vector3.Distance(transform.position, target);

        if (distanceToTarget > unit.maxRange)
        {
            Vector3 directionToTarget = (target - transform.position).normalized;

            Vector3 targetPosition = target - directionToTarget * unit.maxRange;

            unit.gameObject.GetComponentInChildren<AIUnitMoveState>().destination = targetPosition; //Jank I know. It'l be fine. it doesn't run that often.
        }

    }

    private void MarkTaskComplete(AIUnitBrain aIUnitBrain)
    {
        if (aIUnitBrain.currentTask is DefendStationTask defendStationTask)
        {
            aIUnitBrain.CompleteTask();
        }
        else if (aIUnitBrain.currentTask is HuntTask huntTask)
        {
            aIUnitBrain.CompleteTask();
        }
    }
}
