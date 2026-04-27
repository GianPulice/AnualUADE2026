using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class InventoryManager : Singleton<InventoryManager>
{
    [Header("Datos iniciales (opcional, para testing)")]
    [SerializeField] private List<SO_InventoryItem> initialItems = new List<SO_InventoryItem>();

    // La lista interna. Privada — el exterior solo lee a través de GetAllItems().
    private List<SO_InventoryItem> items = new List<SO_InventoryItem>();

    // -- Unity --------------------
    void Awake()
    {
        CreateSingleton(true);

        // Solo para testing
        foreach (SO_InventoryItem item in initialItems)
        {
            if (item != null) items.Add(item);
        }
    }

    // -- Solo lectura ----------
    public IReadOnlyList<SO_InventoryItem> GetAllItems() => items.AsReadOnly();

    public IEnumerable<SO_InventoryItem> GetItemsByCategory(ItemCategory category) =>
       items.Where(i => i.Category == category);

    public bool HasItem(SO_InventoryItem item) =>
        item != null && items.Contains(item);
    public bool HasMetallicItem() =>
       items.Any(i => i.IsMetallic);
    public List<string> GetItemIDs() =>
        items.Select(i => i.ItemID).ToList();

    // -- Modificación (solo a través de estos métodos) ----------
    public void AddItem(SO_InventoryItem item)
    {
        if (item == null)
        {
            Debug.LogWarning("[InventoryManager] AddItem: item es null.");
            return;
        }

        items.Add(item);
        Debug.Log($"[Inventario] + {item.ItemName}");
        InventoryEvents.ItemAdded(item);
    }

    public void DiscardItem(SO_InventoryItem item)
    {
        if (!ValidateItemExists(item, "DiscardItem")) return;

        items.Remove(item);
        Debug.Log($"[Inventario] Descartado: {item.ItemName}");
        InventoryEvents.ItemRemoved(item);
    }

    public void ConsumeItem(SO_InventoryItem item)
    {
        if (!ValidateItemExists(item, "ConsumeItem")) return;

        if (!item.IsConsumable)
        {
            Debug.LogWarning($"[InventoryManager] ConsumeItem: {item.ItemName} no es consumible.");
            return;
        }

        items.Remove(item);
        Debug.Log($"[Inventario] Consumido: {item.ItemName}");
        InventoryEvents.ItemConsumed(item);
        InventoryEvents.ItemRemoved(item);
    }

    /// <summary>
    /// Restaura la lista desde un save. Llamado por el sistema de guardado.
    /// items: todos los SO_InventoryItem del proyecto, para buscar por ID.
    /// </summary>
    public void RestoreFromIDs(List<string> savedIDs, List<SO_InventoryItem> allPossibleItems)
    {
        items.Clear();

        foreach (string id in savedIDs)
        {
            SO_InventoryItem found = allPossibleItems.Find(i => i.ItemID == id);
            if (found != null) items.Add(found);
            else Debug.LogWarning($"[InventoryManager] RestoreFromIDs: no se encontró ítem con ID '{id}'.");
        }
    }
    // -- IsValidCheck --------------
    private bool ValidateItemExists(SO_InventoryItem item, string callerName)
    {
        if (item == null)
        {
            Debug.LogWarning($"[InventoryManager] {callerName}: item es null.");
            return false;
        }

        if (!items.Contains(item))
        {
            Debug.LogWarning($"[InventoryManager] {callerName}: '{item.ItemName}' no está en el inventario.");
            return false;
        }

        return true;
    }
}
