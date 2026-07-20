using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de Volume del Settings. Soporta hasta 8 sliders, uno por AudioMixerGroup
/// del juego: Master, Music, SFX, Voice, Ambience, Player, Nemesis, UI.
///
/// Los sliders son opcionales — si el prefab solo asigna un subconjunto, el resto
/// queda inactivo sin warnings.
///
/// Los sliders trabajan en rango 0..100 (configurar en el Inspector:
/// Min Value = 0, Max Value = 100, Whole Numbers = true). El modelo y el
/// AudioManager siguen usando 0..1; la conversión se hace acá, en el borde.
/// </summary>
public class SettingsPanelVolumeView : MonoBehaviour
{
    private const float SLIDER_MAX = 100f;

    [Header("Master")]
    [SerializeField] private Slider _sliderMaster;
    [SerializeField] private TextMeshProUGUI _labelMaster;

    [Header("Music")]
    [SerializeField] private Slider _sliderMusic;
    [SerializeField] private TextMeshProUGUI _labelMusic;

    [Header("SFX")]
    [SerializeField] private Slider _sliderSFX;
    [SerializeField] private TextMeshProUGUI _labelSFX;

    [Header("Voice")]
    [SerializeField] private Slider _sliderVoice;
    [SerializeField] private TextMeshProUGUI _labelVoice;

    [Header("Ambience")]
    [SerializeField] private Slider _sliderAmbience;
    [SerializeField] private TextMeshProUGUI _labelAmbience;

    [Header("Player")]
    [SerializeField] private Slider _sliderPlayer;
    [SerializeField] private TextMeshProUGUI _labelPlayer;

    [Header("Nemesis")]
    [SerializeField] private Slider _sliderNemesis;
    [SerializeField] private TextMeshProUGUI _labelNemesis;

    [Header("UI")]
    [SerializeField] private Slider _sliderUI;
    [SerializeField] private TextMeshProUGUI _labelUI;

    public event Action<float> OnMasterChanged;
    public event Action<float> OnMusicChanged;
    public event Action<float> OnSFXChanged;
    public event Action<float> OnVoiceChanged;
    public event Action<float> OnAmbienceChanged;
    public event Action<float> OnPlayerChanged;
    public event Action<float> OnNemesisChanged;
    public event Action<float> OnUIChanged;

    private void Awake()
    {
        Wire(_sliderMaster,   _labelMaster,   v => OnMasterChanged?.Invoke(v));
        Wire(_sliderMusic,    _labelMusic,    v => OnMusicChanged?.Invoke(v));
        Wire(_sliderSFX,      _labelSFX,      v => OnSFXChanged?.Invoke(v));
        Wire(_sliderVoice,    _labelVoice,    v => OnVoiceChanged?.Invoke(v));
        Wire(_sliderAmbience, _labelAmbience, v => OnAmbienceChanged?.Invoke(v));
        Wire(_sliderPlayer,   _labelPlayer,   v => OnPlayerChanged?.Invoke(v));
        Wire(_sliderNemesis,  _labelNemesis,  v => OnNemesisChanged?.Invoke(v));
        Wire(_sliderUI,       _labelUI,       v => OnUIChanged?.Invoke(v));
    }

    private void OnDestroy()
    {
        Unwire(_sliderMaster);
        Unwire(_sliderMusic);
        Unwire(_sliderSFX);
        Unwire(_sliderVoice);
        Unwire(_sliderAmbience);
        Unwire(_sliderPlayer);
        Unwire(_sliderNemesis);
        Unwire(_sliderUI);
    }

    public void Populate(SettingsModel model)
    {
        if (model == null) return;

        SetSilent(_sliderMaster,   model.MasterVolume);
        SetSilent(_sliderMusic,    model.MusicVolume);
        SetSilent(_sliderSFX,      model.SFXVolume);
        SetSilent(_sliderVoice,    model.VoiceVolume);
        SetSilent(_sliderAmbience, model.AmbienceVolume);
        SetSilent(_sliderPlayer,   model.PlayerVolume);
        SetSilent(_sliderNemesis,  model.NemesisVolume);
        SetSilent(_sliderUI,       model.UIVolume);

        SetLabel(_labelMaster,   model.MasterVolume);
        SetLabel(_labelMusic,    model.MusicVolume);
        SetLabel(_labelSFX,      model.SFXVolume);
        SetLabel(_labelVoice,    model.VoiceVolume);
        SetLabel(_labelAmbience, model.AmbienceVolume);
        SetLabel(_labelPlayer,   model.PlayerVolume);
        SetLabel(_labelNemesis,  model.NemesisVolume);
        SetLabel(_labelUI,       model.UIVolume);
    }

    // El slider trabaja 0..100; al modelo se reenvía normalizado a 0..1.
    private static void Wire(Slider slider, TextMeshProUGUI label, Action<float> forward01)
    {
        if (slider == null) return;
        slider.onValueChanged.AddListener(v =>
        {
            SetLabelRaw(label, v);
            forward01(v / SLIDER_MAX);
        });
    }

    private static void Unwire(Slider slider)
    {
        if (slider == null) return;
        slider.onValueChanged.RemoveAllListeners();
    }

    private static void SetSilent(Slider slider, float value01)
    {
        if (slider != null) slider.SetValueWithoutNotify(value01 * SLIDER_MAX);
    }

    private static void SetLabel(TextMeshProUGUI label, float value01)
    {
        if (label != null) label.text = Mathf.RoundToInt(value01 * SLIDER_MAX).ToString();
    }

    private static void SetLabelRaw(TextMeshProUGUI label, float value0to100)
    {
        if (label != null) label.text = Mathf.RoundToInt(value0to100).ToString();
    }
}
