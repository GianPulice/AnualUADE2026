using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// The save slots the menu shows. In Play the cards are built from the save files on disk
/// (<see cref="SaveGame"/> / <see cref="SaveFileStore"/>), <see cref="SlotCount"/> of them, refreshed
/// with <see cref="ReloadFromDisk"/>.
///
/// The serialized <c>slots</c> list is the old mock data (sub-assets): kept only so the cards can
/// be looked at outside Play. It is never read in Play. The <c>Generate Mock Data</c> context menu
/// fills it with 6 representative entries (3 with data + 3 empty), following the
/// main_menu_wired.html wireframe.
/// </summary>
[CreateAssetMenu(fileName = "SO_SaveSlotDatabase", menuName = "Scriptable Objects/SaveSlots/Save Slot Database")]
public class SO_SaveSlotDatabase : ScriptableObject
{
    [Tooltip("How many save slots the game has (cards in the Load screen). Slots are numbered 1..N.")]
    [SerializeField, Range(1, SaveFileStore.MaxSlotIndex)] private int slotCount = 6;

    [SerializeField] private List<SO_SaveSlotData> slots = new List<SO_SaveSlotData>();

    // The cards built from disk in Play. Not serialized: the files are the truth.
    [NonSerialized] private readonly List<SO_SaveSlotData> runtimeSlots = new List<SO_SaveSlotData>();

    public int SlotCount => slotCount;

    /// <summary>In Play, one card per slot as on disk (loaded on first use, then on
    /// <see cref="ReloadFromDisk"/>). Outside Play, the mock data.</summary>
    public IReadOnlyList<SO_SaveSlotData> Slots
    {
        get
        {
            if (!Application.isPlaying) return slots;
            if (!RuntimeSlotsValid()) ReloadFromDisk();
            return runtimeSlots;
        }
    }

    /// <summary>
    /// True if at least one slot has a game in it. Queried by the MainMenu to disable
    /// "Load Game" when there is nothing to load. Read from disk in Play.
    /// </summary>
    public bool HasAnySave
    {
        get
        {
            if (Application.isPlaying) return SaveGame.HasAnySave(slotCount);

            foreach (SO_SaveSlotData slot in slots)
                if (slot != null && !slot.IsEmpty) return true;
            return false;
        }
    }

    /// <summary>First slot with no save (1-based), or <see cref="SaveGame.NoSlot"/> when all are taken.</summary>
    public int FindFirstEmptySlot() => SaveGame.FindFirstEmptySlot(slotCount);

    /// <summary>Rebuilds the cards from the files. Call after anything writes or deletes a slot.</summary>
    public void ReloadFromDisk()
    {
        foreach (SO_SaveSlotData old in runtimeSlots)
            if (old != null) Destroy(old);
        runtimeSlots.Clear();

        for (int i = 1; i <= slotCount; i++)
        {
            SaveGame.TryReadSlot(i, out SaveData data);
            runtimeSlots.Add(SO_SaveSlotData.CreateRuntime(i, data));
        }
    }

    // The list outlives Play mode when domain reload is off: its instances are destroyed then.
    private bool RuntimeSlotsValid()
    {
        if (runtimeSlots.Count != slotCount) return false;
        foreach (SO_SaveSlotData slot in runtimeSlots)
            if (slot == null) return false;
        return true;
    }

    /// <summary>
    /// Leaves the 6 slots empty. This is the safe way to strip the mock data out of the asset
    /// (editing the YAML by hand is fragile). Run it once from the Inspector and commit.
    /// </summary>
    [ContextMenu("Clear All Slots")]
    private void ClearAllSlotsMenu()
    {
#if UNITY_EDITOR
        ClearAllSlots();
#else
        Debug.LogWarning("[SO_SaveSlotDatabase] Clear All Slots only works in the editor.");
#endif
    }

    [ContextMenu("Generate Mock Data")]
    private void GenerateMockDataMenu()
    {
#if UNITY_EDITOR
        GenerateMockData();
#else
        Debug.LogWarning("[SO_SaveSlotDatabase] Generate Mock Data only works in the editor.");
#endif
    }

#if UNITY_EDITOR
    private void ClearAllSlots()
    {
        int cleared = 0;

        foreach (SO_SaveSlotData slot in slots)
        {
            if (slot == null) continue;

            SerializedObject so = new SerializedObject(slot);
            so.FindProperty("isEmpty").boolValue           = true;
            so.FindProperty("zoneName").stringValue        = string.Empty;
            so.FindProperty("playTimeSeconds").floatValue  = 0f;
            so.FindProperty("lastSavedIso").stringValue    = string.Empty;
            so.FindProperty("modules").arraySize           = 0;
            so.FindProperty("currentZoneId").stringValue   = string.Empty;
            so.FindProperty("collectedItemIds").arraySize  = 0;
            so.FindProperty("completedPuzzleIds").arraySize = 0;
            so.FindProperty("insertedSocketIds").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(slot);
            cleared++;
        }

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"<color=cyan>[SO_SaveSlotDatabase] {cleared} slots cleared.</color>");
    }

    private void GenerateMockData()
    {
        // 1. Clean up previous sub-assets (the SO_SaveSlotData already saved as children).
        string mainAssetPath = AssetDatabase.GetAssetPath(this);
        if (string.IsNullOrEmpty(mainAssetPath))
        {
            Debug.LogError("[SO_SaveSlotDatabase] The asset has not been saved to disk yet. " +
                           "Save it (Ctrl+S) before generating mock data.");
            return;
        }

        UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(mainAssetPath);
        foreach (UnityEngine.Object sub in subAssets)
        {
            if (sub is SO_SaveSlotData) AssetDatabase.RemoveObjectFromAsset(sub);
        }

        slots.Clear();

        // 2. Create the 6 sub-assets with the wireframe data.
        slots.Add(CreateMockSlot(1, false, "Restricted Area", PlayTimeSeconds(2, 34),
            DateTime.UtcNow.AddMinutes(-12), new[]
            {
                Module("M1_Energetico", ModuleSlotStatus.Resolved, 0f, 300f),
                Module("M2_Mecanico",   ModuleSlotStatus.Resolved, 0f, 300f),
                Module("M3_Presion",    ModuleSlotStatus.Active, 180f, 300f),
            }));

        slots.Add(CreateMockSlot(2, false, "Floor 2 — Technical", PlayTimeSeconds(1, 8),
            DateTime.UtcNow.AddDays(-2), new[]
            {
                Module("M1_Energetico", ModuleSlotStatus.Resolved, 0f, 300f),
                Module("M2_Mecanico",   ModuleSlotStatus.Active, 240f, 300f),
                Module("M3_Presion",    ModuleSlotStatus.Inactive, 0f, 300f),
            }));

        slots.Add(CreateMockSlot(3, false, "Central Hub", PlayTimeSeconds(0, 22),
            DateTime.UtcNow.AddDays(-7), new[]
            {
                Module("M1_Energetico", ModuleSlotStatus.Active, 90f, 300f),
                Module("M2_Mecanico",   ModuleSlotStatus.Inactive, 0f, 300f),
                Module("M3_Presion",    ModuleSlotStatus.Inactive, 0f, 300f),
            }));

        // 3 empty slots.
        slots.Add(CreateMockSlot(4, true, string.Empty, 0f, DateTime.MinValue, Array.Empty<ModuleSaveEntry>()));
        slots.Add(CreateMockSlot(5, true, string.Empty, 0f, DateTime.MinValue, Array.Empty<ModuleSaveEntry>()));
        slots.Add(CreateMockSlot(6, true, string.Empty, 0f, DateTime.MinValue, Array.Empty<ModuleSaveEntry>()));

        // 4. Mark the database dirty and save.
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"<color=cyan>[SO_SaveSlotDatabase] Generated 6 mock data sub-assets.</color>");
    }

    private SO_SaveSlotData CreateMockSlot(int slotIndex, bool isEmpty, string zoneName,
        float playTime, DateTime lastSavedUtc, ModuleSaveEntry[] modulesArr)
    {
        SO_SaveSlotData slot = ScriptableObject.CreateInstance<SO_SaveSlotData>();
        slot.name = $"SaveSlot_{slotIndex:00}";

        // Set the fields via SerializedObject (no need to expose setters on the SO).
        SerializedObject so = new SerializedObject(slot);
        so.FindProperty("slotIndex").intValue = slotIndex;
        so.FindProperty("isEmpty").boolValue = isEmpty;
        so.FindProperty("zoneName").stringValue = zoneName;
        so.FindProperty("playTimeSeconds").floatValue = playTime;
        so.FindProperty("lastSavedIso").stringValue = isEmpty ? string.Empty : lastSavedUtc.ToString("o");

        SerializedProperty modulesProp = so.FindProperty("modules");
        modulesProp.arraySize = modulesArr.Length;
        for (int i = 0; i < modulesArr.Length; i++)
        {
            SerializedProperty entry = modulesProp.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("moduleId").stringValue       = modulesArr[i].moduleId;
            entry.FindPropertyRelative("status").enumValueIndex      = (int)modulesArr[i].status;
            entry.FindPropertyRelative("timeRemaining").floatValue   = modulesArr[i].timeRemaining;
            entry.FindPropertyRelative("timerDuration").floatValue   = modulesArr[i].timerDuration;
        }

        // The placeholders stay empty (unused today).
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.AddObjectToAsset(slot, this);
        return slot;
    }

    private static float PlayTimeSeconds(int hours, int minutes) => hours * 3600f + minutes * 60f;

    private static ModuleSaveEntry Module(string id, ModuleSlotStatus status, float timeRemaining, float duration)
        => new ModuleSaveEntry
        {
            moduleId = id,
            status = status,
            timeRemaining = timeRemaining,
            timerDuration = duration,
        };
#endif
}
