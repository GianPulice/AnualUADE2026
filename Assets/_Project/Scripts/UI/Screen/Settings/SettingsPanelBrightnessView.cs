using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Brightness panel. The controls are stored in the model and DO affect the game:
/// Brightness/Contrast/Gamma are applied by PostProcessSettingsApplier (Global Volume) and
/// CRTScanlines/PSXDithering by PS1EffectApplier, both when
/// SettingsModel.OnSettingsApplied fires. Requirement: each applier placed on its GameObject.
/// </summary>
public class SettingsPanelBrightnessView : MonoBehaviour
{
    [Header("Sliders")]
    [SerializeField] private Slider _sliderBrightness;
    [SerializeField] private Slider _sliderContrast;
    [SerializeField] private Slider _sliderGamma;

    [Header("Labels")]
    [SerializeField] private TextMeshProUGUI _labelBrightness;
    [SerializeField] private TextMeshProUGUI _labelContrast;
    [SerializeField] private TextMeshProUGUI _labelGamma;

    [Header("Toggles")]
    [SerializeField] private Toggle _toggleCRT;
    [SerializeField] private Toggle _toggleDither;

    public event Action<float> OnBrightnessChanged;
    public event Action<float> OnContrastChanged;
    public event Action<float> OnGammaChanged;
    public event Action<bool>  OnCRTChanged;
    public event Action<bool>  OnDitherChanged;

    private void Awake()
    {
        if (_sliderBrightness != null)
            _sliderBrightness.onValueChanged.AddListener(v => { SetLabel(_labelBrightness, v); OnBrightnessChanged?.Invoke(v); });
        if (_sliderContrast != null)
            _sliderContrast.onValueChanged.AddListener(v => { SetLabel(_labelContrast, v); OnContrastChanged?.Invoke(v); });
        if (_sliderGamma != null)
            _sliderGamma.onValueChanged.AddListener(v => { SetLabel(_labelGamma, v); OnGammaChanged?.Invoke(v); });
        if (_toggleCRT != null)
            _toggleCRT.onValueChanged.AddListener(v => OnCRTChanged?.Invoke(v));
        if (_toggleDither != null)
            _toggleDither.onValueChanged.AddListener(v => OnDitherChanged?.Invoke(v));
    }

    private void OnDestroy()
    {
        if (_sliderBrightness != null) _sliderBrightness.onValueChanged.RemoveAllListeners();
        if (_sliderContrast != null)   _sliderContrast.onValueChanged.RemoveAllListeners();
        if (_sliderGamma != null)      _sliderGamma.onValueChanged.RemoveAllListeners();
        if (_toggleCRT != null)        _toggleCRT.onValueChanged.RemoveAllListeners();
        if (_toggleDither != null)     _toggleDither.onValueChanged.RemoveAllListeners();
    }

    public void Populate(SettingsModel model)
    {
        if (model == null) return;
        if (_sliderBrightness != null) _sliderBrightness.SetValueWithoutNotify(model.Brightness);
        if (_sliderContrast != null)   _sliderContrast.SetValueWithoutNotify(model.Contrast);
        if (_sliderGamma != null)      _sliderGamma.SetValueWithoutNotify(model.Gamma);
        if (_toggleCRT != null)        _toggleCRT.SetIsOnWithoutNotify(model.CRTScanlines);
        if (_toggleDither != null)     _toggleDither.SetIsOnWithoutNotify(model.PSXDithering);

        SetLabel(_labelBrightness, model.Brightness);
        SetLabel(_labelContrast,   model.Contrast);
        SetLabel(_labelGamma,      model.Gamma);
    }

    private static void SetLabel(TextMeshProUGUI label, float value01)
    {
        if (label != null) label.text = Mathf.RoundToInt(value01 * 100f).ToString();
    }
}
