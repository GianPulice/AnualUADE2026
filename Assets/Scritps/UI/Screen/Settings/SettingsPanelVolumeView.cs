using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Volume panel of the Settings screen. Three sliders: Master, Music and SFX.
///
/// SFX is not the plain SFX bus — it groups everything that is not music (SFX, Ambience,
/// Player, Nemesis, UI and Voice). See AudioManager.SetGameplaySfxBundle.
///
/// The sliders are optional — if the prefab only assigns a subset, the rest stays inactive
/// with no warnings.
///
/// The sliders work in a 0..100 range (configure in the Inspector:
/// Min Value = 0, Max Value = 100, Whole Numbers = true). The model and the AudioManager
/// still use 0..1; the conversion happens here, at the boundary.
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

    [Header("SFX (groups Ambience, Player, Nemesis, UI and Voice)")]
    [SerializeField] private Slider _sliderSFX;
    [SerializeField] private TextMeshProUGUI _labelSFX;

    public event Action<float> OnMasterChanged;
    public event Action<float> OnMusicChanged;
    public event Action<float> OnSFXChanged;

    private void Awake()
    {
        Wire(_sliderMaster, _labelMaster, v => OnMasterChanged?.Invoke(v));
        Wire(_sliderMusic,  _labelMusic,  v => OnMusicChanged?.Invoke(v));
        Wire(_sliderSFX,    _labelSFX,    v => OnSFXChanged?.Invoke(v));
    }

    private void OnDestroy()
    {
        Unwire(_sliderMaster);
        Unwire(_sliderMusic);
        Unwire(_sliderSFX);
    }

    public void Populate(SettingsModel model)
    {
        if (model == null) return;

        SetSilent(_sliderMaster, model.MasterVolume);
        SetSilent(_sliderMusic,  model.MusicVolume);
        SetSilent(_sliderSFX,    model.SFXVolume);

        SetLabel(_labelMaster, model.MasterVolume);
        SetLabel(_labelMusic,  model.MusicVolume);
        SetLabel(_labelSFX,    model.SFXVolume);
    }

    // The slider works in 0..100; the value forwarded to the model is normalized to 0..1.
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
