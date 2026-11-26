using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A capture while carrying a puzzle's reward costs the reward and the puzzle (design decision,
/// 2026-11-26): the item is taken out of the inventory and the puzzle it came from starts over —
/// state, objects, the reward box locked again, the gates it opened shut again.
///
/// Runs from <see cref="CheckpointManager.OnPuzzleProgressRestored"/>, after the respawn has rolled the
/// puzzle progress back to the checkpoint's snapshot and before the puzzles' own respawn handlers
/// look at it, so nothing sees a half-restarted puzzle. A reward that is not in the inventory —
/// still in its box, installed in the hub, or left on the floor — is not carried, and its puzzle is
/// left alone.
///
/// The checkpoint's snapshot was taken while the puzzle was solved, so every later rollback would put
/// the "solved" back with the reward gone. <see cref="Pending"/> remembers the restarted puzzles until
/// they are solved again and has <see cref="IPuzzleRestartable.ResetState"/> run again after each
/// restore. Static and installed at load, like <see cref="PuzzleCompletionAftermath"/>: no scene
/// object to place, and it works in every level that has restartable puzzles.
///
/// What is final: a piece installed in the hub is gone from the inventory, so its puzzle never
/// restarts and stays solved, with the hub's lights as the sign. Once the module is resolved none of
/// its sub-puzzles can restart (<see cref="ModuleIsDone"/>): the earlier module is behind the player
/// and never asks them to repeat what they finished.
///
/// Taken back with the puzzle: the power the first panel switched on (<see cref="PoweredLightSwitch"/>
/// listens to <see cref="Restarted"/>). Not undone, on purpose: the Nemesis route and wake-up, the
/// module clock that solving a puzzle started, and the checkpoint it activated; they latch, so solving
/// it again does not run them twice.
/// </summary>
public static class PuzzleCaptureRestart
{
    private static readonly List<IPuzzleRestartable> Puzzles = new List<IPuzzleRestartable>();
    private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>One puzzle was restarted by a capture, with its id. Raised once state and objects
    /// are back at the start: the reward box locks again and the gates it opened close.</summary>
    public static event Action<string> Restarted;

    /// <summary>A capture restarted this many puzzles (at least one). Raised once per capture,
    /// after every <see cref="Restarted"/>: the Architect's hint.</summary>
    public static event Action<int> CaptureRestartedPuzzles;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Puzzles.Clear();
        Pending.Clear();
        Restarted = null;
        CaptureRestartedPuzzles = null;
    }

    // After the other statics have been cleared, and again on every Play start with domain reload
    // off: unsubscribe first so it is never hooked twice.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        CheckpointManager.OnPuzzleProgressRestored -= HandleProgressRestored;
        CheckpointManager.OnPuzzleProgressRestored += HandleProgressRestored;

        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;

        GameSession.OnNewSessionStarting -= Pending.Clear;
        GameSession.OnNewSessionStarting += Pending.Clear;
    }

    public static void Register(IPuzzleRestartable puzzle)
    {
        if (puzzle != null && !Puzzles.Contains(puzzle)) Puzzles.Add(puzzle);
    }

    public static void Unregister(IPuzzleRestartable puzzle) => Puzzles.Remove(puzzle);

    private static void HandlePuzzleCompleted(string puzzleId) => Pending.Remove(puzzleId);

    private static void HandleProgressRestored()
    {
        // Restarted by an earlier capture and not solved since: the snapshot just put it back.
        foreach (string id in Pending)
            Find(id)?.ResetState();

        if (!InventoryManager.Exists) return;
        InventoryManager inventory = InventoryManager.Instance;

        // Collected first: restarting takes the item out of the inventory under the loop.
        List<(IPuzzleRestartable puzzle, SO_InventoryItem item)> carried =
            new List<(IPuzzleRestartable, SO_InventoryItem)>();

        foreach (IPuzzleRestartable puzzle in Puzzles)
        {
            if (Pending.Contains(puzzle.PuzzleId)) continue;
            if (ModuleIsDone(puzzle.PuzzleId)) continue;

            SO_InventoryItem item = PuzzleRewardBox.RewardItemOf(puzzle.PuzzleId);
            if (item == null) item = puzzle.RewardItem;
            if (item != null && inventory.HasItem(item)) carried.Add((puzzle, item));
        }

        foreach ((IPuzzleRestartable puzzle, SO_InventoryItem item) in carried)
        {
            inventory.DiscardItem(item);

            puzzle.ResetState();
            puzzle.ResetObjects();
            Pending.Add(puzzle.PuzzleId);

            Debug.Log($"[{nameof(PuzzleCaptureRestart)}] Caught carrying '{item.ItemName}': " +
                      $"puzzle '{puzzle.PuzzleId}' starts over.");
            Restarted?.Invoke(puzzle.PuzzleId);
        }

        if (carried.Count > 0) CaptureRestartedPuzzles?.Invoke(carried.Count);
    }

    /// <summary>
    /// The module this sub-puzzle belongs to is resolved: the hub took all its pieces, so the puzzle
    /// is behind the player for good and must never ask them to do it again. A piece that is
    /// installed is already out of the inventory; this covers a spare one still in the pocket.
    /// </summary>
    private static bool ModuleIsDone(string puzzleId)
    {
        if (!ModuleManager.Exists) return false;

        foreach (ModuleRuntime module in ModuleManager.Instance.GetAllModules())
            if (module.Data != null && module.Data.IsStartedBy(puzzleId) && module.Status == ModuleStatus.Resolved)
                return true;
        return false;
    }

    private static IPuzzleRestartable Find(string puzzleId)
    {
        foreach (IPuzzleRestartable puzzle in Puzzles)
            if (puzzle.PuzzleId == puzzleId) return puzzle;
        return null;
    }
}
