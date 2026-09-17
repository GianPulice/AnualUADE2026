using System;
using UnityEngine;

/// <summary>
/// General HUD alert: a short system message at the top centre of the screen
/// (<see cref="HUDAlertView"/>). Any system can raise one; the Architect uses it on key lines.
/// </summary>
public static class HUDMessageEvents
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => OnAlert = null;

    public static event Action<string, float> OnAlert;

    /// <summary>Queues an alert. Ignored when the text is blank.</summary>
    public static void ShowAlert(string text, float seconds = 3f)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        OnAlert?.Invoke(text, seconds);
    }
}
