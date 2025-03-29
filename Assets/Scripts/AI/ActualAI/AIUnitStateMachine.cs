using SolarStudios;
using UnityEngine;


public interface IAIState //Make the states inherit from this. Basically will force that script to have these functions. If it dont it dont work.
{
    void Enter(AIUnitStateMachine stateMachine);
    void Run();
    void Exit();

}

public class AIUnitStateMachine : MonoBehaviour
{
    [HideInInspector]
    public Animator anim;
    [HideInInspector]
    public IAIState currentState; //DONT TOUCH 

    private void Start()
    {
        SetState(gameObject.GetComponent<AIUnitIdleState>()); //This is how you change state.
    }

    private void Update()
    {
        if (currentState != null)
        {
            currentState.Run();
        }
        else
        {
            SetState(gameObject.GetComponent<AIUnitIdleState>());
        }

    }

    public void SetState(IAIState newState)
    {
        if (currentState != null)
        {
            currentState.Exit();
        }

        currentState = newState;
        currentState.Enter(this);
    }
}

