using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class InventoryManager : Singleton<InventoryManager>
{
    [Header("Initial data (optional, for testing)")]
    [SerializeField] private List<SO_InventoryItem> initialItems = new List<SO_InventoryItem>();

    // The internal list. Private — the outside world only reads through GetAllItems().
    private List<SO_InventoryItem> items = new List<SO_InventoryItem>();

    // -- Unity --------------------
    void Awake()
    {
        CreateSingleton(true);

        // Testing only
        foreach (SO_InventoryItem item in initialItems)
        {
            if (item != null) items.Add(item);
        }
    }

    // -- Read only ----------
    public IReadOnlyList<SO_InventoryItem> GetAllItems() => items.AsReadOnly();

    public IEnumerable<SO_InventoryItem> GetItemsByCategory(ItemCategory category) =>
       items.Where(i => i.Category == category);

    public bool HasItem(SO_InventoryItem item) =>
        item != null && items.Contains(item);
    public bool HasMetallicItem() =>
       items.Any(i => i.IsMetallic);
    public List<string> GetItemIDs() =>
        items.Select(i => i.ItemID).ToList();

    // -- Mutation (only through these methods) ----------
    public void AddItem(SO_InventoryItem item)
    {
        if (item == null)
        {
            Debug.LogWarning("[InventoryManager] AddItem: item is null.");
            return;
        }

        items.Add(item);
        Debug.Log($"[Inventory] + {item.ItemName}");
        InventoryEvents.ItemAdded(item);
    }

    public void DiscardItem(SO_InventoryItem item)
    {
        if (!ValidateItemExists(item, "DiscardItem")) return;

        items.Remove(item);
        Debug.Log($"[Inventory] Discarded: {item.ItemName}");
        InventoryEvents.ItemRemoved(item);
    }

    public void ConsumeItem(SO_InventoryItem item)
    {
        if (!ValidateItemExists(item, "ConsumeItem")) return;

        if (!item.IsConsumable)
        {
            Debug.LogWarning($"[InventoryManager] ConsumeItem: {item.ItemName} is not consumable.");
            return;
        }

        items.Remove(item);
        Debug.Log($"[Inventory] Consumed: {item.ItemName}");
        InventoryEvents.ItemConsumed(item);
        InventoryEvents.ItemRemoved(item);
    }

    /// <summary>
    /// Restores the list from a save. Called by the save system.
    /// items: every SO_InventoryItem in the project, used to look them up by ID.
    /// </summary>
    public void RestoreFromIDs(List<string> savedIDs, List<SO_InventoryItem> allPossibleItems)
    {
        items.Clear();

        foreach (string id in savedIDs)
        {
            SO_InventoryItem found = allPossibleItems.Find(i => i.ItemID == id);
            if (found != null) items.Add(found);
            else Debug.LogWarning($"[InventoryManager] RestoreFromIDs: no item found with ID '{id}'.");
        }
    }
    // -- IsValidCheck --------------
    private bool ValidateItemExists(SO_InventoryItem item, string callerName)
    {
        if (item == null)
        {
            Debug.LogWarning($"[InventoryManager] {callerName}: item is null.");
            return false;
        }

        if (!items.Contains(item))
        {
            Debug.LogWarning($"[InventoryManager] {callerName}: '{item.ItemName}' is not in the inventory.");
            return false;
        }

        return true;
    }
}
