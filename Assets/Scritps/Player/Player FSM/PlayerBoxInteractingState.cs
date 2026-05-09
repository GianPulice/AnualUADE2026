using UnityEngine;

public class PlayerBoxInteractingState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    public PlayerBoxInteractingState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        Debug.Log("Enter Interacting State");
        playerStateManager.SpeedMultiplier = 0.5f;
    }

    public override void ExitState()
    {
        Debug.Log("Exit Interacting State");
        playerStateManager.SpeedMultiplier = 1f;
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
        if(!playerStateManager.IsInteracting) NextState = PlayerStateManager.EPlayerState.Idle;
        else if (playerStateManager.MoveDir != Vector3.zero)
        {
            //playerStateManager.PlayerBody.forward = Vector3.Slerp(playerStateManager.PlayerBody.forward, playerStateManager.MoveDir, Time.deltaTime * playerStateManager.Movement.RotationSpeed);
            if (playerStateManager.CurrentVelocity < playerStateManager.Movement.MoveSpeed * playerStateManager.SpeedMultiplier)
            {
                playerStateManager.CurrentVelocity += playerStateManager.Movement.Acceleration * Time.deltaTime;
            }
            else
            {
                playerStateManager.CurrentVelocity = playerStateManager.Movement.MoveSpeed * playerStateManager.SpeedMultiplier;
            }
            playerStateManager.CharController.Move((playerStateManager.MoveDir * playerStateManager.CurrentVelocity + playerStateManager.CharGravity) * Time.deltaTime);
            //playerStateManager.AnimatorController.SetFloat("moveSpeed", playerStateManager.CurrentVelocity);
        }
    }
}
