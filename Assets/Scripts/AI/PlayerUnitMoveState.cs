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
        public void Enter(PlayerUnitStateMachine stateMachine) //Runs when we enter the state
        {
            this.stateMachine = stateMachine;
            agent = GetComponentInParent<NavMeshAgent>();
        }
        public void Run() //Runs every frame
        {
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
        }
        public void Exit() //Runs when we exit
        {

        }
    }

}
