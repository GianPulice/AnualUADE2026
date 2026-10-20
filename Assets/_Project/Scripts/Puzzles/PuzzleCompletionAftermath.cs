using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// When the world gets to react to a solved puzzle.
///
/// <see cref="PuzzleStateManager.OnPuzzleCompleted"/> fires the moment a puzzle is solved, and most
/// listeners want exactly that: the power coming back, the Architect's line, the checkpoint. A few
/// must wait until the player has the controls back. A reward box (<see cref="PuzzleRewardBox"/>)
/// takes the screen for a few seconds to show itself opening, with the player frozen, and the
/// Nemesis waking up, the Director's pressure or its staged entrance landing inside that window
/// would happen to a player who cannot move. Those listen to <see cref="Settled"/> instead.
///
/// A puzzle with no presenter settles in the same call as its completion, right after
/// OnPuzzleCompleted, so for every puzzle without a box nothing changes. A presenter registers its
/// puzzle id while it is enabled and calls <see cref="Finish"/> when its moment is over, never in
/// the same frame as the completion. A presenter that goes away mid-way (its scene unloads) settles
/// on its way out, so a listener can be late but is never left waiting forever.
///
/// Like OnPuzzleCompleted, Settled is raised once per completion and never by
/// <see cref="PuzzleStateManager.RestoreSnapshot"/>; an object loaded after the fact catches up
/// with <see cref="PuzzleStateManager.IsPuzzleCompleted"/>.
/// </summary>
public static class PuzzleCompletionAftermath
{
    /// <summary>A completed puzzle's on-screen aftermath is over: the world may react now.</summary>
    public static event Action<string> Settled;

    // Puzzle id → how many presenters are enabled for it. More than one is a setup mistake
    // (PuzzleRewardBox warns), but counting keeps one of them going away from releasing the other.
    private static readonly Dictionary<string, int> presenters = new Dictionary<string, int>(StringComparer.Ordinal);
    private static readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Static state outlives Play mode when domain reload is disabled.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Settled = null;
        presenters.Clear();
        pending.Clear();
    }

    /// <summary>The puzzle is solved but its presenter has not finished yet.</summary>
    public static bool IsPending(string puzzleId) =>
        !string.IsNullOrWhiteSpace(puzzleId) && pending.Contains(puzzleId);

    public static void RegisterPresenter(string puzzleId)
    {
        if (string.IsNullOrWhiteSpace(puzzleId)) return;
        presenters.TryGetValue(puzzleId, out int count);
        presenters[puzzleId] = count + 1;
    }

    /// <summary>The last presenter of a pending puzzle leaving settles it.</summary>
    public static void UnregisterPresenter(string puzzleId)
    {
        if (string.IsNullOrWhiteSpace(puzzleId)) return;
        if (!presenters.TryGetValue(puzzleId, out int count)) return;

        if (count > 1)
        {
            presenters[puzzleId] = count - 1;
            return;
        }

        presenters.Remove(puzzleId);
        Finish(puzzleId);
    }

    /// <summary>The presenter's moment is over. Does nothing for a puzzle that is not pending.</summary>
    public static void Finish(string puzzleId)
    {
        if (string.IsNullOrWhiteSpace(puzzleId)) return;
        if (pending.Remove(puzzleId)) Settled?.Invoke(puzzleId);
    }

    /// <summary>
    /// Called by <see cref="PuzzleStateManager"/> just before it raises OnPuzzleCompleted. Returns
    /// whether a presenter holds the puzzle; when none does, the caller settles it with
    /// <see cref="SettleNow"/> once OnPuzzleCompleted is out.
    /// </summary>
    internal static bool BeginHold(string puzzleId)
    {
        if (!presenters.ContainsKey(puzzleId)) return false;
        pending.Add(puzzleId);
        return true;
    }

    internal static void SettleNow(string puzzleId) => Settled?.Invoke(puzzleId);
}
