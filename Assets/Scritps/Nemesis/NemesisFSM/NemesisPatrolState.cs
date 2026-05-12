using UnityEngine;
using UnityEngine.InputSystem.Controls;

public class NemesisPatrolState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    public NemesisPatrolState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        Debug.Log("Nemesis Enter Patrol State");
    }

    public override void ExitState()
    {
        Debug.Log("Nemesis Exit Patrol State");
        NextState = StateKey;
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
        if (nemesisStateManager.HasTarget)
        {
            NextState = NemesisStateManager.ENemesisState.Chasing;
        }
        else
        {
            nemesisStateManager.SelfTransform.RotateAround(Vector3.up, 3 * Time.deltaTime);
        }
    }
}
