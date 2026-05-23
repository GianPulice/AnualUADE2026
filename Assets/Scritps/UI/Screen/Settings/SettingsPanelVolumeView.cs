using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de Volume del Settings: 4 sliders (Master, SFX, Música, Voces).
/// Voces se almacena en el modelo pero todavía no afecta al audio del juego.
/// </summary>
public class SettingsPanelVolumeView : MonoBehaviour
{
    [Header("Master")]
    [SerializeField] private Slider _sliderMaster;
    [SerializeField] private TextMeshProUGUI _labelMaster;

    [Header("SFX")]
    [SerializeField] private Slider _sliderSFX;
    [SerializeField] private TextMeshProUGUI _labelSFX;

    [Header("Music")]
    [SerializeField] private Slider _sliderMusic;
    [SerializeField] private TextMeshProUGUI _labelMusic;

    //[Header("Voice (placeholder)")]
    //[SerializeField] private Slider _sliderVoice;
    //[SerializeField] private TextMeshProUGUI _labelVoice;

    public event Action<float> OnMasterChanged;
    public event Action<float> OnSFXChanged;
    public event Action<float> OnMusicChanged;
    public event Action<float> OnVoiceChanged;

    private void Awake()
    {
        if (_sliderMaster != null)
            _sliderMaster.onValueChanged.AddListener(v => { SetLabel(_labelMaster, v); OnMasterChanged?.Invoke(v); });
        if (_sliderSFX != null)
            _sliderSFX.onValueChanged.AddListener(v => { SetLabel(_labelSFX, v); OnSFXChanged?.Invoke(v); });
        if (_sliderMusic != null)
            _sliderMusic.onValueChanged.AddListener(v => { SetLabel(_labelMusic, v); OnMusicChanged?.Invoke(v); });
        //if (_sliderVoice != null)
        //    _sliderVoice.onValueChanged.AddListener(v => { SetLabel(_labelVoice, v); OnVoiceChanged?.Invoke(v); });
    }

    private void OnDestroy()
    {
        if (_sliderMaster != null) _sliderMaster.onValueChanged.RemoveAllListeners();
        if (_sliderSFX != null)    _sliderSFX.onValueChanged.RemoveAllListeners();
        if (_sliderMusic != null)  _sliderMusic.onValueChanged.RemoveAllListeners();
       // if (_sliderVoice != null)  _sliderVoice.onValueChanged.RemoveAllListeners();
    }

    public void Populate(SettingsModel model)
    {
        if (model == null) return;

        SetSilent(_sliderMaster, model.MasterVolume);
        SetSilent(_sliderSFX,    model.SFXVolume);
        SetSilent(_sliderMusic,  model.MusicVolume);
       // SetSilent(_sliderVoice,  model.VoiceVolume);

        SetLabel(_labelMaster, model.MasterVolume);
        SetLabel(_labelSFX,    model.SFXVolume);
        SetLabel(_labelMusic,  model.MusicVolume);
       // SetLabel(_labelVoice,  model.VoiceVolume);
    }

    private static void SetSilent(Slider slider, float value) => slider?.SetValueWithoutNotify(value);
    private static void SetLabel(TextMeshProUGUI label, float value01)
    {
        if (label != null) label.text = Mathf.RoundToInt(value01 * 100f).ToString();
    }
}
