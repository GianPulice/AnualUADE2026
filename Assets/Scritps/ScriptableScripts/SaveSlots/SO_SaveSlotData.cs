using UnityEngine;

public enum ModuleSlotStatus { Inactive, Active, Resolved }

[CreateAssetMenu(fileName = "SO_SaveSlot", menuName = "Scriptable Objects/SaveSlots/Save Slot Data")]
public class SO_SaveSlotData : ScriptableObject
{
    [Header("Identidad")]
    [SerializeField] private int slotIndex = 1;
    [SerializeField] private bool isEmpty = true;

    [Header("Contenido (ignorado si isEmpty=true)")]
    [SerializeField] private string zoneName = "Zona Restringida";
    [SerializeField] private float playTimeSeconds = 0f;
    [SerializeField] private string lastSavedDescription = "hace 12 min";

    [Header("Estado de módulos")]
    [SerializeField] private ModuleSlotStatus module1Status = ModuleSlotStatus.Inactive;
    [SerializeField] private ModuleSlotStatus module2Status = ModuleSlotStatus.Inactive;
    [SerializeField] private ModuleSlotStatus module3Status = ModuleSlotStatus.Inactive;

    public int SlotIndex => slotIndex;
    public bool IsEmpty => isEmpty;
    public string ZoneName => zoneName;
    public float PlayTimeSeconds => playTimeSeconds;
    public string LastSavedDescription => lastSavedDescription;
    public ModuleSlotStatus Module1Status => module1Status;
    public ModuleSlotStatus Module2Status => module2Status;
    public ModuleSlotStatus Module3Status => module3Status;

    public int ResolvedModulesCount
    {
        get
        {
            int count = 0;
            if (module1Status == ModuleSlotStatus.Resolved) count++;
            if (module2Status == ModuleSlotStatus.Resolved) count++;
            if (module3Status == ModuleSlotStatus.Resolved) count++;
            return count;
        }
    }
}
