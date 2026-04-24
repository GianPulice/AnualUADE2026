using UnityEngine;

public class PlayerInteractingState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    public PlayerInteractingState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        Debug.Log("Enter Interacting State");
    }

    public override void ExitState()
    {
        Debug.Log("Exit Interacting State");
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
        if(!playerStateManager.IsInteracting) NextState = PlayerStateManager.EPlayerState.Idle;
    }
}
