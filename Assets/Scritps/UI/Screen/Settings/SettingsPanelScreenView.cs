using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panel de Screen — PLACEHOLDER.
/// Resolución / Modo / FPSLimit / VSync se almacenan en el modelo pero todavía no se aplican
/// (esto requiere Screen.SetResolution + Application.targetFrameRate + QualitySettings.vSyncCount
/// además de un listado de resoluciones soportadas; se hará junto con el resto del Brightness/Screen).
/// </summary>
public class SettingsPanelScreenView : MonoBehaviour
{
    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown _dropdownResolution;
    [SerializeField] private TMP_Dropdown _dropdownWindowMode;
    [SerializeField] private TMP_Dropdown _dropdownFPSLimit;

    [Header("Toggle")]
    [SerializeField] private Toggle _toggleVSync;

    public event Action<int>  OnResolutionChanged;
    public event Action<int>  OnWindowModeChanged;
    public event Action<int>  OnFPSLimitChanged;
    public event Action<bool> OnVSyncChanged;

    private void Awake()
    {
        if (_dropdownResolution != null)
            _dropdownResolution.onValueChanged.AddListener(v => OnResolutionChanged?.Invoke(v));
        if (_dropdownWindowMode != null)
            _dropdownWindowMode.onValueChanged.AddListener(v => OnWindowModeChanged?.Invoke(v));
        if (_dropdownFPSLimit != null)
            _dropdownFPSLimit.onValueChanged.AddListener(v => OnFPSLimitChanged?.Invoke(v));
        if (_toggleVSync != null)
            _toggleVSync.onValueChanged.AddListener(v => OnVSyncChanged?.Invoke(v));
    }

    private void OnDestroy()
    {
        if (_dropdownResolution != null) _dropdownResolution.onValueChanged.RemoveAllListeners();
        if (_dropdownWindowMode != null) _dropdownWindowMode.onValueChanged.RemoveAllListeners();
        if (_dropdownFPSLimit != null)   _dropdownFPSLimit.onValueChanged.RemoveAllListeners();
        if (_toggleVSync != null)        _toggleVSync.onValueChanged.RemoveAllListeners();
    }

    public void Populate(SettingsModel model)
    {
        if (model == null) return;
        if (_dropdownResolution != null) _dropdownResolution.SetValueWithoutNotify(model.ResolutionIndex);
        if (_dropdownWindowMode != null) _dropdownWindowMode.SetValueWithoutNotify(model.WindowMode);
        if (_dropdownFPSLimit != null)   _dropdownFPSLimit.SetValueWithoutNotify(model.FPSLimit);
        if (_toggleVSync != null)        _toggleVSync.SetIsOnWithoutNotify(model.VSync);
    }
}
