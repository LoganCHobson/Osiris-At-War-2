using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SolarStudios
{
    public class PlayerUnitIdleState : MonoBehaviour, IUnitState
    {
        private PlayerUnitStateMachine stateMachine;
        

        
        public void Enter(PlayerUnitStateMachine stateMachine) //Runs when we enter the state
        {
            this.stateMachine = stateMachine;
           
        }
        public void Run() //Runs every frame
        {
            Debug.Log("Idleing. .");
        }
        public void Exit() //Runs when we exit
        {

        }
    }

}
