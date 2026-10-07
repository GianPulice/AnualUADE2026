using UnityEngine;

/// <summary>
/// [E] completes the puzzle of its <see cref="SO_PuzzleActivatorData"/>, once, and only after the
/// required puzzle (if any). Central Puzzle 2 — SP3: the stabilization mechanism inside the access
/// room.
///
/// The child called "Handle", if there is one, is turned on its local X once used, so the
/// mechanism reads as done — immediately when the level loads with the puzzle already completed.
/// </summary>
public class PuzzleActivatorInteractable : BaseRangeInteractable, IPuzzleInteractable
{
    [SerializeField] private SO_PuzzleActivatorData activatorData;

    [Tooltip("Degrees the Handle child turns on its local X once used.")]
    [SerializeField] private float usedHandleAngle = 90f;

    private Transform handle;
    private Quaternion handleRest;

    private bool IsCompleted =>
        activatorData != null && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsPuzzleCompleted(activatorData.PuzzleId);

    private bool IsUnlocked =>
        activatorData != null &&
        (string.IsNullOrWhiteSpace(activatorData.RequiredPuzzleId) ||
         (PuzzleStateManager.Exists && PuzzleStateManager.Instance.IsPuzzleCompleted(activatorData.RequiredPuzzleId)));

    protected override void Awake()
    {
        base.Awake();
        handle = transform.Find("Handle");
        if (handle != null) handleRest = handle.localRotation;

        if (activatorData == null)
            Debug.LogError($"[{nameof(PuzzleActivatorInteractable)}] No SO_PuzzleActivatorData on '{name}'.", this);
    }

    private void Update()
    {
        // Read live: a checkpoint rollback can undo the completion.
        if (handle != null)
            handle.localRotation = IsCompleted ? handleRest * Quaternion.Euler(usedHandleAngle, 0f, 0f) : handleRest;
    }

    public override string GetPromptText()
    {
        if (activatorData == null) return "Unconfigured mechanism";
        return IsCompleted ? string.Empty : activatorData.PromptText;
    }

    public override string GetInfoText() =>
        activatorData != null && !IsCompleted && !IsUnlocked ? activatorData.LockedInfoText : string.Empty;

    public override bool IsRepeatable() => false;

    public override bool IsFinished() => IsCompleted;

    protected override bool CanInteractInCloseRange() => activatorData != null && !IsCompleted && IsUnlocked;

    protected override void OnInteract()
    {
        if (!PuzzleStateManager.Exists)
        {
            Debug.LogWarning($"[{nameof(PuzzleActivatorInteractable)}] No PuzzleStateManager — " +
                             $"'{activatorData.PuzzleId}' was not recorded.", this);
            return;
        }

        if (AudioManager.Exists && !string.IsNullOrEmpty(activatorData.UseSoundId))
            AudioManager.Instance.PlaySFX(activatorData.UseSoundId, transform.position);

        PuzzleStateManager.Instance.SetPuzzleCompleted(activatorData.PuzzleId);

        if (AudioManager.Exists && !string.IsNullOrEmpty(activatorData.CompletedSoundId))
            AudioManager.Instance.PlaySFX(activatorData.CompletedSoundId, transform.position);

        Debug.Log($"[{nameof(PuzzleActivatorInteractable)}] Puzzle completed: {activatorData.PuzzleId}");
    }
}
