using UnityEngine;

/// <summary>
/// Switch room puzzle (Central Puzzle 2 — SP2, light and configuration). A row of up/down switches
/// and a wall whose painted arrows only show under the player's module light. Matching every switch
/// to its arrow solves it.
///
/// One asset drives the three prefabs: the switch panel checks <see cref="CorrectUp"/>, the arrow
/// wall points each arrow the same way (so changing the answer here changes the wall too), and the
/// reveal zone reads the fade time.
/// </summary>
[CreateAssetMenu(fileName = "SO_SwitchPuzzleData", menuName = "Scriptable Objects/Puzzles/Switch Puzzle Data")]
public class SO_SwitchPuzzleData : ScriptableObject
{
    [Tooltip("Completed in PuzzleStateManager when every switch matches its arrow.")]
    [SerializeField] private string puzzleId;

    [Header("Switches (element 0 = the leftmost)")]
    [Tooltip("The answer: ticked = that switch must be UP, unticked = DOWN. Its length is the " +
             "number of switches (the prefabs carry 5).")]
    [SerializeField] private bool[] correctUp = { true, false, true, true, false };

    [Tooltip("Where every switch starts. Ticked = up.")]
    [SerializeField] private bool startUp;

    [Header("Arrow wall")]
    [Tooltip("Seconds the arrows take to appear (and to fade) when the player's light reaches the wall.")]
    [SerializeField, Min(0.01f)] private float revealFadeSeconds = 0.5f;

    [Header("Sounds and prompts")]
    [SoundId, SerializeField] private string flipSoundId = "sfx_interaction_panel_control_01";
    [SoundId, SerializeField] private string completedSoundId = "sfx_subpuzzle_2_completo";
    [SerializeField] private string switchPrompt = "Flip switch";

    [Tooltip("Seconds a switch takes to flip.")]
    [SerializeField, Min(0.01f)] private float flipSeconds = 0.15f;

    public string PuzzleId => puzzleId;
    public int SwitchCount => correctUp != null ? correctUp.Length : 0;
    public bool StartUp => startUp;
    public float RevealFadeSeconds => revealFadeSeconds;
    public string FlipSoundId => flipSoundId;
    public string CompletedSoundId => completedSoundId;
    public string SwitchPrompt => switchPrompt;
    public float FlipSeconds => flipSeconds;

    /// <summary>Whether the switch at <paramref name="index"/> (from 0) must be up.</summary>
    public bool IsCorrectUp(int index) => correctUp != null && index >= 0 && index < correctUp.Length && correctUp[index];

    /// <summary>
    /// Key the switch position is stored under in PuzzleStateManager (as a 0/1 "valve position"),
    /// so checkpoints snapshot and restore it like every other puzzle state.
    /// </summary>
    public string SwitchStateKey(int index) => $"{puzzleId}/switch_{index}";
}
