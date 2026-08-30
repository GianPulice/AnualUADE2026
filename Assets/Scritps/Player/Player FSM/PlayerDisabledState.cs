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
        playerStateManager.AnimController.SetBool("isTrapped", true);
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
