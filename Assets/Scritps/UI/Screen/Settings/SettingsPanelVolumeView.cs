using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de Volume del Settings. Tres sliders: Master, Music y SFX.
///
/// SFX no es el bus SFX a secas — agrupa todo lo que no es musica (SFX, Ambience,
/// Player, Nemesis, UI y Voice). Ver AudioManager.SetGameplaySfxBundle.
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

    [Header("SFX (agrupa Ambience, Player, Nemesis, UI y Voice)")]
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
