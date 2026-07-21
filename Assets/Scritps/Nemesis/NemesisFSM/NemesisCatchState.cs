using UnityEngine;

public class NemesisCatchState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;
    public NemesisCatchState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        NextState = StateKey;
        Debug.Log("Dispara animacion de captura");
        nemesisStateManager.FieldOfView.GetCurrentTarget().OnCaptured();
        nemesisStateManager.AnimController.SetBool("isCatching", true);
    }

    public override void ExitState()
    {
        nemesisStateManager.AnimController.SetBool("isCatching", false);
    }

    public override NemesisStateManager.ENemesisState GetNextState()
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
