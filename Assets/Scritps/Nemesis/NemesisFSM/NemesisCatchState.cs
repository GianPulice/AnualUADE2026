using UnityEngine;

public class NemesisCatchState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;
    private PlayerStateManager player;
    public NemesisCatchState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        NextState = StateKey;
        Debug.Log("Dispara animacion de captura");
        player = nemesisStateManager.FieldOfView.GetCurrentTarget();
        player.OnCaptured();
        player.transform.LookAt(nemesisStateManager.transform);
        nemesisStateManager.transform.LookAt(player.transform);
        nemesisStateManager.AnimController.SetBool("isCatching", true);
    }

    public override void ExitState()
    {
        nemesisStateManager.AnimController.SetBool("isCatching", false);
    }

    public override NemesisStateManager.ENemesisState GetNextState()
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
        
    }
}
