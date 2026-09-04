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
/// If no checkpoint has been reached yet, the player goes back to where the run started instead
/// — recorded automatically the first time a player registers, so a level with no checkpoints
/// placed at all still plays. The old defeat screen is now only reached when there is genuinely
/// nowhere to send the player (no registered player, or no recorded spawn).
/// </summary>
public class CheckpointManager : Singleton<CheckpointManager>
{
    [Header("Respawn")]
    [Tooltip("Where the player reappears when it has not reached any checkpoint yet.\n\n" +
             "Leave empty and the position the player started the level at is captured " +
             "automatically — which is the usual case and needs no setup at all. Assign a " +
             "Transform only to send the player somewhere other than where it spawned.")]
    [SerializeField] private Transform playerSpawnPoint;

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

    // Where the run started, used when the player is caught before reaching any checkpoint.
    private Vector3 fallbackPosition;
    private Quaternion fallbackRotation;
    private bool hasFallbackSpawn;

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

    private void OnEnable()
    {
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
        PlayerRegistry.SubscribeAndCatchUp(CaptureFallbackSpawn);
    }

    private void OnDisable()
    {
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        PlayerRegistry.Unsubscribe(CaptureFallbackSpawn);
    }

    /// <summary>
    /// Records where the run started, so a capture before the first checkpoint has somewhere to
    /// send the player instead of ending the run.
    ///
    /// Re-read on every registration rather than latched once. A player only registers from its
    /// own Awake, so this fires for a genuinely new player instance — a scene reload or a new
    /// level — and never mid-run: a checkpoint respawn moves the existing player with TeleportTo
    /// and does not re-register it. Latching would leave this manager (which survives scene loads)
    /// pointing at the previous level's coordinates.
    /// </summary>
    private void CaptureFallbackSpawn(PlayerStateManager player)
    {
        if (player == null) return;

        // A scene-owned spawn Transform is destroyed by a scene change while this manager lives
        // on; Unity's null check catches that and the new player's own position takes over.
        Transform source = playerSpawnPoint != null ? playerSpawnPoint : player.transform;
        fallbackPosition = source.position;
        fallbackRotation = source.rotation;
        hasFallbackSpawn = true;
    }

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
    /// Nothing to respawn to at all — no registered player, or no checkpoint and no recorded
    /// start position: ends the run the old way. Lives here and not on the Nemesis for the same
    /// reason as the rest of this file — the Nemesis does not reach into save/UI itself.
    /// </summary>
    private void FallbackToDefeat()
    {
        // ModuleManager is the one tracking session time and resolved count, so the result screen
        // shows the same stats as the timer-driven game over.
        if (ModuleManager.Exists) ModuleManager.Instance.ReportLoss();
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
    /// Moves the player back to the active checkpoint and hands control back. With no checkpoint
    /// reached yet it falls back to where the run started (see <see cref="CaptureFallbackSpawn"/>),
    /// so getting caught early costs progress instead of ending the run.
    /// </summary>
    /// <returns>
    /// false only when there is genuinely nowhere to go: no registered player, or no checkpoint
    /// AND no recorded spawn — <see cref="RunCaptureSequence"/> falls back to the defeat screen in
    /// that case. Kept public so other callers (e.g. a debug menu) can trigger a respawn directly.
    /// </returns>
    public bool RespawnAtActiveCheckpoint()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null)
        {
            Debug.LogWarning("[CheckpointManager] Respawn requested with no player registered.", this);
            return false;
        }

        if (activeCheckpoint != null)
        {
            if (restorePuzzleProgress && activeSnapshot != null && PuzzleStateManager.Exists)
                PuzzleStateManager.Instance.RestoreSnapshot(activeSnapshot);

            Transform target = activeCheckpoint.RespawnPoint;
            player.TeleportTo(target.position, target.rotation);
        }
        else if (hasFallbackSpawn)
        {
            // Caught before reaching any checkpoint. No snapshot to restore — nothing was ever
            // taken, and whatever the player solved on the way here it keeps.
            player.TeleportTo(fallbackPosition, fallbackRotation);
        }
        else
        {
            return false;
        }

        if (applyModuleTimePenalty) ApplyCaptureCost(player);

        // Last: clearing IsDisabled is what lets PlayerDisabledState hand control back, and the
        // player must already be at the respawn point by then or it regains control mid-teleport.
        player.IsDisabled = false;

        // NOTE: null when this was a fallback respawn — there is no Checkpoint object involved.
        // The only listener today (NemesisStateManager) ignores the argument; anything that
        // starts reading it has to handle null.
        OnRespawned?.Invoke(activeCheckpoint);
        return true;
    }

    /// <summary>
    /// The cost of being caught: a fixed number of seconds off the running module timer.
    /// The amount is designer data, so it lives on the player's SO_Movement asset rather than
    /// being hard-coded here.
    ///
    /// ModuleManager clamps the timer at 0 rather than exploding the module on the spot; its own
    /// tick takes it negative and explodes it on the next frame, so a penalty big enough to run a
    /// module out still does, one frame later.
    /// </summary>
    private void ApplyCaptureCost(PlayerStateManager player)
    {
        if (player.Movement == null) return;

        float penalty = player.Movement.CaptureModuleTimePenalty;
        if (penalty <= 0f) return;

        if (!ModuleManager.Exists) return;
        ModuleManager.Instance.ApplyTimePenalty(penalty);
    }
}
