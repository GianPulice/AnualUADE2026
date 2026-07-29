using System;
using UnityEngine;

public class InventoryEvents : MonoBehaviour
{
    /// <summary>An item was added to the inventory (picked up from the world).</summary>
    public static event Action<SO_InventoryItem> OnItemAdded;

    /// <summary>An item was removed from the inventory (by discarding or consuming).</summary>
    public static event Action<SO_InventoryItem> OnItemRemoved;

    /// <summary>The player selected an item in the list. Populate the detail panel.</summary>
    public static event Action<SO_InventoryItem> OnItemSelected;

    /// <summary>A consumable item was used by interacting with a world object.</summary>
    public static event Action<SO_InventoryItem> OnItemConsumed;

    // ------------------ Discard ------------------

    /// <summary>The player pressed "discard". Open the confirmation dialog.</summary>
    public static event Action<SO_InventoryItem> OnDiscardRequested;

    /// <summary>The player confirmed the discard. Remove the item for good.</summary>
    public static event Action<SO_InventoryItem> OnDiscardConfirmed;

    /// <summary>The player cancelled the discard (ESC or the cancel button).</summary>
    public static event Action OnDiscardCancelled;

    //------------------ Modules ------------------

    /// <summary>A module's status changed (Inactive, Active, Resolved, Exploded).</summary>
    public static event Action<ModuleData> OnModuleStateChanged;

    /// <summary>Tick of the active module's timer. Raised every frame while it runs.</summary>
    public static event Action<ModuleData> OnModuleTimerTick;

    /// <summary>A module reached zero and exploded. Apply the penalty.</summary>
    public static event Action<ModuleData> OnModuleExploded;

    // ------------------ UI ------------------

    public static event Action<bool> OnInventoryToggled;

    /// <summary>A module with causesBlindness exploded. Duration in seconds.</summary>
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
