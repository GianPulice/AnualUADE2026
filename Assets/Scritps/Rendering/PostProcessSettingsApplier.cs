using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Applies Brightness / Contrast / Gamma from PlayerPrefs to a URP Global Volume.
/// The Volume must have Color Adjustments (postExposure + contrast) and
/// Lift Gamma Gain (gamma) overrides. Enable the corresponding properties in the profile.
///
/// Mappings:
///   - Brightness 0..1 → postExposure -2..+2 EV
///   - Contrast   0..1 → contrast    -50..+50
///   - Gamma      0..1 → gamma.w     0.7..1.3
///
/// Volume.profile returns an auto-instanced runtime instance (it does NOT modify the .asset).
/// Place this component on the same GameObject as the global Volume (S5a).
/// </summary>
[RequireComponent(typeof(Volume))]
public class PostProcessSettingsApplier : MonoBehaviour
{
    private const string KEY_BRIGHTNESS = "Settings_Brightness";
    private const string KEY_CONTRAST   = "Settings_Contrast";
    private const string KEY_GAMMA      = "Settings_Gamma";

    private const float DEFAULT_BRIGHTNESS = 0.7f;
    private const float DEFAULT_CONTRAST   = 0.55f;
    private const float DEFAULT_GAMMA      = 0.5f;

    private ColorAdjustments _color;
    private LiftGammaGain    _lgg;

    private void Awake()
    {
        var profile = GetComponent<Volume>().profile;
        profile.TryGet(out _color);
        profile.TryGet(out _lgg);

        SettingsModel.OnSettingsApplied += Apply;
        Apply();
    }

    private void OnDestroy() => SettingsModel.OnSettingsApplied -= Apply;

    private void Apply()
    {
        float b = PlayerPrefs.GetFloat(KEY_BRIGHTNESS, DEFAULT_BRIGHTNESS);
        float c = PlayerPrefs.GetFloat(KEY_CONTRAST,   DEFAULT_CONTRAST);
        float g = PlayerPrefs.GetFloat(KEY_GAMMA,      DEFAULT_GAMMA);

        if (_color != null)
        {
            _color.postExposure.value = Mathf.Lerp(-2f, 2f, b);
            _color.contrast.value     = Mathf.Lerp(-50f, 50f, c);
        }
        if (_lgg != null)
        {
            _lgg.gamma.value = new Vector4(1f, 1f, 1f, Mathf.Lerp(0.7f, 1.3f, g));
        }
    }
}
