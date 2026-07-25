using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Base de datos de slots de guardado (stub). Hoy contiene datos fake en sub-assets;
/// cuando exista un sistema de save real, esto se reemplaza por carga desde disco.
///
/// El menú contextual <c>Generate Mock Data</c> rellena la lista con 6 entries
/// representativos (3 con datos + 3 vacíos), según el wireframe main_menu_wired.html.
/// </summary>
[CreateAssetMenu(fileName = "SO_SaveSlotDatabase", menuName = "Scriptable Objects/SaveSlots/Save Slot Database")]
public class SO_SaveSlotDatabase : ScriptableObject
{
    [SerializeField] private List<SO_SaveSlotData> slots = new List<SO_SaveSlotData>();

    public IReadOnlyList<SO_SaveSlotData> Slots => slots;

    /// <summary>
    /// True si al menos un slot tiene partida. Lo consulta el MainMenu para deshabilitar
    /// "Load Game" cuando no hay nada que cargar.
    /// </summary>
    public bool HasAnySave
    {
        get
        {
            foreach (SO_SaveSlotData slot in slots)
                if (slot != null && !slot.IsEmpty) return true;
            return false;
        }
    }

    /// <summary>
    /// Deja los 6 slots vacíos. Es la forma segura de sacar la mock data del asset
    /// (editar el YAML a mano es frágil). Correr una vez desde el Inspector y commitear.
    /// </summary>
    [ContextMenu("Clear All Slots")]
    private void ClearAllSlotsMenu()
    {
#if UNITY_EDITOR
        ClearAllSlots();
#else
        Debug.LogWarning("[SO_SaveSlotDatabase] Clear All Slots solo funciona en el editor.");
#endif
    }

    [ContextMenu("Generate Mock Data")]
    private void GenerateMockDataMenu()
    {
#if UNITY_EDITOR
        GenerateMockData();
#else
        Debug.LogWarning("[SO_SaveSlotDatabase] Generate Mock Data solo funciona en el editor.");
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

        Debug.Log($"<color=cyan>[SO_SaveSlotDatabase] {cleared} slots vaciados.</color>");
    }

    private void GenerateMockData()
    {
        // 1. Limpiar sub-assets anteriores (los SO_SaveSlotData ya guardados como hijos).
        string mainAssetPath = AssetDatabase.GetAssetPath(this);
        if (string.IsNullOrEmpty(mainAssetPath))
        {
            Debug.LogError("[SO_SaveSlotDatabase] El asset todavía no fue guardado a disco. " +
                           "Guardalo (Ctrl+S) antes de generar mock data.");
            return;
        }

        UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(mainAssetPath);
        foreach (UnityEngine.Object sub in subAssets)
        {
            if (sub is SO_SaveSlotData) AssetDatabase.RemoveObjectFromAsset(sub);
        }

        slots.Clear();

        // 2. Crear los 6 sub-assets con los datos del wireframe.
        slots.Add(CreateMockSlot(1, false, "Zona Restringida", PlayTimeSeconds(2, 34),
            DateTime.UtcNow.AddMinutes(-12), new[]
            {
                Module("M1_Energetico", ModuleSlotStatus.Resolved, 0f, 300f),
                Module("M2_Mecanico",   ModuleSlotStatus.Resolved, 0f, 300f),
                Module("M3_Presion",    ModuleSlotStatus.Active, 180f, 300f),
            }));

        slots.Add(CreateMockSlot(2, false, "Piso 2 — Tecnico", PlayTimeSeconds(1, 8),
            DateTime.UtcNow.AddDays(-2), new[]
            {
                Module("M1_Energetico", ModuleSlotStatus.Resolved, 0f, 300f),
                Module("M2_Mecanico",   ModuleSlotStatus.Active, 240f, 300f),
                Module("M3_Presion",    ModuleSlotStatus.Inactive, 0f, 300f),
            }));

        slots.Add(CreateMockSlot(3, false, "Hub Central", PlayTimeSeconds(0, 22),
            DateTime.UtcNow.AddDays(-7), new[]
            {
                Module("M1_Energetico", ModuleSlotStatus.Active, 90f, 300f),
                Module("M2_Mecanico",   ModuleSlotStatus.Inactive, 0f, 300f),
                Module("M3_Presion",    ModuleSlotStatus.Inactive, 0f, 300f),
            }));

        // 3 slots vacíos.
        slots.Add(CreateMockSlot(4, true, string.Empty, 0f, DateTime.MinValue, Array.Empty<ModuleSaveEntry>()));
        slots.Add(CreateMockSlot(5, true, string.Empty, 0f, DateTime.MinValue, Array.Empty<ModuleSaveEntry>()));
        slots.Add(CreateMockSlot(6, true, string.Empty, 0f, DateTime.MinValue, Array.Empty<ModuleSaveEntry>()));

        // 4. Marcar el database como dirty y guardar.
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"<color=cyan>[SO_SaveSlotDatabase] Generados 6 sub-assets de mock data.</color>");
    }

    private SO_SaveSlotData CreateMockSlot(int slotIndex, bool isEmpty, string zoneName,
        float playTime, DateTime lastSavedUtc, ModuleSaveEntry[] modulesArr)
    {
        SO_SaveSlotData slot = ScriptableObject.CreateInstance<SO_SaveSlotData>();
        slot.name = $"SaveSlot_{slotIndex:00}";

        // Setear campos vía SerializedObject (no necesita exponer setters en el SO).
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

        // Los placeholders quedan vacíos (sin uso hoy).
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
