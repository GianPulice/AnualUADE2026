using Cysharp.Threading.Tasks;
using UnityEngine;

public class PauseManagerUI : BaseScreenController<PauseView, EmptyScreenModel>, IModalUI
{
    [Header("Architecture (MVC)")]
    [Tooltip("We need the event channel to exit correctly to the Main Menu")]
    [SerializeField] private ScreenEventChannel screenChannel;

    private bool _isTransitioning;

    // -- IModalUI --
    public string ModalId => "Pause";
    public bool ConsumesEscape => true;   // ESC closes the pause menu.
    public bool BlocksPause   => false;   // It IS the pause menu: it does not block itself.
    public bool PausesGame    => true;
    public void RequestClose() => PauseManager.RequestUnpause();

    private void Awake()
    {
        if (view == null)
        {
            Debug.LogError($"[{nameof(PauseManagerUI)}] view not assigned in the Inspector.");
            return;
        }

        view.gameObject.SetActive(false); // Make sure it starts switched off

        if (model == null)
        {
            model = new EmptyScreenModel();
            model.Initialize();
        }

        // A level always begins unpaused. PauseManager lives in the persistent Data scene, so its
        // state outlives this scene: a stray pause latched before gameplay existed (e.g. ESC on
        // the main menu) would otherwise carry in here and freeze the run, and because the state
        // changed before this subscription there is no event left to open the menu on. Clearing it
        // here guarantees a clean start regardless of how it got stuck.
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) PauseManager.RequestUnpause();

        PauseManager.OnPauseStateChanged += HandlePauseStateChanged;

        view.OnContinueClicked += HandleContinue;
        view.OnSettingsClicked += HandleSettings;
        view.OnExitClicked     += HandleExit;
    }

    private void OnDestroy()
    {
        PauseManager.OnPauseStateChanged -= HandlePauseStateChanged;

        if (view == null) return;

        view.OnContinueClicked -= HandleContinue;
        view.OnSettingsClicked -= HandleSettings;
        view.OnExitClicked     -= HandleExit;
    }
    private void HandlePauseStateChanged(PauseState state)
    {
        if (_isTransitioning) return;

        if (state == PauseState.Paused)
            OpenSafe().Forget();
        else
            CloseSafe().Forget();
    }

    protected override void OnBeforeOpen()
    {
        // Time.timeScale and the cursor are governed by UIStateManager.
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);
    }

    protected override void OnBeforeClose()
    {
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);
        view.ResetButtonStates();
    }

    private async UniTaskVoid OpenSafe()
    {
        _isTransitioning = true;
        await Open();
        _isTransitioning = false;
    }

    private async UniTaskVoid CloseSafe()
    {
        _isTransitioning = true;
        await Close();
        _isTransitioning = false;
    }

    // ── Reaction to the UI buttons ───────────────────────────

    private void HandleContinue()
    {
        // "Continue" only unpauses. If there was a modal open underneath (e.g. SequencePanel),
        // the player goes back to that modal — we do NOT close it. To get cleanly back to
        // gameplay they have to close the modal manually or use "Exit to menu".
        PauseManager.RequestUnpause();
    }

    private void HandleSettings()
    {
        if (SettingsController.Instance == null)
        {
            Debug.LogError("[PauseManagerUI] SettingsController.Instance is null. " +
                           "Is the UI_Settings scene loaded in the bootstrap?");
            return;
        }
        SettingsController.Instance.OpenScreen();
    }

    private void HandleExit()
    {
        // The flag stops HandlePauseStateChanged: the RequestUnpause calls below would fire a
        // CloseSafe() that competes with CloseAll(). It is released at the end of the method,
        // once those events have already gone through — leaving it true hung the pause forever.
        _isTransitioning = true;

        // Close any leftover modal (sequence panel, etc.) before exiting.
        if (UIStateManager.Exists) UIStateManager.Instance.CloseAll();
        Time.timeScale = 1f;

        PauseManager.RequestUnpause();

        if (screenChannel != null)
            screenChannel.RaisePushScreen("Menu");
        else
            Debug.LogError("[PauseManagerUI] The ScreenEventChannel is not assigned.");

        _isTransitioning = false;
    }

}
