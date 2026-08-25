using UnityEngine;
using UnityEngine.InputSystem;
//using UnityEditor.Animations;

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
        NextState = StateKey;
        playerStateManager.AudioEmitingZone.radius = playerStateManager.Movement.FootstepNoiseRadius;
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
        if (playerStateManager.IsDisabled)
        {
            NextState = PlayerStateManager.EPlayerState.Disabled;
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
        else if (!playerStateManager.IsGrounded)
        {
            NextState = PlayerStateManager.EPlayerState.Idle;
        }
        else
        {
            if (playerStateManager.InputDir != Vector3.zero)
            {
                // Sprint mechanic (chest penalty reduces the sprint multiplier — 1 while healthy)
                if (Input.GetButton("Sprint"))
                {
                    playerStateManager.SpeedMultiplier = playerStateManager.Movement.SprintSpeedMultiplier * playerStateManager.SprintPenaltyFactor;
                    playerStateManager.AudioEmitingZone.radius = playerStateManager.Movement.RunNoiseRadius;
                }
                else
                {
                    playerStateManager.SpeedMultiplier = 1f;
                    playerStateManager.AudioEmitingZone.radius = playerStateManager.Movement.FootstepNoiseRadius;
                }

                playerStateManager.PlayerBody.forward = Vector3.Slerp(playerStateManager.PlayerBody.forward, playerStateManager.InputDir, Time.deltaTime * playerStateManager.Movement.RotationSpeed);
                float targetSpeed = playerStateManager.EffectiveMoveSpeed * playerStateManager.SpeedMultiplier;
                if (playerStateManager.CurrentVelocity < targetSpeed)
                {
                    playerStateManager.CurrentVelocity += playerStateManager.Movement.Acceleration * Time.deltaTime;
                }
                else
                {
                    playerStateManager.CurrentVelocity = targetSpeed;
                }
                playerStateManager.RigBody.linearVelocity = playerStateManager.MoveDir * playerStateManager.CurrentVelocity;
                // Feed the Animator the intent velocity (pre-cojera), not the physical one, so the
                // walk/run blend tree still switches to Run when the player sprints with a legs
                // penalty active. The character still moves at CurrentVelocity — only the anim
                // decision uses the un-penalized value.
                float animSpeed = playerStateManager.CurrentVelocity / Mathf.Max(playerStateManager.MoveSpeedPenaltyFactor, 0.01f);
                playerStateManager.AnimController.SetFloat("moveSpeed", animSpeed);
            }
            else
            {
                playerStateManager.RigBody.linearVelocity = playerStateManager.MoveDir * playerStateManager.CurrentVelocity;
                NextState = PlayerStateManager.EPlayerState.Idle;
            }
        }
    }
}

