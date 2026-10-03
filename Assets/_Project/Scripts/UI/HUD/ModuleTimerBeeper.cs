using System;
using UnityEngine;

/// <summary>
/// The device's countdown beep. It beeps from the moment the active module starts, slowly at
/// first and quicker as the module runs out (<see cref="BeepCadence"/>):
///
///   start → amber   one beep every 30 s, shrinking in a straight line to one every 10 s
///   amber → red     10 s → 5 s
///   red → urgent    5 s → 0.5 s, exponentially
///   last <see cref="urgentBelowSeconds"/>   the urgent clip, every 0.5 s
///
/// Amber and red are where the module's readout turns amber and red (<see cref="stageSource"/>), so
/// colour and beep change pace together. The first beep comes one interval after the module starts.
///
/// Driven by <see cref="ModuleEvents.OnTimerTick"/>, which only fires while the timer really runs —
/// so the beep stops on its own in the pause menu and while the player is down after a capture,
/// with no pause handling of its own.
///
/// Each beep is scheduled from the one before, not from the frame it was heard on, so the pace does
/// not drift. A time jump never turns into a burst: a penalty that skips several beeps beeps once
/// and carries on from the clock; a bonus that lifts the time more than a step re-arms from there.
///
/// Raises <see cref="Beeped"/> so what blinks (the module's LED, <see cref="ModuleLED"/>) does it in
/// time with what the player hears.
/// </summary>
public class ModuleTimerBeeper : MonoBehaviour
{
    [Header("Stages")]
    [Tooltip("Where each module's readout turns amber and red (the same SO_PlayerCameraFeed the " +
             "camera feed uses), so the beep changes pace with the colour. Empty = that asset's " +
             "defaults, and a warning.")]
    [SerializeField] private SO_PlayerCameraFeedConfig stageSource;

    [Tooltip("Seconds left under which the urgent clip and the urgent interval take over.")]
    [SerializeField, Min(0f)] private float urgentBelowSeconds = 10f;

    [Header("Cadence (seconds between beeps)")]
    [Tooltip("When the module starts. Shrinks in a straight line to the amber interval as amber nears.")]
    [SerializeField, Min(0.05f)] private float startInterval = 30f;

    [Tooltip("When the readout turns amber. Shrinks in a straight line to the red interval.")]
    [SerializeField, Min(0.05f)] private float amberInterval = 10f;

    [Tooltip("When the readout turns red. Falls exponentially to the urgent interval.")]
    [SerializeField, Min(0.05f)] private float redInterval = 5f;

    [Tooltip("From the urgent threshold to the end.")]
    [SerializeField, Min(0.05f)] private float urgentInterval = 0.5f;

    [Header("Audio")]
    [SerializeField] private AudioClip normalClip;
    [SerializeField] private AudioClip urgentClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

    /// <summary>Raised on every beep, with the module counting down; true when it was an urgent one.</summary>
    public static event Action<ModuleRuntime, bool> Beeped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Beeped = null;

    public float UrgentBelowSeconds => urgentBelowSeconds;

    private ModuleRuntime tracked;
    private BeepCadence cadence;
    private float nextBeepAt;
    private float lastLeft;
    private SO_PlayerCameraFeedConfig fallbackStages;

    private void Awake()
    {
        if (stageSource == null)
        {
            Debug.LogWarning($"[{nameof(ModuleTimerBeeper)}] '{name}' has no Stage Source: using the " +
                             "camera feed config's defaults for amber and red.", this);
            fallbackStages = ScriptableObject.CreateInstance<SO_PlayerCameraFeedConfig>();
            fallbackStages.hideFlags = HideFlags.HideAndDontSave;
        }

        ModuleEvents.OnTimerTick += HandleTimerTick;
        ModuleEvents.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy()
    {
        ModuleEvents.OnTimerTick -= HandleTimerTick;
        ModuleEvents.OnStateChanged -= HandleStateChanged;
        if (fallbackStages != null) Destroy(fallbackStages);
    }

    private void HandleStateChanged(ModuleRuntime module)
    {
        // A resolved or exploded module stops beeping; the next one starts from a clean schedule.
        if (module == tracked && module.Status != ModuleStatus.Active) tracked = null;
    }

    private void HandleTimerTick(ModuleRuntime module)
    {
        if (module == null || module.Status != ModuleStatus.Active) return;

        float left = module.TimeRemaining;

        if (module != tracked)
        {
            tracked = module;
            cadence = BuildCadence(module);
            nextBeepAt = left - cadence.IntervalAt(left);
            lastLeft = left;
        }

        // The clock went UP (a bonus) and the next beep is now more than one step away: re-arm from
        // here instead of going quiet until the clock catches up with the old point. Only on a real
        // lift: in a stage where the interval changes faster than the clock this test would
        // otherwise hold every frame and the beep would never come.
        float interval = cadence.IntervalAt(left);
        if (left > lastLeft && left - nextBeepAt > interval) nextBeepAt = left - interval;
        lastLeft = left;

        if (left > nextBeepAt) return;

        Play(module, left <= urgentBelowSeconds);

        // From the schedule, so the pace holds. If a penalty left the clock past the next point
        // too, from the clock: it beeps once, not once per point it skipped.
        float next = nextBeepAt - cadence.IntervalAt(nextBeepAt);
        nextBeepAt = next < left ? next : left - cadence.IntervalAt(left);
    }

    private BeepCadence BuildCadence(ModuleRuntime module)
    {
        SO_PlayerCameraFeedConfig stages = stageSource != null ? stageSource : fallbackStages;
        float duration = module.Data != null ? module.Data.TimerDuration : module.TimeRemaining;

        return new BeepCadence(duration,
            stages.WarningSecondsLeft(module), stages.CriticalSecondsLeft(module), urgentBelowSeconds,
            startInterval, amberInterval, redInterval, urgentInterval);
    }

    private void Play(ModuleRuntime module, bool urgent)
    {
        AudioClip clip = urgent && urgentClip != null ? urgentClip : normalClip;
        if (clip != null && AudioManager.Exists) AudioManager.Instance.PlayUIClip(clip, volume);

        Beeped?.Invoke(module, urgent);
    }
}
