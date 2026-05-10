using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor.Animations;
using UnityEditorInternal;

public class PlayerCrouchState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    public PlayerCrouchState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        //Debug.Log("Enter Moving State");
        playerStateManager.AnimController.SetBool("isCrouch", true);
        playerStateManager.SpeedMultiplier = playerStateManager.Movement.CrouchSpeedMultiplier;
        playerStateManager.CharController.height = 0.9f;
        playerStateManager.CharController.center = new Vector3(0, 0.45f, 0);
    }

    public override void ExitState()
    {
        //Debug.Log("Exit Moving State");
        playerStateManager.AnimController.SetBool("isCrouch", false);
        playerStateManager.SpeedMultiplier = 1;
        playerStateManager.CharController.height = 1.8f;
        playerStateManager.CharController.center = new Vector3(0, 0.9f, 0);
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
        if (!playerStateManager.IsCrouch) 
        {
            NextState = PlayerStateManager.EPlayerState.Idle;
        }
        if (playerStateManager.IsInteracting)
        {
            NextState = PlayerStateManager.EPlayerState.Interacting;
        }
        else if (playerStateManager.IsHidden)
        {
            NextState = PlayerStateManager.EPlayerState.Hidden;
        }
        else
        {
            if (playerStateManager.MoveDir != Vector3.zero)
            {
                playerStateManager.PlayerBody.forward = Vector3.Slerp(playerStateManager.PlayerBody.forward, playerStateManager.MoveDir, Time.deltaTime * playerStateManager.Movement.RotationSpeed);
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
            playerStateManager.CharController.Move((playerStateManager.PlayerBody.forward * playerStateManager.CurrentVelocity + playerStateManager.CharGravity) * Time.deltaTime);
            playerStateManager.AnimController.SetFloat("moveSpeed", playerStateManager.CurrentVelocity);
        }
    } 
}

