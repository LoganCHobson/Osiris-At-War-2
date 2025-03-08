using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SolarStudios
{

    public interface IUnitState //Make the states inherit from this. Basically will force that script to have these functions. If it dont it dont work.
    {
        void Enter(PlayerUnitStateMachine stateMachine);
        void Run();
        void Exit();

    }

    public class PlayerUnitStateMachine : MonoBehaviour //Dont touch this script.
    {
        [HideInInspector]
        public Animator anim;
        [HideInInspector]
        public IUnitState currentState; //DONT TOUCH 


        private void Start()
        {
           
            SetState(gameObject.GetComponent<PlayerUnitIdleState>()); //This is how you change state.
        }

        private void Update()
        {
            if (currentState != null)
            {
                currentState.Run();
            }
            else
            {
                SetState(gameObject.GetComponent<PlayerUnitIdleState>());
            }

        }

        public void SetState(IUnitState newState)
        {
            if (currentState != null)
            {
                currentState.Exit();
            }

            currentState = newState;
            currentState.Enter(this);
        }
    }
}
