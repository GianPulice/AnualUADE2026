using UnityEngine;

/// <summary>
/// Switch room puzzle (Central Puzzle 2 — SP2, light and configuration). A row of up/down switches
/// and a wall whose painted arrows only show under the player's module light. Matching every switch
/// to its arrow solves it.
///
/// One asset drives both prefabs: the switch panel checks the answer, and the arrow wall points
/// each arrow the same way (so changing the answer here changes the wall too) and decides how the
/// player's light reveals them, degradation included.
/// </summary>
[CreateAssetMenu(fileName = "SO_SwitchPuzzleData", menuName = "Scriptable Objects/Puzzles/Switch Puzzle Data")]
public class SO_SwitchPuzzleData : ScriptableObject
{
    public enum RevealMode
    {
        ModuleLight,
        TriggerZone
    }

    public enum StartPosition
    {
        Middle,
        Down,
        Up
    }

    [System.Serializable]
    public struct ModuleLightDegradation
    {
        [Tooltip("The module whose explosion degrades the light.")]
        public ModuleData module;

        [Tooltip("The reach is multiplied by this once that module has exploded. 0.5 = half the reach.")]
        [Range(0f, 1f)] public float reachMultiplier;
    }

    [Tooltip("Completed in PuzzleStateManager when every switch matches its arrow.")]
    [SerializeField] private string puzzleId;

    [Header("Switches (element 0 = the leftmost)")]
    [Tooltip("The answer: ticked = that switch must be UP, unticked = DOWN. Its length is the " +
             "number of switches (the prefabs carry 5).")]
    [SerializeField] private bool[] correctUp = { true, false, true, true, false };

    [Tooltip("Where every switch starts. Middle = handle straight out, neither up nor down: the " +
             "first flip takes it up, after that it only goes between up and down, and the puzzle " +
             "cannot be solved while any switch is still in the middle.")]
    [SerializeField] private StartPosition startPosition = StartPosition.Middle;

    [Header("Arrow wall")]
    [Tooltip("What reveals the arrows.\n" +
             "Module Light = the player's real module light: each arrow shows while it is within " +
             "the light's reach and in its line of sight (the reach follows the vision fog preset " +
             "of the room, so a darker room means getting closer).\n" +
             "Trigger Zone = the player standing in the wall's RevealZone box, all arrows at once.")]
    [SerializeField] private RevealMode revealMode = RevealMode.ModuleLight;

    [Tooltip("Seconds the arrows take to appear (and to fade) when the player's light reaches the wall.")]
    [SerializeField, Min(0.01f)] private float revealFadeSeconds = 0.5f;

    [Tooltip("Module Light mode: share of the module light's radius that is bright enough to reveal " +
             "the paint. 1 = the whole lit radius, 0.5 = half of it (the player has to get closer).")]
    [SerializeField, Range(0.05f, 1.5f)] private float lightReachFraction = 0.6f;

    [Tooltip("Module Light mode: radius of the module light in metres when the scene has no vision " +
             "fog to read it from.")]
    [SerializeField, Min(0.1f)] private float fallbackLightRange = 4.4f;

    [Tooltip("Module Light mode: height of the light above the player's feet (the modules sit on " +
             "the body), in metres.")]
    [SerializeField, Min(0f)] private float lightHeight = 1f;

    [Tooltip("Module Light mode: what blocks the light between the player and an arrow.")]
    [SerializeField] private LayerMask lightBlockers = (1 << 0) | (1 << 11) | (1 << 12);   // Default, Wall, Props

    [Tooltip("Light degradation: for every listed module that has EXPLODED, the reach is multiplied " +
             "by its value (spec: an exploded M2 shrinks the light, the arrows still show but the " +
             "player has to get closer). In Trigger Zone mode it scales the RevealZone box instead.")]
    [SerializeField] private ModuleLightDegradation[] lightDegradation = new ModuleLightDegradation[0];

    [Header("Sounds and prompts")]
    [SoundId, SerializeField] private string flipSoundId = "sfx_interaction_panel_control_01";
    [SoundId, SerializeField] private string completedSoundId = "sfx_subpuzzle_1_completo";
    [SerializeField] private string switchPrompt = "Flip switch";

    [Tooltip("Seconds a switch takes to flip.")]
    [SerializeField, Min(0.01f)] private float flipSeconds = 0.15f;

    public string PuzzleId => puzzleId;
    public int SwitchCount => correctUp != null ? correctUp.Length : 0;
    public StartPosition StartsAt => startPosition;
    public RevealMode Reveal => revealMode;
    public float RevealFadeSeconds => revealFadeSeconds;
    public float LightReachFraction => lightReachFraction;
    public float FallbackLightRange => fallbackLightRange;
    public float LightHeight => lightHeight;
    public LayerMask LightBlockers => lightBlockers;

    /// <summary>
    /// Product of the reach multipliers of every listed module that has exploded: 1 while none
    /// has. Read live, so it follows an explosion the moment it happens.
    /// </summary>
    public float LightDegradationMultiplier()
    {
        if (lightDegradation == null || !ModuleManager.Exists) return 1f;

        float multiplier = 1f;
        foreach (ModuleLightDegradation entry in lightDegradation)
        {
            if (entry.module == null) continue;
            ModuleRuntime runtime = ModuleManager.Instance.GetRuntime(entry.module.ModuleID);
            if (runtime != null && runtime.Status == ModuleStatus.Exploded)
                multiplier *= Mathf.Max(0f, entry.reachMultiplier);
        }
        return multiplier;
    }
    public string FlipSoundId => flipSoundId;
    public string CompletedSoundId => completedSoundId;
    public string SwitchPrompt => switchPrompt;
    public float FlipSeconds => flipSeconds;

    /// <summary>Whether the switch at <paramref name="index"/> (from 0) must be up.</summary>
    public bool IsCorrectUp(int index) => correctUp != null && index >= 0 && index < correctUp.Length && correctUp[index];

    /// <summary>
    /// Key the switch position is stored under in PuzzleStateManager (as a "valve position":
    /// SwitchPanelController.Middle, Down or Up),
    /// so checkpoints snapshot and restore it like every other puzzle state.
    /// </summary>
    public string SwitchStateKey(int index) => $"{puzzleId}/switch_{index}";
}
