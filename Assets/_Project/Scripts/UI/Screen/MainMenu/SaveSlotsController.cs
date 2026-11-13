using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Controller of the Save Slots canvas. Lives embedded in the MainMenuUI scene.
/// The canvas starts disabled; call Show() to open it and Hide() to close it.
///
/// The cards are the save files on disk (<see cref="SO_SaveSlotDatabase"/>, reloaded on every Show).
/// Clicking a card raises <see cref="OnSlotSelected"/>: the MainMenu starts a new run in an empty slot
/// or loads the one with data. <see cref="DeleteSlot"/> is ready for a delete button.
/// </summary>
public class SaveSlotsController : BaseScreenController<SaveSlotsView, SaveSlotsModel>
{
    [Header("Data")]
    [SerializeField] private SO_SaveSlotDatabase _database;

    public event Action<int> OnSlotSelected;

    /// <summary>A slot was deleted from here (its index). The MainMenu refreshes Load Game with it.</summary>
    public event Action<int> OnSlotDeleted;

    /// <summary>True if there is at least one saved game. Queried by the MainMenu.</summary>
    public bool HasAnySave => _database != null && _database.HasAnySave;

    /// <summary>How many slots there are (1..SlotCount).</summary>
    public int SlotCount => _database != null ? _database.SlotCount : 0;

    private void Awake()
    {
        if (view == null)
        {
            Debug.LogError($"[{nameof(SaveSlotsController)}] view not assigned in the Inspector.");
            return;
        }

        if (model == null)
        {
            model = new SaveSlotsModel();
            model.Initialize();
        }

        model.SetDatabase(_database);

        view.OnSlotClicked += HandleSlotClicked;
        view.OnBackClicked += HandleBackClicked;
    }

    private void OnDestroy()
    {
        if (view == null) return;
        view.OnSlotClicked -= HandleSlotClicked;
        view.OnBackClicked -= HandleBackClicked;
    }

    public void Show()
    {
        RefreshSlots();
        Open().Forget();
    }

    public void Hide()
    {
        Close().Forget();
    }

    /// <summary>Re-reads the slots from disk and redraws the cards.</summary>
    public void RefreshSlots()
    {
        if (_database != null) _database.ReloadFromDisk();
        model.SetDatabase(_database);
        view.Populate(model.Slots);
    }

    /// <summary>First slot with no save (1-based), or <see cref="SaveGame.NoSlot"/> when all are taken.</summary>
    public int FindFirstEmptySlot() => _database != null ? _database.FindFirstEmptySlot() : SaveGame.NoSlot;

    /// <summary>
    /// Deletes the save in this slot and redraws the cards. For the delete button (ask for
    /// confirmation before calling it: it cannot be undone).
    /// </summary>
    /// <returns>false when the file could not be deleted.</returns>
    public bool DeleteSlot(int slotIndex)
    {
        bool deleted = SaveGame.DeleteSlot(slotIndex);
        RefreshSlots();
        if (deleted) OnSlotDeleted?.Invoke(slotIndex);
        return deleted;
    }

    /// <summary>Returns the slot by its SlotIndex (1-based), or null if it does not exist.</summary>
    public SO_SaveSlotData GetSlot(int slotIndex)
    {
        if (model?.Slots == null) return null;
        foreach (SO_SaveSlotData slot in model.Slots)
            if (slot != null && slot.SlotIndex == slotIndex) return slot;
        return null;
    }

    private void HandleSlotClicked(int slotIndex)
    {
        Debug.Log($"<color=cyan>[SaveSlotsController] Slot {slotIndex} clicked.</color>");
        OnSlotSelected?.Invoke(slotIndex);
    }

    private void HandleBackClicked() => Hide();
}
