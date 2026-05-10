using UnityEngine;

public class NemesisPatrolState : BaseState<NemesisStateManager.ENemesisState>
{
    public NemesisPatrolState(NemesisStateManager.ENemesisState key) : base(key)
    {
    }

    public override void EnterState()
    {
        
    }

    public override void ExitState()
    {
        
    }

    public override NemesisStateManager.ENemesisState GetNextState()
    {
       return Statekey;
    }

    public override void OnTriggerEnter(Collider other)
    {
        throw new System.NotImplementedException();
    }

    public override void OnTriggerExit(Collider other)
    {
        throw new System.NotImplementedException();
    }

    public override void OnTriggerStay(Collider other)
    {
        throw new System.NotImplementedException();
    }

    public override void UpdateState()
    {
        
    }
}
