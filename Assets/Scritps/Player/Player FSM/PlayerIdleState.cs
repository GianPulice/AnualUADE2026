using UnityEngine;

public class PlayerIdleState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerController playerController;
    public PlayerIdleState(PlayerStateManager.EPlayerState key, PlayerController playerController) : base(key)
    {
        this.playerController = playerController;
    }

    public override void EnterState()
    {
        Debug.Log("Enter Idle State");
    }

    public override void ExitState()
    {
        Debug.Log("Exit Idle State");
        NextState = Statekey;
    }

    public override PlayerStateManager.EPlayerState GetNextState()
    {
        if(NextState != Statekey) return NextState;
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
        //Debug.Log("Updating Idle State");
        if(playerController.CurrentVelocity > 0) NextState = PlayerStateManager.EPlayerState.Moving;
    }
}
