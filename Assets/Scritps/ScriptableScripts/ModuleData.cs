using UnityEngine;

[CreateAssetMenu(fileName = "ModuleData", menuName = "Scriptable Objects/ModuleData")]
public class ModuleData : ScriptableObject
{
    [Header("ID")]
    public string ModuleID;
    public string ModuleLogLabel;

    [Header("State")]
    public ModuleStatus Status = ModuleStatus.Inactive;

    [Header("Timer")]
    public float TimerDuration;
    public float TimeRemaining;
    public bool IsTimerRunning;

    [Header("Blindness")]
    public bool causesBlindness = false;
    [Tooltip("Seconds of total darkness when this module explodes.")]
    public float blindnessDuration = 3f;

    // Read-only for the UI
    public float TimerProgress => TimerDuration > 0f
          ? Mathf.Clamp01(TimeRemaining / TimerDuration)
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
                > 0.50f => new Color(0.80f, 0.10f, 0.10f), // #cc1a1a red
                > 0.25f => new Color(0.67f, 0.27f, 0.00f), // #aa4400 orange
                _ => new Color(0.53f, 0.13f, 0.00f)  // #882200 dark red
            };
        }
    }

    private Color GetStatusBarColor()
    {
        return Status switch
        {
            ModuleStatus.Resolved => new Color(0.10f, 0.42f, 0.10f), // #1a6a1a green
            ModuleStatus.Exploded => new Color(0.35f, 0.16f, 0.00f), // #5a2a00 brown
            ModuleStatus.Inactive => new Color(0.13f, 0.13f, 0.13f), // #222 grey
            _ => new Color(0.13f, 0.13f, 0.13f)
        };
    }
}
public enum ModuleStatus
{
    Inactive,   // Not started — grey bar
    Active,     // Running — red bar, timer alive
    Resolved,   // Completed correctly — green bar, fixed at 100%
    Exploded    // Timer reached zero — brown bar, 0%
}
