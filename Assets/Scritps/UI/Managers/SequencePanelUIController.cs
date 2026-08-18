using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Controller of the sequence panel. Follows the MVC pattern and registers itself in the
/// <see cref="UIStateManager"/> as an <see cref="IModalUI"/> when it opens.
///
/// It lives in the <c>LevelUI</c> scene. The puzzle logic lives in the Interactable;
/// this controller only orchestrates the open/close flow. Time.timeScale, the cursor and
/// ESC handling are governed by the <see cref="UIStateManager"/>.
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
    public bool   ConsumesEscape => false;   // ESC goes to the pause menu (the panel stays underneath).
    public bool   BlocksPause   => false;   // Allows pausing on top.
    public bool   PausesGame    => true;
    public void RequestClose() => CloseSafe().Forget();

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;

        if (view == null)
        {
            Debug.LogError($"[{nameof(SequencePanelUIController)}] view not assigned in the Inspector.");
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

        // Model → Controller (puzzle feedback towards the view)
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

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Entry point: the Interactable calls here when activated.</summary>
    public void Open(SequencePanelInteractable panel)
    {
        if (panel == null) return;
        if (_isOpen || _isTransitioning) return;

        model.BindPanel(panel);
        OpenSafe().Forget();
    }

    // ── BaseScreenController hooks ──────────────────────────────────────────

    protected override void OnBeforeOpen()
    {
        _isOpen = true;
        view.Populate(model);

        // Time.timeScale and the cursor are governed by UIStateManager.
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

    /// <summary>
    /// The model has already cleared the attempt at this point, but the display is left alone:
    /// the view keeps the wrong digits on screen for as long as the red flash lasts and wipes
    /// them itself when it ends. Refreshing here would blank the LCD before the player reads it.
    /// </summary>
    private void HandleModelSequenceFailed() => view.ShowFailFlash();

    private void HandleModelSequenceCompleted()
    {
        view.ShowCompleted();
        CloseAfterDelay(0.6f).Forget();
    }

    // ── Async helpers ───────────────────────────────────────────────────────

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
