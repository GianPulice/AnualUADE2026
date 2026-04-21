using UnityEngine;

[CreateAssetMenu(fileName = "SO_InventoryManager", menuName = "Scriptable Objects/SO_InventoryManager")]
public class SO_InventoryManager : ScriptableObject
{
    [SerializeField] private int maxInventoryItems;

    public int MaxInventoryItems { get => maxInventoryItems; }
}
