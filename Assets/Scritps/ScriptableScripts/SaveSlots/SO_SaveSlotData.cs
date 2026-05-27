using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public enum ModuleSlotStatus { Inactive, Active, Resolved }

/// <summary>
/// Una entrada de módulo dentro del save. Refleja la estructura del ModuleManager real:
/// el moduleId mapea con <c>ModuleData.ModuleID</c>, el timeRemaining permite reconstruir
/// el countdown al cargar la partida.
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
    [Header("Identidad")]
    [SerializeField] private int slotIndex = 1;
    [SerializeField] private bool isEmpty = true;

    [Header("Display (lo que muestra la card)")]
    [SerializeField] private string zoneName = "Zona Restringida";
    [SerializeField] private float playTimeSeconds = 0f;

    [Header("Timestamp del save")]
    [Tooltip("ISO 8601 UTC. El cálculo de \"hace X min\" se hace en runtime. Vacío = nunca guardado.")]
    [SerializeField] private string lastSavedIso = string.Empty;

    [Header("Progresión (preparado para conectar con ModuleManager)")]
    [Tooltip("Lista de módulos del piso. La card muestra los primeros 3 como pips.")]
    [SerializeField] private List<ModuleSaveEntry> modules = new List<ModuleSaveEntry>();

    [Header("Placeholders para save real futuro (sin uso visual hoy)")]
    [Tooltip("ID de la zona/sala actual del player. Hoy sin uso.")]
    [SerializeField] private string currentZoneId = string.Empty;
    [Tooltip("Items recogidos. Hoy sin uso — la card no los muestra.")]
    [SerializeField] private List<string> collectedItemIds = new List<string>();
    [Tooltip("Puzzles completados (mapea con PuzzleStateManager). Hoy sin uso.")]
    [SerializeField] private List<string> completedPuzzleIds = new List<string>();
    [Tooltip("Sockets con item insertado (mapea con PuzzleStateManager). Hoy sin uso.")]
    [SerializeField] private List<string> insertedSocketIds = new List<string>();

    // ── Getters públicos ───────────────────────────────────────────────────

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
    /// "hace 12 min", "hace 2 días", etc., calculado a partir de <see cref="LastSavedIso"/>.
    /// Si el ISO es inválido o vacío, retorna "—".
    /// </summary>
    public string GetRelativeSavedDescription()
    {
        if (string.IsNullOrEmpty(lastSavedIso)) return "—";
        if (!DateTime.TryParse(lastSavedIso, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime savedUtc))
            return "—";

        TimeSpan elapsed = DateTime.UtcNow - savedUtc;
        if (elapsed.TotalSeconds < 60) return "hace segundos";
        if (elapsed.TotalMinutes < 60) return $"hace {(int)elapsed.TotalMinutes} min";
        if (elapsed.TotalHours < 24)   return $"hace {(int)elapsed.TotalHours} h";
        if (elapsed.TotalDays < 7)     return $"hace {(int)elapsed.TotalDays} días";
        return $"hace {(int)(elapsed.TotalDays / 7)} semanas";
    }
}
