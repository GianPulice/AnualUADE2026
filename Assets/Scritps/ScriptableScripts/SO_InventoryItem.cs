using UnityEngine;

[CreateAssetMenu(fileName = "SO_InventoryItem", menuName = "Scriptable Objects/SO_InventoryItem")]
public class SO_InventoryItem : ScriptableObject
{
    [Header("Basic Info")]
    [SerializeField] private string itemName;
    [SerializeField] private ItemType itemType;
    [SerializeField][TextArea] private string itemDescription;

    [Header("Visual")]
    [SerializeField] private Sprite itemIconInventory;

    [Header("Extra Parameters")]
    [SerializeField] private ItemParameter[] parameters;

    public string ItemName { get => itemName; }
    public ItemType ItemType { get => itemType; }
    public string ItemDescription { get => itemDescription; }
    public Sprite ItemIconInventory { get => itemIconInventory; }
    public string DisplayName => itemName;
    public string Description => itemDescription;
    public Sprite ItemIcon => itemIconInventory;
    public ItemParameter[] Parameters => parameters;
}
public enum ItemType
{
    Keys,
    Components,
    Notes,
    Essentials
}
[System.Serializable]
public struct ItemParameter
{
    public string ParameterName;
    public string ParameterValue;
}
