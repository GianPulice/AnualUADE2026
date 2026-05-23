using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base de datos de slots de guardado (stub). Hoy contiene datos fake en assets;
/// cuando exista un sistema de save real, esto se reemplaza por carga desde disco.
/// </summary>
[CreateAssetMenu(fileName = "SO_SaveSlotDatabase", menuName = "Scriptable Objects/SaveSlots/Save Slot Database")]
public class SO_SaveSlotDatabase : ScriptableObject
{
    [SerializeField] private List<SO_SaveSlotData> slots = new List<SO_SaveSlotData>();

    public IReadOnlyList<SO_SaveSlotData> Slots => slots;
}
