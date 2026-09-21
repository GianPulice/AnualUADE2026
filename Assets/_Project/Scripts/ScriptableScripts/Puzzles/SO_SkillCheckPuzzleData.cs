using UnityEngine;

/// <summary>
/// The puzzle a skill check panel solves: which puzzle id it completes and which sequence it plays.
/// The sequence tuning itself lives in <see cref="SO_SkillCheckData"/>, so the same tuning can back
/// more than one panel — and the F6 test key — without any of them completing a puzzle by accident.
/// </summary>
[CreateAssetMenu(fileName = "SO_SkillCheckPuzzleData", menuName = "Scriptable Objects/Puzzles/Skill Check Puzzle Data")]
public class SO_SkillCheckPuzzleData : ScriptableObject
{
    [Tooltip("Completed in PuzzleStateManager when the whole sequence is passed. The module whose " +
             "associatedPuzzleId matches resolves on it.")]
    [SerializeField] private string puzzleId;

    [SerializeField] private string promptText = "Stabilize the ventilation";

    [Tooltip("Sequence played. Empty = the SkillCheckController's default.")]
    [SerializeField] private SO_SkillCheckData sequence;

    public string PuzzleId => puzzleId;
    public string PromptText => promptText;
    public SO_SkillCheckData Sequence => sequence;
}
