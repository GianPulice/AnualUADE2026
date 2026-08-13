using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

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
        //Debug.Log("Nemesis Enter Chasing State");
        NextState = StateKey;
        currentTime = 0;
        nemesisStateManager.AnimController.SetBool("isRunning", true);
        nemesisStateManager.NavAgent.speed = nemesisStateManager.NemesisMovement.ChaseSpeed;
    }

    public override void ExitState()
    {
        //Debug.Log("Nemesis Exit Chasing State");
        nemesisStateManager.AnimController.SetBool("isRunning", false);
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
        NavMeshAgent agent = nemesisStateManager.NavAgent;

        if (nemesisStateManager.HasVisualTarget)
        {
            agent.destination = nemesisStateManager.FieldOfView.LastKnownPosition;

            // remainingDistance (actual path length) instead of a straight-line check — the
            // capture trigger cannot afford to fire through a floor slab just because the
            // player is a couple of meters straight up/down, nor stay permanently unreachable
            // because the last known position sits slightly above the walkable surface.
            bool hasArrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;
            if (hasArrived)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
                nemesisStateManager.AnimController.SetBool("isRunning", false);
                NextState = NemesisStateManager.ENemesisState.Catch;
            }
            else nemesisStateManager.AnimController.SetBool("isRunning", true);
        }
        else
        {
            if (currentTime < timeToExit)
            {
                bool hasArrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;
                if (hasArrived)
                {
                    NextState = NemesisStateManager.ENemesisState.Searching;
                }
                currentTime += Time.deltaTime;
            }
            else
            {
                NextState = NemesisStateManager.ENemesisState.Searching;
            }
        }
    }
}
