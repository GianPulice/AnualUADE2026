using UnityEngine;

/// <summary>
/// The stabilization panel of the Ventilation Hub (Central Puzzle 2 — final phase). Visible all
/// along; while sub-puzzles are missing it does not respond to [E] and says how many are left
/// ("Pending systems: X"). With all of them completed, [E] opens the
/// <see cref="StabilizationCheckController"/>, and passing every check completes the hub puzzle —
/// which resolves M2 in ModuleManager. An interrupted sequence completes nothing: the panel can be
/// used again, from the first check.
/// </summary>
public class StabilizationPanelInteractable : BaseRangeInteractable, IPuzzleInteractable
{
    [SerializeField] private SO_StabilizationPuzzleData puzzleData;

    public SO_StabilizationPuzzleData PuzzleData => puzzleData;

    // Read live: a checkpoint restore rewinds PuzzleStateManager and the panel has to follow it.
    private bool IsCompleted =>
        puzzleData != null && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleData.PuzzleId);

    /// <summary>The checks of this panel are on screen right now.</summary>
    public bool IsRunning =>
        StabilizationCheckController.Instance != null &&
        StabilizationCheckController.Instance.IsOpen &&
        StabilizationCheckController.Instance.ActiveData == puzzleData;

    protected override void Awake()
    {
        base.Awake();
        if (puzzleData == null)
            Debug.LogError($"[{nameof(StabilizationPanelInteractable)}] No SO_StabilizationPuzzleData on '{name}'.", this);
    }

    public override string GetPromptText()
    {
        if (puzzleData == null) return "Unconfigured panel";
        return IsCompleted ? string.Empty : puzzleData.StartPrompt;
    }

    public override string GetInfoText()
    {
        if (puzzleData == null || IsCompleted || IsRunning) return string.Empty;
        int pending = puzzleData.PendingCount();
        return pending > 0 ? string.Format(puzzleData.PendingInfoFormat, pending) : string.Empty;
    }

    public override bool IsFinished() => IsCompleted;

    public override bool IsRepeatable() => !IsCompleted;

    protected override bool CanInteractInCloseRange() =>
        puzzleData != null && !IsCompleted && puzzleData.PendingCount() == 0 &&
        StabilizationCheckController.Instance != null && !StabilizationCheckController.Instance.IsOpen;

    protected override void OnInteract()
    {
        if (StabilizationCheckController.Instance == null)
        {
            Debug.LogError($"[{nameof(StabilizationPanelInteractable)}] There is no StabilizationCheckController " +
                           "in the scene (LevelUI).", this);
            return;
        }

        StabilizationCheckController.Instance.Open(puzzleData, HandleFinished);
    }

    private void HandleFinished(bool completed)
    {
        // The panel may be gone (scene unloaded while the overlay was closing).
        if (!completed || this == null || IsCompleted) return;

        if (!PuzzleStateManager.Exists)
        {
            Debug.LogWarning($"[{nameof(StabilizationPanelInteractable)}] No PuzzleStateManager — " +
                             $"'{puzzleData.PuzzleId}' was not recorded, so no module resolves.", this);
            return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(puzzleData.PuzzleId);
        Debug.Log($"[{nameof(StabilizationPanelInteractable)}] Puzzle completed: {puzzleData.PuzzleId}");
    }
}
