using Cysharp.Threading.Tasks;
using UnityEngine;

public class SaveSlotsController : BaseScreenController<SaveSlotsView, SaveSlotsModel>
{
    [Header("Event Channels")]
    [SerializeField] private ScreenEventChannel _screenChannel;

    [Header("Data")]
    [SerializeField] private SO_SaveSlotDatabase _database;

    [Header("Navigation (stub)")]
    [Tooltip("Mientras no exista save real, todos los slots cargan este grupo de escena.")]
    [SerializeField] private string _firstSceneLabel = "TestBlocking";

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
        // Stub: cualquier slot dispara el flow de nueva partida.
        // Cuando exista save real, distinguir entre cargar (slot con datos) y nueva (vacío).
        LoadSlotStub(slotIndex).Forget();
    }

    private async UniTaskVoid LoadSlotStub(int slotIndex)
    {
        Debug.Log($"<color=cyan>[SaveSlotsController] Slot {slotIndex} → cargando '{_firstSceneLabel}' (stub).</color>");

        if (_screenChannel == null)
        {
            Debug.LogError("[SaveSlotsController] Falta asignar el ScreenEventChannel.");
            return;
        }

        await Close();
        _screenChannel.RaisePushScreen(_firstSceneLabel);
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
