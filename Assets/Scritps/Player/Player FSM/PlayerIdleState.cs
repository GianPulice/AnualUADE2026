using UnityEngine;

public class PlayerIdleState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    public PlayerIdleState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        //Debug.Log("Enter Idle State");
        NextState = StateKey;
    }

    public override void ExitState()
    {
        //Debug.Log("Exit Idle State");
    }

    public override PlayerStateManager.EPlayerState GetNextState()
    {
        if(NextState != StateKey) return NextState;
        else return StateKey;
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
        if (playerStateManager.IsGrounded)
        {
            playerStateManager.RigBody.linearVelocity = new Vector3(0,playerStateManager.RigBody.linearVelocity.y,0);
        }
        if (playerStateManager.IsInteracting)
        {
            NextState = PlayerStateManager.EPlayerState.Interacting;
        }
        else if (playerStateManager.IsHidden)
        {
            NextState = PlayerStateManager.EPlayerState.Hidden;
        }
        else if (playerStateManager.IsCrouch)
        {
            NextState = PlayerStateManager.EPlayerState.Crouch;
        }        
        else if (playerStateManager.InputDir != Vector3.zero)
        {
            NextState = PlayerStateManager.EPlayerState.Moving;
        }
    }
}  
