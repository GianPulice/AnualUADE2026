using UnityEngine;

/// <summary>
/// The Nemesis reached the player.
///
/// Per spec, this state's job stops at calling player.OnCaptured() — it does not decide when
/// the checkpoint loads or when the run ends. CheckpointManager (standing in for the spec's
/// "Sistema de Guardado") reacts to that call on its own and, once it is done, notifies the
/// Nemesis back through NemesisStateManager.HasReceivedRespawnNotification. Only then does this
/// state start counting down the post-checkpoint grace period before warping to a random spawn
/// point (or waypoint, if none are configured) and returning to Patrolling.
///
/// If there is nowhere to respawn to, CheckpointManager falls back to the old defeat screen on
/// its own and no notification ever arrives — this state simply stays parked, which is correct
/// since the run is over and the scene is about to reload from the UI.
///
/// A PLAYER IN A HIDING SPOT IS PULLED OUT FIRST (plan §3.5): for SO_NemesisData.HiddenPullOutTime
/// the Nemesis stands at the spot opening the locker or reaching under the table, and only then
/// calls OnCaptured(). There is no pull-out animation yet — the grab plays — but the beat is the
/// design: from inside, the player sees the monster at the door before the hands arrive.
/// </summary>
public class NemesisCatchState : BaseState<NemesisStateManager.ENemesisState>
{
    // Private and never serialised, so a phase can be inserted anywhere — unlike the state and
    // predicate enums, which designer assets store as integers.
    private enum ECatchPhase
    {
        PullingOut,
        WaitingForCheckpoint,
        Grace,
    }

    private NemesisStateManager nemesisStateManager;
    private PlayerStateManager player;

    private ECatchPhase phase;
    private float graceTimer;
    private float pullOutTimer;

    public NemesisCatchState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        NextState = StateKey;
        phase = ECatchPhase.WaitingForCheckpoint;
        graceTimer = 0f;
        pullOutTimer = 0f;
        nemesisStateManager.BeginCapture();

        player = nemesisStateManager.FieldOfView.GetCurrentTarget();
        if (player == null)
        {
            // Used to return here leaving NextState == Catch, with an empty UpdateState and no
            // transition out: the Nemesis stayed frozen in Catch for the rest of the run.
            // Searching is the honest resolution — it thought it had someone and it does not.
            // The one decision this state still takes for itself, and it is not a decision about
            // the world — it is this state reporting that it cannot execute. NemesisDecision put
            // the Nemesis here because the player was in reach a moment ago; only the state that
            // tried to grab them can discover that there is nobody to grab.
            //
            // StateManager.TransitionToState re-reads GetNextState right after EnterState for
            // exactly this case, so the rejection resolves in the same frame. Before that it lived
            // one frame in Catch — one frame of red vignette and a crossfade into the capture
            // loop, both of which the player saw.
            Debug.LogWarning("[NemesisCatchState] Entered Catch without a target — nobody to " +
                             "capture. Falling back to Searching.");
            NextState = NemesisStateManager.ENemesisState.Searching;
            return;
        }

        // Hidden: get them out of there first. The body stops where it is — at the door — and turns
        // to the spot; the player is left alone, still inside, still looking out through the slats.
        if (player.CurrentHidingSpot != null && nemesisStateManager.NemesisData.HiddenPullOutTime > 0f)
        {
            phase = ECatchPhase.PullingOut;
            HoldBody();
            FaceTowards(player.transform.position);
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Grabbing, 0f);
            return;
        }

        Capture();
    }

    /// <summary>The one call the spec allows: from here on the Nemesis waits, it does not act.</summary>
    private void Capture()
    {
        phase = ECatchPhase.WaitingForCheckpoint;
        player.OnCaptured();

        // Refused: a cinematic has the player frozen (a module blowing up, the wake-up) and
        // OnCaptured ignores a frozen player, so no respawn notification is ever coming. Waiting
        // for it parked the Nemesis here for the rest of the run. Same as "nobody to capture"
        // above: the state reporting it cannot execute; the cooldown this exit opens keeps it
        // from grabbing straight back.
        if (!player.IsRecoveringFromCapture)
        {
            NextState = NemesisStateManager.ENemesisState.Chasing;
            return;
        }

        FaceEachOther();
        nemesisStateManager.SetGait(NemesisStateManager.EGait.Grabbing, 0f);
    }

    /// <summary>Stops the agent where it stands. The capture itself never needed this — it is
    /// entered already on top of the player — but the pull-out is entered at a door and must not
    /// keep sliding along whatever path brought it there.</summary>
    private void HoldBody()
    {
        if (!nemesisStateManager.IsAgentReady) return;

        nemesisStateManager.NavAgent.ResetPath();
        nemesisStateManager.NavAgent.velocity = Vector3.zero;
    }

    /// <summary>Turns the Nemesis alone towards a point, yaw only.</summary>
    private void FaceTowards(Vector3 point)
    {
        Transform nemesis = nemesisStateManager.transform;
        Vector3 toPoint = point - nemesis.position;
        toPoint.y = 0f;

        if (toPoint.sqrMagnitude <= 0.0001f) return;

        nemesis.rotation = Quaternion.LookRotation(toPoint);
    }

    /// <summary>
    /// Makes the Nemesis and the player face each other, yaw only.
    /// Transform.LookAt rotates on all 3 axes, so with a height difference between the two
    /// it tilted them forwards or backwards.
    /// </summary>
    private void FaceEachOther()
    {
        Transform nemesis = nemesisStateManager.transform;
        Transform playerTransform = player.transform;

        Vector3 toPlayer = playerTransform.position - nemesis.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude <= 0.0001f) return;   // One on top of the other: no usable direction.

        nemesis.rotation         = Quaternion.LookRotation(toPlayer);
        playerTransform.rotation = Quaternion.LookRotation(-toPlayer);
    }

    public override void ExitState()
    {
        player = null;

        // Opens the no-re-entry window. Runs on every exit, the "nobody to capture" bail out
        // included: without it that fallback goes Catch -> Searching -> Chasing -> Catch again on
        // the next frame, because the Nemesis is still standing on top of the player.
        nemesisStateManager.BeginCatchCooldown();
    }

    public override void UpdateState()
    {
        switch (phase)
        {
            case ECatchPhase.PullingOut:
                // Scaled time: a gameplay beat, and the state manager's Update does not run while
                // paused anyway.
                pullOutTimer += Time.deltaTime;
                if (pullOutTimer < nemesisStateManager.NemesisData.HiddenPullOutTime) return;

                // They climbed out while the door was being opened and are no longer within reach.
                // Nobody is in its hands, so report it the way the "nobody to capture" case does —
                // the ladder picks the chase up, and the catch cooldown this exit opens keeps it
                // from grabbing straight back on the next frame.
                if (player == null ||
                    (player.CurrentHidingSpot == null && !nemesisStateManager.CanReachPlayerNow))
                {
                    NextState = NemesisStateManager.ENemesisState.Chasing;
                    return;
                }

                Capture();
                break;

            case ECatchPhase.WaitingForCheckpoint:
                // Passive: CheckpointManager is off doing its own thing on its own timing
                // (cutscene delay, then respawn-or-defeat). This state does not poll it, it
                // just waits for the explicit notification.
                if (!nemesisStateManager.HasReceivedRespawnNotification) return;

                graceTimer = 0f;
                phase = ECatchPhase.Grace;
                break;

            case ECatchPhase.Grace:
                // Unscaled: a menu opened during the grace window (pause, inventory) sets
                // Time.timeScale = 0, and this countdown must not be held hostage by that — it
                // is a real-world "you have X seconds" window, not a gameplay-paced one.
                graceTimer += Time.unscaledDeltaTime;
                if (graceTimer < nemesisStateManager.CaptureGracePeriod) return;

                nemesisStateManager.RepositionAfterCapture();

                // AFTER the warp, not before: this is what CaptureFadeView waits for to reveal
                // the screen. Firing it earlier would defeat the entire point — the Nemesis would
                // already be mid-teleport, or worse, still visibly standing at the capture spot,
                // while the screen said it was safe to look.
                NemesisEvents.CaptureResolved();

                NextState = NemesisStateManager.ENemesisState.Patrolling;
                break;
        }
    }
}
