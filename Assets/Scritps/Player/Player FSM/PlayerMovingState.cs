using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor.Animations;

public class PlayerMovingState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    public PlayerMovingState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        //Debug.Log("Enter Moving State");
    }

    public override void ExitState()
    {
        //Debug.Log("Exit Moving State");
        playerStateManager.SpeedMultiplier = 1;
        if (NextState != PlayerStateManager.EPlayerState.Crouch)
        {
            playerStateManager.CurrentVelocity = 0;
            playerStateManager.AnimController.SetFloat("moveSpeed", playerStateManager.CurrentVelocity);
        }
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
        else
        {
            if (playerStateManager.MoveDir != Vector3.zero)
            {
                //Mecánica Correr
                if (Input.GetButton("Sprint")) playerStateManager.SpeedMultiplier = 1.5f;
                else playerStateManager.SpeedMultiplier = 1f;

                playerStateManager.PlayerBody.forward = Vector3.Slerp(playerStateManager.PlayerBody.forward, playerStateManager.MoveDir, Time.deltaTime * playerStateManager.Movement.RotationSpeed);
                if (playerStateManager.CurrentVelocity < playerStateManager.Movement.MoveSpeed * playerStateManager.SpeedMultiplier)
                {
                    playerStateManager.CurrentVelocity += playerStateManager.Movement.Acceleration * Time.deltaTime;
                }
                else 
                { 
                    playerStateManager.CurrentVelocity = playerStateManager.Movement.MoveSpeed * playerStateManager.SpeedMultiplier; 
                }
                playerStateManager.CharController.Move((playerStateManager.PlayerBody.forward * playerStateManager.CurrentVelocity + playerStateManager.CharGravity) * Time.deltaTime);
                playerStateManager.AnimController.SetFloat("moveSpeed", playerStateManager.CurrentVelocity);
            }
            else
            {
                NextState = PlayerStateManager.EPlayerState.Idle;
            }
        }
    } 
}

