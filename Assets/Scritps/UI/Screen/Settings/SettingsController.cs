using Cysharp.Threading.Tasks;
using UnityEngine;

public class SettingsController : BaseScreenController<SettingsView, SettingsModel>, IModalUI
{
    /// <summary>
    /// Accessor global. La escena UI_Settings es persistente (cargada en el bootstrap),
    /// así que vive una sola instancia durante toda la sesión.
    /// </summary>
    public static SettingsController Instance { get; private set; }

    [Header("Input")]
    [SerializeField] private KeyCode _closeKey = KeyCode.Escape;

    private bool _isOpen;
    private bool _isTransitioning;

    public bool IsOpen => _isOpen;

    // -- IModalUI --
    public string ModalId => "Settings";
    public bool ConsumesEscape => true;   // ESC cierra Settings (vuelve a pausa o menu).
    public bool BlocksPause   => true;    // Estando en Settings no debe abrirse otra pausa.
    public void RequestClose() => HandleBack();

    private void Awake()
    {
        Instance = this;

        if (view == null)
        {
            Debug.LogError($"[{nameof(SettingsController)}] view no asignada en el Inspector.");
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

    // El cierre por ESC ahora lo gobierna UIStateManager via UI/Exit -> RequestClose -> HandleBack.

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
    /// Punto de entrada público para abrir Settings desde cualquier escena
    /// (Pausa, MainMenu). Encapsula el guard contra transiciones simultáneas.
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

    // ── Botones ──────────────────────────────────────────────────

    private void HandleApply()
    {
        model.Apply();
        CloseSafe().Forget();
    }

    private void HandleReset()
    {
        model.ResetToDefaults();
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

        // Conectados
        view.OnMasterChanged      += model.SetMasterVolume;
        view.OnMusicChanged       += model.SetMusicVolume;
        view.OnSFXChanged         += model.SetSFXVolume;
        view.OnSensitivityChanged += model.SetSensitivity;

        // Placeholders (se guardan en el modelo pero todavía no afectan al juego)
        view.OnVoiceChanged      += model.SetVoiceVolume;
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

        view.OnVoiceChanged      -= model.SetVoiceVolume;
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
