using UnityEngine;
using UnityEngine.AI;

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
        nemesisStateManager.AnimController.SetBool("isRunning", true);
    }

    public override void ExitState()
    {
        Debug.Log("Nemesis Exit Searching State");
        nemesisStateManager.AnimController.SetBool("isRunning", false);
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
        else
        {
            if (currentTime < timeOut)
            {
                currentTime += Time.deltaTime;
                if (nemesisStateManager.HasAudioTarget)
                {
                    nemesisStateManager.NavAgent.destination = nemesisStateManager.FieldOfListenig.LastKnownPosition;
                }
                float tempDistance = Vector3.Distance(nemesisStateManager.transform.position, nemesisStateManager.NavAgent.destination);
                if (tempDistance < nemesisStateManager.NavAgent.stoppingDistance)
                {
                   nemesisStateManager.NavAgent.destination = GetRandomPointInNavMesh();
                }
            }
            else
            {
                NextState = NemesisStateManager.ENemesisState.Patrolling;
            }
        }
    }
    private Vector3 GetRandomPointInNavMesh()
    {
        // Search radius
        float range = 5f;
        Vector3 randomPoint = Vector3.zero;
        do
        {
            randomPoint = nemesisStateManager.NavAgent.destination + (Random.onUnitSphere + nemesisStateManager.transform.forward ) * range;
        }
        while (!NavMesh.SamplePosition(randomPoint,out NavMeshHit hit,1f,NavMesh.AllAreas));
        Debug.Log("Random point found");
        return randomPoint;
    }
}
