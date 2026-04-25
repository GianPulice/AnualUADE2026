using UnityEngine;

[CreateAssetMenu(fileName = "SO_InventoryItem", menuName = "Scriptable Objects/SO_InventoryItem")]
public class SO_InventoryItem : ScriptableObject
{
    [SerializeField] private string itemName;
    [SerializeField] private ItemType itemType;
    [SerializeField] private string itemDescription;
    [SerializeField] private Sprite itemIcon;

    public string ItemName { get => itemName; }
    public ItemType ItemType { get => itemType; }
    public string ItemDescription { get => itemDescription; }
    public Sprite ItemIcon { get => itemIcon; }
}
public enum ItemType
{
    Keys,
    Components,
    Notes,
    Essentials
}
