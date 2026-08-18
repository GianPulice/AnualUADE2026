using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main VIEW of the inventory.
///
/// Responsibilities:
///   - Show/hide the panel
///   - Instantiate a GroupLabelUI + ItemSlotView per item
///   - Use object pooling so it does not instantiate on every refresh
///   - It knows nothing about business or module logic
///
/// NOTE: the panel appears/disappears instantly [animation later]
/// Remember to add an animation further down the line.
/// The pools are also temporary; more generic ones will be added later.
/// </summary>

public class InventoryView : MonoBehaviour
{
    // ── Serialized ───────────────────────────────────────────────────────────

    [Header("List container")]
    [SerializeField] private Transform itemListContainer;   // ScrollRect Content
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform viewport;

    [Header("Prefabs")]
    [SerializeField] private ItemSlotView itemSlotPrefab;
    [SerializeField] private GroupLabelView groupLabelPrefab;

    [Header("Root panel")]
    [SerializeField] private GameObject rootPanel;

    // ── Pools ─────────────────────────────────────────────────────────────────

    private readonly Queue<ItemSlotView> slotPool = new Queue<ItemSlotView>();
    private readonly Queue<GroupLabelView> groupLabelPool = new Queue<GroupLabelView>();

    private readonly List<ItemSlotView> activeSlots = new List<ItemSlotView>();
    private readonly List<GroupLabelView> activeGroupLabels = new List<GroupLabelView>();

    // ── Category order in the list ────────────────────────────────

    private static readonly ItemCategory[] CategoryOrder =
    {
        ItemCategory.Key,
        ItemCategory.Component,
        ItemCategory.Note,
        ItemCategory.Special
    };

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Shows or hides the inventory panel.</summary>
    public void SetVisible(bool visible)
    {
        if (rootPanel != null)
            rootPanel.SetActive(visible);
    }

    /// <summary>
    /// Rebuilds the whole list from the Model.
    /// Groups items by category. Skips empty groups (spec §4.2).
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

            if (group.Count == 0) continue;

            GroupLabelView label = GetOrCreateGroupLabel();
            label.Setup(category);
            label.gameObject.SetActive(true);
            activeGroupLabels.Add(label);

            // Items in the group
            foreach (SO_InventoryItem item in group)
            {
                ItemSlotView slot = GetOrCreateSlot();
                slot.Setup(item, OnSlotClicked);
                slot.gameObject.SetActive(true);
                activeSlots.Add(slot);
            }
        }

        int siblingIndex = 0;
        for (int i = 0; i < activeGroupLabels.Count; i++)
        {
            // Find the slots belonging to this label by category
            ItemCategory category = CategoryOrder.First(c =>
                activeGroupLabels[i].Category == c);

            activeGroupLabels[i].transform.SetSiblingIndex(siblingIndex++);

            foreach (ItemSlotView slot in activeSlots.Where(s => s.Item.Category == category))
            {
                slot.transform.SetSiblingIndex(siblingIndex++);
            }
        }

        CheckScrollNeeded().Forget();
    }
    public void HighlightItem(SO_InventoryItem item)
    {
        foreach (ItemSlotView slot in activeSlots)
        {
            slot.SetSelected(slot.Item == item);
        }
    }

    private async UniTaskVoid CheckScrollNeeded()
    {
        await UniTask.WaitForEndOfFrame(this);

        RectTransform content = itemListContainer as RectTransform;

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        // 3. Measure using rect.height (real size in pixels) instead of sizeDelta
        float contentHeight = content.rect.height;
        float viewportHeight = viewport.rect.height;

        bool overflows = contentHeight > viewportHeight;

        // Informational log for debugging
        Debug.Log($"[Scroll] Real Content Height: {contentHeight} | Viewport: {viewportHeight} | Overflows: {overflows}");

        scrollRect.vertical = true;
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
