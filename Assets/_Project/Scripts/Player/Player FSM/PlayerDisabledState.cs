using UnityEngine;

/// <summary>
/// The player has been immobilized (today: captured by the Nemesis).
///
/// It only holds the pose. The end of the run is NOT triggered from here: that is done by
/// NemesisCatchState, which is the one that knows the timing of the capture animation.
/// </summary>
public class PlayerDisabledState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;

    public PlayerDisabledState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        // Without this the first entry inherits NextState = default(EPlayerState) = Idle,
        // so GetNextState() bounces straight back out and the capture animation flickers.
        NextState = StateKey;

        Debug.Log("Enter Disabled State");

        // Every capture path funnels through this state, so the locomotion parameters are
        // cleared HERE rather than in each source state's ExitState. Without it, a player
        // grabbed while crouch-walking keeps a non-zero moveSpeed -- PlayerCrouchState.ExitState
        // clears isCrouch but not the speed -- and the rig pops into the standing Walking blend
        // while the character is frozen mid-capture.
        //
        // Setting the parameters rather than forcing a CrossFade on purpose: the controller's
        // own transitions already resolve to Idle from these values, and hardcoding a state
        // name here would fight the state machine the moment someone renames it.
        Animator anim = playerStateManager.AnimController;
        anim.SetFloat("moveSpeed", 0f);
        anim.SetBool("isCrouch", false);
        anim.SetBool("isPushing", false);

        // NOTE: isTrapped is not referenced by any transition in PlayerController.controller and
        // there is no capture state to reach, so this currently has no visual effect. It is kept
        // because the parameter is the seam a real grab animation would hang off.
        anim.SetBool("isTrapped", true);

        // Stop the body too, not just the blend. PlayerStateManager.TeleportTo already
        // zeroes velocity -- its own doc comment says it is there so the player does not
        // "keep sliding at the speed it was running at when the Nemesis grabbed it" -- but
        // that only runs at the respawn, after CheckpointManager.captureCutsceneDelay.
        // For that second and a half the player was still coasting while visually frozen.
        playerStateManager.CurrentVelocity = 0f;
        if (playerStateManager.RigBody != null)
        {
            playerStateManager.RigBody.linearVelocity  = Vector3.zero;
            playerStateManager.RigBody.angularVelocity = Vector3.zero;
        }
    }

    public override void ExitState()
    {
        Debug.Log("Exit Disabled State");
        NextState = StateKey;
        playerStateManager.AnimController.SetBool("isTrapped", false);
    }

    public override void UpdateState()
    {
        // No longer terminal. CheckpointManager clears IsDisabled once it has moved the player
        // back to the active checkpoint, and that is the signal to hand control back.
        // Idle rather than the pre-capture state on purpose: the player has been teleported, so
        // resuming a crouch or a box interaction from the old position makes no sense.
        if (!playerStateManager.IsDisabled)
        {
            NextState = PlayerStateManager.EPlayerState.Idle;
        }
    }
}
