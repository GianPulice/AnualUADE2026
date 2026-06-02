using UnityEngine;

public class NemesisInvestigatingState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    public NemesisInvestigatingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        Debug.Log("Nemesis Enter Investigating State");
        NextState = StateKey;
        nemesisStateManager.AnimController.SetBool("isWalking", true);
    }

    public override void ExitState()
    {
        Debug.Log("Nemesis Exit Investigating State");
        nemesisStateManager.AnimController.SetBool("isWalking", false);
    }

    public override NemesisStateManager.ENemesisState GetNextState()
    {
        if (NextState != StateKey) return NextState;
        else return StateKey;
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
        if (nemesisStateManager.HasVisualTarget)
        {
            NextState = NemesisStateManager.ENemesisState.Chasing;
        }
        else if (nemesisStateManager.HasAudioTarget)
        {
            nemesisStateManager.NavAgent.destination = nemesisStateManager.FieldOfListenig.LastKnownPosition;
        }
        else 
        {
            float tempDistance = Vector3.Distance(nemesisStateManager.transform.position, nemesisStateManager.NavAgent.destination);
            if (tempDistance < nemesisStateManager.NavAgent.stoppingDistance)
            {
                NextState = NemesisStateManager.ENemesisState.Patrolling;
            }
        }
    }
}
