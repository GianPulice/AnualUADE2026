using System;
using UnityEngine;

/// <summary>
/// During the escape a capture restarts the chase, not the run: the player is sent back to where
/// the chase starts and it plays again from there.
///
/// One job: turn the ordinary capture flow into "from the top of the chase", and report its
/// moments. It does not reposition anything itself — <see cref="EscapeSequenceDirector"/> reacts to
/// the events below (the fog, the gate, the Nemesis) — and it does not change how the Nemesis
/// catches you.
///
/// It works WITH <see cref="CheckpointManager"/>, not around it: <see cref="Begin"/> makes the
/// chase's own checkpoint (a <see cref="Checkpoint"/> on Player_Spot that never activates by
/// itself) the active one, so the normal capture does the rest — the capture fade, the respawn at
/// that spot, the capture cost, the player getting up, the Nemesis being told. Its snapshot is taken
/// there, with the escape already under way, so the rollback cannot undo the three cores.
///
/// The moments, in the order the capture flow raises them:
///   <see cref="Respawned"/>       the player is back at the spot, the screen still black.
///   <see cref="NemesisFreed"/>    the Nemesis finished its capture (it has just warped away): it
///                                 can be placed. One frame after the event, once its own state
///                                 machine is done with the frame.
///   <see cref="ControlRegained"/> the player is up and has control again.
///
/// It used to take CheckpointManager out of the way so that a capture was a game over.
///
/// Sits on the escape sequence object.
/// </summary>
public class EscapeChaseRestart : MonoBehaviour
{
    /// <summary>The player was put back at the start of the chase (the screen is still black).</summary>
    public event Action Respawned;

    /// <summary>The Nemesis let go and warped away: it can be placed for the new chase.</summary>
    public event Action NemesisFreed;

    /// <summary>The player is back on their feet with control.</summary>
    public event Action ControlRegained;

    private bool active;
    private bool awaitingControl;
    private bool nemesisFreedPending;

    public bool IsActive => active;

    /// <summary>
    /// Arms the restart: from here a capture sends the player to <paramref name="chaseStart"/>.
    /// Call it on the frame the chase starts. Safe to call again.
    /// </summary>
    public void Begin(Checkpoint chaseStart)
    {
        if (!active)
        {
            active = true;
            CheckpointManager.OnRespawned += HandleRespawned;
            NemesisEvents.OnCaptureResolved += HandleCaptureResolved;
        }

        awaitingControl = false;
        nemesisFreedPending = false;

        if (chaseStart == null)
        {
            Debug.LogWarning($"[{nameof(EscapeChaseRestart)}] No chase checkpoint: a capture sends " +
                             "the player to the level's last checkpoint instead of the start of " +
                             "the chase.", this);
            return;
        }

        if (!CheckpointManager.Exists)
        {
            Debug.LogWarning($"[{nameof(EscapeChaseRestart)}] No CheckpointManager in the scene: " +
                             "a capture during the escape has nowhere to send the player.", this);
            return;
        }

        CheckpointManager.Instance.ActivateCheckpoint(chaseStart);
    }

    /// <summary>Stops reporting. Called when the escape ends, whichever way it ends.</summary>
    public void End()
    {
        if (!active) return;
        active = false;
        awaitingControl = false;
        nemesisFreedPending = false;

        CheckpointManager.OnRespawned -= HandleRespawned;
        NemesisEvents.OnCaptureResolved -= HandleCaptureResolved;
    }

    private void OnDestroy() => End();

    private void HandleRespawned(Checkpoint checkpoint)
    {
        awaitingControl = true;
        Respawned?.Invoke();
    }

    // Raised from inside the Nemesis's Catch state, which still has the rest of its frame to run:
    // taking the Nemesis over from here would pull the body out from under it mid-update.
    private void HandleCaptureResolved() => nemesisFreedPending = true;

    private void Update()
    {
        if (!active) return;

        if (nemesisFreedPending)
        {
            nemesisFreedPending = false;
            NemesisFreed?.Invoke();
        }

        if (!awaitingControl) return;

        // IsRecoveringFromCapture covers the whole span, black screen and stand-up included; the
        // respawn itself clears IsDisabled a moment before the player is laid down to get up.
        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null || player.IsRecoveringFromCapture || player.IsImmobilized) return;

        awaitingControl = false;
        ControlRegained?.Invoke();
    }
}
