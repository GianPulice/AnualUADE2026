using UnityEngine;

[CreateAssetMenu(fileName = "SO_InventoryItem", menuName = "Scriptable Objects/SO_InventoryItem")]
public class SO_InventoryItem : ScriptableObject
{
    [SerializeField] private string itemName;
    [SerializeField] private string itemDescription;
    [SerializeField] private Sprite spriteUI;

    public string ItemName { get => itemName; }
    public string ItemDescription { get => itemDescription; }
    public Sprite SpriteUI { get => spriteUI; }
}
