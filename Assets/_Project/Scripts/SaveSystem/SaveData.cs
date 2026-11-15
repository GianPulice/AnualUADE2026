using System;
using System.Collections.Generic;

/// <summary>
/// Everything one save slot holds, as written to disk (JSON, see <see cref="SaveFileStore"/>).
///
/// A save is a level checkpoint, not a snapshot of the moment: it is written once, when the player
/// reaches the start of a level (<see cref="SaveGame.ReachLevel"/>), and it holds the run as it was
/// right then. Nothing done inside the level is ever written, so loading it always starts that level
/// from the top with the state the previous levels left behind.
///
/// Plain serializable fields only: JsonUtility cannot write dictionaries, enums are stored by name so
/// reordering <see cref="ModuleStatus"/> cannot silently change a save, and every list is initialised
/// so a file missing a field still reads as empty instead of null.
/// </summary>
[Serializable]
public class SaveData
{
    /// <summary>Bumped when the format changes in a way older code cannot read. Files from a newer
    /// version are refused rather than half-read.</summary>
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    public int slotIndex;

    /// <summary><see cref="SO_SaveLevel.LevelId"/> of the level this save starts.</summary>
    public string levelId = string.Empty;

    /// <summary>Scene group (SO_SceneList label) the level lives in: what Load pushes.</summary>
    public string sceneGroupLabel = string.Empty;

    /// <summary>What the slot card shows as the location.</summary>
    public string zoneName = string.Empty;

    public float playTimeSeconds;

    /// <summary>ISO 8601 UTC ("o" format). For a resolved slot, when the run was won.</summary>
    public string lastSavedIso = string.Empty;

    /// <summary>
    /// The run in this slot was won (<see cref="SaveGame"/> marks it the moment the win is reported).
    /// A resolved slot cannot be loaded: the menu shows it as RESOLVED, and a new game may take the
    /// slot, the same as after a loss. Missing in older files = false.
    /// </summary>
    public bool resolved;

    public List<SavedModule> modules = new List<SavedModule>();
    public SavedPuzzleState puzzles = new SavedPuzzleState();
}

[Serializable]
public class SavedModule
{
    public string moduleId = string.Empty;

    /// <summary><see cref="ModuleStatus"/> by name.</summary>
    public string status = nameof(ModuleStatus.Inactive);

    public float timeRemaining;
    public float timerDuration;

    public ModuleStatus Status =>
        Enum.TryParse(status, out ModuleStatus parsed) ? parsed : ModuleStatus.Inactive;
}

/// <summary>
/// Serializable twin of <see cref="PuzzleSnapshot"/>: the five puzzle-progress collections of
/// <see cref="PuzzleStateManager"/>, with the two dictionaries flattened into lists.
/// </summary>
[Serializable]
public class SavedPuzzleState
{
    public List<string> completedPuzzles = new List<string>();
    public List<string> insertedSockets = new List<string>();
    public List<string> openedDoors = new List<string>();
    public List<SavedValve> valves = new List<SavedValve>();
    public List<SavedContainer> containers = new List<SavedContainer>();

    public static SavedPuzzleState FromSnapshot(PuzzleSnapshot snapshot)
    {
        SavedPuzzleState state = new SavedPuzzleState();
        if (snapshot == null) return state;

        state.completedPuzzles.AddRange(snapshot.CompletedPuzzles);
        state.insertedSockets.AddRange(snapshot.InsertedSockets);
        state.openedDoors.AddRange(snapshot.OpenedDoors);

        foreach (KeyValuePair<string, int> pair in snapshot.ValvePositions)
            state.valves.Add(new SavedValve { id = pair.Key, position = pair.Value });

        foreach (KeyValuePair<string, string> pair in snapshot.ContainerSlots)
            state.containers.Add(new SavedContainer { id = pair.Key, slotId = pair.Value });

        return state;
    }

    public PuzzleSnapshot ToSnapshot()
    {
        HashSet<string> completed = ToSet(completedPuzzles);
        HashSet<string> sockets = ToSet(insertedSockets);
        HashSet<string> doors = ToSet(openedDoors);

        Dictionary<string, int> valvePositions = new Dictionary<string, int>();
        if (valves != null)
            foreach (SavedValve valve in valves)
                if (valve != null && !string.IsNullOrWhiteSpace(valve.id)) valvePositions[valve.id] = valve.position;

        Dictionary<string, string> containerSlots = new Dictionary<string, string>();
        if (containers != null)
            foreach (SavedContainer container in containers)
                if (container != null && !string.IsNullOrWhiteSpace(container.id) &&
                    !string.IsNullOrWhiteSpace(container.slotId))
                    containerSlots[container.id] = container.slotId;

        return new PuzzleSnapshot(completed, sockets, doors, valvePositions, containerSlots);
    }

    private static HashSet<string> ToSet(List<string> list)
    {
        HashSet<string> set = new HashSet<string>();
        if (list == null) return set;

        foreach (string id in list)
            if (!string.IsNullOrWhiteSpace(id)) set.Add(id);

        return set;
    }
}

[Serializable]
public class SavedValve
{
    public string id = string.Empty;
    public int position;
}

[Serializable]
public class SavedContainer
{
    public string id = string.Empty;
    public string slotId = string.Empty;
}
