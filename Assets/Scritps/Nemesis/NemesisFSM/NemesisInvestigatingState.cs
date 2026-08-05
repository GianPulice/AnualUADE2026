using UnityEngine;

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
            nemesisStateManager.NavAgent.destination = nemesisStateManager.FieldOfListenig.LastKnownPosition;
            return;
        }

        currentTime += Time.deltaTime;

        // Two independent ways out: it arrived and found nothing, or it ran out of patience.
        // Without the timeout an unreachable destination left it stuck here forever.
        float tempDistance = Vector3.Distance(nemesisStateManager.transform.position, nemesisStateManager.NavAgent.destination);
        if (tempDistance < nemesisStateManager.NavAgent.stoppingDistance || currentTime >= timeOut)
        {
            NextState = NemesisStateManager.ENemesisState.Patrolling;
        }
    }
}
