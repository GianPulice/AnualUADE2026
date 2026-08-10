using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Tracks which <see cref="Checkpoint"/> is active and reacts to a capture on its own —
/// subscribed to <see cref="PlayerEvents.OnPlayerCaptured"/> instead of being called directly by
/// the Nemesis. This is the "Sistema de Guardado" side of the spec's capture flow:
/// Nemesis -> Player.OnCaptured -> save system loads checkpoint -> Nemesis gets notified back
/// through <see cref="OnRespawned"/>. The Nemesis never reaches into this class.
///
/// This is what turns the capture from a hard Game Over into a cost: the run continues, the
/// player reappears at the last safe point, puzzle progress rolls back to the state it had when
/// that point was reached, and the active module timer takes a fixed hit.
///
/// If no checkpoint has been reached yet, the capture falls back to the old defeat screen
/// instead. A level with no checkpoints placed keeps behaving exactly as it did before, rather
/// than trapping the player in a captured pose.
/// </summary>
public class CheckpointManager : Singleton<CheckpointManager>
{
    [Header("Respawn")]
    [Tooltip("Roll puzzle progress back to the snapshot taken when the active checkpoint was " +
             "reached. Off = the player respawns but keeps everything solved since.")]
    [SerializeField] private bool restorePuzzleProgress = true;

    [Tooltip("Subtract the capture penalty (SO_Movement.CaptureModuleTimePenalty) from the " +
             "running module timers on respawn.")]
    [SerializeField] private bool applyModuleTimePenalty = true;

    [Header("Capture")]
    [Tooltip("Seconds between the Nemesis grabbing the player and the checkpoint actually " +
             "loading. Stands in for the capture animation, audio stinger and brief cinematic " +
             "the spec calls for (none of which exist yet) — deliberately owned here and not by " +
             "the Nemesis, which per spec only calls Player.OnCaptured() and stops there.")]
    [SerializeField] private float captureCutsceneDelay = 1.5f;

    private Checkpoint activeCheckpoint;
    private PuzzleSnapshot activeSnapshot;

    /// <summary>Unity's null check covers the destroyed-on-scene-unload case here.</summary>
    public bool HasActiveCheckpoint => activeCheckpoint != null;
    public Checkpoint ActiveCheckpoint => activeCheckpoint;

    /// <summary>A checkpoint became the active one.</summary>
    public static event Action<Checkpoint> OnCheckpointActivated;

    /// <summary>The player was respawned at the given checkpoint after a capture.</summary>
    public static event Action<Checkpoint> OnRespawned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnCheckpointActivated = null;
        OnRespawned = null;
    }

    private void Awake()
    {
        CreateSingleton(true);
    }

    private void OnEnable()  => PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
    private void OnDisable() => PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;

    /// <summary>
    /// Reacts to a capture without the Nemesis being involved past OnCaptured(). This is the
    /// save-system side of the flow the spec describes — the Nemesis finds out afterwards
    /// through <see cref="OnRespawned"/>, it never calls in here directly.
    /// </summary>
    private void HandlePlayerCaptured(PlayerStateManager player) => RunCaptureSequence().Forget();

    private async UniTaskVoid RunCaptureSequence()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(captureCutsceneDelay),
                            DelayType.UnscaledDeltaTime,
                            cancellationToken: this.GetCancellationTokenOnDestroy());

        if (!RespawnAtActiveCheckpoint()) FallbackToDefeat();
    }

    /// <summary>
    /// Nothing to respawn to (no checkpoint reached yet, or no registered player): ends the run
    /// the old way. Lives here and not on the Nemesis for the same reason as the rest of this
    /// file — the Nemesis does not reach into save/UI itself.
    /// </summary>
    private void FallbackToDefeat()
    {
        if (InventoryManagerUI.Exists) InventoryManagerUI.Instance.ReportLoss();
        else GameResultManager.ReportLoss(0f, 0);
    }

    public void ActivateCheckpoint(Checkpoint checkpoint)
    {
        if (checkpoint == null) return;
        if (ReferenceEquals(activeCheckpoint, checkpoint)) return;

        activeCheckpoint = checkpoint;

        // Snapshot at activation, not at capture: the point of the rollback is to undo whatever
        // the player did between the checkpoint and getting caught.
        activeSnapshot = PuzzleStateManager.Exists
            ? PuzzleStateManager.Instance.Snapshot()
            : null;

        OnCheckpointActivated?.Invoke(checkpoint);
    }

    /// <summary>
    /// Moves the player back to the active checkpoint and hands control back.
    /// </summary>
    /// <returns>
    /// false when there is nothing to respawn to (no checkpoint reached yet, or no registered
    /// player) — <see cref="RunCaptureSequence"/> falls back to the defeat screen in that case.
    /// Kept public so other callers (e.g. a debug menu) can still trigger a respawn directly.
    /// </returns>
    public bool RespawnAtActiveCheckpoint()
    {
        if (activeCheckpoint == null) return false;

        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null)
        {
            Debug.LogWarning("[CheckpointManager] Respawn requested with no player registered.", this);
            return false;
        }

        if (restorePuzzleProgress && activeSnapshot != null && PuzzleStateManager.Exists)
            PuzzleStateManager.Instance.RestoreSnapshot(activeSnapshot);

        Transform target = activeCheckpoint.RespawnPoint;
        player.TeleportTo(target.position, target.rotation);

        if (applyModuleTimePenalty) ApplyCaptureCost(player);

        // Last: clearing IsDisabled is what lets PlayerDisabledState hand control back, and the
        // player must already be at the checkpoint by then or it regains control mid-teleport.
        player.IsDisabled = false;

        OnRespawned?.Invoke(activeCheckpoint);
        return true;
    }

    /// <summary>
    /// The cost of being caught: a fixed number of seconds off the running module timers.
    /// The amount is designer data, so it lives on the player's SO_Movement asset rather than
    /// being hard-coded here.
    /// </summary>
    private void ApplyCaptureCost(PlayerStateManager player)
    {
        if (player.Movement == null) return;

        float penalty = player.Movement.CaptureModuleTimePenalty;
        if (penalty <= 0f) return;

        if (!InventoryManagerUI.Exists) return;
        InventoryManagerUI.Instance.ApplyCaptureTimePenalty(penalty);
    }
}
