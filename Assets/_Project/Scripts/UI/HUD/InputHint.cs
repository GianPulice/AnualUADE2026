using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One on-screen help: "USE [WASD] TO MOVE". Authored on an <see cref="InputHintTrigger"/>.
/// </summary>
[Serializable]
public class InputHint
{
    [Tooltip("Identifies the hint. A hint shows once per run: another trigger with the same id stays " +
             "quiet. Empty = keys + action.")]
    public string id = string.Empty;

    [Tooltip("What to press, as shown between brackets. E.g. WASD, E, TAB, SHIFT.")]
    public string keys = "WASD";

    [Tooltip("What it does. E.g. move, interact, open the inventory.")]
    public string action = "move";

    [Tooltip("Seconds on screen before it fades.")]
    [Min(0.5f)] public float seconds = 5f;

    [Tooltip("Pressing any of these counts as having done it, and the hint fades early.")]
    public KeyCode[] dismissKeys = Array.Empty<KeyCode>();

    [Tooltip("Moving (the Horizontal / Vertical axes) counts as having done it.")]
    public bool dismissOnMove;

    public string Id => string.IsNullOrWhiteSpace(id) ? $"{keys}|{action}" : id;
}

/// <summary>
/// Static bus for input hints, and the once-per-run bookkeeping. <see cref="InputHintView"/> listens;
/// anything can raise one — an <see cref="InputHintTrigger"/> or code.
/// </summary>
public static class InputHintEvents
{
    private static readonly HashSet<string> shown = new HashSet<string>();

    public static event Action<InputHint> OnHint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnHint = null;
        shown.Clear();
        GameSession.OnNewSessionStarting -= ClearShown;
        GameSession.OnNewSessionStarting += ClearShown;
    }

    // A New Game or a Retry is a new run: every help shows again. A checkpoint respawn is not.
    private static void ClearShown() => shown.Clear();

    public static bool HasShown(string id) => shown.Contains(id);

    /// <summary>Shows the hint unless one with its id already showed this run.</summary>
    /// <returns>True if it was sent to the screen.</returns>
    public static bool Show(InputHint hint)
    {
        if (hint == null || string.IsNullOrWhiteSpace(hint.keys)) return false;

        // With nothing listening (no HUD loaded) the hint is not spent: it can still show later.
        if (OnHint == null || shown.Contains(hint.Id)) return false;

        shown.Add(hint.Id);
        OnHint.Invoke(hint);
        return true;
    }
}
