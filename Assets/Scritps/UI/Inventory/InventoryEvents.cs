using System;
using UnityEngine;

public class InventoryEvents : MonoBehaviour
{
    /// <summary>Un ítem fue agregado al inventario (recogido del mundo).</summary>
    public static event Action<SO_InventoryItem> OnItemAdded;

    /// <summary>Un ítem fue removido del inventario (por descarte o consumo).</summary>
    public static event Action<SO_InventoryItem> OnItemRemoved;

    /// <summary>El jugador seleccionó un ítem en la lista. Poblar el panel de detalle.</summary>
    public static event Action<SO_InventoryItem> OnItemSelected;

    /// <summary>Un ítem consumible fue usado al interactuar con un objeto del mundo.</summary>
    public static event Action<SO_InventoryItem> OnItemConsumed;

    // ------------------ Descarte ------------------

    /// <summary>El jugador presionó "descartar". Abrir el diálogo de confirmación.</summary>
    public static event Action<SO_InventoryItem> OnDiscardRequested;

    /// <summary>El jugador confirmó el descarte. Eliminar el ítem definitivamente.</summary>
    public static event Action<SO_InventoryItem> OnDiscardConfirmed;

    /// <summary>El jugador canceló el descarte (ESC o botón cancelar).</summary>
    public static event Action OnDiscardCancelled;

    //------------------ Módulos ------------------

    /// <summary>El estado de un módulo cambió (Activo, Resuelto, Explotado, Inactivo).</summary>
    public static event Action<ModuleData> OnModuleStateChanged;

    /// <summary>Tick del timer del módulo activo. Disparado cada frame mientras corre.</summary>
    public static event Action<ModuleData> OnModuleTimerTick;

    /// <summary>Un módulo llegó a cero y explotó. Aplicar penalización.</summary>
    public static event Action<ModuleData> OnModuleExploded;

    // ------------------ UI ------------------

    public static event Action<bool> OnInventoryToggled;


    // ------------------ Invokers ------------------

    public static void ItemAdded(SO_InventoryItem item) => OnItemAdded?.Invoke(item);
    public static void ItemRemoved(SO_InventoryItem item) => OnItemRemoved?.Invoke(item);
    public static void ItemSelected(SO_InventoryItem item) => OnItemSelected?.Invoke(item);
    public static void ItemConsumed(SO_InventoryItem item) => OnItemConsumed?.Invoke(item);

    public static void DiscardRequested(SO_InventoryItem item) => OnDiscardRequested?.Invoke(item);
    public static void DiscardConfirmed(SO_InventoryItem item) => OnDiscardConfirmed?.Invoke(item);
    public static void DiscardCancelled() => OnDiscardCancelled?.Invoke();

    public static void ModuleStateChanged(ModuleData data) => OnModuleStateChanged?.Invoke(data);
    public static void ModuleTimerTick(ModuleData data) => OnModuleTimerTick?.Invoke(data);
    public static void ModuleExploded(ModuleData data) => OnModuleExploded?.Invoke(data);

    public static void InventoryToggled(bool isOpen) => OnInventoryToggled?.Invoke(isOpen);
}
