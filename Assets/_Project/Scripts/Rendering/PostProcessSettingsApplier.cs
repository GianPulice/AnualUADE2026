using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies Brightness / Contrast / Gamma from PlayerPrefs to a URP Global Volume.
/// The Volume must have Color Adjustments (postExposure + contrast) and
/// Lift Gamma Gain (gamma) overrides. Enable the corresponding properties in the profile.
///
/// Mappings (0.5 on every slider is neutral: the picture as authored):
///   - Brightness 0..1 → postExposure -1.5..+1.5 EV
///   - Contrast   0..1 → contrast     -40..+40
///   - Gamma      0..1 → gamma.w      -0.4..+0.4 (w is an offset: 0 is neutral)
///
/// Two things the sliders need besides the volume, both handled here:
///   - **Post-processing on the camera.** Volume effects only render on a camera with
///     renderPostProcessing on, and the level's Main Camera ships with it off; without this the
///     three sliders did nothing at all. Turned on for the main camera of every loaded scene.
///   - **Live preview.** While the sliders move, <see cref="SettingsModel.OnPicturePreview"/>
///     carries the unsaved values; Apply persists them and Back/Reset sends the old ones back.
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

    private const float DEFAULT_BRIGHTNESS = 0.5f;
    private const float DEFAULT_CONTRAST   = 0.5f;
    private const float DEFAULT_GAMMA      = 0.5f;

    private ColorAdjustments _color;
    private LiftGammaGain    _lgg;

    private void Awake()
    {
        var profile = GetComponent<Volume>().profile;
        profile.TryGet(out _color);
        profile.TryGet(out _lgg);

        SettingsModel.OnSettingsApplied += Apply;
        SettingsModel.OnPicturePreview  += Set;
        SceneManager.sceneLoaded        += HandleSceneLoaded;
        Apply();
        EnablePostProcessingOnMainCamera();
    }

    private void OnDestroy()
    {
        SettingsModel.OnSettingsApplied -= Apply;
        SettingsModel.OnPicturePreview  -= Set;
        SceneManager.sceneLoaded        -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => EnablePostProcessingOnMainCamera();

    private void Apply()
    {
        Set(PlayerPrefs.GetFloat(KEY_BRIGHTNESS, DEFAULT_BRIGHTNESS),
            PlayerPrefs.GetFloat(KEY_CONTRAST,   DEFAULT_CONTRAST),
            PlayerPrefs.GetFloat(KEY_GAMMA,      DEFAULT_GAMMA));
        EnablePostProcessingOnMainCamera();
    }

    private void Set(float brightness, float contrast, float gamma)
    {
        if (_color != null)
        {
            _color.postExposure.value = Mathf.Lerp(-1.5f, 1.5f, brightness);
            _color.contrast.value     = Mathf.Lerp(-40f, 40f, contrast);
        }
        if (_lgg != null)
        {
            _lgg.gamma.value = new Vector4(1f, 1f, 1f, Mathf.Lerp(-0.4f, 0.4f, gamma));
        }
    }

    private static void EnablePostProcessingOnMainCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null && !data.renderPostProcessing) data.renderPostProcessing = true;
    }
}
