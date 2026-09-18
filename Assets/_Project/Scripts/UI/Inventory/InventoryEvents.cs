using System;
using UnityEngine;

public class InventoryEvents : MonoBehaviour
{
    /// <summary>An item was added to the inventory (picked up from the world).</summary>
    public static event Action<SO_InventoryItem> OnItemAdded;

    /// <summary>An item was granted to the inventory WITHOUT a world pickup (puzzle reward, script
    /// grant, etc.). Fires in addition to OnItemAdded, so listeners that only want automatic grants
    /// can filter cleanly without inspecting the world state.</summary>
    public static event Action<SO_InventoryItem> OnItemAutoAdded;

    /// <summary>An item was removed from the inventory (consumed or taken by a puzzle).</summary>
    public static event Action<SO_InventoryItem> OnItemRemoved;

    /// <summary>The player selected an item in the list. Populate the detail panel.</summary>
    public static event Action<SO_InventoryItem> OnItemSelected;

    /// <summary>A consumable item was used by interacting with a world object.</summary>
    public static event Action<SO_InventoryItem> OnItemConsumed;

    // ------------------ UI ------------------

    public static event Action<bool> OnInventoryToggled;

    // ------------------ Invokers ------------------

    public static void ItemAdded(SO_InventoryItem item) => OnItemAdded?.Invoke(item);
    public static void ItemAutoAdded(SO_InventoryItem item) => OnItemAutoAdded?.Invoke(item);
    public static void ItemRemoved(SO_InventoryItem item) => OnItemRemoved?.Invoke(item);
    public static void ItemSelected(SO_InventoryItem item) => OnItemSelected?.Invoke(item);
    public static void ItemConsumed(SO_InventoryItem item) => OnItemConsumed?.Invoke(item);

    public static void InventoryToggled(bool isOpen) => OnInventoryToggled?.Invoke(isOpen);
}
// Module lifecycle events moved to ModuleEvents. Blindness overlay listens directly to
// ModuleEvents.OnExploded and filters by PenaltyType.Head.
