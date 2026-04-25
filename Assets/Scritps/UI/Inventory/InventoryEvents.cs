using System;
using UnityEngine;

public class InventoryEvents : MonoBehaviour
{
    public static event Action<SO_InventoryItem> OnItemAdded;
    
    public static event Action<SO_InventoryItem> OnItemRemoved;
    
    public static event Action<SO_InventoryItem> OnItemUsed;

    public static event Action<SO_InventoryItem> OnItemDiscarded;

    public static event Action<SO_InventoryItem> OnItemSelected;

    public static event Action OnInventoryFull;

    //------------------ Módulos ------------------
    
    public static event Action<ModuleData> OnModuleStatusChanged;

    public static event Action<ModuleData> OnModuleTimerTick;

    public static event Action<ModuleData> OnModuleFailure;
    
    // ------------------ UI ------------------

    public static event Action<bool> OnInventoryToggled;


    // ------------------ Invokers ------------------

    public static void ItemAdded(SO_InventoryItem item) => OnItemAdded?.Invoke(item);
    public static void ItemRemoved(SO_InventoryItem item) => OnItemRemoved?.Invoke(item);
    public static void ItemUsed(SO_InventoryItem item) => OnItemUsed?.Invoke(item);
    public static void ItemDiscarded(SO_InventoryItem item) => OnItemDiscarded?.Invoke(item);
    public static void ItemSelected(SO_InventoryItem item) => OnItemSelected?.Invoke(item);
    public static void InventoryFull() => OnInventoryFull?.Invoke();

    public static void ModuleStatusChanged(ModuleData data) => OnModuleStatusChanged?.Invoke(data);
    public static void ModuleTimerTick(ModuleData data) => OnModuleTimerTick?.Invoke(data);
    public static void ModuleFailure(ModuleData data) => OnModuleFailure?.Invoke(data);
    public static void InventoryToggled(bool isOpen) => OnInventoryToggled?.Invoke(isOpen);
}
