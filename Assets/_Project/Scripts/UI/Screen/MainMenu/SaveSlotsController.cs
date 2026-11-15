using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Controller of the Save Slots canvas. Lives embedded in the MainMenuUI scene.
/// The canvas starts disabled; call Show() to open it and Hide() to close it.
///
/// The cards are the save files on disk (<see cref="SO_SaveSlotDatabase"/>, reloaded on every Show).
/// Clicking a card raises <see cref="OnSlotSelected"/>: the MainMenu starts a new run in an empty or
/// resolved slot, or loads the one with a save. A card's delete button asks for confirmation (the
/// view's dialog) and only then deletes the slot (<see cref="DeleteSlot"/>).
///
/// <see cref="ShowOverwrite"/> is New Game with every slot holding a save: a banner says so, every
/// card offers [ overwrite ], and choosing one asks YES / NO before raising <see cref="OnSlotOverwrite"/>.
///
/// It is a modal of the <see cref="UIStateManager"/> closed with ESC, like the pause menu and
/// Settings: ESC answers NO to an open question first, and closes the screen otherwise.
/// </summary>
public class SaveSlotsController : BaseScreenController<SaveSlotsView, SaveSlotsModel>, IModalUI
{
    [Header("Data")]
    [SerializeField] private SO_SaveSlotDatabase _database;

    /// <summary>A card's action: new run in an empty / resolved slot, or load the save.</summary>
    public event Action<int> OnSlotSelected;

    /// <summary>The player confirmed that New Game overwrites this slot (its index).</summary>
    public event Action<int> OnSlotOverwrite;

    /// <summary>A slot was deleted from here (its index). The MainMenu refreshes Load Game with it.</summary>
    public event Action<int> OnSlotDeleted;

    /// <summary>True if there is at least one save that can be loaded. Queried by the MainMenu.</summary>
    public bool HasAnySave => _database != null && _database.HasAnySave;

    /// <summary>How many slots there are (1..SlotCount).</summary>
    public int SlotCount => _database != null ? _database.SlotCount : 0;

    private enum Mode { Browse, Overwrite }

    private Mode _mode = Mode.Browse;

    // -- IModalUI --
    public string ModalId => "SaveSlots";
    public ModalCloseKey CloseKey => ModalCloseKey.Escape;   // A menu screen: ESC closes it, as in pause and Settings.
    public bool BlocksPause => true;
    public bool PausesGame => false;                          // The main menu runs at normal speed.

    /// <summary>ESC: answers NO to an open question, or closes the screen.</summary>
    public void RequestClose()
    {
        if (view.IsDialogOpen)
        {
            view.CancelDialog();
            return;
        }

        Hide();
    }

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
        view.OnDeleteClicked += HandleDeleteClicked;
        view.OnBackClicked += HandleBackClicked;
    }

    private void OnDestroy()
    {
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);

        if (view == null) return;
        view.OnSlotClicked -= HandleSlotClicked;
        view.OnDeleteClicked -= HandleDeleteClicked;
        view.OnBackClicked -= HandleBackClicked;
    }

    protected override void OnBeforeOpen()
    {
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);
    }

    protected override void OnBeforeClose()
    {
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);
    }

    public void Show() => ShowInternal(Mode.Browse);

    /// <summary>
    /// New Game with every slot holding a save: the screen asks which one to overwrite.
    /// </summary>
    public void ShowOverwrite() => ShowInternal(Mode.Overwrite);

    public void Hide()
    {
        view.CloseOverlays();
        _mode = Mode.Browse;
        Close().Forget();
    }

    /// <summary>Re-reads the slots from disk and redraws the cards.</summary>
    public void RefreshSlots()
    {
        if (_database != null) _database.ReloadFromDisk();
        model.SetDatabase(_database);

        bool overwrite = _mode == Mode.Overwrite;
        view.Populate(model.Slots, model.LastPlayedSlot, overwrite);
        view.SetNoFreeSlotNotice(overwrite);
    }

    /// <summary>
    /// The slot New Game takes (1-based): the first empty one, else the first resolved one.
    /// <see cref="SaveGame.NoSlot"/> when every slot holds a save that can still be loaded.
    /// </summary>
    public int FindFirstEmptySlot() => _database != null ? _database.FindFirstEmptySlot() : SaveGame.NoSlot;

    /// <summary>
    /// Deletes the save in this slot and redraws the cards. The delete button gets here only through
    /// the confirmation dialog: call it directly only after asking.
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

    private void ShowInternal(Mode mode)
    {
        // A dialog left open by a previous visit must not greet the player on the next one.
        view.CloseOverlays();
        _mode = mode;
        RefreshSlots();
        Open().Forget();
    }

    private void HandleSlotClicked(int slotIndex)
    {
        Debug.Log($"<color=cyan>[SaveSlotsController] Slot {slotIndex} clicked ({_mode}).</color>");

        SO_SaveSlotData slot = GetSlot(slotIndex);
        if (_mode == Mode.Overwrite && slot != null && slot.IsLoadable)
        {
            view.AskToOverwrite(slot, () => OnSlotOverwrite?.Invoke(slotIndex));
            return;
        }

        OnSlotSelected?.Invoke(slotIndex);
    }

    private void HandleDeleteClicked(int slotIndex)
    {
        SO_SaveSlotData slot = GetSlot(slotIndex);
        if (slot == null || !slot.IsLoadable) return;

        view.AskToDelete(slot, () =>
        {
            if (!DeleteSlot(slotIndex))
                Debug.LogError($"[{nameof(SaveSlotsController)}] Slot {slotIndex} could not be deleted (see the log above).");
        });
    }

    private void HandleBackClicked() => Hide();
}
