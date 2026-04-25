using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : Singleton<InventoryManager>
{
    [SerializeField] private List<SO_InventoryItem> SO_inventoryItems = new List<SO_InventoryItem>();
    [SerializeField] private SO_InventoryManager SO_inventoryManager;

    //public IReadOnlyList<SO_InventoryItem> SO_InventoryItems => SO_inventoryItems;
    //public SO_InventoryManager SO_InventoryManager { get => SO_inventoryManager; }

    private int currentInventoryItems = 0;


    void Awake()
    {
        CreateSingleton(true);
    }
    public IReadOnlyList<SO_InventoryItem> GetItems() => SO_inventoryItems.AsReadOnly();
    public void AddItem(SO_InventoryItem item)
    {
        if (item == null) return;

        SO_inventoryItems.Add(item);
        currentInventoryItems++;
        Debug.Log($"Item agregado: {item.ItemName}");
        InventoryEvents.ItemAdded(item);
    }

    public void RemoveItem(SO_InventoryItem item)
    {
        if (item == null) return;

        if (SO_inventoryItems.Contains(item))
        {
            SO_inventoryItems.Remove(item);
            currentInventoryItems--;
            Debug.Log($"Item removido: {item.ItemName}");
            InventoryEvents.ItemRemoved(item);
        }
    }

    public bool HasItem(SO_InventoryItem item)
    {
        if (item == null) return false;
        return SO_inventoryItems.Contains(item);
    }

    public void UseItem(SO_InventoryItem item)
    {
        if (item == null) return;

        if (HasItem(item))
        {
            /// Analizar esta linea de codigo, porque determinados elementos del inventario no se pueden usar
            currentInventoryItems--;
            Debug.Log($"Usando item: {item.ItemName}");
            InventoryEvents.ItemUsed(item);
        }
    }

    public bool ReturnTrueIfInventoryIsFull()
    {
        if (currentInventoryItems >= SO_inventoryManager.MaxInventoryItems)
        {
            return true;
        }

        return false;
    }
}
