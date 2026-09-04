using UnityEngine;

/// <summary>
/// Applies the "low-frequency ambience" comfort preference by reading Settings_LowFreqAmbience and
/// pushing it into <see cref="AmbienceComfort"/>, which AmbienceDriftLayer ramps towards.
///
/// Structurally identical to AudioBackgroundApplier — same subscription shape, same PlayerPrefs
/// read, same placement. Put this component on a persistent GameObject (the AudioManager's, in the
/// Data scene) so the preference is live before any gameplay scene loads.
///
/// The preference has no Settings UI yet. It reads with a sensible default, so the system works
/// without one — the same state Settings_VHSGlitch is in today (read by GlitchController, not
/// exposed in the Options panel). AmbienceDriftLayer carries a context menu for toggling it during
/// development.
/// </summary>
public class AmbienceComfortApplier : MonoBehaviour
{
    private void Awake()
    {
        SettingsModel.OnSettingsApplied += Apply;
        Apply();
    }

    private void OnDestroy() => SettingsModel.OnSettingsApplied -= Apply;

    private void Apply() => AmbienceComfort.LowFrequencyEnabled = AmbienceComfort.ReadFromPrefs();
}
