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
        // Terminal state: there is nothing to update while the capture lasts.
    }
}
