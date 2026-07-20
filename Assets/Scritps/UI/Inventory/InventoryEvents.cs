using System;
using UnityEngine;

public class InventoryEvents : MonoBehaviour
{
    /// <summary>Un �tem fue agregado al inventario (recogido del mundo).</summary>
    public static event Action<SO_InventoryItem> OnItemAdded;

    /// <summary>Un �tem fue removido del inventario (por descarte o consumo).</summary>
    public static event Action<SO_InventoryItem> OnItemRemoved;

    /// <summary>El jugador seleccion� un �tem en la lista. Poblar el panel de detalle.</summary>
    public static event Action<SO_InventoryItem> OnItemSelected;

    /// <summary>Un �tem consumible fue usado al interactuar con un objeto del mundo.</summary>
    public static event Action<SO_InventoryItem> OnItemConsumed;

    // ------------------ Descarte ------------------

    /// <summary>El jugador presion� "descartar". Abrir el di�logo de confirmaci�n.</summary>
    public static event Action<SO_InventoryItem> OnDiscardRequested;

    /// <summary>El jugador confirm� el descarte. Eliminar el �tem definitivamente.</summary>
    public static event Action<SO_InventoryItem> OnDiscardConfirmed;

    /// <summary>El jugador cancel� el descarte (ESC o bot�n cancelar).</summary>
    public static event Action OnDiscardCancelled;

    //------------------ M�dulos ------------------

    /// <summary>El estado de un m�dulo cambi� (Activo, Resuelto, Explotado, Inactivo).</summary>
    public static event Action<ModuleData> OnModuleStateChanged;

    /// <summary>Tick del timer del m�dulo activo. Disparado cada frame mientras corre.</summary>
    public static event Action<ModuleData> OnModuleTimerTick;

    /// <summary>Un m�dulo lleg� a cero y explot�. Aplicar penalizaci�n.</summary>
    public static event Action<ModuleData> OnModuleExploded;

    // ------------------ UI ------------------

    public static event Action<bool> OnInventoryToggled;

    /// <summary>Un módulo con causesBlindness explotó. Duración en segundos.</summary>
    public static event Action<float> OnBlindnessTriggered;

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
    public static void BlindnessTriggered(float duration) => OnBlindnessTriggered?.Invoke(duration);
}
