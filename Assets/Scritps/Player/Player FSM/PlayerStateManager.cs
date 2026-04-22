using UnityEngine;

public class PlayerStateManager : StateManager<PlayerStateManager.EPlayerState>
{
    private PlayerController playerController;


    public enum EPlayerState 
    {
        Idle,
        Moving,
        Interacting,
        Hidden,
        InDanger,
        Disabled,
    }
    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        InitializeStates();
    }
    public override void Start()
    {
        base.Start();
    }
    private void InitializeStates() 
    {
        States.Add(EPlayerState.Idle,new PlayerIdleState(EPlayerState.Idle, playerController));
        States.Add(EPlayerState.Moving, new PlayerMovingState(EPlayerState.Moving, playerController));
        States.Add(EPlayerState.Interacting, new PlayerInteractingState(EPlayerState.Interacting, this));
        States.Add(EPlayerState.Hidden, new PlayerHiddenState(EPlayerState.Hidden, this));
        States.Add(EPlayerState.Disabled, new PlayerDisabledState(EPlayerState.Disabled, this));
        CurrentState = States[EPlayerState.Idle];
    }
}
