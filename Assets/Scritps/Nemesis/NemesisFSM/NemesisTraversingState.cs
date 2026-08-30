using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The Nemesis is going after the player across floors, and getting there means the freight
/// elevator.
///
/// WHY THIS IS A STATE AND NOT A BRANCH INSIDE CHASING
///
/// The NavMesh already routes through the lift on its own: with the link healthy, the path to a
/// player one storey up is a complete path that happens to cross a NavMeshLink, and
/// <see cref="NemesisElevatorUser"/> takes over the moment the agent steps onto it. Nothing here
/// steers that.
///
/// What the pathfinder cannot supply is *commitment*. The walk to the landing plus the ride is
/// tens of seconds, and for almost all of it the player is invisible — a floor slab is the one
/// thing vision can never penetrate. Chasing reads that as having lost the target, hands over to
/// Searching after VisionLossGracePeriod, and Searching starts sweeping the floor it is already
/// standing on. The lift trip is abandoned three metres from the landing, every single time.
///
/// So this state exists to hold one decision open: keep walking to the lift even though I cannot
/// see you, for as long as ElevatorCommitTime. That is the whole job.
///
/// It also breaks the oscillation that pinned the Nemesis under the player: Chasing used to treat
/// a partial path as "lost", Searching treated a visible player as "found", and the two swapped
/// every other frame. Chasing now sends the vertical case here instead, and here the visible
/// player is not an exit condition.
/// </summary>
public class NemesisTraversingState : BaseState<NemesisStateManager.ENemesisState>
{
    private readonly NemesisStateManager nemesisStateManager;

    /// <summary>Where it is heading. Held rather than re-read every frame because the belief
    /// stops updating the moment the slab cuts line of sight, and re-reading a
    /// LastKnownPosition that is no longer being written is not the same as remembering the
    /// destination this trip was started for.</summary>
    private Vector3 believedTarget;

    public NemesisTraversingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager)
        : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        NextState = StateKey;

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.ChaseSpeed);

        // The cached verdict is deliberately NOT invalidated here, even though it may be up to
        // RouteVerdictInterval old. This state and Chasing decide on complementary readings of
        // the same value — "the lift is on the way" sends it here, "it is not" sends it back — so
        // what keeps them from trading the Nemesis back and forth is that they read the SAME
        // number, not that either reads a fresh one. Re-measuring on entry would let the answer
        // change between the frame that decided to come here and the first frame spent here,
        // which is precisely the flip this state exists to stop.
        if (nemesisStateManager.TryGetBelief(out Vector3 belief)) believedTarget = belief;
        else believedTarget = nemesisStateManager.transform.position;
    }

    public override void ExitState() { }

    public override void UpdateState()
    {
        // Refreshing the destination is all this state does now.
        //
        // Every reason to LEAVE used to be computed here — out of patience, the belief left the
        // NavMesh, the lift stopped being on the way — and all three are questions about the
        // world rather than about this state's own execution, so they live in NemesisDecision's
        // ladder. Rung 2 keeps the Nemesis here for as long as the route to the belief still
        // crosses a lift and the belief is younger than ElevatorCommitTime; the moment either
        // stops holding, the ladder falls through to the rung that fits and the machine moves.
        // Nothing here has to detect "arrived upstairs" or "the player came down" separately.
        if (nemesisStateManager.HasVisualTarget || nemesisStateManager.HasAudioTarget)
        {
            if (nemesisStateManager.TryGetBelief(out Vector3 fresh)) believedTarget = fresh;
        }

        // The agent is switched off for the whole ride — NemesisElevatorUser owns the Nemesis
        // from the moment it steps onto the link until it is warped off at the far landing. There
        // is nothing to ask of a disabled agent, and asking logs an error per frame.
        if (!nemesisStateManager.IsAgentReady) return;

        nemesisStateManager.NavAgent.destination = believedTarget;
    }
}
