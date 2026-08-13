using UnityEngine;
using UnityEngine.AI;

public class NemesisPatrolState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;
    private float timeToNextWP = 0f;
    private float currentTime = 0f;

    public NemesisPatrolState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        timeToNextWP = nemesisStateManager.NemesisData.PatrolWaypointWaitTime;
    }

    public override void EnterState()
    {
        //Debug.Log("Nemesis Enter Patrol State");
        NextState = StateKey;
        currentTime = 0f;
        nemesisStateManager.NavAgent.speed = nemesisStateManager.NemesisMovement.PatrolSpeed;

        // Picks the active route (weighted, among the unlocked ones) and rolls this cycle's
        // reverse/skip variation. Tier 3.1: see NemesisController.BeginPatrolCycle.
        nemesisStateManager.NemesisController?.BeginPatrolCycle();
    }

    public override void ExitState()
    {
        //Debug.Log("Nemesis Exit Patrol State");
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
        }
        else if (nemesisStateManager.HasAudioTarget)
        {
            NextState = NemesisStateManager.ENemesisState.Investigating;
        }
        else
        {
            NemesisController controller = nemesisStateManager.NemesisController;
            Transform target = controller != null ? controller.CurrentWaypoint : null;

            // No usable route yet (nothing unlocked, or none configured at all): stand by
            // instead of throwing, same as the old "WayPoints.Count > 0" guard did.
            if (target == null) return;

            NavMeshAgent agent = nemesisStateManager.NavAgent;
            agent.destination = target.position;

            // NavMeshAgent.remainingDistance measures distance along the actual path, not a
            // straight line through the air. Vector3.Distance was used here before and it never
            // accounted for height: a waypoint one floor up read as "close" the moment the agent
            // was nearly underneath it, long before it had climbed the stairs, and a waypoint
            // marker sitting even slightly above floor height (common — markers tend to get
            // placed at eye level) added a permanent Y gap that could keep this from ever
            // reading as arrived, freezing patrol there for good.
            bool hasArrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;

            if (!hasArrived)
            {
                nemesisStateManager.AnimController.SetBool("isWalking", true);
            }
            else
            {
                if (currentTime < timeToNextWP)
                {
                    currentTime += Time.deltaTime;
                    nemesisStateManager.AnimController.SetBool("isWalking", false);
                }
                else
                {
                    currentTime = 0;
                    controller.AdvanceToNextWaypoint();
                }
            }
        }
    }
}
