using UnityEngine;

public class NemesisChasingState : BaseState<NemesisStateManager.ENemesisState>
{
    public NemesisChasingState(NemesisStateManager.ENemesisState key) : base(key)
    {
    }

    public override void EnterState()
    {
        NemesisEvents.ChaseStarted();
    }

    public override void ExitState()
    {
        NemesisEvents.ChaseEnded();
    }

    public override NemesisStateManager.ENemesisState GetNextState()
    {
        throw new System.NotImplementedException();
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
        throw new System.NotImplementedException();
    }
}
