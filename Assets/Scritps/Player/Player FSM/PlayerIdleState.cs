using System.Threading;
using Unity.Android.Gradle.Manifest;
using UnityEngine;

public class PlayerIdleState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    public PlayerIdleState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
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
        playerStateManager.CharController.Move(playerStateManager.CharGravity * Time.deltaTime);
        if (playerStateManager.IsInteracting)
        {
            NextState = PlayerStateManager.EPlayerState.Interacting;
        }
        else if (playerStateManager.IsHidden)
        {
            NextState = PlayerStateManager.EPlayerState.Hidden;
        }
        else if (playerStateManager.MoveDir != Vector3.zero) NextState = PlayerStateManager.EPlayerState.Moving;
    }
}
