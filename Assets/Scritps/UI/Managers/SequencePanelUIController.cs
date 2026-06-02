using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Controller del panel de secuencia. Sigue el patrón MVC y se registra en el
/// <see cref="UIStateManager"/> como <see cref="IModalUI"/> al abrirse.
///
/// Vive en la escena <c>LevelUI</c>. La lógica del puzzle vive en el Interactable;
/// este controller solo orquesta el flow open/close. Time.timeScale, cursor y el
/// manejo de ESC los gobierna el <see cref="UIStateManager"/>.
/// </summary>
public class SequencePanelUIController
    : BaseScreenController<SequencePanelView, SequencePanelModel>, IModalUI
{
    public static SequencePanelUIController Instance { get; private set; }

    private bool _isOpen;
    private bool _isTransitioning;

    public bool IsOpen => _isOpen;

    // ── IModalUI ────────────────────────────────────────────────────────────
    public string ModalId       => "SequencePanel";
    public bool   ConsumesEscape => false;   // ESC pasa a la pausa (panel queda debajo).
    public bool   BlocksPause   => false;   // Permite pausar encima.
    public bool   PausesGame    => true;
    public void RequestClose() => CloseSafe().Forget();

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;

        if (view == null)
        {
            Debug.LogError($"[{nameof(SequencePanelUIController)}] view no asignada en el Inspector.");
            return;
        }

        if (model == null)
        {
            model = new SequencePanelModel();
            model.Initialize();
        }

        view.gameObject.SetActive(false);

        // View → Controller
        view.OnButtonClicked += HandleButtonClicked;
        view.OnCloseClicked  += HandleCloseClicked;

        // Model → Controller (feedback del puzzle hacia la view)
        model.OnButtonPressed    += HandleModelButtonPressed;
        model.OnSequenceFailed   += HandleModelSequenceFailed;
        model.OnSequenceCompleted += HandleModelSequenceCompleted;
    }

    private void OnDestroy()
    {
        if (view != null)
        {
            view.OnButtonClicked -= HandleButtonClicked;
            view.OnCloseClicked  -= HandleCloseClicked;
        }

        if (model != null)
        {
            model.OnButtonPressed    -= HandleModelButtonPressed;
            model.OnSequenceFailed   -= HandleModelSequenceFailed;
            model.OnSequenceCompleted -= HandleModelSequenceCompleted;
            model.UnbindPanel();
        }
    }

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>Punto de entrada: el Interactable llama aquí al activarse.</summary>
    public void Open(SequencePanelInteractable panel)
    {
        if (panel == null) return;
        if (_isOpen || _isTransitioning) return;

        model.BindPanel(panel);
        OpenSafe().Forget();
    }

    // ── Hooks del BaseScreenController ──────────────────────────────────────

    protected override void OnBeforeOpen()
    {
        _isOpen = true;
        view.Populate(model);

        // Time.timeScale y cursor los gobierna UIStateManager.
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);
    }

    protected override void OnBeforeClose()
    {
        _isOpen = false;
        model.ResetSequenceIfIncomplete();

        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);
    }

    protected override void OnAfterClose() => model.UnbindPanel();

    // ── Handlers ────────────────────────────────────────────────────────────

    private void HandleButtonClicked(int id) => model.TryPressButton(id);

    private void HandleCloseClicked() => CloseSafe().Forget();

    private void HandleModelButtonPressed(int id)
    {
        view.HighlightPressedButton(id);
        view.RefreshSequenceDisplay(model.EnteredSequence);
    }

    private void HandleModelSequenceFailed()
    {
        view.ShowFailFlash();
        view.RefreshSequenceDisplay(model.EnteredSequence);
    }

    private void HandleModelSequenceCompleted()
    {
        view.ShowCompleted();
        CloseAfterDelay(0.6f).Forget();
    }

    // ── Helpers async ───────────────────────────────────────────────────────

    private async UniTaskVoid OpenSafe()
    {
        _isTransitioning = true;
        await base.Open();
        _isTransitioning = false;
    }

    private async UniTaskVoid CloseSafe()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;
        await base.Close();
        _isTransitioning = false;
    }

    private async UniTaskVoid CloseAfterDelay(float seconds)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime);
        CloseSafe().Forget();
    }
}
