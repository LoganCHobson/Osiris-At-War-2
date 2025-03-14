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

            if (destinations.Count == 0)
            {
                stateMachine.SetState(agent.GetComponentInChildren<PlayerUnitIdleState>());
                return;
            }

            if (destinations.Count > 0)
            {
                agent.SetDestination(destinations[0]);

                if (Vector3.Distance(agent.transform.position, destinations[0]) <= agent.stoppingDistance)
                {
                    destinations.RemoveAt(0);
                }
            }

            lastRotation = agent.transform.rotation;
        }

        public void Exit() // Runs when we exit the state
        {
        }
    }
}
