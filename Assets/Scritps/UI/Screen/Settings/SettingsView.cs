using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Root view of the Settings screen. Delegates each tab to its own sub-view and re-raises the
/// events as a single set so the Controller does not have to know the internal structure.
/// </summary>
public class SettingsView : BaseScreenView
{
    [Header("Tabs")]
    [SerializeField] private SettingsTabSelector _tabSelector;

    [Header("Sub-views (assign the GameObject holding each component)")]
    [SerializeField] private SettingsPanelVolumeView     _volumePanel;
    [SerializeField] private SettingsPanelControlsView   _controlsPanel;
    [SerializeField] private SettingsPanelBrightnessView _brightnessPanel;
    [SerializeField] private SettingsPanelScreenView     _screenPanel;

    [Header("Footer")]
    [SerializeField] private Button _btnApply;
    [SerializeField] private Button _btnReset;
    [SerializeField] private Button _btnBack;

    // ── Events towards the Controller ────────────────────────────────────────
    public event Action OnApplyClicked;
    public event Action OnResetClicked;
    public event Action OnBackClicked;

    // Connected (SFX groups Ambience/Player/Nemesis/UI/Voice):
    public event Action<float> OnMasterChanged;
    public event Action<float> OnMusicChanged;
    public event Action<float> OnSFXChanged;
    public event Action<float> OnSensitivityChanged;

    // Applied to the game by their appliers (see *Applier.cs) on Apply:
    public event Action<bool>  OnInvertYChanged;
    public event Action<float> OnBrightnessChanged;
    public event Action<float> OnContrastChanged;
    public event Action<float> OnGammaChanged;
    public event Action<bool>  OnCRTChanged;
    public event Action<bool>  OnDitherChanged;
    public event Action<int>   OnResolutionChanged;
    public event Action<int>   OnWindowModeChanged;
    public event Action<int>   OnFPSLimitChanged;
    public event Action<bool>  OnVSyncChanged;

    private void Awake()
    {
        if (_btnApply != null) _btnApply.onClick.AddListener(() => OnApplyClicked?.Invoke());
        if (_btnReset != null) _btnReset.onClick.AddListener(() => OnResetClicked?.Invoke());
        if (_btnBack != null)  _btnBack.onClick.AddListener(()  => OnBackClicked?.Invoke());

        WireVolumePanel();
        WireControlsPanel();
        WireBrightnessPanel();
        WireScreenPanel();
    }

    private void OnDestroy()
    {
        if (_btnApply != null) _btnApply.onClick.RemoveAllListeners();
        if (_btnReset != null) _btnReset.onClick.RemoveAllListeners();
        if (_btnBack != null)  _btnBack.onClick.RemoveAllListeners();

        UnwireVolumePanel();
        UnwireControlsPanel();
        UnwireBrightnessPanel();
        UnwireScreenPanel();
    }

    public void Populate(SettingsModel model)
    {
        _volumePanel?.Populate(model);
        _controlsPanel?.Populate(model);
        _brightnessPanel?.Populate(model);
        _screenPanel?.Populate(model);
    }

    // ── Wiring helpers ───────────────────────────────────────────────────────

    private void WireVolumePanel()
    {
        if (_volumePanel == null) return;
        _volumePanel.OnMasterChanged += ForwardMaster;
        _volumePanel.OnMusicChanged  += ForwardMusic;
        _volumePanel.OnSFXChanged    += ForwardSFX;
    }
    private void UnwireVolumePanel()
    {
        if (_volumePanel == null) return;
        _volumePanel.OnMasterChanged -= ForwardMaster;
        _volumePanel.OnMusicChanged  -= ForwardMusic;
        _volumePanel.OnSFXChanged    -= ForwardSFX;
    }
    private void ForwardMaster(float v) => OnMasterChanged?.Invoke(v);
    private void ForwardMusic(float v)  => OnMusicChanged?.Invoke(v);
    private void ForwardSFX(float v)    => OnSFXChanged?.Invoke(v);

    private void WireControlsPanel()
    {
        if (_controlsPanel == null) return;
        _controlsPanel.OnSensitivityChanged += ForwardSensitivity;
        _controlsPanel.OnInvertYChanged     += ForwardInvertY;
    }
    private void UnwireControlsPanel()
    {
        if (_controlsPanel == null) return;
        _controlsPanel.OnSensitivityChanged -= ForwardSensitivity;
        _controlsPanel.OnInvertYChanged     -= ForwardInvertY;
    }
    private void ForwardSensitivity(float v) => OnSensitivityChanged?.Invoke(v);
    private void ForwardInvertY(bool v)      => OnInvertYChanged?.Invoke(v);

    private void WireBrightnessPanel()
    {
        if (_brightnessPanel == null) return;
        _brightnessPanel.OnBrightnessChanged += ForwardBrightness;
        _brightnessPanel.OnContrastChanged   += ForwardContrast;
        _brightnessPanel.OnGammaChanged      += ForwardGamma;
        _brightnessPanel.OnCRTChanged        += ForwardCRT;
        _brightnessPanel.OnDitherChanged     += ForwardDither;
    }
    private void UnwireBrightnessPanel()
    {
        if (_brightnessPanel == null) return;
        _brightnessPanel.OnBrightnessChanged -= ForwardBrightness;
        _brightnessPanel.OnContrastChanged   -= ForwardContrast;
        _brightnessPanel.OnGammaChanged      -= ForwardGamma;
        _brightnessPanel.OnCRTChanged        -= ForwardCRT;
        _brightnessPanel.OnDitherChanged     -= ForwardDither;
    }
    private void ForwardBrightness(float v) => OnBrightnessChanged?.Invoke(v);
    private void ForwardContrast(float v)   => OnContrastChanged?.Invoke(v);
    private void ForwardGamma(float v)      => OnGammaChanged?.Invoke(v);
    private void ForwardCRT(bool v)         => OnCRTChanged?.Invoke(v);
    private void ForwardDither(bool v)      => OnDitherChanged?.Invoke(v);

    private void WireScreenPanel()
    {
        if (_screenPanel == null) return;
        _screenPanel.OnResolutionChanged += ForwardResolution;
        _screenPanel.OnWindowModeChanged += ForwardWindowMode;
        _screenPanel.OnFPSLimitChanged   += ForwardFPSLimit;
        _screenPanel.OnVSyncChanged      += ForwardVSync;
    }
    private void UnwireScreenPanel()
    {
        if (_screenPanel == null) return;
        _screenPanel.OnResolutionChanged -= ForwardResolution;
        _screenPanel.OnWindowModeChanged -= ForwardWindowMode;
        _screenPanel.OnFPSLimitChanged   -= ForwardFPSLimit;
        _screenPanel.OnVSyncChanged      -= ForwardVSync;
    }
    private void ForwardResolution(int v) => OnResolutionChanged?.Invoke(v);
    private void ForwardWindowMode(int v) => OnWindowModeChanged?.Invoke(v);
    private void ForwardFPSLimit(int v)   => OnFPSLimitChanged?.Invoke(v);
    private void ForwardVSync(bool v)     => OnVSyncChanged?.Invoke(v);
}
