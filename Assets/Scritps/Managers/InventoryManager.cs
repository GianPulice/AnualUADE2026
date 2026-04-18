using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : Singleton<InventoryManager>
{
    [SerializeField] private List<SO_InventoryItem> items = new List<SO_InventoryItem>();

    public IReadOnlyList<SO_InventoryItem> Items => items;


    void Awake()
    {
        CreateSingleton(true);
    }


    public void AddItem(SO_InventoryItem item)
    {
        if (item == null) return;

        items.Add(item);
        Debug.Log($"Item agregado: {item.ItemName}");
    }

    public void RemoveItem(SO_InventoryItem item)
    {
        if (item == null) return;

        if (items.Contains(item))
        {
            items.Remove(item);
            Debug.Log($"Item removido: {item.ItemName}");
        }
    }

    public bool HasItem(SO_InventoryItem item)
    {
        if (item == null) return false;
        return items.Contains(item);
    }

    public void UseItem(SO_InventoryItem item)
    {
        if (item == null) return;

        if (HasItem(item))
        {
            Debug.Log($"Usando item: {item.ItemName}");
        }
    }
}
