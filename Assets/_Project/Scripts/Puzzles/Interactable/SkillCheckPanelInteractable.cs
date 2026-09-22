using UnityEngine;

/// <summary>
/// The Ventilation Hub panel (Central Puzzle 2): [E] opens the skill check, and passing the whole
/// sequence completes the puzzle — the module that declares it (M2) resolves on that, in
/// <see cref="ModuleManager"/>. A cancelled sequence completes nothing and the panel can be used
/// again from the first check.
///
/// Its one job is that hand-off. The sequence itself — needle, penalties, bonuses — is
/// <see cref="SkillCheckController"/>'s, and whether the player has reached the Hub is the level's.
/// </summary>
public class SkillCheckPanelInteractable : BaseRangeInteractable, IPuzzleInteractable
{
    [SerializeField] private SO_SkillCheckPuzzleData puzzleData;

    // Read live rather than cached in Awake: a checkpoint restore rewinds PuzzleStateManager, and
    // the panel has to follow it.
    private bool IsCompleted =>
        puzzleData != null && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleData.PuzzleId);

    private static bool IsCheckOpen =>
        SkillCheckController.Instance != null && SkillCheckController.Instance.IsOpen;

    protected override void Awake()
    {
        base.Awake();
        if (puzzleData == null)
            Debug.LogError($"[{nameof(SkillCheckPanelInteractable)}] No SO_SkillCheckPuzzleData on {name}.", this);
    }

    public override string GetInteractText()
    {
        if (puzzleData == null) return "Unconfigured panel";
        return IsCompleted ? string.Empty : puzzleData.PromptText;
    }

    public override bool IsFinished() => IsCompleted;

    public override bool IsRepeatable() => !IsCompleted;

    protected override bool CanInteractInCloseRange() =>
        puzzleData != null && !IsCompleted && !IsCheckOpen;

    protected override void OnInteract()
    {
        if (SkillCheckController.Instance == null)
        {
            Debug.LogError($"[{nameof(SkillCheckPanelInteractable)}] There is no SkillCheckController in the scene (LevelUI).", this);
            return;
        }

        if (AudioManager.Exists)
            AudioManager.Instance.PlaySFX("sfx_interaction_panel_electrico", transform.position);

        SkillCheckController.Instance.Open(puzzleData.Sequence, HandleFinished);
    }

    private void HandleFinished(bool completed)
    {
        // The panel may be gone (scene unloaded while the overlay was closing), or the run may have
        // ended — a cancelled sequence completes nothing either way.
        if (!completed || this == null || IsCompleted) return;

        if (!PuzzleStateManager.Exists)
        {
            Debug.LogWarning($"[{nameof(SkillCheckPanelInteractable)}] No PuzzleStateManager — completing " +
                             $"'{puzzleData.PuzzleId}' was not recorded, so no module resolves.", this);
            return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(puzzleData.PuzzleId);

        if (AudioManager.Exists)
            AudioManager.Instance.PlaySFX("sfx_subpuzzle_1_completo");

        Debug.Log($"[{nameof(SkillCheckPanelInteractable)}] Puzzle completed: {puzzleData.PuzzleId}");
    }
}
