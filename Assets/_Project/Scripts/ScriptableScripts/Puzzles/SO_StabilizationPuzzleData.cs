using System;
using UnityEngine;

/// <summary>
/// The Ventilation Hub of Central Puzzle 2 — the monitoring panel and the final phase — as the
/// design spec describes it. One asset drives the hub's two prefabs: the monitor lights one
/// indicator per <see cref="RequiredPuzzleIds"/> entry, and the stabilization panel only opens once
/// all of them are completed, then plays <see cref="Checks"/>.
///
/// The checks (spec §3): a needle turning at constant speed and a success zone on the dial; [E]
/// inside the zone passes, anywhere else fails. Difficulty goes DOWN — the first check is the
/// hardest and every next one has a wider zone and a slower needle. A failure takes
/// <see cref="StabilizationCheck.timerPenaltyOnFail"/> seconds off the running module and repeats
/// the SAME check; the sequence never starts over unless it is interrupted (ESC), in which case its
/// progress is lost and the panel starts from the first check next time.
///
/// Completing every check completes <see cref="PuzzleId"/>, which resolves M2 (its
/// associatedPuzzleId) in ModuleManager.
///
/// Not to be confused with SO_SkillCheckData / SkillCheckController: that is a different, earlier
/// take on the same panel (easy-to-hard, a miss restarts the sequence) kept as it is.
/// </summary>
[CreateAssetMenu(fileName = "SO_StabilizationPuzzleData", menuName = "Scriptable Objects/Puzzles/Stabilization Puzzle Data")]
public class SO_StabilizationPuzzleData : ScriptableObject
{
    [Serializable]
    public struct StabilizationCheck
    {
        [Tooltip("Needle speed in laps per second. Higher = harder.")]
        [Min(0.05f)] public float needleSpeed;

        [Tooltip("Size of the success zone as a fraction of the dial (0.25 = a quarter). Higher = easier.")]
        [Range(0.02f, 0.9f)] public float successZoneWidth;

        [Tooltip("Seconds taken off the running module's timer when this check is failed.")]
        [Min(0f)] public float timerPenaltyOnFail;
    }

    [Header("Puzzle")]
    [Tooltip("Completed when every check is passed. The module whose associatedPuzzleId matches " +
             "(M2) resolves on it.")]
    [SerializeField] private string puzzleId = "puzzle_central_piso2";

    [Tooltip("The sub-puzzles that must be completed before the panel opens. The monitor shows one " +
             "indicator per entry, in this order.")]
    [SerializeField, PuzzleId] private string[] requiredPuzzleIds = new string[0];

    [Header("Prompts")]
    [SerializeField] private string startPrompt = "Start stabilization";
    [Tooltip("Shown while sub-puzzles are missing. {0} = how many.")]
    [SerializeField] private string pendingInfoFormat = "Pending systems: {0}";

    [Header("Checks (played in order, hardest first)")]
    [SerializeField] private StabilizationCheck[] checks =
    {
        new StabilizationCheck { needleSpeed = 0.9f,  successZoneWidth = 0.12f, timerPenaltyOnFail = 10f },
        new StabilizationCheck { needleSpeed = 0.8f,  successZoneWidth = 0.18f, timerPenaltyOnFail = 10f },
        new StabilizationCheck { needleSpeed = 0.7f,  successZoneWidth = 0.25f, timerPenaltyOnFail = 10f },
        new StabilizationCheck { needleSpeed = 0.6f,  successZoneWidth = 0.35f, timerPenaltyOnFail = 10f },
    };

    [Tooltip("Full laps the needle may turn without a press before the attempt counts as failed. " +
             "0 = it keeps turning until the player presses.")]
    [SerializeField, Min(0)] private int maxLapsPerAttempt = 2;

    [Tooltip("Where the success zone may start, in degrees (0 = twelve o'clock, clockwise). " +
             "The needle starts at twelve, so keep the minimum away from 0 to give time to react.")]
    [SerializeField, Range(0f, 359f)] private float zoneStartMin = 90f;
    [SerializeField, Range(0f, 359f)] private float zoneStartMax = 300f;

    [Header("Rhythm (seconds)")]
    [Tooltip("From the zone appearing to the needle moving. [E] is ignored in this window.")]
    [SerializeField, Min(0f)] private float warningLeadTime = 0.6f;
    [Tooltip("How long a SUCCESS / FAILURE stays on screen before the next attempt.")]
    [SerializeField, Min(0f)] private float resultHoldTime = 0.6f;
    [Tooltip("How long SYSTEM STABILIZED stays on screen before the overlay closes.")]
    [SerializeField, Min(0f)] private float completeHoldTime = 1.2f;

    [Header("Sounds")]
    [SoundId, SerializeField] private string openSoundId = "sfx_interaction_panel_electrico";
    [SoundId, SerializeField] private string successSoundId = "sfx_interaction_panel_control_success";
    [SoundId, SerializeField] private string failSoundId = "sfx_interaction_panel_password_error";
    [SoundId, SerializeField] private string completeSoundId = "sfx_subpuzzle_3_completo";

    [Header("Progressive calm-down (provisional values)")]
    [Tooltip("Camera shake while the system is unstable (the checks are running), in degrees.")]
    [SerializeField, Min(0f)] private float shakeAmplitude = 0.6f;
    [Tooltip("Camera shake speed while unstable.")]
    [SerializeField, Min(0f)] private float shakeFrequency = 9f;
    [Tooltip("Shake left after each passed check, as a fraction of Shake Amplitude (spec: 75%, " +
             "50%, 25%, 0%). Element 0 = after the first check.")]
    [SerializeField] private float[] shakeAfterCheck = { 0.75f, 0.5f, 0.25f, 0f };
    [Tooltip("Ambience bed volume while unstable, as a multiplier (1 = normal).")]
    [SerializeField, Min(0f)] private float ambienceAgitation = 1.5f;
    [Tooltip("Ambience agitation left after each passed check (spec: 70%, 50%, 30%, normal).")]
    [SerializeField] private float[] ambienceAfterCheck = { 0.7f, 0.5f, 0.3f, 0f };

    [Header("Monitor")]
    [SerializeField] private Color pendingColor = new Color(0.8f, 0.1f, 0.1f);
    [SerializeField] private Color resolvedColor = new Color(0.15f, 0.85f, 0.25f);
    [Tooltip("Every indicator once the hub puzzle is completed.")]
    [SerializeField] private Color stabilizedColor = new Color(0.35f, 0.75f, 1f);
    [SerializeField, Min(0.01f)] private float indicatorFadeSeconds = 0.5f;
    [Tooltip("Blinks per second of the indicators while the checks are running.")]
    [SerializeField, Min(0.1f)] private float runningBlinkRate = 2f;
    [SoundId, SerializeField] private string indicatorResolvedSoundId = "sfx_interaction_panel_control_success";

    public string PuzzleId => puzzleId;
    public string[] RequiredPuzzleIds => requiredPuzzleIds ?? Array.Empty<string>();
    public string StartPrompt => startPrompt;
    public string PendingInfoFormat => pendingInfoFormat;
    public StabilizationCheck[] Checks => checks ?? Array.Empty<StabilizationCheck>();
    public int MaxLapsPerAttempt => maxLapsPerAttempt;
    public float ZoneStartMin => Mathf.Min(zoneStartMin, zoneStartMax);
    public float ZoneStartMax => Mathf.Max(zoneStartMin, zoneStartMax);
    public float WarningLeadTime => warningLeadTime;
    public float ResultHoldTime => resultHoldTime;
    public float CompleteHoldTime => completeHoldTime;
    public string OpenSoundId => openSoundId;
    public string SuccessSoundId => successSoundId;
    public string FailSoundId => failSoundId;
    public string CompleteSoundId => completeSoundId;
    public float ShakeAmplitude => shakeAmplitude;
    public float ShakeFrequency => shakeFrequency;
    public float AmbienceAgitation => ambienceAgitation;
    public Color PendingColor => pendingColor;
    public Color ResolvedColor => resolvedColor;
    public Color StabilizedColor => stabilizedColor;
    public float IndicatorFadeSeconds => indicatorFadeSeconds;
    public float RunningBlinkRate => runningBlinkRate;
    public string IndicatorResolvedSoundId => indicatorResolvedSoundId;

    /// <summary>Shake left (0..1 of the full amplitude) once <paramref name="passed"/> checks are done.</summary>
    public float ShakeLeft(int passed) => Left(shakeAfterCheck, passed);

    /// <summary>Ambience agitation left (0..1) once <paramref name="passed"/> checks are done.</summary>
    public float AmbienceLeft(int passed) => Left(ambienceAfterCheck, passed);

    /// <summary>The authored value for this many passed checks; falls back to a straight line when
    /// the list is shorter than the checks, and 0 once every check is passed.</summary>
    private float Left(float[] table, int passed)
    {
        int total = Checks.Length;
        if (passed <= 0) return 1f;
        if (total > 0 && passed >= total) return 0f;
        if (table != null && passed - 1 < table.Length) return Mathf.Clamp01(table[passed - 1]);
        return total > 0 ? 1f - passed / (float)total : 0f;
    }

    /// <summary>How many of the required sub-puzzles are not completed yet.</summary>
    public int PendingCount()
    {
        int pending = 0;
        foreach (string id in RequiredPuzzleIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!PuzzleStateManager.Exists || !PuzzleStateManager.Instance.IsPuzzleCompleted(id)) pending++;
        }
        return pending;
    }
}
