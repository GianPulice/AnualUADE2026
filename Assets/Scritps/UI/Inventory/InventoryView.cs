using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// VIEW principal del inventario.
/// 
/// Responsabilidades:
///   - Mostrar/ocultar el panel
///   - Instanciar GroupLabelUI + ItemSlotView por cada ítem
///   - Usar object pooling para no instanciar en cada refresh
///   - NO conoce lógica de negocio ni de módulos
///
/// NOTA: El panel aparece/desaparece instantáneamente [Luego animacion]
/// Recordar añadir una animacion mas adelante
/// Las pools tambien son temporales, despues se van a añadir unas mas genericas
/// </summary>

public class InventoryView : MonoBehaviour
{
    // ── Serialized ───────────────────────────────────────────────────────────

    [Header("Contenedor de la lista")]
    [SerializeField] private Transform itemListContainer;   // ScrollRect Content 

    [Header("Prefabs")]
    [SerializeField] private ItemSlotView itemSlotPrefab;
    [SerializeField] private GroupLabelView groupLabelPrefab;

    [Header("Panel raíz")]
    [SerializeField] private GameObject rootPanel;

    // ── Pools ─────────────────────────────────────────────────────────────────

    private readonly Queue<ItemSlotView> slotPool = new Queue<ItemSlotView>();
    private readonly Queue<GroupLabelView> groupLabelPool = new Queue<GroupLabelView>();

    private readonly List<ItemSlotView> activeSlots = new List<ItemSlotView>();
    private readonly List<GroupLabelView> activeGroupLabels = new List<GroupLabelView>();

    // ── Orden de categorías en la lista ───────────────────────────

    private static readonly ItemCategory[] CategoryOrder =
    {
        ItemCategory.Key,
        ItemCategory.Component,
        ItemCategory.Note,
        ItemCategory.Special
    };

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>Muestra u oculta el panel de inventario.</summary>
    public void SetVisible(bool visible)
    {
        if (rootPanel != null)
            rootPanel.SetActive(visible);
    }

    /// <summary>
    /// Reconstruye la lista completa a partir del Model.
    /// Agrupa ítems por categoría. Omite grupos vacíos (spec §4.2).
    /// </summary>
    public void RefreshList(InventoryManager model)
    {
        ReturnAllToPool();

        IReadOnlyList<SO_InventoryItem> allItems = model.GetAllItems();

        foreach (ItemCategory category in CategoryOrder)
        {
            List<SO_InventoryItem> group = allItems
                .Where(i => i.Category == category)
                .ToList();

            // Omitir grupos vacíos (spec §4.2)
            if (group.Count == 0) continue;

            // Etiqueta de grupo
            GroupLabelView label = GetOrCreateGroupLabel();
            label.Setup(category);
            label.gameObject.SetActive(true);
            activeGroupLabels.Add(label);

            // Ítems del grupo
            foreach (SO_InventoryItem item in group)
            {
                ItemSlotView slot = GetOrCreateSlot();
                slot.Setup(item, OnSlotClicked);
                slot.gameObject.SetActive(true);
                activeSlots.Add(slot);
            }
        }
    }
    public void HighlightItem(SO_InventoryItem item)
    {
        foreach (ItemSlotView slot in activeSlots)
        {
            slot.SetSelected(slot.Item == item);
        }
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────

    private void OnSlotClicked(SO_InventoryItem item)
    {
        HighlightItem(item);
        InventoryManagerUI.Instance.SelectItem(item);
    }

    // ── Pool (Temporary) ──────────────────────────────────────────────────

    private void ReturnAllToPool()
    {
        foreach (ItemSlotView slot in activeSlots)
        {
            slot.gameObject.SetActive(false);
            slotPool.Enqueue(slot);
        }
        activeSlots.Clear();

        foreach (GroupLabelView label in activeGroupLabels)
        {
            label.gameObject.SetActive(false);
            groupLabelPool.Enqueue(label);
        }
        activeGroupLabels.Clear();
    }

    private ItemSlotView GetOrCreateSlot()
    {
        if (slotPool.Count > 0) return slotPool.Dequeue();
        return Instantiate(itemSlotPrefab, itemListContainer);
    }

    private GroupLabelView GetOrCreateGroupLabel()
    {
        if (groupLabelPool.Count > 0) return groupLabelPool.Dequeue();
        return Instantiate(groupLabelPrefab, itemListContainer);
    }
}
