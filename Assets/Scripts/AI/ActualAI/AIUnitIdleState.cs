using UnityEngine;

public class AIUnitIdleState : MonoBehaviour, IAIState
{
    public void Enter(AIUnitStateMachine stateMachine)
    {
        
    }

    public void Run()
    {
        Debug.Log("Enemy ship idleing. . ");
    }

    public void Exit()
    {
        
    }

    
}
