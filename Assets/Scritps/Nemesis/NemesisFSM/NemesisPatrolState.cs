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
    }

    /// <summary>
    /// Rolls how long to stand at the waypoint being walked to.
    ///
    /// Per waypoint, and no longer once in the constructor. Two things were wrong with the old
    /// version and only one of them was the metronome: reading PatrolWaypointWaitTime at
    /// construction meant the value was sampled during Awake and never again, so retuning the wait
    /// in Play mode did nothing at all until the scene was reloaded — the same staleness the
    /// Investigating state's cached timeout had before the decision layer started reading the SO
    /// directly.
    ///
    /// The other thing is the point of the change: an identical pause at every marker means that
    /// once the player has timed one round, they have timed every round. Waiting behind a crate
    /// for the monster to move on stops being a bet and becomes arithmetic.
    /// </summary>
    private void RollWaitTime()
    {
        SO_NemesisData data = nemesisStateManager.NemesisData;

        timeToNextWP = data != null
            ? RouletteSelection.GetRandom(data.PatrolWaitMin, data.PatrolWaitMax)
            : 0f;
    }

    public override void EnterState()
    {
        NextState = StateKey;
        currentTime = 0f;
        unreachableTime = 0f;
        RollWaitTime();

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Walking,
                                    nemesisStateManager.NemesisMovement.PatrolSpeed);

        // Picks the active route (weighted, among the unlocked ones) and rolls this cycle's
        // reverse/skip variation. Tier 3.1: see NemesisController.BeginPatrolCycle.
        nemesisStateManager.NemesisController?.BeginPatrolCycle();
    }

    public override void ExitState() { }

    public override void UpdateState()
    {
        // Agent switched off (freight elevator ride) or off the NavMesh: nothing to ask of it this
        // frame. Every other state carries this and this one lost it when the transition logic
        // came out — without it, a Nemesis parked in Patrolling while the elevator owns its body
        // reads agent.isOnNavMesh on a disabled agent and Unity logs once per frame.
        if (!nemesisStateManager.IsAgentReady) return;

        // Seeing and hearing used to be tested here first, ahead of the walking. They are rungs 3
        // and 5 of NemesisDecision's ladder now, so what is left is the walking.
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

                controller.AdvanceToNextWaypoint();
                return;
            }

            unreachableTime = 0f;

            // Idle while waiting out PatrolWaypointWaitTime, walking otherwise. The gait carries
            // the speed with it, so a waiting Nemesis cannot end up standing still with a walk
            // animation playing — which is the class of mismatch SetGait exists to prevent.
            if (!nemesisStateManager.HasArrived)
            {
                nemesisStateManager.SetGait(NemesisStateManager.EGait.Walking,
                                            nemesisStateManager.NemesisMovement.PatrolSpeed);
            }
            else if (currentTime < timeToNextWP)
            {
                currentTime += Time.deltaTime;
                nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
            }
            else
            {
                currentTime = 0;
                RollWaitTime();
                controller.AdvanceToNextWaypoint();
            }
        }
    }
}
