using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The save system: which slot the current run belongs to, when that slot is written, restored and
/// deleted. Static, like <see cref="GameResultManager"/> and <see cref="GameSession"/>: it needs no
/// scene and survives every scene change. The files themselves are <see cref="SaveFileStore"/>'s.
///
/// The rules:
///   • A run belongs to one slot, chosen in the main menu (<see cref="BeginNewRun"/> for an empty
///     slot, <see cref="ApplyLoadedRun"/> for one with a save). No slot = nothing is ever written.
///   • The slot is written only when the player reaches the start of a level through the real path
///     (<see cref="ReachLevel"/>: Zona 2, at the end of the escape). Never in the first level, never
///     during a level, so a save always starts its level from the top.
///   • Reaching a level empties the inventory: nothing found in one level travels to the next, the
///     same rule <see cref="LevelTransition"/> applies between scenes.
///   • Losing the run (Lose or GameOver) deletes the slot the moment the loss is reported, before
///     the defeat cinematic, so closing the game on it cannot keep the save. A Retry then starts the
///     first level again, in the same slot, with no save until the next level is reached.
///   • A Nemesis capture is not a loss (the checkpoint respawn), so it never touches the save.
///   • Winning the run marks the slot resolved the moment the win is reported (<see cref="SaveData.resolved"/>):
///     it can no longer be loaded, the menu shows it as RESOLVED, and a new game may take it, as
///     after a loss. New Game prefers a truly empty slot, then a resolved one.
///   • A new run takes its slot whole: whatever the slot held (a resolved run, or a save the player
///     chose to overwrite) is deleted when the run begins (<see cref="BeginNewRun"/>).
///
/// Loading restores the managers that live in the Data scene (modules, puzzles, inventory, play
/// time) before the level scene is pushed, so every Awake and Start of the level already sees the
/// restored run; the level's entry point then moves the player onto itself
/// (<see cref="TryConsumePendingSpawn"/>) and the opening cinematic is skipped (<see cref="SkipsLevelIntro"/>).
/// </summary>
public static class SaveGame
{
    public const int NoSlot = 0;

    /// <summary>The slot this run saves into. <see cref="NoSlot"/> = none (played without the menu).</summary>
    public static int ActiveSlot { get; private set; } = NoSlot;

    /// <summary>This run came from a save: it starts mid-game, so the level's opening (the wake-up
    /// cinematic) must not play. Cleared by the next <see cref="GameSession.BeginNewSession"/>.</summary>
    public static bool SkipsLevelIntro { get; private set; }

    // Level id whose entry point must take the player on arrival. Consumed once.
    private static string pendingSpawnLevelId;

    // PlayerPrefs, not a save file: it is only a hint for the menu, and it must outlive the slots
    // (deleting or losing a slot does not have to touch it; the menu ignores it then).
    private const string LastPlayedSlotKey = "WIRED.Save.LastPlayedSlot";

    /// <summary>
    /// Slot of the last run started or loaded from the menu, across sessions, or <see cref="NoSlot"/>.
    /// The Save Slots screen marks that card; it may point at a slot that is empty by now.
    /// </summary>
    public static int LastPlayedSlot => PlayerPrefs.GetInt(LastPlayedSlotKey, NoSlot);

    /// <summary>A slot was written (its index).</summary>
    public static event Action<int> OnSlotSaved;

    /// <summary>A slot was deleted (its index), by a loss or by <see cref="DeleteSlot"/>.</summary>
    public static event Action<int> OnSlotDeleted;

    /// <summary>The run in a slot was won and the slot marked resolved (its index).</summary>
    public static event Action<int> OnSlotResolved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveSlot = NoSlot;
        SkipsLevelIntro = false;
        pendingSpawnLevelId = null;
        OnSlotSaved = null;
        OnSlotDeleted = null;
        OnSlotResolved = null;
    }

    /// <summary>
    /// AfterAssembliesLoaded, not SubsystemRegistration: GameSession clears its event there, and two
    /// methods of the same load type run in no fixed order (see GameResultManager.HookSessionReset).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void HookEvents()
    {
        GameSession.OnNewSessionStarting -= HandleNewSession;
        GameSession.OnNewSessionStarting += HandleNewSession;

        GameResultManager.OnRunLost -= HandleRunLost;
        GameResultManager.OnRunLost += HandleRunLost;

        GameResultManager.OnRunWon -= HandleRunWon;
        GameResultManager.OnRunWon += HandleRunWon;
    }

    // ── Queries (menu) ──────────────────────────────────────────────────────

    /// <summary>Any valid file in the slot, a resolved one included (the menu shows those too).</summary>
    public static bool TryReadSlot(int slotIndex, out SaveData data) => SaveFileStore.TryRead(slotIndex, out data);

    /// <summary>A save that can be loaded: a valid file that is not resolved.</summary>
    public static bool HasSave(int slotIndex) => SaveFileStore.TryRead(slotIndex, out SaveData data) && !data.resolved;

    /// <summary>The slot holds a won run: shown as RESOLVED, never loaded.</summary>
    public static bool IsResolved(int slotIndex) => SaveFileStore.TryRead(slotIndex, out SaveData data) && data.resolved;

    /// <summary>At least one slot can be loaded (resolved ones do not count).</summary>
    public static bool HasAnySave(int slotCount)
    {
        for (int i = 1; i <= slotCount; i++)
            if (HasSave(i)) return true;
        return false;
    }

    /// <summary>
    /// The slot New Game takes (1..slotCount): the first with nothing in it, else the first resolved
    /// one (a won run frees its slot, as a lost one does). <see cref="NoSlot"/> when every slot holds
    /// a save that can still be loaded.
    /// </summary>
    public static int FindFirstEmptySlot(int slotCount)
    {
        int firstResolved = NoSlot;
        for (int i = 1; i <= slotCount; i++)
        {
            if (!SaveFileStore.TryRead(i, out SaveData data)) return i;
            if (data.resolved && firstResolved == NoSlot) firstResolved = i;
        }
        return firstResolved;
    }

    // ── Run lifecycle (menu) ────────────────────────────────────────────────

    /// <summary>
    /// A new run in this slot, from the first level. Call after <see cref="GameSession.BeginNewSession"/>.
    /// The slot is the new run's from now on: whatever it held (a resolved run, or a save the player
    /// confirmed to overwrite) is deleted here. Nothing new is written until the next level is reached.
    /// </summary>
    public static void BeginNewRun(int slotIndex)
    {
        ActiveSlot = SaveFileStore.IsValidSlot(slotIndex) ? slotIndex : NoSlot;
        SkipsLevelIntro = false;
        pendingSpawnLevelId = null;

        if (ActiveSlot != NoSlot && SaveFileStore.TryRead(ActiveSlot, out SaveData previous))
        {
            Debug.Log($"[{nameof(SaveGame)}] Slot {ActiveSlot} held a {(previous.resolved ? "resolved run" : "save")}: " +
                      "replaced by the new run.");
            DeleteSlot(ActiveSlot);
        }

        RememberLastPlayed(ActiveSlot);
        Debug.Log($"[{nameof(SaveGame)}] New run in slot {Describe(ActiveSlot)}.");
    }

    /// <summary>
    /// Restores a save into the persistent managers. Call after <see cref="GameSession.BeginNewSession"/>
    /// (which clears them) and before the level scene is pushed.
    /// </summary>
    public static void ApplyLoadedRun(SaveData data)
    {
        if (data == null)
        {
            Debug.LogError($"[{nameof(SaveGame)}] ApplyLoadedRun with no data.");
            return;
        }

        ActiveSlot = data.slotIndex;
        SkipsLevelIntro = true;
        pendingSpawnLevelId = data.levelId;
        RememberLastPlayed(ActiveSlot);

        RestoreModules(data);
        RestorePuzzles(data);

        // The level always starts with an empty inventory: that is how it was when it was saved
        // (ReachLevel empties it), and the session reset re-seeds the testing items.
        if (InventoryManager.Exists) InventoryManager.Instance.ClearAll();

        Debug.Log($"[{nameof(SaveGame)}] Slot {data.slotIndex} loaded: level '{data.levelId}' in group " +
                  $"'{data.sceneGroupLabel}'.");
    }

    // ── Level arrival (gameplay) ────────────────────────────────────────────

    /// <summary>
    /// The player has just reached the start of <paramref name="level"/> through the real path.
    /// Empties the inventory and writes the run into the active slot.
    /// </summary>
    public static void ReachLevel(SO_SaveLevel level)
    {
        if (level == null || !level.IsValid)
        {
            Debug.LogWarning($"[{nameof(SaveGame)}] ReachLevel with no valid SO_SaveLevel: nothing done.");
            return;
        }

        if (InventoryManager.Exists) InventoryManager.Instance.ClearAll();

        if (ActiveSlot == NoSlot)
        {
            Debug.Log($"[{nameof(SaveGame)}] Reached '{level.LevelId}' with no save slot (the run did not " +
                      "start from the main menu): not saved.");
            return;
        }

        string group = ScreenManager.Exists ? ScreenManager.Instance.CurrentGroupLabel : null;
        if (string.IsNullOrWhiteSpace(group))
        {
            Debug.LogError($"[{nameof(SaveGame)}] Reached '{level.LevelId}' but the current scene group is " +
                           "unknown: not saved.");
            return;
        }

        SaveData data = Capture(level, group);
        if (!SaveFileStore.Write(ActiveSlot, data)) return;

        Debug.Log($"[{nameof(SaveGame)}] Slot {ActiveSlot} saved at '{level.LevelId}'.");
        OnSlotSaved?.Invoke(ActiveSlot);
    }

    /// <summary>
    /// True once, for the entry point of the level a save was just loaded at: it then puts the
    /// player on itself.
    /// </summary>
    public static bool TryConsumePendingSpawn(string levelId)
    {
        if (!HasPendingSpawn(levelId)) return false;

        pendingSpawnLevelId = null;
        return true;
    }

    public static bool HasPendingSpawn(string levelId) =>
        !string.IsNullOrWhiteSpace(levelId) && pendingSpawnLevelId == levelId;

    // ── Deletion ────────────────────────────────────────────────────────────

    /// <summary>Deletes a slot (the UI's delete button, a new run taking it). Safe on an empty slot.</summary>
    public static bool DeleteSlot(int slotIndex)
    {
        if (!SaveFileStore.IsValidSlot(slotIndex)) return false;

        bool hadFile = TryReadSlot(slotIndex, out _);
        if (!SaveFileStore.Delete(slotIndex)) return false;

        if (hadFile)
        {
            Debug.Log($"[{nameof(SaveGame)}] Slot {slotIndex} deleted.");
            OnSlotDeleted?.Invoke(slotIndex);
        }

        return true;
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private static void HandleNewSession()
    {
        // The slot stays: a Retry keeps playing in it. Only what a load set up is dropped.
        SkipsLevelIntro = false;
        pendingSpawnLevelId = null;
    }

    private static void HandleRunLost()
    {
        if (ActiveSlot == NoSlot) return;
        if (!HasSave(ActiveSlot)) return;

        Debug.Log($"[{nameof(SaveGame)}] The run was lost: deleting slot {ActiveSlot}.");
        DeleteSlot(ActiveSlot);
    }

    /// <summary>
    /// The run was won: its slot becomes resolved, with the play time, modules and date of the win.
    /// The rest of the save stays (the card still says where it was), but it is never loaded again.
    /// A run that never reached a save point has no file to mark: nothing is written then.
    /// </summary>
    private static void HandleRunWon()
    {
        if (ActiveSlot == NoSlot) return;

        if (!TryReadSlot(ActiveSlot, out SaveData data))
        {
            Debug.Log($"[{nameof(SaveGame)}] The run was won with no save in slot {ActiveSlot}: nothing to mark.");
            return;
        }

        data.resolved = true;
        data.lastSavedIso = DateTime.UtcNow.ToString("o");
        if (ModuleManager.Exists)
        {
            data.playTimeSeconds = ModuleManager.Instance.SessionTime;
            data.modules = CaptureModules(warnActive: false);
        }

        if (!SaveFileStore.Write(ActiveSlot, data)) return;

        Debug.Log($"[{nameof(SaveGame)}] The run was won: slot {ActiveSlot} is resolved.");
        OnSlotResolved?.Invoke(ActiveSlot);
    }

    private static SaveData Capture(SO_SaveLevel level, string group)
    {
        SaveData data = new SaveData
        {
            slotIndex = ActiveSlot,
            levelId = level.LevelId,
            sceneGroupLabel = group,
            zoneName = level.DisplayName,
            lastSavedIso = DateTime.UtcNow.ToString("o"),
        };

        if (ModuleManager.Exists)
        {
            data.playTimeSeconds = ModuleManager.Instance.SessionTime;
            data.modules = CaptureModules(warnActive: true);
        }

        if (PuzzleStateManager.Exists)
            data.puzzles = SavedPuzzleState.FromSnapshot(PuzzleStateManager.Instance.Snapshot());

        return data;
    }

    private static void RestoreModules(SaveData data)
    {
        if (!ModuleManager.Exists)
        {
            Debug.LogWarning($"[{nameof(SaveGame)}] No ModuleManager: the modules of slot {data.slotIndex} " +
                             "were not restored.");
            return;
        }

        foreach (SavedModule saved in data.modules)
        {
            if (saved == null || string.IsNullOrWhiteSpace(saved.moduleId)) continue;

            ModuleStatus status = saved.Status;
            if (status != ModuleStatus.Resolved && status != ModuleStatus.Exploded) continue;

            if (ModuleManager.Instance.GetRuntime(saved.moduleId) == null)
            {
                Debug.LogWarning($"[{nameof(SaveGame)}] Saved module '{saved.moduleId}' is not in the modules " +
                                 "config any more: ignored.");
                continue;
            }

            // A restore, not progress: no explosion, no penalty event, no GameOver check. The player
            // picks the penalties up from the manager in its own Start.
            ModuleManager.Instance.RestoreModuleState(saved.moduleId, status);
        }

        ModuleManager.Instance.RestoreSessionTime(data.playTimeSeconds);
    }

    private static void RestorePuzzles(SaveData data)
    {
        if (!PuzzleStateManager.Exists)
        {
            Debug.LogWarning($"[{nameof(SaveGame)}] No PuzzleStateManager: the puzzles of slot {data.slotIndex} " +
                             "were not restored.");
            return;
        }

        // RestoreSnapshot raises no OnPuzzleCompleted: nothing replays (doors, the escape, the Architect).
        // Everything that depends on a completed puzzle catches up on its own Start, as after a checkpoint.
        PuzzleStateManager.Instance.RestoreSnapshot(data.puzzles.ToSnapshot());
    }

    // The modules as the manager has them now. warnActive: at a save point a running module is worth a
    // warning (it loads as not started); at a win it is not.
    private static List<SavedModule> CaptureModules(bool warnActive)
    {
        List<SavedModule> modules = new List<SavedModule>();
        foreach (ModuleRuntime runtime in ModuleManager.Instance.GetAllModules())
        {
            if (runtime == null || runtime.Data == null) continue;

            if (warnActive && runtime.Status == ModuleStatus.Active)
                Debug.LogWarning($"[{nameof(SaveGame)}] Module '{runtime.ModuleID}' is Active at the save " +
                                 "point: it will load as not started (only Resolved / Exploded restore).");

            modules.Add(new SavedModule
            {
                moduleId = runtime.ModuleID,
                status = runtime.Status.ToString(),
                timeRemaining = runtime.TimeRemaining,
                timerDuration = runtime.Data.TimerDuration,
            });
        }
        return modules;
    }

    private static void RememberLastPlayed(int slotIndex)
    {
        if (slotIndex == NoSlot) return;

        PlayerPrefs.SetInt(LastPlayedSlotKey, slotIndex);
        PlayerPrefs.Save();
    }

    private static string Describe(int slotIndex) => slotIndex == NoSlot ? "(none)" : slotIndex.ToString();
}
