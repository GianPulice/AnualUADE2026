using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public enum ModuleSlotStatus { Inactive, Active, Resolved }

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
