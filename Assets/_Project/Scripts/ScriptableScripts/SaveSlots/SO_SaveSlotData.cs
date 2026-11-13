using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Exploded goes last on purpose: the mock sub-assets store this enum by index.
public enum ModuleSlotStatus { Inactive, Active, Resolved, Exploded }

/// <summary>
/// A module entry inside the save. Mirrors the structure of the real ModuleManager:
/// moduleId maps to <c>ModuleData.ModuleID</c>, and timeRemaining allows the countdown
/// to be rebuilt when loading the game.
/// </summary>
[System.Serializable]
public struct ModuleSaveEntry
{
    public string moduleId;
    public ModuleSlotStatus status;
    public float timeRemaining;
    public float timerDuration;
}

[CreateAssetMenu(fileName = "SO_SaveSlot", menuName = "Scriptable Objects/SaveSlots/Save Slot Data")]
public class SO_SaveSlotData : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private int slotIndex = 1;
    [SerializeField] private bool isEmpty = true;

    [Header("Display (what the card shows)")]
    [SerializeField] private string zoneName = "Restricted Area";
    [SerializeField] private float playTimeSeconds = 0f;

    [Header("Save timestamp")]
    [Tooltip("ISO 8601 UTC. The \"X min ago\" calculation is done at runtime. Empty = never saved.")]
    [SerializeField] private string lastSavedIso = string.Empty;

    [Header("Progression (ready to connect with the ModuleManager)")]
    [Tooltip("List of modules on the floor. The card shows the first 3 as pips.")]
    [SerializeField] private List<ModuleSaveEntry> modules = new List<ModuleSaveEntry>();

    [Header("Placeholders for the future real save (no visual use today)")]
    [Tooltip("ID of the player's current zone/room. Unused today.")]
    [SerializeField] private string currentZoneId = string.Empty;
    [Tooltip("Collected items. Unused today — the card does not show them.")]
    [SerializeField] private List<string> collectedItemIds = new List<string>();
    [Tooltip("Completed puzzles (maps to PuzzleStateManager). Unused today.")]
    [SerializeField] private List<string> completedPuzzleIds = new List<string>();
    [Tooltip("Sockets with an item inserted (maps to PuzzleStateManager). Unused today.")]
    [SerializeField] private List<string> insertedSocketIds = new List<string>();

    // ── Public getters ─────────────────────────────────────────────────────

    public int SlotIndex => slotIndex;
    public bool IsEmpty => isEmpty;
    public string ZoneName => zoneName;
    public float PlayTimeSeconds => playTimeSeconds;
    public string LastSavedIso => lastSavedIso;
    public IReadOnlyList<ModuleSaveEntry> Modules => modules;
    public string CurrentZoneId => currentZoneId;
    public IReadOnlyList<string> CollectedItemIds => collectedItemIds;
    public IReadOnlyList<string> CompletedPuzzleIds => completedPuzzleIds;
    public IReadOnlyList<string> InsertedSocketIds => insertedSocketIds;

    /// <summary>
    /// A card's data built at runtime from a save on disk (<see cref="SaveData"/>), or an empty slot
    /// when <paramref name="data"/> is null. Never saved as an asset (HideFlags.DontSave): the files
    /// are the truth, this is only what the card reads. Built by <see cref="SO_SaveSlotDatabase"/>.
    /// </summary>
    public static SO_SaveSlotData CreateRuntime(int slotIndex, SaveData data)
    {
        SO_SaveSlotData slot = CreateInstance<SO_SaveSlotData>();
        slot.hideFlags = HideFlags.DontSave;
        slot.name = $"SaveSlot_{slotIndex:00} (runtime)";
        slot.slotIndex = slotIndex;

        if (data == null)
        {
            slot.isEmpty = true;
            slot.zoneName = string.Empty;
            return slot;
        }

        slot.isEmpty = false;
        slot.zoneName = data.zoneName;
        slot.playTimeSeconds = data.playTimeSeconds;
        slot.lastSavedIso = data.lastSavedIso;
        slot.currentZoneId = data.levelId;

        foreach (SavedModule module in data.modules)
        {
            if (module == null) continue;
            slot.modules.Add(new ModuleSaveEntry
            {
                moduleId = module.moduleId,
                status = ToSlotStatus(module.Status),
                timeRemaining = module.timeRemaining,
                timerDuration = module.timerDuration,
            });
        }

        if (data.puzzles != null)
        {
            if (data.puzzles.completedPuzzles != null) slot.completedPuzzleIds.AddRange(data.puzzles.completedPuzzles);
            if (data.puzzles.insertedSockets != null) slot.insertedSocketIds.AddRange(data.puzzles.insertedSockets);
        }

        return slot;
    }

    private static ModuleSlotStatus ToSlotStatus(ModuleStatus status) => status switch
    {
        ModuleStatus.Active   => ModuleSlotStatus.Active,
        ModuleStatus.Resolved => ModuleSlotStatus.Resolved,
        ModuleStatus.Exploded => ModuleSlotStatus.Exploded,
        _                     => ModuleSlotStatus.Inactive,
    };

    public int ResolvedModulesCount
    {
        get
        {
            int count = 0;
            foreach (ModuleSaveEntry m in modules)
                if (m.status == ModuleSlotStatus.Resolved) count++;
            return count;
        }
    }

    /// <summary>
    /// "12 min ago", "2 days ago", etc., computed from <see cref="LastSavedIso"/>.
    /// If the ISO string is invalid or empty, returns "—".
    /// </summary>
    public string GetRelativeSavedDescription()
    {
        if (string.IsNullOrEmpty(lastSavedIso)) return "—";
        if (!DateTime.TryParse(lastSavedIso, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime savedUtc))
            return "—";

        TimeSpan elapsed = DateTime.UtcNow - savedUtc;
        if (elapsed.TotalSeconds < 60) return "seconds ago";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed.TotalHours < 24)   return $"{(int)elapsed.TotalHours} h ago";
        if (elapsed.TotalDays < 7)     return $"{(int)elapsed.TotalDays} days ago";
        return $"{(int)(elapsed.TotalDays / 7)} weeks ago";
    }
}
