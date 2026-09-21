using UnityEngine;

/// <summary>
/// During the escape a capture ends the run: no checkpoint, no respawn, game over.
///
/// One job: swap what a capture means while the escape is on. It does not decide when the escape
/// starts or ends (the director does), and it does not change how the Nemesis catches you.
///
/// It works by taking <see cref="CheckpointManager"/> out of the way. That manager subscribes to
/// <see cref="PlayerEvents.OnPlayerCaptured"/> in its OnEnable and unsubscribes in its OnDisable,
/// so disabling the component is enough to stop the respawn cleanly. Deleting the checkpoint or
/// leaving the section without one does NOT work: the manager records a fallback spawn the moment
/// the player registers, so it always has somewhere to send them.
///
/// Left out on purpose: no defeat cinematic of its own. If you want one, assign a
/// <see cref="IGameOverPresenter"/> to <see cref="GameResultManager.GameOverPresenter"/> and it
/// plays before the result screen, here as everywhere else.
///
/// Sits on the escape sequence object.
/// </summary>
public class EscapeCaptureGameOver : MonoBehaviour
{
    private bool active;
    private bool checkpointsWereEnabled;

    public bool IsActive => active;

    /// <summary>Arms the game over. Call it on the same frame the player gets control back: the
    /// cinematic itself cannot be interrupted by a capture, because a disabled player never
    /// reports one.</summary>
    public void Begin()
    {
        if (active) return;
        active = true;

        if (CheckpointManager.Exists)
        {
            CheckpointManager manager = CheckpointManager.Instance;
            checkpointsWereEnabled = manager.enabled;
            manager.enabled = false;
        }

        PlayerEvents.OnPlayerCaptured += HandleCaptured;
    }

    /// <summary>Gives checkpoints back. Called when the escape ends, whichever way it ends.</summary>
    public void End()
    {
        if (!active) return;
        active = false;

        PlayerEvents.OnPlayerCaptured -= HandleCaptured;

        if (checkpointsWereEnabled && CheckpointManager.Exists)
            CheckpointManager.Instance.enabled = true;
    }

    private void OnDestroy() => End();

    private void HandleCaptured(PlayerStateManager player)
    {
        // Stats come from the module session so the result screen shows the same time and count
        // as every other ending. GameResultManager guards against a second report by itself.
        if (ModuleManager.Exists)
            GameResultManager.ReportGameOver(ModuleManager.Instance.SessionTime,
                                             ModuleManager.Instance.GetResolvedCount());
        else
            GameResultManager.ReportGameOver(0f, 0);
    }
}
