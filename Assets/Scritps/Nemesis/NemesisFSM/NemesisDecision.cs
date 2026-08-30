using UnityEngine;

/// <summary>
/// Decides WHICH state the Nemesis should be in. The FSM owns entering it, running it and leaving
/// it.
///
/// WHY THE DECISION LEFT THE STATES
///
/// Every transition used to be written inside the state it left from, which meant each state had
/// to know about the others. That cost is documented all over the old code: NemesisTraversingState
/// exists in large part because Chasing and Searching each re-derived the same borderline path
/// verdict and read it differently, and NemesisSearchingState carried a hand-placed
/// MinimumDwellTime to stop the two of them trading the Nemesis every other frame. Both are
/// symptoms of the same thing — a decision taken in six places cannot be made consistent, only
/// patched.
///
/// Here it is one prioritised ladder, read top to bottom, and the patches become visible rules.
///
/// WHY IT IS A PLAIN CLASS AND NOT A GRAPH — YET
///
/// The intended authoring surface is a Unity Behavior graph, so a designer can reorder priorities
/// without a recompile. This class is the same ladder in C#, and it exists for two reasons: the
/// Nemesis has to keep working while that graph is being built, and every predicate below is
/// public so a graph node is a three-line wrapper rather than a reimplementation. When the graph
/// is wired, tick <see cref="NemesisStateManager.DecisionsFromGraph"/> and the graph drives
/// instead — calling these same predicates, so the two can never disagree about what "sees the
/// player" means.
///
/// IT IS STATELESS ON PURPOSE. Everything it needs to know about time comes from
/// <see cref="StateManager{EState}.TimeInCurrentState"/> and
/// <see cref="NemesisStateManager.BeliefAge"/>. A decision layer with its own memory is a second
/// state machine, and then there are two.
/// </summary>
public sealed class NemesisDecision
{
    /// <summary>
    /// Shortest time the Nemesis may stay in Searching before anything is allowed to pull it out.
    ///
    /// This was a private const inside NemesisSearchingState and it is the clearest example of why
    /// the ladder is better than distributed transitions: Chasing handing over a target it can see
    /// (because the path to it was partial) and Searching handing it straight back (because it can
    /// see it) is a closed loop that turns over every other frame. As a floor buried in one state
    /// it read as an arbitrary magic number; as the first rung of the ladder it reads as what it
    /// is — a commitment that outranks everything, including sight.
    /// </summary>
    private const float MinimumSearchDwell = 0.5f;

    private readonly NemesisStateManager stateManager;

    public NemesisDecision(NemesisStateManager manager)
    {
        stateManager = manager;
    }

    private SO_NemesisData Data => stateManager.NemesisData;

    // ── Predicates ──────────────────────────────────────────────────────────
    //
    // Public and side-effect free. Each one is exactly one question, so a Behavior graph node is a
    // wrapper around a property rather than a second copy of the reasoning.

    public bool SeesPlayer => stateManager.HasVisualTarget;

    public bool HearsPlayer => stateManager.HasAudioTarget;

    public bool HasBelief => stateManager.TryGetBelief(out _);

    /// <summary>Seconds since either sensor last caught the player. Infinity if neither ever has.
    /// </summary>
    public float BeliefAge => stateManager.BeliefAge;

    public bool IsIn(NemesisStateManager.ENemesisState key) => stateManager.CurrentStateKey == key;

    /// <summary>Whether the player is close enough, level enough and unobstructed enough to grab,
    /// and the post-capture cooldown has expired.</summary>
    public bool CanCatchPlayer => stateManager.CanEnterCatch && stateManager.CanReachPlayerNow;

    /// <summary>
    /// Whether getting to where the Nemesis believes the player is means taking the freight
    /// elevator.
    ///
    /// GOES THROUGH THE THROTTLED ORACLE, AND THAT IS NOT AN OPTIMISATION. NemesisPathOracle holds
    /// one answer for RouteVerdictInterval seconds so that everything asking this question reads
    /// the SAME number. Querying NemesisNav directly here would give a freshly measured verdict
    /// that can differ from the one taken a frame ago on a borderline path — which is precisely
    /// the flip that used to leave the Nemesis shuddering in place directly below the player, and
    /// which NemesisTraversingState was built to stop. It would come back looking like a new bug.
    /// </summary>
    public bool RouteToBeliefCrossesFloors
    {
        get
        {
            if (!stateManager.TryGetBelief(out Vector3 belief)) return false;

            return stateManager.TryGetThrottledRoute(belief, out NemesisNav.NavRoute route) &&
                   stateManager.IsRouteAcrossFloors(route);
        }
    }

    // ── The ladder ──────────────────────────────────────────────────────────

    /// <summary>
    /// The state the Nemesis should be in this frame, read top to bottom: the first rung that
    /// holds wins.
    ///
    /// Order is the whole design. Capture outranks pursuit because a Nemesis with its hands on you
    /// should not be re-deciding; the lift outranks sight because a visible player one floor up is
    /// exactly the case a plain chase handles worst; and the dwell floor outranks all of it,
    /// because a decision reversed within half a second was never a decision.
    /// </summary>
    public NemesisStateManager.ENemesisState Decide()
    {
        SO_NemesisData data = Data;

        // 0. Commitment. While it holds, nothing below is even consulted.
        if (IsIn(NemesisStateManager.ENemesisState.Searching) &&
            stateManager.TimeInCurrentState < MinimumSearchDwell)
        {
            return NemesisStateManager.ENemesisState.Searching;
        }

        // 1. Close enough to grab.
        if (CanCatchPlayer) return NemesisStateManager.ENemesisState.Catch;

        // 2. It believes the player is on another floor and the lift is the way there. Bounded by
        //    ElevatorCommitTime measured from the last time it actually sensed them — the walk plus
        //    the ride is tens of seconds with the player invisible behind a slab, so without a
        //    bound this would hold forever on a belief that has gone cold.
        float commitTime = data != null ? data.ElevatorCommitTime : 12f;
        if (RouteToBeliefCrossesFloors && BeliefAge < commitTime)
        {
            return NemesisStateManager.ENemesisState.Traversing;
        }

        // 3. Plainly visible.
        if (SeesPlayer) return NemesisStateManager.ENemesisState.Chasing;

        // 4. Just lost sight. Measured from the last SENSE rather than from entering the state, so
        //    hearing them mid-chase renews the pursuit exactly the way seeing them would — which
        //    is what the old per-state counter did by resetting itself.
        float grace = data != null ? data.VisionLossGracePeriod : 2f;
        if (IsIn(NemesisStateManager.ENemesisState.Chasing) && BeliefAge < grace)
        {
            return NemesisStateManager.ENemesisState.Chasing;
        }

        // 5. A noise to walk towards. Leaves on arrival or on running out of patience, and a fresh
        //    noise renews it because BeliefAge resets on every detection.
        float investigateTimeOut = data != null ? data.InvestigationTimeOut : 8f;
        if (HearsPlayer) return NemesisStateManager.ENemesisState.Investigating;

        if (IsIn(NemesisStateManager.ENemesisState.Investigating) &&
            !stateManager.HasArrived &&
            BeliefAge < investigateTimeOut)
        {
            return NemesisStateManager.ENemesisState.Investigating;
        }

        // 6. Sweeping. Once in, it runs on its own clock: the search is a fixed budget of time to
        //    spend on a belief, not something to re-justify every frame.
        float searchTimeOut = data != null ? data.SearchTimeOut : 4f;
        if (IsIn(NemesisStateManager.ENemesisState.Searching))
        {
            return stateManager.TimeInCurrentState < searchTimeOut
                ? NemesisStateManager.ENemesisState.Searching
                : NemesisStateManager.ENemesisState.Patrolling;
        }

        // Coming off a pursuit still believing something: sweep rather than file it away.
        if (HasBelief && (IsIn(NemesisStateManager.ENemesisState.Chasing) ||
                          IsIn(NemesisStateManager.ENemesisState.Traversing)))
        {
            return NemesisStateManager.ENemesisState.Searching;
        }

        // 7. Nothing to act on.
        return NemesisStateManager.ENemesisState.Patrolling;
    }
}
