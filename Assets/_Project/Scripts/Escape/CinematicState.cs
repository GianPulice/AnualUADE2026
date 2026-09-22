using UnityEngine;

/// <summary>
/// Whether a scripted, skippable cinematic is on screen right now (today: the escape sequence,
/// <see cref="EscapeSequenceDirector"/>). A flag and not an event, so pieces in other scenes —
/// the player's look camera, the "[Press F to skip]" text in the HUD — read the current state on
/// their own Update instead of having to be wired to the director.
///
/// The wake-up cinematic has its own bus (<see cref="WakeUpCinematicEvents"/>) and is untouched.
/// </summary>
public static class CinematicState
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsPlaying = false;
        IsSkippable = false;
        PromptText = string.Empty;
    }

    /// <summary>A cinematic owns the screen: look input is off, and the skip prompt may show.</summary>
    public static bool IsPlaying { get; private set; }

    /// <summary>The prompt should show (the cinematic can be cut with its skip key).</summary>
    public static bool IsSkippable { get; private set; }

    /// <summary>The text of the prompt, already formatted with the skip key.</summary>
    public static string PromptText { get; private set; } = string.Empty;

    public static void Begin(bool skippable, string promptText)
    {
        IsPlaying = true;
        IsSkippable = skippable;
        PromptText = promptText ?? string.Empty;
    }

    public static void End()
    {
        IsPlaying = false;
        IsSkippable = false;
        PromptText = string.Empty;
    }
}
