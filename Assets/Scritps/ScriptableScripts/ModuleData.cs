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
    [SerializeField] private string associatedPuzzleId;

    [Header("Timer")]
    [Tooltip("Seconds from activation to explosion.")]
    [SerializeField] private float timerDuration = 60f;

    [Header("Legs penalty (only used when Penalty == Legs)")]
    [Tooltip("MoveSpeed multiplier applied when the module explodes. 0.6 = 40% slower.")]
    [SerializeField, Range(0.1f, 1f)] private float cojeraMultiplier = 0.6f;

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

    public float CojeraMultiplier => cojeraMultiplier;

    public float SprintReduction => sprintReduction;
    public float ShakeAmplitude => shakeAmplitude;
    public float ShakeFrequency => shakeFrequency;

    public float BlindnessInterval => blindnessInterval;
    public float BlindnessDuration => blindnessDuration;
    public float BlindnessFadeIn => blindnessFadeIn;
    public float BlindnessFadeOut => blindnessFadeOut;
}
