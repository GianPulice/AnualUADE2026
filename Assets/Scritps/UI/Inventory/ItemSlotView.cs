using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// VIEW de una fila de ítem en la lista del inventario.
/// 
/// Responsabilidades:
///   - Mostrar nombre, tipo y color del ítem
///   - Notificar al Controller cuando es seleccionado
/// </summary>
public class ItemSlotView : MonoBehaviour
{
    // ── Serialized ───────────────────────────────────────────────────────────

    [Header("Texto")]
    [SerializeField] private TextMeshProUGUI itemNameText;

    [Header("Visuales de tipo")]
    [SerializeField] private Image itemTypeColorBox;   // El cuadrado de color por tipo
    [SerializeField] private Image itemTypeIcon;       // Ícono opcional por tipo (puede ser null)

    [Header("Selección")]
    [SerializeField] private Button selectButton;
    [SerializeField] private Image slotBackground;
    [SerializeField] private Color normalColor    = new Color(0.1f, 0.1f, 0.1f, 1f);
    [SerializeField] private Color selectedColor  = new Color(0.2f, 0.4f, 0.2f, 1f);

    [Header("Item Colors")]
    [SerializeField] private Color keysColor;
    [SerializeField] private Color componentColor;
    [SerializeField] private Color notesColor;
    [SerializeField] private Color esentialColor;

    // ── Privados ─────────────────────────────────────────────────────────────

    private SO_InventoryItem currentItem;
    private Action<SO_InventoryItem> onSelected;

    // ── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        selectButton?.onClick.AddListener(OnButtonClicked);
    }

    void OnDestroy()
    {
        selectButton?.onClick.RemoveListener(OnButtonClicked);
    }

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>
    /// Configura el slot con un ítem y el callback de selección.
    /// Llamado por InventoryView al refrescar la lista.
    /// </summary>
    public void Setup(SO_InventoryItem item, Action<SO_InventoryItem> onSelectedCallback)
    {
        currentItem = item;
        onSelected = onSelectedCallback;

        // Nombre
        if (itemNameText != null)
            itemNameText.text = item.ItemName;

        // Color por tipo
        if (itemTypeColorBox != null)
            itemTypeColorBox.color = GetColorForItemType(item.ItemType);

        // Ícono por tipo (si existe en el SO)
        if (itemTypeIcon != null && item.ItemIconInventory != null)
            itemTypeIcon.sprite = item.ItemIconInventory;

        // Estado visual normal (no seleccionado)
        SetSelectedVisual(false);
    }

    public void SetSelectedVisual(bool isSelected)
    {
        if (slotBackground != null)
            slotBackground.color = isSelected ? selectedColor : normalColor;
    }

    // ── Privados ─────────────────────────────────────────────────────────────

    private void OnButtonClicked()
    {
        onSelected?.Invoke(currentItem);
    }

    private Color GetColorForItemType(ItemType type)
    {
        return type switch
        {
            ItemType.Keys       => keysColor, //Rojo
            ItemType.Components => componentColor, // Verde
            ItemType.Notes      => notesColor,  // Azul
            ItemType.Essentials => esentialColor,  //Amarillo     
            _                   => Color.white
        };
    }
}
