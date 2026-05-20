using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsView : BaseScreenView
{
    [Header("Audio")]
    [SerializeField] private Slider           _sliderMaster;
    [SerializeField] private Slider           _sliderMusic;
    [SerializeField] private Slider           _sliderSFX;
    [SerializeField] private TextMeshProUGUI  _labelMaster;
    [SerializeField] private TextMeshProUGUI  _labelMusic;
    [SerializeField] private TextMeshProUGUI  _labelSFX;

    [Header("Gameplay")]
    [SerializeField] private Slider           _sliderSensitivity;
    [SerializeField] private TextMeshProUGUI  _labelSensitivity;

    [Header("Buttons")]
    [SerializeField] private Button _btnApply;
    [SerializeField] private Button _btnBack;

    public event Action         OnApplyClicked;
    public event Action         OnBackClicked;
    public event Action<float>  OnMasterChanged;
    public event Action<float>  OnMusicChanged;
    public event Action<float>  OnSFXChanged;
    public event Action<float>  OnSensitivityChanged;

    private void Awake()
    {
        _btnApply?.onClick.AddListener(() => OnApplyClicked?.Invoke());
        _btnBack?.onClick.AddListener(()  => OnBackClicked?.Invoke());

        _sliderMaster?.onValueChanged.AddListener(v =>
        {
            SetLabel(_labelMaster, v, "P0");
            OnMasterChanged?.Invoke(v);
        });
        _sliderMusic?.onValueChanged.AddListener(v =>
        {
            SetLabel(_labelMusic, v, "P0");
            OnMusicChanged?.Invoke(v);
        });
        _sliderSFX?.onValueChanged.AddListener(v =>
        {
            SetLabel(_labelSFX, v, "P0");
            OnSFXChanged?.Invoke(v);
        });
        _sliderSensitivity?.onValueChanged.AddListener(v =>
        {
            SetLabel(_labelSensitivity, v, "0.00");
            OnSensitivityChanged?.Invoke(v);
        });
    }

    private void OnDestroy()
    {
        _btnApply?.onClick.RemoveAllListeners();
        _btnBack?.onClick.RemoveAllListeners();
        _sliderMaster?.onValueChanged.RemoveAllListeners();
        _sliderMusic?.onValueChanged.RemoveAllListeners();
        _sliderSFX?.onValueChanged.RemoveAllListeners();
        _sliderSensitivity?.onValueChanged.RemoveAllListeners();
    }

        public void Populate(SettingsModel model)
    {
        SetSilent(_sliderMaster,      model.MasterVolume);
        SetSilent(_sliderMusic,       model.MusicVolume);
        SetSilent(_sliderSFX,         model.SFXVolume);
        SetSilent(_sliderSensitivity, model.Sensitivity);

        SetLabel(_labelMaster,      model.MasterVolume, "P0");
        SetLabel(_labelMusic,       model.MusicVolume,  "P0");
        SetLabel(_labelSFX,         model.SFXVolume,    "P0");
        SetLabel(_labelSensitivity, model.Sensitivity,  "0.00");
    }

    private static void SetSilent(Slider slider, float value)
        => slider?.SetValueWithoutNotify(value);

    private static void SetLabel(TextMeshProUGUI label, float value, string format)
    {
        if (label != null) label.text = value.ToString(format);
    }
}
