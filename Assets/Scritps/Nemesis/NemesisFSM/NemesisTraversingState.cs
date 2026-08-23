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

    /// <summary>Seconds since the player was last seen or heard. Reset by any fresh sense, not by
    /// arriving anywhere — it measures how old the reason for this trip is.</summary>
    private float sinceLastSensed;

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
        sinceLastSensed = 0f;

        nemesisStateManager.AnimController.SetBool("isRunning", true);
        nemesisStateManager.NavAgent.speed = nemesisStateManager.NemesisMovement.ChaseSpeed;

        // The cached verdict is deliberately NOT invalidated here, even though it may be up to
        // RouteVerdictInterval old. This state and Chasing decide on complementary readings of
        // the same value — "the lift is on the way" sends it here, "it is not" sends it back — so
        // what keeps them from trading the Nemesis back and forth is that they read the SAME
        // number, not that either reads a fresh one. Re-measuring on entry would let the answer
        // change between the frame that decided to come here and the first frame spent here,
        // which is precisely the flip this state exists to stop.
        if (TryGetBelief(out Vector3 belief)) believedTarget = belief;
        else                                  believedTarget = nemesisStateManager.transform.position;
    }

    public override void ExitState()
    {
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
        // The agent is switched off for the whole ride — NemesisElevatorUser owns the Nemesis
        // from the moment it steps onto the link until it is warped off at the far landing. There
        // is nothing to ask of a disabled agent, and asking logs an error per frame.
        //
        // Note the timer keeps running through the ride on purpose: a lift stuck because the
        // player is riding it should eventually give up, and that is exactly what
        // ElevatorCommitTime is for.
        sinceLastSensed += Time.deltaTime;

        if (nemesisStateManager.HasVisualTarget || nemesisStateManager.HasAudioTarget)
        {
            sinceLastSensed = 0f;
            if (TryGetBelief(out Vector3 fresh)) believedTarget = fresh;
        }

        if (!nemesisStateManager.IsAgentReady) return;

        SO_NemesisData data = nemesisStateManager.NemesisData;
        float commitTime = data != null ? data.ElevatorCommitTime : 12f;

        // Out of patience. Searching and not Patrolling: it still has a rough idea of where the
        // player was, and a sweep is a better use of that than filing it away.
        if (sinceLastSensed >= commitTime)
        {
            NextState = NemesisStateManager.ENemesisState.Searching;
            return;
        }

        NavMeshAgent agent = nemesisStateManager.NavAgent;
        agent.destination = believedTarget;

        // Re-measure the route. This is the exit condition and it is self-correcting: the trip is
        // over exactly when the lift stops being part of the way there, which covers arriving
        // upstairs, the player coming down, and the player moving somewhere the lift does not
        // help with — without any of those needing to be detected separately.
        if (!nemesisStateManager.TryGetThrottledRoute(believedTarget, out NemesisNav.NavRoute route))
        {
            // The belief does not land on the NavMesh any more. Nothing to walk towards.
            NextState = NemesisStateManager.ENemesisState.Searching;
            return;
        }

        if (route.CrossesLink) return;

        // No lift on the way any more.
        if (!route.IsComplete)
        {
            // Unreachable by any route. Not a vertical problem, so this state cannot help.
            NextState = NemesisStateManager.ENemesisState.Searching;
            return;
        }

        // Same floor and reachable on foot: hand back to the normal pursuit, which owns the
        // capture.
        NextState = nemesisStateManager.HasVisualTarget
            ? NemesisStateManager.ENemesisState.Chasing
            : NemesisStateManager.ENemesisState.Searching;
    }

    /// <summary>
    /// Where the Nemesis currently believes the player is: seen first, heard second.
    ///
    /// Same order and same "memory, not state" semantics as the patrol bias in NemesisController:
    /// HasLastKnownPosition is checked rather than HasVisualTarget, because a belief that is no
    /// longer being refreshed is still the reason this trip started.
    /// </summary>
    private bool TryGetBelief(out Vector3 position)
    {
        position = Vector3.zero;

        FieldOfView view = nemesisStateManager.FieldOfView;
        if (view != null && view.HasLastKnownPosition)
        {
            position = view.LastKnownPosition;
            return true;
        }

        FieldOfListening listening = nemesisStateManager.FieldOfListening;
        if (listening != null && listening.HasLastKnownPosition)
        {
            position = listening.LastKnownPosition;
            return true;
        }

        return false;
    }
}
