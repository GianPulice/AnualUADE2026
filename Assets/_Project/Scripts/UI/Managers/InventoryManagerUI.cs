using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Main controller of the inventory UI system.
///
/// Responsibilities:
///   - Handle input (Tab opens, ESC/Tab closes, layer stack)
///   - Enable/disable the mouse cursor
///   - Orchestrate communication between Model and Views through InventoryEvents
///   - Keep the stack of active layers (inventory -> dialog -> ESC)
///
/// Modules are NOT handled here. This class used to own a parallel copy of the module system
/// (its own list, timers, explosion and session time) that ModuleManager has since replaced;
/// that copy was left behind by a bad merge and stopped compiling once ModuleData was split into
/// data (ModuleData) and runtime state (ModuleRuntime). ModuleManager is the single owner now,
/// and ModuleHUDView subscribes to ModuleEvents on its own — it needs nothing from here.
///
/// It does NOT manipulate UI directly.
/// It does NOT contain business logic (that is InventoryManager).
/// </summary>

public class InventoryManagerUI : Singleton<InventoryManagerUI>, IModalUI
{
    // -- IModalUI -------------------
    public string ModalId => "Inventory";
    public bool ConsumesEscape => true;   // ESC closes inventory layers if pause is NOT on top
    public bool BlocksPause   => false;   // Pause is a global overlay: it can open on top of the inventory

    // The world keeps running while you read your inventory. Checking your bag is not a time-out:
    // freezing everything turned it into a free pause the player could take mid-chase, and a
    // survival horror that stops the monster whenever you open a menu has no tension in the menu.
    // Player INPUT is still blocked while it is open — that comes from the modal stack
    // (PauseManager.IsGameplayInputBlocked), not from timeScale, so nothing here has to change for
    // it. Module timers and the Nemesis carry on, which is the point.
    public bool PausesGame    => false;
    // RequestClose handles ALL inventory layers (doc -> selection -> inventory).
    public void RequestClose() => HandleCancelInput();

    [Header("Views")]
    [SerializeField] private InventoryView inventoryView;
    [SerializeField] private ItemDetailView itemDetailView;
    [SerializeField] private InventoryTabPanelAnimator panelAnimator;

    // -- Internal state -------------------

    private bool isInventoryOpen = false;

    private SO_InventoryItem selectedItem = null;
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
    }

    private void SubscribeToEvents()
    {
        InventoryEvents.OnItemAdded += HandleItemAdded;
        InventoryEvents.OnItemRemoved += HandleItemRemoved;
        InventoryEvents.OnItemSelected += HandleItemSelected;
    }

    private void UnsubscribeFromEvents()
    {
        InventoryEvents.OnItemAdded -= HandleItemAdded;
        InventoryEvents.OnItemRemoved -= HandleItemRemoved;
        InventoryEvents.OnItemSelected -= HandleItemSelected;
    }

    // ------------------ Input ------------------

    private void HandleInput()
    {
        // Closing with ESC is governed by UIStateManager (UI/Exit action -> RequestClose).
        // Here we only handle Player/Inventory (Tab / Select) to open/close it.

        if (!GameInput.InventoryPressed) return;
        if (ScreenManager.IsInputLocked) return;

        if (isInventoryOpen)
        {
            // We only close with the toggle if the inventory is the top of the stack.
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
            // Not while captured. The original reason was that opening a menu froze the Nemesis
            // mid-capture — it could not finish its grace period and get back to Patrolling — for
            // as long as the inventory stayed open; that one is gone now that the inventory does
            // not touch timeScale. The guard stays for the plainer reason: being grabbed is not a
            // moment the player gets to go rummaging through their bag.
            // Nor while lying on the floor or getting up.
            if (PlayerRegistry.Current != null && PlayerRegistry.Current.IsImmobilized) return;

            // Only opens if there is no other modal on top (pause, panel, doc...).
            if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen) return;
            OpenInventory();
        }
    }

    // -- Open / Close --------------------

    /// <summary>
    /// Opens the inventory:
    ///   - Cursor enabled and visible
    ///   - List refreshed
    ///   - Nothing selected: the player picks an item by hand, and only then the detail pop-up unfolds
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

        // Clean slate: no leftover highlight and the pop-up folded.
        ClearSelection();

        if (AudioManager.Exists) AudioManager.Instance.PlaySFX("sfx_abrir_inventario");

        InventoryEvents.InventoryToggled(true);
    }

    /// <summary>
    /// Closes the inventory:
    ///   - Cursor disabled (back to the gameplay state)
    ///   - Stop the recording audio if it was playing
    /// </summary>
    public void CloseInventory()
    {
        if (!isInventoryOpen) return;

        // Tab and ESC peel one layer per press, so by the time they get here nothing is stacked on
        // top. The title-bar X closes from any depth. The doc pop-up lives inside LAYOUT, so it
        // vanishes with it — but its own state would still say open, and it would reappear on the
        // next open.
        if (itemDetailView != null && itemDetailView.IsDocOpen) itemDetailView.HideDoc();

        isInventoryOpen = false;
        selectedItem = null;

        // The UIStateManager restores Time.timeScale and the cursor when the stack becomes empty.
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);

        if (panelAnimator != null) panelAnimator.Close();
        else inventoryView?.SetVisible(false);
        itemDetailView?.ShowEmpty();

        // Stop the recording audio
        //  itemDetailView?.StopAudio();

        if (AudioManager.Exists) AudioManager.Instance.PlaySFX("sfx_cerrar_inventario");

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
            ClearSelection();
        }
    }

    private void HandleItemSelected(SO_InventoryItem item)
    {
        itemDetailView?.ShowDetail(item);
    }

    private void HandleCancelInput()
    {
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

    /// <summary>
    /// Drops the selection: no highlighted row, pop-up folded. Called by ESC and by clicking the
    /// selected row again. An open doc belongs to the item, so it goes with it.
    /// </summary>
    public void ClearSelection()
    {
        if (itemDetailView != null && itemDetailView.IsDocOpen) itemDetailView.HideDoc();

        selectedItem = null;
        currentSelectedItem = null;

        InventoryEvents.ItemSelected(null);

        inventoryView?.HighlightItem(null);
    }

    // -- Helpers --------------------

    private void RefreshItemList()
    {
        if (!isInventoryOpen) return;
        if (!InventoryManager.Exists) return;
        inventoryView?.RefreshList(InventoryManager.Instance);
    }

    // -- State accessors --------------------

    public bool IsInventoryOpen => isInventoryOpen;
    public SO_InventoryItem SelectedItem => selectedItem;
}
