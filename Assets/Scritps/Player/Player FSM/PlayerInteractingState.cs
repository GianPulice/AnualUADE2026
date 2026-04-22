using UnityEngine;

public class PlayerInteractingState : BaseState<PlayerStateManager.EPlayerState>
{
    public PlayerInteractingState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
    }

    public override void EnterState()
    {
        
    }

    public override void ExitState()
    {
        
    }

    public override PlayerStateManager.EPlayerState GetNextState()
    {
        throw new System.NotImplementedException();
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
        
    }
}
