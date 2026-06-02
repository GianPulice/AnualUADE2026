using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Controller del Save Slots screen. **Solo visual** — el click en un slot no carga ninguna
/// escena; emite un evento <see cref="OnSlotSelected"/> + log para que el sistema de save
/// real se conecte en el futuro sin tocar esta UI.
///
/// El botón Back sí está conectado a <c>ScreenEventChannel.RaisePopScreen()</c> para volver
/// al MainMenu durante testing.
/// </summary>
public class SaveSlotsController : BaseScreenController<SaveSlotsView, SaveSlotsModel>
{
    [Header("Event Channels")]
    [SerializeField] private ScreenEventChannel _screenChannel;

    [Header("Data")]
    [SerializeField] private SO_SaveSlotDatabase _database;

    /// <summary>
    /// Disparado cuando el player clickea un slot (sea "cargar" o "nueva"). Hoy ningún
    /// listener real está conectado — cuando se haga el save system, el GameLoader se
    /// suscribe a este evento y decide qué hacer según <see cref="SO_SaveSlotData.IsEmpty"/>.
    /// </summary>
    public event Action<int> OnSlotSelected;

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

        view.gameObject.SetActive(false);

        view.OnSlotClicked += HandleSlotClicked;
        view.OnBackClicked += HandleBackClicked;
    }

    private void Start()
    {
        Open().Forget();
    }

    private void OnDestroy()
    {
        if (view == null) return;
        view.OnSlotClicked -= HandleSlotClicked;
        view.OnBackClicked -= HandleBackClicked;
    }

    protected override void OnBeforeOpen()
    {
        view.Populate(model.Slots);
    }

    // ── Handlers ──────────────────────────────────────────────────

    private void HandleSlotClicked(int slotIndex)
    {
        Debug.Log($"<color=cyan>[SaveSlotsController] Slot {slotIndex} clickeado (visual stub).</color>");
        OnSlotSelected?.Invoke(slotIndex);
    }

    private void HandleBackClicked()
    {
        if (_screenChannel == null)
        {
            Debug.LogError("[SaveSlotsController] Falta asignar el ScreenEventChannel.");
            return;
        }
        _screenChannel.RaisePopScreen();
    }
}
