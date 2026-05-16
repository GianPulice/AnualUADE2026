using UnityEngine;

public class NemesisSearchingState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    private float timeOut = 0;
    private float currentTime = 0;

    public NemesisSearchingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        timeOut = nemesisStateManager.NemesisData.SearchTimeOut;
    }

    public override void EnterState()
    {
        Debug.Log("Nemesis Enter Searching State");
        NextState = StateKey;
        currentTime = 0;
    }

    public override void ExitState()
    {
        Debug.Log("Nemesis Exit Searching State");
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
            if (currentTime < timeOut)
            {
                currentTime += Time.deltaTime;
                nemesisStateManager.SelfTransform.RotateAround(Vector3.up, -3 * Time.deltaTime);
            }
            else
            {
                NextState = NemesisStateManager.ENemesisState.Patrolling;
                Debug.Log(currentTime);
            }
        }
    }
}
