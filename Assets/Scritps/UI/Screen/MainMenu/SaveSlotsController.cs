using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Controller of the Save Slots canvas. Lives embedded in the MainMenuUI scene.
/// The canvas starts disabled; call Show() to open it and Hide() to close it.
/// Clicking a slot raises OnSlotSelected so the real save system can hook in.
/// </summary>
public class SaveSlotsController : BaseScreenController<SaveSlotsView, SaveSlotsModel>
{
    [Header("Data")]
    [SerializeField] private SO_SaveSlotDatabase _database;

    public event Action<int> OnSlotSelected;

    /// <summary>True if there is at least one saved game. Queried by the MainMenu.</summary>
    public bool HasAnySave => _database != null && _database.HasAnySave;

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
        view.Populate(model.Slots);
        Open().Forget();
    }

    public void Hide()
    {
        Close().Forget();
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
        Debug.Log($"<color=cyan>[SaveSlotsController] Slot {slotIndex} clicked (visual stub).</color>");
        OnSlotSelected?.Invoke(slotIndex);
    }

    private void HandleBackClicked() => Hide();
}
