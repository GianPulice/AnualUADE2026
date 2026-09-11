using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Applies the player's "CRT Scanlines" and "PSX Dithering" settings to a UI PSX material — the UI
/// counterpart of <see cref="PS1EffectApplier"/>, which does the same for the world's full-screen
/// pass. Without it, switching those options off would clean up the world and leave the inventory
/// still striped and dithered.
///
/// Works on a runtime copy of the Graphic's material, so the asset on disk never changes during Play
/// — the dirty-in-git problem PS1EffectApplier's note describes.
/// </summary>
[RequireComponent(typeof(Graphic))]
[AddComponentMenu("WIRED/UI/UI PSX Settings Applier")]
public class UIPSXSettingsApplier : MonoBehaviour
{
    // Same keys as SettingsModel and PS1EffectApplier.
    private const string KEY_CRT    = "Settings_CRTScanlines";
    private const string KEY_DITHER = "Settings_PSXDithering";

    private static readonly int PropScanlines = Shader.PropertyToID("_EnableScanlines");
    private static readonly int PropDither    = Shader.PropertyToID("_EnableDither");

    private Material instance;

    private void Awake()
    {
        Graphic graphic = GetComponent<Graphic>();
        if (graphic.material == graphic.defaultMaterial)
        {
            Debug.LogWarning($"[UIPSXSettingsApplier] '{name}' has no PSX material assigned.", this);
            enabled = false;
            return;
        }

        instance = new Material(graphic.material) { name = graphic.material.name + " (Runtime)" };
        graphic.material = instance;

        SettingsModel.OnSettingsApplied += Apply;
        Apply();
    }

    private void OnDestroy()
    {
        SettingsModel.OnSettingsApplied -= Apply;
        if (instance != null) Destroy(instance);
    }

    private void Apply()
    {
        instance.SetFloat(PropScanlines, PlayerPrefs.GetInt(KEY_CRT,    1) != 0 ? 1f : 0f);
        instance.SetFloat(PropDither,    PlayerPrefs.GetInt(KEY_DITHER, 1) != 0 ? 1f : 0f);
    }
}
