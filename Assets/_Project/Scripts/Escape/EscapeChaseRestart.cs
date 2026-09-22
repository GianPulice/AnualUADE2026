using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// What a capture during the escape's chase means, from <see cref="SO_EscapeSequenceConfig.CaptureOutcome"/>:
///
///   GameOver      the run ends: once the grab has played, the defeat screen.
///   RestartChase  the chase starts over: the player is sent back to where it starts and it plays
///                 again from there.
///
/// One job: turn the ordinary capture flow into one of those two, and report its moments. It does
/// not reposition anything itself — <see cref="EscapeSequenceDirector"/> reacts to the events below
/// (the fog, the gate, the Nemesis) — and it does not change how the Nemesis catches you.
///
/// GAME OVER works by taking <see cref="CheckpointManager"/> out of the way. That manager subscribes
/// to <see cref="PlayerEvents.OnPlayerCaptured"/> in its OnEnable and unsubscribes in its OnDisable,
/// so disabling the component is enough to stop the respawn cleanly (deleting the checkpoint does
/// NOT work: the manager records a fallback spawn the moment the player registers). The defeat goes
/// through <see cref="ModuleManager.ReportLoss"/>, the same one the manager's own no-respawn
/// fallback uses — not ReportGameOver, which plays the module explosion and deletes the save.
///
/// RESTART works WITH <see cref="CheckpointManager"/>, not around it: <see cref="Begin"/> makes the
/// chase's own checkpoint (a <see cref="Checkpoint"/> on Player_Spot that never activates by
/// itself) the active one, so the normal capture does the rest — the capture fade, the respawn at
/// that spot, the capture cost, the player getting up, the Nemesis being told. Its snapshot is taken
/// there, with the escape already under way, so the rollback cannot undo the three cores. Its
/// moments, in the order the capture flow raises them:
///   <see cref="Respawned"/>       the player is back at the spot, the screen still black.
///   <see cref="NemesisFreed"/>    the Nemesis finished its capture (it has just warped away): it
///                                 can be placed. One frame after the event, once its own state
///                                 machine is done with the frame.
///   <see cref="ControlRegained"/> the player is up and has control again.
///
/// The choice has gone back and forth (game over in ae59f620, restart in 4a163d72, game over again
/// with the 22/09 script), which is why it is a setting and not a rewrite.
///
/// Sits on the escape sequence object. (Stored in the scene under its old class name,
/// EscapeCaptureGameOver: same script, same GUID.)
/// </summary>
public class EscapeChaseRestart : MonoBehaviour
{
    [Tooltip("Sólo en Game Over: segundos entre el agarre y la pantalla de derrota, para que se vea " +
             "el agarre. El mismo tiempo que espera CheckpointManager antes de un respawn.")]
    [SerializeField, Min(0f)] private float gameOverDelay = 1.5f;

    /// <summary>The player was put back at the start of the chase (the screen is still black).</summary>
    public event Action Respawned;

    /// <summary>The Nemesis let go and warped away: it can be placed for the new chase.</summary>
    public event Action NemesisFreed;

    /// <summary>The player is back on their feet with control.</summary>
    public event Action ControlRegained;

    private bool active;
    private EscapeCaptureOutcome outcome;
    private bool awaitingControl;
    private bool nemesisFreedPending;

    private bool checkpointsDisabled;
    private bool checkpointsWereEnabled;
    private CancellationTokenSource defeatCts;

    public bool IsActive => active;

    /// <summary>
    /// Arms the capture outcome for the chase. Call it on the frame the chase starts. Safe to call
    /// again. With <see cref="EscapeCaptureOutcome.RestartChase"/> a capture sends the player to
    /// <paramref name="chaseStart"/>; with <see cref="EscapeCaptureOutcome.GameOver"/> it ends the run.
    /// </summary>
    public void Begin(Checkpoint chaseStart, EscapeCaptureOutcome captureOutcome)
    {
        if (active && outcome != captureOutcome) End();
        outcome = captureOutcome;

        if (outcome == EscapeCaptureOutcome.GameOver) BeginGameOver();
        else BeginRestart(chaseStart);
    }

    /// <summary>Stops reporting, and gives checkpoints back. Called when the escape ends, whichever
    /// way it ends.</summary>
    public void End()
    {
        if (!active) return;
        active = false;
        awaitingControl = false;
        nemesisFreedPending = false;

        CheckpointManager.OnRespawned -= HandleRespawned;
        NemesisEvents.OnCaptureResolved -= HandleCaptureResolved;
        PlayerEvents.OnPlayerCaptured -= HandleCapturedGameOver;

        CancelDefeat();

        if (checkpointsDisabled && checkpointsWereEnabled && CheckpointManager.Exists)
            CheckpointManager.Instance.enabled = true;
        checkpointsDisabled = false;
    }

    private void OnDestroy() => End();

    // ── Game over ───────────────────────────────────────────────────────────

    private void BeginGameOver()
    {
        if (!active)
        {
            active = true;
            PlayerEvents.OnPlayerCaptured += HandleCapturedGameOver;
        }

        if (!checkpointsDisabled && CheckpointManager.Exists)
        {
            CheckpointManager manager = CheckpointManager.Instance;
            checkpointsWereEnabled = manager.enabled;
            manager.enabled = false;
            checkpointsDisabled = true;
        }
    }

    private void HandleCapturedGameOver(PlayerStateManager player)
    {
        if (defeatCts != null) return;

        defeatCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        ReportDefeatAfterAsync(gameOverDelay, defeatCts.Token).Forget();
    }

    // Unscaled, like CheckpointManager's own wait: the grab plays out whatever the time scale.
    private async UniTaskVoid ReportDefeatAfterAsync(float seconds, CancellationToken token)
    {
        if (seconds > 0f)
            await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime,
                                cancellationToken: token);

        // Stats come from the module session so the result screen shows the same time and count
        // as every other ending. GameResultManager guards against a second report by itself.
        if (ModuleManager.Exists) ModuleManager.Instance.ReportLoss();
        else GameResultManager.ReportLoss(0f, 0);
    }

    private void CancelDefeat()
    {
        defeatCts?.Cancel();
        defeatCts?.Dispose();
        defeatCts = null;
    }

    // ── Restart ─────────────────────────────────────────────────────────────

    private void BeginRestart(Checkpoint chaseStart)
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
        if (!active || outcome != EscapeCaptureOutcome.RestartChase) return;

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
