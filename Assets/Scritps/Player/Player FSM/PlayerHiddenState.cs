using UnityEngine;

public class PlayerHiddenState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    public PlayerHiddenState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        // Without this the first entry inherits NextState = default(EPlayerState) = Idle,
        // so GetNextState() bounces straight back out on the next frame.
        NextState = StateKey;

        Debug.Log("Enter Hidden State");
    }

    public override void ExitState()
    {
        Debug.Log("Exit Hidden State");
        NextState = StateKey;
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
        if(!playerStateManager.IsHidden) NextState = PlayerStateManager.EPlayerState.Idle;
    }
}
