using System;
using System.Collections.Generic;
using UnityEngine;

public class PuzzleStateManager : Singleton<PuzzleStateManager>, ISessionResettable
{
    private readonly HashSet<string> completedPuzzles = new HashSet<string>();
    private readonly HashSet<string> insertedSockets = new HashSet<string>();
    private readonly HashSet<string> openedDoors = new HashSet<string>();
    private readonly Dictionary<string, int> valvePositions = new Dictionary<string, int>();
    private readonly Dictionary<string, string> containerSlots = new Dictionary<string, string>();

    /// <summary>
    /// A puzzle was completed for the first time. Raised once per id — re-completing an already
    /// completed puzzle is silent, and <see cref="RestoreSnapshot"/> never raises it.
    ///
    /// Static so that <see cref="Checkpoint"/> can subscribe from its own OnEnable without
    /// depending on this singleton's GameObject having woken up first.
    /// </summary>
    public static event Action<string> OnPuzzleCompleted;

    /// <summary>
    /// Static event state survives leaving Play mode when domain reload is disabled, which would
    /// leave listeners from the previous run hooked to destroyed objects.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => OnPuzzleCompleted = null;

    void Awake()
    {
        CreateSingleton(true);
        GameSession.Register(this);
    }

    void OnDestroy()
    {
        GameSession.Unregister(this);
    }

    /// <summary>
    /// <see cref="ISessionResettable"/> — dispatched by <see cref="GameSession.BeginNewSession"/>.
    /// Clears every collection so the next run starts with no puzzles completed, no sockets
    /// inserted, no doors opened, and every valve / container back at its default.
    /// </summary>
    public void ResetForNewSession()
    {
        completedPuzzles.Clear();
        insertedSockets.Clear();
        openedDoors.Clear();
        valvePositions.Clear();
        containerSlots.Clear();
    }


    public void SetPuzzleCompleted(string puzzleId)
    {
        if (string.IsNullOrWhiteSpace(puzzleId)) return;

        // HashSet.Add returns false when the id was already there: only the first completion
        // is an event, so a checkpoint does not re-activate every time the puzzle is re-run.
        if (!completedPuzzles.Add(puzzleId)) return;

        OnPuzzleCompleted?.Invoke(puzzleId);
    }

    public bool IsPuzzleCompleted(string puzzleId)
    {
        return !string.IsNullOrWhiteSpace(puzzleId) && completedPuzzles.Contains(puzzleId);
    }

    public void SetSocketInserted(string socketId)
    {
        if (string.IsNullOrWhiteSpace(socketId)) return;
        insertedSockets.Add(socketId);
    }

    public bool IsSocketInserted(string socketId)
    {
        return !string.IsNullOrWhiteSpace(socketId) && insertedSockets.Contains(socketId);
    }

    public void SetDoorOpened(string doorId)
    {
        if (string.IsNullOrWhiteSpace(doorId)) return;
        openedDoors.Add(doorId);
    }

    public bool IsDoorOpened(string doorId)
    {
        return !string.IsNullOrWhiteSpace(doorId) && openedDoors.Contains(doorId);
    }

    public void SetValvePosition(string valveId, int position)
    {
        if (string.IsNullOrWhiteSpace(valveId)) return;
        valvePositions[valveId] = position;
    }

    public int GetValvePosition(string valveId, int defaultValue = 0)
    {
        if (string.IsNullOrWhiteSpace(valveId)) return defaultValue;
        return valvePositions.TryGetValue(valveId, out int position) ? position : defaultValue;
    }

    public void SetContainerSlot(string containerId, string slotId)
    {
        if (string.IsNullOrWhiteSpace(containerId)) return;
        if (string.IsNullOrWhiteSpace(slotId)) return;

        containerSlots[containerId] = slotId;
    }

    public string GetContainerSlot(string containerId, string defaultSlotId = "")
    {
        if (string.IsNullOrWhiteSpace(containerId)) return defaultSlotId;

        return containerSlots.TryGetValue(containerId, out string slotId)
            ? slotId
            : defaultSlotId;
    }

    public void ClearContainerSlot(string containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId)) return;

        if (containerSlots.ContainsKey(containerId))
            containerSlots.Remove(containerId);
    }

    // ── Snapshot / restore (checkpoints) ────────────────────────────────────

    /// <summary>Deep copy of the whole puzzle progress right now.</summary>
    public PuzzleSnapshot Snapshot() =>
        new PuzzleSnapshot(completedPuzzles, insertedSockets, openedDoors, valvePositions, containerSlots);

    /// <summary>
    /// Rolls the puzzle progress back to a previously taken snapshot.
    ///
    /// The collections are cleared and refilled rather than reassigned because they are
    /// readonly, and deliberately so: other systems may already hold no reference to them, but
    /// keeping the same instances rules out a stale-reference class of bug entirely.
    ///
    /// Does not raise <see cref="OnPuzzleCompleted"/> — a restore is a rollback, not progress.
    /// </summary>
    public void RestoreSnapshot(PuzzleSnapshot snapshot)
    {
        if (snapshot == null) return;

        completedPuzzles.Clear();
        completedPuzzles.UnionWith(snapshot.CompletedPuzzles);

        insertedSockets.Clear();
        insertedSockets.UnionWith(snapshot.InsertedSockets);

        openedDoors.Clear();
        openedDoors.UnionWith(snapshot.OpenedDoors);

        valvePositions.Clear();
        foreach (KeyValuePair<string, int> pair in snapshot.ValvePositions)
            valvePositions[pair.Key] = pair.Value;

        containerSlots.Clear();
        foreach (KeyValuePair<string, string> pair in snapshot.ContainerSlots)
            containerSlots[pair.Key] = pair.Value;
    }
}

/// <summary>
/// Copy of the five puzzle-progress collections at one point in time.
///
/// Taken when a <see cref="Checkpoint"/> activates and restored when the Nemesis catches the
/// player, so a capture rolls back to the last safe state instead of ending the run.
///
/// The collections are copied, never referenced: the live ones keep mutating after the snapshot
/// is taken, so holding references would make the snapshot track the present.
/// </summary>
public sealed class PuzzleSnapshot
{
    internal readonly HashSet<string> CompletedPuzzles;
    internal readonly HashSet<string> InsertedSockets;
    internal readonly HashSet<string> OpenedDoors;
    internal readonly Dictionary<string, int> ValvePositions;
    internal readonly Dictionary<string, string> ContainerSlots;

    internal PuzzleSnapshot(
        HashSet<string> completedPuzzles,
        HashSet<string> insertedSockets,
        HashSet<string> openedDoors,
        Dictionary<string, int> valvePositions,
        Dictionary<string, string> containerSlots)
    {
        CompletedPuzzles = new HashSet<string>(completedPuzzles);
        InsertedSockets  = new HashSet<string>(insertedSockets);
        OpenedDoors      = new HashSet<string>(openedDoors);
        ValvePositions   = new Dictionary<string, int>(valvePositions);
        ContainerSlots   = new Dictionary<string, string>(containerSlots);
    }
}
