using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What the subtitle needs to know about a line that just started.</summary>
public readonly struct ArchitectLinePlayback
{
    public readonly ArchitectLineID Id;
    public readonly string Text;
    public readonly float Duration;

    /// <summary>
    /// The part of <see cref="Duration"/> that is actually spoken (or read): Duration minus the
    /// hold after the line. Pages (<see cref="ArchitectLinePages"/>) are spread over this.
    /// </summary>
    public readonly float SpeakingDuration;

    public readonly bool PlaysOverMenus;

    public ArchitectLinePlayback(ArchitectLineID id, string text, float duration, float speakingDuration,
                                 bool playsOverMenus)
    {
        Id = id;
        Text = text;
        Duration = duration;
        SpeakingDuration = Mathf.Clamp(speakingDuration, 0f, duration);
        PlaysOverMenus = playsOverMenus;
    }
}

/// <summary>
/// A line can be split into subtitle pages with <see cref="Separator"/> in its bank text
/// ("First sentence. | The rest."). Each page stays up for a share of the speaking time
/// proportional to its length, so the subtitle and anything synced to a page (the wake-up
/// cinematic) compute the same instants from the same text.
/// </summary>
public static class ArchitectLinePages
{
    public const char Separator = '|';

    public static string[] Split(string text)
    {
        if (string.IsNullOrEmpty(text)) return new[] { string.Empty };

        string[] raw = text.Split(Separator);
        List<string> pages = new List<string>(raw.Length);
        foreach (string page in raw)
        {
            string trimmed = page.Trim();
            if (trimmed.Length > 0) pages.Add(trimmed);
        }
        return pages.Count > 0 ? pages.ToArray() : new[] { string.Empty };
    }

    /// <summary>The text without separators, for logs and anything that shows the line whole.</summary>
    public static string Joined(string text) => string.Join(" ", Split(text));

    /// <summary>Seconds after the line starts at which each page appears. The first is always 0.</summary>
    public static float[] StartTimes(ArchitectLinePlayback playback, string[] pages)
    {
        float[] starts = new float[pages.Length];

        int total = 0;
        foreach (string page in pages) total += page.Length;
        if (total == 0) return starts;

        int before = 0;
        for (int i = 0; i < pages.Length; i++)
        {
            starts[i] = playback.SpeakingDuration * before / total;
            before += pages[i].Length;
        }
        return starts;
    }
}

/// <summary>
/// Static bus between <see cref="ArchitectVoiceController"/> and the HUD that shows it
/// (<see cref="ArchitectSubtitleView"/>), so the controller does not need a scene reference to the
/// UI canvas and the subtitle can live in LevelUI.
/// </summary>
public static class ArchitectEvents
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnLineStarted = null;
        OnLineEnded = null;
    }

    public static event Action<ArchitectLinePlayback> OnLineStarted;

    /// <summary>The active line ended. The bool is true when it was cut short (ARC_10 interrupting).</summary>
    public static event Action<bool> OnLineEnded;

    public static void LineStarted(ArchitectLinePlayback playback) => OnLineStarted?.Invoke(playback);
    public static void LineEnded(bool interrupted) => OnLineEnded?.Invoke(interrupted);
}
