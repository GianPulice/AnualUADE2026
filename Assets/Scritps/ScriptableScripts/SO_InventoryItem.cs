using UnityEngine;

[CreateAssetMenu(fileName = "SO_InventoryItem", menuName = "Scriptable Objects/SO_InventoryItem")]
public class SO_InventoryItem : ScriptableObject
{
    [SerializeField] private string itemId;
    [SerializeField] private string itemName;
    [SerializeField] private Sprite spriteUI;

    public string ItemId { get => itemId; }
    public string ItemName { get => itemName; }
    public Sprite SpriteUI { get => spriteUI; }
}
