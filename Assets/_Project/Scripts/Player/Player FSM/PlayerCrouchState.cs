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
        // Reset the blend speed too, the same way PlayerMovingState and
        // PlayerBoxInteractingState do on their way out. Clearing isCrouch alone leaves the
        // crouch-walk speed in moveSpeed, so any transition out of a crouch -- capture,
        // interaction, or just standing up mid-stride -- pops the rig into the standing
        // Walking blend for a frame.
        playerStateManager.AnimController.SetFloat("moveSpeed", 0f);
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

            // GROUNDED ONLY. This used to run unconditionally, and that is the whole of the
            // slow-motion fall while crouched: ApplyMoveVelocity takes the WHOLE vector it is
            // given and writes it straight onto linearVelocity, and PlayerBody.forward * speed
            // has no vertical component. Stepping off a ledge while crouched left gravity
            // exactly one FixedUpdate to build up Y speed before this ran again next frame and
            // reset it back to ~0 — a real fall, replayed one physics step at a time forever.
            //
            // PlayerMovingState and PlayerIdleState already only touch linearVelocity while
            // grounded (Moving drops to Idle on !IsGrounded; Idle's own zeroing is gated the
            // same way) — Crouch was the one state that never got that guard, presumably because
            // stepping off a ledge crouched is rarer than doing it upright.
            if (playerStateManager.IsGrounded)
            {
                playerStateManager.ApplyMoveVelocity(playerStateManager.PlayerBody.forward * playerStateManager.CurrentVelocity);
            }

            // Send the pre-cojera velocity to the Animator so the crouch blend keeps the same
            // anim while the legs penalty scales physical speed. See PlayerMovingState for details.
            float animSpeed = playerStateManager.CurrentVelocity / Mathf.Max(playerStateManager.MoveSpeedPenaltyFactor, 0.01f);
            playerStateManager.AnimController.SetFloat("moveSpeed", animSpeed);
        }
    }
}

