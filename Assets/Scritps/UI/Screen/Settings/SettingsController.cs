using Cysharp.Threading.Tasks;
using UnityEngine;

public class SettingsController : BaseScreenController<SettingsView, SettingsModel>, IModalUI
{
    /// <summary>
    /// Global accessor. The UI_Settings scene is persistent (loaded in the bootstrap),
    /// so a single instance lives for the whole session.
    /// </summary>
    public static SettingsController Instance { get; private set; }

    [Header("Input")]
    [SerializeField] private KeyCode _closeKey = KeyCode.Escape;

    private bool _isOpen;
    private bool _isTransitioning;

    public bool IsOpen => _isOpen;

    // -- IModalUI --
    public string ModalId => "Settings";
    public bool ConsumesEscape => true;   // ESC closes Settings (back to pause or menu).
    public bool BlocksPause   => true;    // While in Settings no other pause should open.
    public bool PausesGame    => true;
    public void RequestClose() => HandleBack();

    private void Awake()
    {
        Instance = this;

        if (view == null)
        {
            Debug.LogError($"[{nameof(SettingsController)}] view not assigned in the Inspector.");
            return;
        }

        model = new SettingsModel();
        model.Initialize();

        view.gameObject.SetActive(false);

        WireViewEvents();
    }

    private void OnDestroy()
    {
        if (view == null) return;
        UnwireViewEvents();
    }

    // Closing with ESC is now governed by UIStateManager via UI/Exit -> RequestClose -> HandleBack.

    protected override void OnBeforeOpen()
    {
        _isOpen = true;
        model.Initialize();
        view.Populate(model);
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);
    }

    protected override void OnBeforeClose()
    {
        _isOpen = false;
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);
    }

    /// <summary>
    /// Public entry point to open Settings from any scene (Pause, MainMenu).
    /// Encapsulates the guard against simultaneous transitions.
    /// </summary>
    public void OpenScreen()
    {
        if (_isOpen || _isTransitioning) return;
        OpenSafe().Forget();
    }

    private async UniTaskVoid OpenSafe()
    {
        _isTransitioning = true;
        await Open();
        _isTransitioning = false;
    }

    // ── Buttons ──────────────────────────────────────────────────

    private void HandleApply()
    {
        model.Apply();
        CloseSafe().Forget();
    }

    /// <summary>
    /// The wireframe's "Reset values": reverts the pending changes to the values that were
    /// there when Settings was opened (or at the last Apply). It does not close the screen —
    /// it only undoes the untouched slider/toggle changes.
    ///
    /// If "Reset to factory defaults" (back to the game's factory values) is ever needed,
    /// add a separate button and call <c>model.ResetToDefaults()</c>.
    /// </summary>
    private void HandleReset()
    {
        model.Revert();
        view.Populate(model);
    }

    private void HandleBack()
    {
        model.Revert();
        CloseSafe().Forget();
    }

    private async UniTaskVoid CloseSafe()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;
        await Close();
        _isTransitioning = false;
    }

    // ── Wiring ───────────────────────────────────────────────────

    private void WireViewEvents()
    {
        view.OnApplyClicked += HandleApply;
        view.OnResetClicked += HandleReset;
        view.OnBackClicked  += HandleBack;

        // Connected to the AudioManager (live preview + persistence on Apply)
        view.OnMasterChanged      += model.SetMasterVolume;
        view.OnMusicChanged       += model.SetMusicVolume;
        view.OnSFXChanged         += model.SetSFXVolume;
        view.OnSensitivityChanged += model.SetSensitivity;

        // All of these DO affect the game via appliers listening to SettingsModel.OnSettingsApplied
        // (each applier must be placed on its GameObject in the scene):
        //   InvertY                     -> CameraSensitivityApplier (camera rig)
        //   Brightness/Contrast/Gamma   -> PostProcessSettingsApplier (Global Volume)
        //   CRT / Dither                -> PS1EffectApplier (persistent, holds PS1Effect.mat)
        //   Resolution/Window/FPS/VSync -> ScreenSettingsApplier (persistent)
        view.OnInvertYChanged    += model.SetInvertYAxis;
        view.OnBrightnessChanged += model.SetBrightness;
        view.OnContrastChanged   += model.SetContrast;
        view.OnGammaChanged      += model.SetGamma;
        view.OnCRTChanged        += model.SetCRTScanlines;
        view.OnDitherChanged     += model.SetPSXDithering;
        view.OnResolutionChanged += model.SetResolutionIndex;
        view.OnWindowModeChanged += model.SetWindowMode;
        view.OnFPSLimitChanged   += model.SetFPSLimit;
        view.OnVSyncChanged      += model.SetVSync;
    }

    private void UnwireViewEvents()
    {
        view.OnApplyClicked -= HandleApply;
        view.OnResetClicked -= HandleReset;
        view.OnBackClicked  -= HandleBack;

        view.OnMasterChanged      -= model.SetMasterVolume;
        view.OnMusicChanged       -= model.SetMusicVolume;
        view.OnSFXChanged         -= model.SetSFXVolume;
        view.OnSensitivityChanged -= model.SetSensitivity;

        view.OnInvertYChanged    -= model.SetInvertYAxis;
        view.OnBrightnessChanged -= model.SetBrightness;
        view.OnContrastChanged   -= model.SetContrast;
        view.OnGammaChanged      -= model.SetGamma;
        view.OnCRTChanged        -= model.SetCRTScanlines;
        view.OnDitherChanged     -= model.SetPSXDithering;
        view.OnResolutionChanged -= model.SetResolutionIndex;
        view.OnWindowModeChanged -= model.SetWindowMode;
        view.OnFPSLimitChanged   -= model.SetFPSLimit;
        view.OnVSyncChanged      -= model.SetVSync;
    }
}
