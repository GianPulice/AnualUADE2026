using UnityEngine;

/// <summary>
/// Runtime state for a single module. ModuleData is the definition (edited by designers as an SO);
/// this class holds the live values that mutate during play (status, remaining time, activation flag).
///
/// Kept as a POCO so the manager can create/reset instances per run without leaving dirty state
/// in the ScriptableObject asset — a common Unity footgun.
///
/// Exposes the read-only helpers the HUD used to read from ModuleData directly (FormattedTime,
/// TimerProgress, BarColor) so migrating the views is a one-line change.
/// </summary>
public class ModuleRuntime
{
    public ModuleData Data { get; }
    public ModuleStatus Status { get; internal set; }
    public float TimeRemaining { get; internal set; }
    public bool IsTimerRunning { get; internal set; }
    public bool HasBeenActivated { get; internal set; }

    public ModuleRuntime(ModuleData data)
    {
        Data = data;
        Status = ModuleStatus.Inactive;
        TimeRemaining = data != null ? data.TimerDuration : 0f;
        IsTimerRunning = false;
        HasBeenActivated = false;
    }

    // ── Convenience passthroughs so views can stay agnostic of Data vs runtime ──────────

    public string ModuleID => Data != null ? Data.ModuleID : string.Empty;
    public string ModuleLogLabel => Data != null ? Data.ModuleLogLabel : string.Empty;

    // ── View helpers (moved from ModuleData so the SO stays a pure definition) ──────────

    public float TimerProgress => Data != null && Data.TimerDuration > 0f
        ? Mathf.Clamp01(TimeRemaining / Data.TimerDuration)
        : 0f;

    public string FormattedTime
    {
        get
        {
            if (!IsTimerRunning && Status != ModuleStatus.Active)
                return "--:--";

            int minutes = Mathf.FloorToInt(TimeRemaining / 60f);
            int seconds = Mathf.FloorToInt(TimeRemaining % 60f);
            return $"{minutes:00}:{seconds:00}";
        }
    }

    public Color BarColor
    {
        get
        {
            if (Status != ModuleStatus.Active) return GetStatusBarColor();

            return TimerProgress switch
            {
                > 0.50f => new Color(0.80f, 0.10f, 0.10f), // #cc1a1a
                > 0.25f => new Color(0.67f, 0.27f, 0.00f), // #aa4400
                _ => new Color(0.53f, 0.13f, 0.00f)  // #882200
            };
        }
    }

    private Color GetStatusBarColor() => Status switch
    {
        ModuleStatus.Resolved => new Color(0.10f, 0.42f, 0.10f), // #1a6a1a
        ModuleStatus.Exploded => new Color(0.35f, 0.16f, 0.00f), // #5a2a00
        ModuleStatus.Inactive => new Color(0.13f, 0.13f, 0.13f), // #222
        _ => new Color(0.13f, 0.13f, 0.13f)
    };
}
