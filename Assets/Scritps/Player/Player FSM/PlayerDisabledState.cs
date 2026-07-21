using UnityEngine;

public class PlayerDisabledState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    private float cooldonwnTimer = 0f;
    private float currentCooldownTime = 0f;
    public PlayerDisabledState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        Debug.Log("Enter Disabled State");
        playerStateManager.AnimController.SetBool("isTrapped", true);
    }

    public override void ExitState()
    {
        Debug.Log("Exit Disabled State");
        NextState = StateKey;
        playerStateManager.AnimController.SetBool("isTrapped", false);
        currentCooldownTime = 0;
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
        if(currentCooldownTime >= cooldonwnTimer) 
        {
            // trigger UI de derrota o Reload
        }
        else currentCooldownTime += cooldonwnTimer;
    }
}
