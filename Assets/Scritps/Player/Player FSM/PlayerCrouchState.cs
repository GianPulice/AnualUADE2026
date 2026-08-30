using UnityEngine;
using UnityEngine.InputSystem;
//using UnityEditor.Animations;
//using UnityEditorInternal;

public class PlayerCrouchState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    public PlayerCrouchState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        //Debug.Log("Enter Crouch State");
        playerStateManager.AnimController.SetBool("isCrouch", true);
        playerStateManager.SpeedMultiplier = playerStateManager.Movement.CrouchSpeedMultiplier;
        // Height and centre come from SO_Movement so the box that has to fit under the
        // containers is authored in one place, next to the crouch speed and the crouch noise.
        // The centre is always half the height: the capsule shrinks from the head down instead
        // of sinking into the floor.
        float crouchHeight = playerStateManager.Movement.CrouchHeight;
        playerStateManager.CapsuleColl.height = crouchHeight;
        playerStateManager.CapsuleColl.center = new Vector3(0, crouchHeight * 0.5f, 0);
        playerStateManager.AudioEmitingZone.radius = playerStateManager.Movement.CrouchNoiseRadius;
        NextState = StateKey;
    }

    public override void ExitState()
    {
        //Debug.Log("Exit Crouch State");
        playerStateManager.AnimController.SetBool("isCrouch", false);
        playerStateManager.SpeedMultiplier = 1;
        float standingHeight = playerStateManager.Movement.StandingHeight;
        playerStateManager.CapsuleColl.height = standingHeight;
        playerStateManager.CapsuleColl.center = new Vector3(0, standingHeight * 0.5f, 0);
        playerStateManager.AudioEmitingZone.gameObject.SetActive(true);
    }

    public override void UpdateState()
    {
        if (playerStateManager.IsDisabled)
        {
            NextState = PlayerStateManager.EPlayerState.Disabled;
        }
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
            if (playerStateManager.InputDir != Vector3.zero)
            {
                playerStateManager.AudioEmitingZone.gameObject.SetActive(true);
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
            }
            else
            {
                playerStateManager.CurrentVelocity = 0;
                playerStateManager.AudioEmitingZone.gameObject.SetActive(false);
            }
            playerStateManager.ApplyMoveVelocity(playerStateManager.PlayerBody.forward * playerStateManager.CurrentVelocity);
            // Send the pre-cojera velocity to the Animator so the crouch blend keeps the same
            // anim while the legs penalty scales physical speed. See PlayerMovingState for details.
            float animSpeed = playerStateManager.CurrentVelocity / Mathf.Max(playerStateManager.MoveSpeedPenaltyFactor, 0.01f);
            playerStateManager.AnimController.SetFloat("moveSpeed", animSpeed);
        }
    }
}

