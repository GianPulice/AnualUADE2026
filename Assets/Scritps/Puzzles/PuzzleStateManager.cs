using System.Collections.Generic;

public class PuzzleStateManager : Singleton<PuzzleStateManager>
{
    private readonly HashSet<string> completedPuzzles = new HashSet<string>();
    private readonly HashSet<string> insertedSockets = new HashSet<string>();
    private readonly HashSet<string> openedDoors = new HashSet<string>();
    private readonly Dictionary<string, int> valvePositions = new Dictionary<string, int>();
    private readonly Dictionary<string, string> containerSlots = new Dictionary<string, string>();


    void Awake()
    {
        CreateSingleton(true);
    }


    public void SetPuzzleCompleted(string puzzleId)
    {
        if (string.IsNullOrWhiteSpace(puzzleId)) return;
        completedPuzzles.Add(puzzleId);
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
}
