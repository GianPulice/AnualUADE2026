using UnityEngine;

/// <summary>
/// Immutable definition of a device module. Designers create one asset per module (M1/M2/M3),
/// tweak the values, and drop them into <see cref="SO_ModulesConfig"/>. The manager reads this
/// asset and never writes to it — runtime state lives in <see cref="ModuleRuntime"/>.
///
/// Fields are grouped by which module type consumes them. A single asset can have irrelevant
/// fields (e.g. a Legs module has no blindnessDuration); leaving them at zero has no effect
/// because <see cref="PlayerStateManager.ApplyPenalty"/> routes by <see cref="Penalty"/>.
/// </summary>
[CreateAssetMenu(fileName = "ModuleData", menuName = "Scriptable Objects/Modules/Module Data")]
public class ModuleData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable id used by triggers, puzzles and the save system. E.g. 'M1_Legs'.")]
    [SerializeField] private string moduleID;

    [Tooltip("Short label shown in the HUD row. E.g. 'M1'.")]
    [SerializeField] private string moduleLogLabel;

    [Tooltip("Which body part this module penalizes. Drives which handler runs when the module explodes.")]
    [SerializeField] private PenaltyType penalty;

    [Tooltip("Id of the puzzle whose completion resolves this module. Must match the PuzzleId on " +
             "the puzzle's SO_PuzzleData / SO_ValvePuzzleData / etc. Leave empty if the module is " +
             "resolved by other means (only for testing).")]
    [PuzzleId]
    [SerializeField] private string associatedPuzzleId;

    [Header("Timer")]
    [Tooltip("What starts this module's timer.\n" +
             "Zone Trigger: walking into its ZoneTrigger (how every module started until 19/10).\n" +
             "First Puzzle: the first of Started By Puzzle Ids the player solves, once its reward box " +
             "has finished showing itself, so that first sub-puzzle is played off the clock. Its " +
             "ZoneTrigger is ignored. With the list empty it falls back to the ZoneTrigger.")]
    [SerializeField] private ModuleStartMode startMode = ModuleStartMode.ZoneTrigger;

    [Tooltip("Only read with Start Mode = First Puzzle: the puzzles that can start the timer. The first " +
             "one solved starts it; the others, solved later, do nothing.")]
    [PuzzleId]
    [SerializeField] private string[] startedByPuzzleIds = new string[0];

    [Tooltip("Seconds from activation to explosion.")]
    [SerializeField] private float timerDuration = 60f;

    [Header("Legs penalty (only used when Penalty == Legs)")]
    [Tooltip("Player speed after the module explodes, as a percentage of the base speed. " +
             "Applies equally to walking and sprinting. 100 = normal speed, 50 = half speed, 1 = almost stopped.")]
    [SerializeField, Range(1, 100)] private int walkAndRunSpeedPercent = 60;

    [Header("Chest penalty (only used when Penalty == Chest)")]
    [Tooltip("Sprint speed reduction applied when the module explodes. 0.25 = -25%.")]
    [SerializeField, Range(0f, 1f)] private float sprintReduction = 0.25f;

    [Tooltip("Continuous camera shake amplitude while this penalty is active. Hook for future CameraController.")]
    [SerializeField] private float shakeAmplitude = 0.03f;

    [Tooltip("Continuous camera shake frequency while this penalty is active. Hook for future CameraController.")]
    [SerializeField] private float shakeFrequency = 1.2f;

    [Header("Head penalty (only used when Penalty == Head)")]
    [Tooltip("Seconds between blindness episodes.")]
    [SerializeField] private float blindnessInterval = 15f;

    [Tooltip("Seconds each blindness episode lasts.")]
    [SerializeField] private float blindnessDuration = 3f;

    [Tooltip("Fade-to-black duration at the start of each episode. Hook for future UI overlay.")]
    [SerializeField] private float blindnessFadeIn = 0.2f;

    [Tooltip("Fade-back duration at the end of each episode. Hook for future UI overlay.")]
    [SerializeField] private float blindnessFadeOut = 0.3f;

    // ── Getters ──────────────────────────────────────────────────────────────────
    public string ModuleID => moduleID;
    public string ModuleLogLabel => moduleLogLabel;
    public PenaltyType Penalty => penalty;
    public string AssociatedPuzzleId => associatedPuzzleId;
    public float TimerDuration => timerDuration;

    public ModuleStartMode StartMode => startMode;

    /// <summary>Started by its puzzles (<see cref="IsStartedBy"/>), not by a ZoneTrigger.</summary>
    public bool StartsOnPuzzle =>
        startMode == ModuleStartMode.FirstPuzzle && startedByPuzzleIds != null && startedByPuzzleIds.Length > 0;
    public bool IsStartedBy(string puzzleId) =>
        StartsOnPuzzle && !string.IsNullOrEmpty(puzzleId) && System.Array.IndexOf(startedByPuzzleIds, puzzleId) >= 0;

    public float CojeraMultiplier => walkAndRunSpeedPercent / 100f;

    public float SprintReduction => sprintReduction;
    public float ShakeAmplitude => shakeAmplitude;
    public float ShakeFrequency => shakeFrequency;

    public float BlindnessInterval => blindnessInterval;
    public float BlindnessDuration => blindnessDuration;
    public float BlindnessFadeIn => blindnessFadeIn;
    public float BlindnessFadeOut => blindnessFadeOut;
}

/// <summary>What starts a module's timer. Serialized by index: only add at the end.</summary>
public enum ModuleStartMode
{
    ZoneTrigger,    // Walking into the module's ZoneTrigger.
    FirstPuzzle     // The first of its Started By Puzzle Ids to be solved.
}
