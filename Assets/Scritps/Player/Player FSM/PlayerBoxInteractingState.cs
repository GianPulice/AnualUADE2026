using UnityEngine;
using UnityEngine.Assemblies;

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
        playerStateManager.BoxColl.enabled = true;
        playerStateManager.IsCrouch = false;
        playerStateManager.AnimController.SetBool("isPushing", true);
        NextState = StateKey;
    }

    public override void ExitState()
    {
        Debug.Log("Exit Interacting State");
        playerStateManager.SpeedMultiplier = 1f;
        playerStateManager.BoxColl.enabled = false;
        playerStateManager.AnimController.SetBool("isPushing", false);
        playerStateManager.AnimController.SetFloat("moveSpeed", 0);
    }

    public override PlayerStateManager.EPlayerState GetNextState()
    {
        if (NextState != StateKey) return NextState;
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
        if (!playerStateManager.IsInteracting) NextState = PlayerStateManager.EPlayerState.Idle;
        else
        {
            if (Input.GetAxis("Vertical") > 0)
            {
                if (playerStateManager.CurrentVelocity < playerStateManager.Movement.MoveSpeed * playerStateManager.SpeedMultiplier)
                {
                    playerStateManager.CurrentVelocity += playerStateManager.Movement.Acceleration * Time.deltaTime;
                }
                else
                {
                    playerStateManager.CurrentVelocity = playerStateManager.Movement.MoveSpeed * playerStateManager.SpeedMultiplier;
                }
            }
            else playerStateManager.CurrentVelocity = 0;
            playerStateManager.RigBody.linearVelocity = playerStateManager.PlayerBody.forward * playerStateManager.CurrentVelocity;
            playerStateManager.AnimController.SetFloat("moveSpeed", playerStateManager.CurrentVelocity);
        }
    }
}
