using UnityEngine;

/// <summary>
/// The Nemesis reached the player.
///
/// Per spec, this state's job stops at calling player.OnCaptured() — it does not decide when
/// the checkpoint loads or when the run ends. CheckpointManager (standing in for the spec's
/// "Sistema de Guardado") reacts to that call on its own and, once it is done, notifies the
/// Nemesis back through NemesisStateManager.HasReceivedRespawnNotification. Only then does this
/// state start counting down the post-checkpoint grace period before warping to a random
/// waypoint and returning to Patrolling.
///
/// If there is nowhere to respawn to, CheckpointManager falls back to the old defeat screen on
/// its own and no notification ever arrives — this state simply stays parked, which is correct
/// since the run is over and the scene is about to reload from the UI.
/// </summary>
public class NemesisCatchState : BaseState<NemesisStateManager.ENemesisState>
{
    private enum ECatchPhase
    {
        WaitingForCheckpoint,
        Grace,
    }

    private NemesisStateManager nemesisStateManager;
    private PlayerStateManager player;

    private ECatchPhase phase;
    private float graceTimer;

    public NemesisCatchState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        NextState = StateKey;
        phase = ECatchPhase.WaitingForCheckpoint;
        graceTimer = 0f;
        nemesisStateManager.BeginCapture();

        player = nemesisStateManager.FieldOfView.GetCurrentTarget();
        if (player == null)
        {
            // Used to return here leaving NextState == Catch, with an empty UpdateState and no
            // transition out: the Nemesis stayed frozen in Catch for the rest of the run.
            // Searching is the honest resolution — it thought it had someone and it does not.
            Debug.LogWarning("[NemesisCatchState] Entered Catch without a target — nobody to " +
                             "capture. Falling back to Searching.");
            NextState = NemesisStateManager.ENemesisState.Searching;
            return;
        }

        // The one call the spec allows: from here on the Nemesis waits, it does not act.
        player.OnCaptured();
        FaceEachOther();
        nemesisStateManager.AnimController.SetBool("isCatching", true);
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
        nemesisStateManager.AnimController.SetBool("isCatching", false);
        player = null;
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
        switch (phase)
        {
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

                nemesisStateManager.RepositionAtRandomWayPoint();
                NextState = NemesisStateManager.ENemesisState.Patrolling;
                break;
        }
    }
}
