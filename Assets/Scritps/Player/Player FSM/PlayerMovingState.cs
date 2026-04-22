using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovingState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerController playerController;
    public PlayerMovingState(PlayerStateManager.EPlayerState key, PlayerController playerController) : base(key)
    {
        this.playerController = playerController;
    }

    public override void EnterState()
    {
        Debug.Log("Enter Moving State");
    }

    public override void ExitState()
    {
        Debug.Log("Exit Moving State");
        NextState = Statekey;
    }

    public override PlayerStateManager.EPlayerState GetNextState()
    {
        if (NextState != Statekey) return NextState;
        else return Statekey;
    }

    public override void OnTriggerEnter(Collider other)
    {
        
    }

    public override void OnTriggerExit(Collider other)
    {
        
    }

    public override void OnTriggerStay(Collider other)
    {
        
    }

    public override void UpdateState()
    {
        //Debug.Log("Updating Moving State");
        if (playerController.CurrentVelocity <= 0) NextState = PlayerStateManager.EPlayerState.Idle;
    }
}
