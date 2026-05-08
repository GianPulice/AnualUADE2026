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

    // ── Flags de comportamiento ───────────────────────────────────────────────

    [Header("Comportamiento")]
    [SerializeField]
    [Tooltip("True si activa la puerta magnética. Usado por HasMetallicItem().")]
    private bool isMetallic;

    [SerializeField]
    [Tooltip("True si el ítem desaparece del inventario al usarse en el mundo.")]
    private bool isConsumable;

    [SerializeField]
    [Tooltip("Descripción de cuándo/cómo se consume. Mostrado en metadata del detalle.")]
    private string consumeDescription;

    [SerializeField]
    [Tooltip("True si solo puede existir una instancia en el inventario.")]
    private bool isUnique = true;

    // ── Contenido del panel de detalle ────────────────────────────────────────

    [Header("Contenido (panel de detalle)")]
    [SerializeField]
    [Tooltip("None = solo descripción y metadata. Text = doc-box. Audio = reproductor.")]
    private ItemContentType contentType;

    [SerializeField]
    [Tooltip("Texto del documento. Solo se usa si ContentType == Text.")]
    [TextArea(4, 10)]
    private string textContent;

    [SerializeField]
    [Tooltip("Clip de audio. Solo se usa si ContentType == Audio.")]
    private AudioClip audioClip;

    // ── Interacción con el mundo ──────────────────────────────────────────────

    [SerializeField]
    [Header("Interacción con el mundo")]
    [Tooltip("ID del objeto del mundo con el que se usa este ítem (puerta, ranura, etc.).")]
    private string targetID;



    // -- Item Properties ───────────────────────────────────────────────────────────────
    public string ItemID { get => itemID; }
    public string ItemName { get => itemName; }
    public string ItemDescription { get => itemDescription; }
    public Sprite ItemIconInventory { get => itemIcon; }
    public ItemCategory Category { get => category; }

    // -- Comportamiento ───────────────────────────────────────────────────────────────    
    public bool IsMetallic { get => isMetallic; }
    public bool IsConsumable { get => isConsumable; }
    public string ConsumeDescription { get => consumeDescription; }
    public bool IsUnique { get => isUnique; }

    // -- Interfaz de visualización ───────────────────────────────────────────────────────────────
    public string DisplayName => itemName;
    public string Description => itemDescription;
    public Sprite ItemIcon => itemIcon;

    // -- Contenido del panel de detalle ───────────────────────────────────────────────────────────────
    public ItemContentType ContentType => contentType;
    public string TextContent => contentType == ItemContentType.Text ? textContent : null;
    public AudioClip AudioClip => contentType == ItemContentType.Audio ? audioClip : null;
    public string TargetID => targetID;

}
public enum ItemContentType
{
    None,   // Solo descripción y metadata
    Text,   // Doc-box scrolleable con textContent
    Audio   // Reproductor con audioClip
}
