using System;
using UnityEngine;

/// <summary>
/// The device's countdown beep: silent until the active module has <see cref="warningStartSeconds"/>
/// left, then one beep per <see cref="normalInterval"/>, and faster with the urgent clip under
/// <see cref="urgentBelowSeconds"/>.
///
/// Driven by <see cref="ModuleEvents.OnTimerTick"/>, which only fires while the timer really runs —
/// so the beep stops on its own in the pause menu and while the player is down after a capture,
/// with no pause handling of its own.
///
/// Beeps sit on a fixed grid (multiples of the interval), so they land on the second the timer
/// display changes. A time jump never turns into a burst: a penalty that skips several grid points
/// beeps once and carries on from the next point below; a bonus that lifts the time back up re-arms
/// the grid from there.
///
/// Raises <see cref="Beeped"/> so the HUD can pulse in time with what the player hears.
/// </summary>
public class ModuleTimerBeeper : MonoBehaviour
{
    [Header("Thresholds (seconds left)")]
    [Tooltip("Beeping starts when the active module has this many seconds left.")]
    [SerializeField, Min(0f)] private float warningStartSeconds = 30f;

    [Tooltip("Below this, the urgent clip and the urgent interval take over.")]
    [SerializeField, Min(0f)] private float urgentBelowSeconds = 10f;

    [Header("Cadence (seconds between beeps)")]
    [SerializeField, Min(0.05f)] private float normalInterval = 1f;
    [SerializeField, Min(0.05f)] private float urgentInterval = 0.5f;

    [Header("Audio")]
    [SerializeField] private AudioClip normalClip;
    [SerializeField] private AudioClip urgentClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

    /// <summary>Raised on every beep; true when it was an urgent one.</summary>
    public event Action<bool> Beeped;

    public float WarningStartSeconds => warningStartSeconds;
    public float UrgentBelowSeconds => urgentBelowSeconds;

    private ModuleRuntime tracked;
    private float nextBeepAt;

    private void Awake()
    {
        ModuleEvents.OnTimerTick += HandleTimerTick;
        ModuleEvents.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy()
    {
        ModuleEvents.OnTimerTick -= HandleTimerTick;
        ModuleEvents.OnStateChanged -= HandleStateChanged;
    }

    private void HandleStateChanged(ModuleRuntime module)
    {
        // A resolved or exploded module stops beeping; the next one starts from a clean grid.
        if (module == tracked && module.Status != ModuleStatus.Active) tracked = null;
    }

    private void HandleTimerTick(ModuleRuntime module)
    {
        if (module == null || module.Status != ModuleStatus.Active) return;

        if (module != tracked)
        {
            tracked = module;
            nextBeepAt = warningStartSeconds;
        }

        float left = module.TimeRemaining;
        if (left > warningStartSeconds)
        {
            // Above the threshold (or lifted back above it by a bonus): wait for it again.
            nextBeepAt = warningStartSeconds;
            return;
        }

        float interval = IntervalAt(left);

        // A bonus lifted the time more than one step above the next beep: re-arm from here instead
        // of going quiet until the clock catches up with the old grid point.
        if (left - nextBeepAt > interval) nextBeepAt = GridPointBelow(left, interval);

        if (left > nextBeepAt) return;

        bool urgent = left <= urgentBelowSeconds;
        Play(urgent);

        // Strictly below the current time, so a penalty that skipped several points beeps once.
        nextBeepAt = GridPointBelow(left, IntervalAt(left));
    }

    private float IntervalAt(float left) => left <= urgentBelowSeconds ? urgentInterval : normalInterval;

    /// <summary>Largest multiple of <paramref name="interval"/> strictly below <paramref name="t"/>.</summary>
    private static float GridPointBelow(float t, float interval) =>
        Mathf.Ceil(t / interval) * interval - interval;

    private void Play(bool urgent)
    {
        AudioClip clip = urgent && urgentClip != null ? urgentClip : normalClip;
        if (clip != null && AudioManager.Exists) AudioManager.Instance.PlayUIClip(clip, volume);

        Beeped?.Invoke(urgent);
    }
}
