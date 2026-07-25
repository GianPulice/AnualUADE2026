using UnityEngine;

/// <summary>
/// El jugador quedo inmovilizado (hoy: capturado por el Nemesis).
///
/// Solo mantiene la pose. El fin de partida NO se dispara desde aca: lo hace
/// NemesisCatchState, que es quien conoce el timing de la animacion de captura.
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
        // Estado terminal: no hay nada que actualizar mientras dura la captura.
    }
}
