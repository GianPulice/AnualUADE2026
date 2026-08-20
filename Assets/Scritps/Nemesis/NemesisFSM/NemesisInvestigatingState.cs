using UnityEngine;
using UnityEngine.AI;

public class NemesisInvestigatingState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    private float timeOut = 0;
    private float currentTime = 0;

    public NemesisInvestigatingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        timeOut = nemesisStateManager.NemesisData.InvestigationTimeOut;
    }

    public override void EnterState()
    {
        Debug.Log("Nemesis Enter Investigating State");
        NextState = StateKey;
        currentTime = 0;
        nemesisStateManager.NavAgent.speed = nemesisStateManager.NemesisMovement.InvestigationSpeed;
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

    }

    public override void OnTriggerExit(Collider other)
    {

    }

    public override void OnTriggerStay(Collider other)
    {

    }

    public override void UpdateState()
    {
        // Agent switched off (freight elevator ride): nothing to ask of it this frame. See
        // NemesisStateManager.IsAgentReady.
        if (!nemesisStateManager.IsAgentReady) return;

        if (nemesisStateManager.HasVisualTarget)
        {
            NextState = NemesisStateManager.ENemesisState.Chasing;
            return;
        }

        if (nemesisStateManager.HasAudioTarget)
        {
            // Fresh noise renews its interest: the timeout only measures how long it has
            // been going with nothing to follow.
            currentTime = 0;
            nemesisStateManager.NavAgent.destination = nemesisStateManager.FieldOfListening.LastKnownPosition;
            return;
        }

        currentTime += Time.deltaTime;

        // Two independent ways out: it arrived and found nothing, or it ran out of patience.
        // Without the timeout an unreachable destination left it stuck here forever.
        //
        // remainingDistance (path length) instead of Vector3.Distance (straight line): a noise
        // heard from a different floor used to read as "arrived" or "unreachable" based on raw
        // 3D distance rather than whether the agent had actually walked there.
        NavMeshAgent agent = nemesisStateManager.NavAgent;
        bool hasArrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;
        if (hasArrived || currentTime >= timeOut)
        {
            NextState = NemesisStateManager.ENemesisState.Patrolling;
        }
    }
}
