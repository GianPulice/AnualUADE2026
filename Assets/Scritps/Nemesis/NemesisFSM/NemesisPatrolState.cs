using UnityEngine;
using UnityEngine.AI;

public class NemesisPatrolState : BaseState<NemesisStateManager.ENemesisState>
{
    /// <summary>
    /// How long the active waypoint has to stay unreachable before it is skipped. pathStatus can
    /// read PathInvalid for a frame or two right after a destination is issued, and reacting to
    /// that would walk the whole route in well under a second.
    /// </summary>
    private const float UnreachableGraceTime = 0.5f;

    private NemesisStateManager nemesisStateManager;
    private float timeToNextWP = 0f;
    private float currentTime = 0f;
    private float unreachableTime = 0f;

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
        unreachableTime = 0f;
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
            if (controller == null) return;

            // Periodic replanning. BeginPatrolCycle only ran on ENTERING this state, so on a long
            // uninterrupted patrol the player bias was computed once, at the start, and the
            // feature felt dead.
            controller.TickPatrol(Time.deltaTime);

            Transform target = controller.CurrentWaypoint;

            if (target == null)
            {
                // No usable route right now. Re-roll instead of standing by forever: routes open
                // up from puzzle progress, and BeginPatrolCycle only runs on entering this state,
                // so a route that unlocks afterwards would otherwise never be picked up. This
                // also covers the load-order case, where a route's catch-up unlock in its own
                // Start() can land after the Nemesis has already begun its first cycle.
                controller.BeginPatrolCycle();
                target = controller.CurrentWaypoint;

                // Still nothing unlocked (or no routes configured at all): stand by, same as the
                // old "WayPoints.Count > 0" guard did.
                if (target == null) return;
            }

            NavMeshAgent agent = nemesisStateManager.NavAgent;

            // Only re-issued when the target actually moves on. Assigning destination every frame
            // restarts the path request, which kept pathPending true often enough to blur both the
            // arrival test below and the reachability test right after this. The isOnNavMesh guard
            // is what keeps a Warp that failed to land from spamming "SetDestination can only be
            // called on an active agent that has been placed on a NavMesh" — the state manager's
            // stuck-escape is what actually resolves that case.
            if (agent.isOnNavMesh && agent.destination != target.position)
                agent.destination = target.position;

            // A waypoint the agent cannot path to — placed off the mesh, or in a room sealed off
            // from here — used to leave the Nemesis walking on the spot forever: remainingDistance
            // reads Infinity, so it never counted as arrived and the route never advanced. Skip
            // the bad marker instead and keep the rest of the route usable.
            if (!agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                unreachableTime += Time.deltaTime;
                if (unreachableTime < UnreachableGraceTime) return;

                unreachableTime = 0f;
                Debug.LogWarning($"[NemesisPatrolState] No path to waypoint '{target.name}' — " +
                                 "skipping it. Check that it sits on the NavMesh and that its " +
                                 "area is reachable from here.", target);

                nemesisStateManager.AnimController.SetBool("isWalking", false);
                controller.AdvanceToNextWaypoint();
                return;
            }

            unreachableTime = 0f;

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
