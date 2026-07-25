using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Controller del Save Slots canvas. Vive embebido en la escena MainMenuUI.
/// El canvas empieza desactivado; llamar Show() para abrirlo y Hide() para cerrarlo.
/// El click en un slot emite OnSlotSelected para que el save system real se conecte.
/// </summary>
public class SaveSlotsController : BaseScreenController<SaveSlotsView, SaveSlotsModel>
{
    [Header("Data")]
    [SerializeField] private SO_SaveSlotDatabase _database;

    public event Action<int> OnSlotSelected;

    /// <summary>True si hay al menos una partida guardada. Lo consulta el MainMenu.</summary>
    public bool HasAnySave => _database != null && _database.HasAnySave;

    private void Awake()
    {
        if (view == null)
        {
            Debug.LogError($"[{nameof(SaveSlotsController)}] view no asignada en el Inspector.");
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

    /// <summary>Devuelve el slot por su SlotIndex (1-based), o null si no existe.</summary>
    public SO_SaveSlotData GetSlot(int slotIndex)
    {
        if (model?.Slots == null) return null;
        foreach (SO_SaveSlotData slot in model.Slots)
            if (slot != null && slot.SlotIndex == slotIndex) return slot;
        return null;
    }

    private void HandleSlotClicked(int slotIndex)
    {
        Debug.Log($"<color=cyan>[SaveSlotsController] Slot {slotIndex} clickeado (visual stub).</color>");
        OnSlotSelected?.Invoke(slotIndex);
    }

    private void HandleBackClicked() => Hide();
}
