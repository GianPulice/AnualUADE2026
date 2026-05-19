using Unity.Mathematics;
using UnityEngine;

public class NemesisChasingState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    private float currentTime = 0f;
    private float timeToExit = 0f;

    public NemesisChasingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        timeToExit = nemesisStateManager.NemesisData.VisionLossGracePeriod;
    }

    public override void EnterState()
    {
        Debug.Log("Nemesis Enter Chasing State");
        NextState = StateKey;
        currentTime = 0;
    }

    public override void ExitState()
    {
        Debug.Log("Nemesis Exit Chasing State");
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
        if (nemesisStateManager.HasTarget) 
        {
            nemesisStateManager.NavAgent.destination = nemesisStateManager.FieldOfView.LastKnownPosition;
        }
        else
        {
            if (currentTime < timeToExit)
            {
                currentTime += Time.deltaTime;
            }
            else 
            {
                NextState = NemesisStateManager.ENemesisState.Searching;
            }
        }
    }
}
