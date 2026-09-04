using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base view of the **Lateral Inventory** — the reduced inventory used during
/// puzzle interaction Variant B (interaction spec §6.2):
///
///   "On pressing E on the puzzle, the camera does a cinematic pan and an inventory panel
///    opens on the left side. The player selects the correct item and places it. The panel
///    shows only the inventory items, without the detail panel."
///
/// STATUS: Initial skeleton. Basic functionality (show items, raise a selection event).
/// PENDING for when Variant B is implemented:
///   - Hook into PlayerController.SetState(Interacting) to block gameplay input.
///   - Camera pan towards <c>puzzleCameraPoint</c> (0.6s Lerp).
///   - Mouse/gamepad navigation over the list.
///   - Shake/sound feedback when the item is wrong (interaction spec §10).
///   - Cancellation with ESC.
///   - Interruption by NemesisController.OnDangerDetected (interaction spec §10).
///
/// This class is meant to be driven by the puzzle's Interactable (Variant B), not by an
/// MVC controller. The puzzle decides when to open it and what to do with the selection.
/// </summary>
public class LateralInventoryView : BaseScreenView
{
    [Header("Refs")]
    [SerializeField] private Transform itemListContainer;
    [SerializeField] private LateralInventorySlotView slotPrefab;

    /// <summary>Raised when the player clicks an item in the list.</summary>
    public event Action<SO_InventoryItem> OnItemSelected;

    /// <summary>Raised when the player cancels (ESC, click outside, etc.).</summary>
    public event Action OnCancelled;

    private readonly List<LateralInventorySlotView> _spawnedSlots = new List<LateralInventorySlotView>();

    /// <summary>
    /// Populates the list with the given items. Variant B typically passes
    /// <c>InventoryManager.Instance.GetAllItems()</c>, optionally filtered by
    /// category (e.g. only "Component" for the Central Hub).
    /// </summary>
    public void Populate(IReadOnlyList<SO_InventoryItem> items)
    {
        ClearSlots();

        if (items == null || slotPrefab == null || itemListContainer == null) return;

        foreach (SO_InventoryItem item in items)
        {
            if (item == null) continue;
            LateralInventorySlotView slot = Instantiate(slotPrefab, itemListContainer);
            slot.Bind(item);
            slot.OnClicked += HandleSlotClicked;
            _spawnedSlots.Add(slot);
        }
    }

    /// <summary>
    /// Error feedback when the selected item is not valid for this puzzle.
    /// Currently a stub — the visual implementation is left for when Variant B is done.
    /// </summary>
    public void ShowInvalidSelectionFeedback(SO_InventoryItem item)
    {
        // TODO: slot shake + error sound (interaction spec §9.2).
    }

    /// <summary>Called externally to cancel (e.g. from the puzzle controller's ESC).</summary>
    public void NotifyCancelled() => OnCancelled?.Invoke();

    private void OnDestroy()
    {
        ClearSlots();
    }

    private void ClearSlots()
    {
        foreach (LateralInventorySlotView slot in _spawnedSlots)
        {
            if (slot == null) continue;
            slot.OnClicked -= HandleSlotClicked;
            Destroy(slot.gameObject);
        }
        _spawnedSlots.Clear();
    }

    private void HandleSlotClicked(SO_InventoryItem item) => OnItemSelected?.Invoke(item);
}
