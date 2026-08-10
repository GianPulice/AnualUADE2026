using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Main controller of the inventory UI system.
///
/// Responsibilities:
///   - Handle input (Tab opens, ESC/Tab closes, layer stack)
///   - Control Time.timeScale on open/close
///   - Enable/disable the mouse cursor
///   - Orchestrate communication between Model and Views through InventoryEvents
///   - Keep the stack of active layers (inventory -> dialog -> ESC)
///
/// It does NOT manipulate UI directly.
/// It does NOT contain business logic (that is InventoryManager).
/// It does NOT own module state — that lives in ModuleManager. The device HUD subscribes to
/// ModuleEvents on its own.
/// </summary>

public class InventoryManagerUI : Singleton<InventoryManagerUI>, IModalUI
{
    // -- Configuration -------------------

    [Header("Input")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    // -- IModalUI -------------------
    public string ModalId => "Inventory";
    public bool ConsumesEscape => true;   // ESC closes inventory layers if pause is NOT on top
    public bool BlocksPause   => false;   // Pause is a global overlay: it can open on top of the inventory
    public bool PausesGame    => true;
    // RequestClose handles ALL inventory layers (discard dialog -> doc -> selection -> inventory).
    public void RequestClose() => HandleCancelInput();

    [Header("Views")]
    [SerializeField] private InventoryView inventoryView;
    [SerializeField] private ItemDetailView itemDetailView;
    [SerializeField] private DiscardDialogView discardDialogView;
    [SerializeField] private InventoryTabPanelAnimator panelAnimator;

    // -- Internal state -------------------

    private bool isInventoryOpen = false;
    private bool isDiscardOpen = false;

    private SO_InventoryItem selectedItem = null;
    private SO_InventoryItem pendingDiscard = null;
    private SO_InventoryItem currentSelectedItem;

    // -- Unity -------------------

    void Awake()
    {
        CreateSingleton(false);
    }

    void Start()
    {
        SubscribeToEvents();
        InitializeViews();
    }

    void Update()
    {
        HandleInput();
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    // ------------------ Initialization ------------------

    private void InitializeViews()
    {
        inventoryView?.SetVisible(false);
        itemDetailView?.ShowEmpty();
        discardDialogView?.Hide();
    }

    private void SubscribeToEvents()
    {
        InventoryEvents.OnItemAdded += HandleItemAdded;
        InventoryEvents.OnItemRemoved += HandleItemRemoved;
        InventoryEvents.OnItemSelected += HandleItemSelected;
        InventoryEvents.OnDiscardRequested += HandleDiscardRequested;
        InventoryEvents.OnDiscardConfirmed += HandleDiscardConfirmed;
        InventoryEvents.OnDiscardCancelled += HandleDiscardCancelled;
    }

    private void UnsubscribeFromEvents()
    {
        InventoryEvents.OnItemAdded -= HandleItemAdded;
        InventoryEvents.OnItemRemoved -= HandleItemRemoved;
        InventoryEvents.OnItemSelected -= HandleItemSelected;
        InventoryEvents.OnDiscardRequested -= HandleDiscardRequested;
        InventoryEvents.OnDiscardConfirmed -= HandleDiscardConfirmed;
        InventoryEvents.OnDiscardCancelled -= HandleDiscardCancelled;
    }

    // ------------------ Input ------------------ To be removed once the Input System is integrated

    private void HandleInput()
    {
        // Closing with ESC is governed by UIStateManager (UI/Exit action -> RequestClose).
        // Here we only handle Tab to open/close the inventory.

        if (!Input.GetKeyDown(toggleKey) || isDiscardOpen) return;

        if (isInventoryOpen)
        {
            // We only close with Tab if the inventory is the top of the stack.
            if (UIStateManager.Exists && !ReferenceEquals(UIStateManager.Instance.Peek(), this)) return;

            if (itemDetailView != null && itemDetailView.IsDocOpen)
            {
                itemDetailView.HideDoc();
                return;
            }
            CloseInventory();
        }
        else
        {
            // Only opens if there is no other modal on top (pause, panel, doc...).
            if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen) return;
            OpenInventory();
        }
    }

    // -- Open / Close --------------------

    /// <summary>
    /// Opens the inventory:
    ///   - Time.timeScale = 0 (pauses gameplay)
    ///   - Cursor enabled and visible
    ///   - List refreshed
    ///   - First item auto-selected if there are any
    /// </summary>
    public void OpenInventory()
    {
        if (isInventoryOpen) return;

        isInventoryOpen = true;

        // The UIStateManager takes care of Time.timeScale and the cursor.
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);

        // Show and populate the view
        if (panelAnimator != null) panelAnimator.Open();
        else inventoryView?.SetVisible(true);
        RefreshItemList();

        AutoSelectFirstItem();

        InventoryEvents.InventoryToggled(true);
    }

    /// <summary>
    /// Closes the inventory:
    ///   - Time.timeScale = 1 (resumes gameplay)
    ///   - Cursor disabled (back to the gameplay state)
    ///   - Stop the recording audio if it was playing
    /// </summary>
    public void CloseInventory()
    {
        if (!isInventoryOpen) return;

        isInventoryOpen = false;
        selectedItem = null;

        // The UIStateManager restores Time.timeScale and the cursor when the stack becomes empty.
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);

        if (panelAnimator != null) panelAnimator.Close();
        else inventoryView?.SetVisible(false);
        itemDetailView?.ShowEmpty();

        // Stop the recording audio
        //  itemDetailView?.StopAudio();

        InventoryEvents.InventoryToggled(false);
    }
    public void OpenDocument() => itemDetailView?.ShowDoc();
    public void CloseDocument() => itemDetailView?.HideDoc();
    // ── Item selection ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ItemSlotView when the player clicks an item.
    /// Notifies the Views through the event.
    /// </summary>
    public void SelectItem(SO_InventoryItem item)
    {
        if (item == null) return;
        selectedItem = item;
        currentSelectedItem = item;
        InventoryEvents.ItemSelected(item);
    }

    private void AutoSelectFirstItem()
    {
        IReadOnlyList<SO_InventoryItem> allItems = InventoryManager.Instance.GetAllItems();

        if (allItems.Count > 0)
        {
            SelectItem(allItems[0]);
        }
        else
        {
            // Empty list: show the empty state in the detail panel
            itemDetailView?.ShowEmpty();
        }
    }

    // ── Discard ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ItemDetailView when the discard button is pressed.
    /// It does NOT remove the item — it only asks for confirmation.
    /// </summary>
    public void RequestDiscard(SO_InventoryItem item)
    {
        if (item == null) return;

        pendingDiscard = item;
        isDiscardOpen = true;

        InventoryEvents.DiscardRequested(item);
    }

    /// <summary>Called by DiscardDialogView on confirm.</summary>
    public void ConfirmDiscard()
    {
        if (pendingDiscard == null) return;

        SO_InventoryItem toDiscard = pendingDiscard;
        pendingDiscard = null;
        isDiscardOpen = false;

        InventoryManager.Instance.DiscardItem(toDiscard);
        InventoryEvents.DiscardConfirmed(toDiscard);
    }

    /// <summary>Called by DiscardDialogView on cancel, or by ESC.</summary>
    public void CancelDiscard()
    {
        pendingDiscard = null;
        isDiscardOpen = false;
        InventoryEvents.DiscardCancelled();
    }

    // -- Event handlers --------------------

    private void HandleItemAdded(SO_InventoryItem item)
    {
        RefreshItemList();
    }

    private void HandleItemRemoved(SO_InventoryItem item)
    {
        RefreshItemList();

        // If the removed item was the selected one, clear the detail panel
        if (selectedItem == item)
        {
            selectedItem = null;
            itemDetailView?.ShowEmpty();
            AutoSelectFirstItem();
        }
    }

    private void HandleItemSelected(SO_InventoryItem item)
    {
        itemDetailView?.ShowDetail(item);
    }

    private void HandleDiscardRequested(SO_InventoryItem item)
    {
        discardDialogView?.Show(item);
    }

    private void HandleDiscardConfirmed(SO_InventoryItem item)
    {
        discardDialogView?.Hide();
    }

    private void HandleDiscardCancelled()
    {
        discardDialogView?.Hide();
    }

    private void HandleCancelInput()
    {
        if (isDiscardOpen)
        {
            CancelDiscard();
            return;
        }

        if (itemDetailView != null && itemDetailView.IsDocOpen)
        {
            itemDetailView.HideDoc();
            return;
        }

        if (currentSelectedItem != null)
        {
            ClearSelection();
            return;
        }

        CloseInventory();
    }

    private void ClearSelection()
    {
        currentSelectedItem = null;

        InventoryEvents.ItemSelected(null);

        inventoryView.HighlightItem(null);
    }

    // -- Helpers --------------------

    private void RefreshItemList()
    {
        if (!isInventoryOpen) return;
        inventoryView?.RefreshList(InventoryManager.Instance);
    }

    // -- State accessors --------------------

    public bool IsInventoryOpen => isInventoryOpen;
    public SO_InventoryItem SelectedItem => selectedItem;
}
