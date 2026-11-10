using System;
using UnityEngine;

/// <summary>
/// The player talking to themselves: a short line of inner monologue, shown by
/// <see cref="PlayerThoughtView"/> in its own look (yellow, monospace) so it never reads as the
/// Architect's subtitles or a system message. Any gameplay system raises one through
/// <see cref="Show"/>; nothing listens but the view.
/// </summary>
public static class PlayerThoughtEvents
{
    /// <summary>Text and seconds on screen.</summary>
    public static event Action<string, float> OnThought;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => OnThought = null;

    public static void Show(string text, float seconds = 3f)
    {
        if (string.IsNullOrEmpty(text)) return;
        OnThought?.Invoke(text, seconds);
    }
}
