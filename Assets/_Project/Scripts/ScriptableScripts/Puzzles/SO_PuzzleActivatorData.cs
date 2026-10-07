using UnityEngine;

/// <summary>
/// A single-use mechanism that completes a puzzle when the player uses it, optionally only once
/// another puzzle is done. Central Puzzle 2 uses it for the stabilization mechanism inside the
/// access room (SP3): the code was the puzzle, this is the last step. Generic on purpose — any
/// "pull this once the door is open" beat of a later puzzle is one more asset, not more code.
/// </summary>
[CreateAssetMenu(fileName = "SO_PuzzleActivatorData", menuName = "Scriptable Objects/Puzzles/Puzzle Activator Data")]
public class SO_PuzzleActivatorData : ScriptableObject
{
    [Tooltip("Completed in PuzzleStateManager when the player uses the mechanism.")]
    [SerializeField] private string puzzleId;

    [Tooltip("Puzzle that must be completed before the mechanism can be used. Empty = always usable.")]
    [SerializeField, PuzzleId] private string requiredPuzzleId;

    [SerializeField] private string promptText = "Activate the mechanism";

    [Tooltip("Shown while Required Puzzle Id is not completed yet.")]
    [SerializeField] private string lockedInfoText = "It has no power";

    [SoundId, SerializeField] private string useSoundId = "sfx_interaction_panel_control_01";
    [SoundId, SerializeField] private string completedSoundId = "sfx_subpuzzle_3_completo";

    public string PuzzleId => puzzleId;
    public string RequiredPuzzleId => requiredPuzzleId;
    public string PromptText => promptText;
    public string LockedInfoText => lockedInfoText;
    public string UseSoundId => useSoundId;
    public string CompletedSoundId => completedSoundId;
}
