using UnityEngine;

[CreateAssetMenu(fileName = "SO_InventoryItem", menuName = "Scriptable Objects/SO_InventoryItem")]
public class SO_InventoryItem : ScriptableObject
{
    [Header("Id")]
    [SerializeField] private string itemID;
    [SerializeField] private string itemName;

    [Header("Category & Visuals")]
    [SerializeField] private ItemCategory category;
    [SerializeField]
    [Tooltip("Brief description for the use of the item In-game")]
    [TextArea(2,5)]
    private string itemDescription;
    [SerializeField] private Sprite itemIcon;

    // ── Behaviour flags ───────────────────────────────────────────────────────

    [Header("Behaviour")]
    [SerializeField]
    [Tooltip("True if it triggers the magnetic door. Used by HasMetallicItem().")]
    private bool isMetallic;

    [SerializeField]
    [Tooltip("True if the item disappears from the inventory when used in the world.")]
    private bool isConsumable;

    [SerializeField]
    [Tooltip("Description of when/how it is consumed. Shown in the detail metadata.")]
    private string consumeDescription;

    [SerializeField]
    [Tooltip("True if only one instance can exist in the inventory.")]
    private bool isUnique = true;

    // ── Detail panel content ──────────────────────────────────────────────────

    [Header("Content (detail panel)")]
    [SerializeField]
    [Tooltip("None = description and metadata only. Text = doc-box. Audio = player.")]
    private ItemContentType contentType;

    [SerializeField]
    [Tooltip("Document text. Only used if ContentType == Text.")]
    [TextArea(4, 10)]
    private string textContent;

    [SerializeField]
    [Tooltip("Audio clip. Only used if ContentType == Audio.")]
    private AudioClip audioClip;

    // ── World interaction ─────────────────────────────────────────────────────

    [SerializeField]
    [Header("World interaction")]
    [Tooltip("ID of the world object this item is used with (door, socket, etc.).")]
    private string targetID;



    // -- Item Properties ───────────────────────────────────────────────────────────────
    public string ItemID { get => itemID; }
    public string ItemName { get => itemName; }
    public string ItemDescription { get => itemDescription; }
    public Sprite ItemIconInventory { get => itemIcon; }
    public ItemCategory Category { get => category; }

    // -- Behaviour ───────────────────────────────────────────────────────────────
    public bool IsMetallic { get => isMetallic; }
    public bool IsConsumable { get => isConsumable; }
    public string ConsumeDescription { get => consumeDescription; }
    public bool IsUnique { get => isUnique; }

    // -- Display interface ───────────────────────────────────────────────────────────────
    public string DisplayName => itemName;
    public string Description => itemDescription;
    public Sprite ItemIcon => itemIcon;

    // -- Detail panel content ───────────────────────────────────────────────────────────────
    public ItemContentType ContentType => contentType;
    public string TextContent => contentType == ItemContentType.Text ? textContent : null;
    public AudioClip AudioClip => contentType == ItemContentType.Audio ? audioClip : null;
    public string TargetID => targetID;

}
public enum ItemContentType
{
    None,   // Description and metadata only
    Text,   // Scrollable doc-box with textContent
    Audio   // Player with audioClip
}
