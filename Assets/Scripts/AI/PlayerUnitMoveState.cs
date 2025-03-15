using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SolarStudios
{
    public class PlayerUnitMoveState : MonoBehaviour, IUnitState
    {
        private PlayerUnitStateMachine stateMachine;
        private NavMeshAgent agent;

        public List<Vector3> destinations = new List<Vector3>();
        public float tiltMultiplier;
        public int maxTilt;
        public GameObject gfx;

        private float currentTilt = 0f;
        private Quaternion lastRotation;

        public void Enter(PlayerUnitStateMachine stateMachine) // Runs when we enter the state
        {
            this.stateMachine = stateMachine;
            agent = GetComponentInParent<NavMeshAgent>();
            lastRotation = agent.transform.rotation;
        }

        public void Run() // Runs every frame
        {


            if (destinations.Count == 0)
            {
                stateMachine.SetState(agent.GetComponentInChildren<PlayerUnitIdleState>());
                return;
            }



            agent.SetDestination(destinations[0]);

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                destinations.RemoveAt(0);
            }



            Tilt();



            lastRotation = agent.transform.rotation;
        }

        public void Exit() // Runs when we exit the state
        {
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

        public void AddDestination(Vector3 destination)
        {
            destinations.Add(destination);
        }

        public void ClearDestinations()
        {
            destinations.Clear();
        }

        public void MoveWithinRangeOfTarget(Vector3 target)
        {
            

            PlayerUnit playerUnit = GetComponentInParent<PlayerUnit>();

            float distanceToTarget = Vector3.Distance(transform.position, target);

            if (distanceToTarget > playerUnit.maxRange)
            {
                Vector3 directionToTarget = (target - transform.position).normalized;

                Vector3 targetPosition = target - directionToTarget * playerUnit.maxRange;

                playerUnit.gameObject.GetComponentInChildren<PlayerUnitMoveState>().AddDestination(targetPosition); //Jank I know. It'l be fine. it doesn't run that often.
            }

        }
    }
}
