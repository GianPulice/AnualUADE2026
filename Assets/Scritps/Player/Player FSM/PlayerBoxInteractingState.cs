using UnityEngine;
using UnityEngine.Assemblies;

public class PlayerBoxInteractingState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    private bool finishAnim = false;
    private float animTimer = 0.2f;
    private float currentTimer = 0f;
    public PlayerBoxInteractingState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        Debug.Log("Enter Interacting State");
        // Box push speed lives on SO_Movement (BoxPushSpeed) and is read directly in UpdateState.
        // SpeedMultiplier is kept at 1 so nothing else (CameraSprintEffect, etc.) misreads it as
        // a stance change — the tempo is capped by the SO value, not by the multiplier.
        playerStateManager.SpeedMultiplier = 1f;
        playerStateManager.BoxColl.enabled = true;
        playerStateManager.IsCrouch = false;
        playerStateManager.AnimController.SetBool("isPushing", true);
        playerStateManager.AudioEmitingZone.radius = playerStateManager.Movement.FootstepNoiseRadius;
        NextState = StateKey;
        finishAnim = false;
        currentTimer = 0f;
    }

    public override void ExitState()
    {
        Debug.Log("Exit Interacting State");
        playerStateManager.SpeedMultiplier = 1f;
        playerStateManager.BoxColl.enabled = false;
        playerStateManager.AnimController.SetBool("isPushing", false);
        playerStateManager.AnimController.SetFloat("moveSpeed", 0);
    }

    public override void UpdateState()
    {
        if (playerStateManager.IsDisabled)
        {
            NextState = PlayerStateManager.EPlayerState.Disabled;
        }
        else if (!playerStateManager.IsInteracting) NextState = PlayerStateManager.EPlayerState.Idle;
        else
        {
            if (!finishAnim) 
            {
                if (currentTimer < animTimer) 
                {
                    playerStateManager.transform.position = Vector3.Slerp(playerStateManager.transform.position, playerStateManager.NextPosition,currentTimer/animTimer);
                    playerStateManager.PlayerBody.forward = Vector3.Slerp(playerStateManager.PlayerBody.forward, playerStateManager.NextDirection, currentTimer / animTimer);
                    currentTimer += Time.deltaTime;
                }
                else 
                {
                    playerStateManager.transform.position = Vector3.Slerp(playerStateManager.transform.position, playerStateManager.NextPosition, 1);
                    playerStateManager.PlayerBody.forward = Vector3.Slerp(playerStateManager.PlayerBody.forward, playerStateManager.NextDirection, 1);
                    finishAnim = true;
                }
            }
            else
            {
                if (Input.GetAxis("Vertical") > 0)
                {
                    float pushCap = playerStateManager.Movement.BoxPushSpeed;
                    if (playerStateManager.CurrentVelocity < pushCap)
                    {
                        playerStateManager.CurrentVelocity += playerStateManager.Movement.Acceleration * Time.deltaTime;
                        if (playerStateManager.CurrentVelocity > pushCap) playerStateManager.CurrentVelocity = pushCap;
                    }
                    else
                    {
                        playerStateManager.CurrentVelocity = pushCap;
                    }
                }
                else playerStateManager.CurrentVelocity = 0;
                playerStateManager.RigBody.linearVelocity = playerStateManager.PlayerBody.forward * playerStateManager.CurrentVelocity + Vector3.down;
                playerStateManager.AnimController.SetFloat("moveSpeed", playerStateManager.CurrentVelocity);
            }
        }
    }
}
