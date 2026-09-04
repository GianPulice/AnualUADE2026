using System;
using UnityEngine;

/// <summary>
/// The one global switch for the low-frequency ambient layers, written by
/// AmbienceComfortApplier and read by AmbienceDriftLayer.
///
/// A static flag rather than an exposed AudioMixer parameter, deliberately: every mixer parameter
/// under Ambience is at the mercy of AudioManager.SetGameplaySfxBundle, which rewrites AmbienceVolume
/// whenever the player touches the SFX slider. Routing the comfort toggle through the drift layer's
/// existing volume envelope instead keeps it out of that entire problem class, and gets a smooth
/// ~3 second ramp for free rather than a click.
///
/// WHY THIS EXISTS
/// Sustained very-low-frequency content is a documented nausea and migraine trigger for a subset of
/// players. The 17 Hz and 32 Hz drones are inaudible-to-barely-audible by design, which makes them
/// exactly the kind of thing a player cannot identify and turn off for themselves — they would only
/// know that something about the game makes them feel unwell. Hence one toggle covering both sub
/// tracks rather than a per-track control: simpler to present and safer to get wrong.
/// </summary>
public static class AmbienceComfort
{
    /// <summary>PlayerPrefs key, following the Settings_* convention of SettingsModel.</summary>
    public const string PrefsKey = "Settings_LowFreqAmbience";

    /// <summary>Enabled by default — the layer ships on, and a player who needs it off opts out.</summary>
    public const bool DefaultEnabled = true;

    private static bool lowFrequencyEnabled = DefaultEnabled;

    /// <summary>
    /// Static state must be reset explicitly: domain reload is disabled in this project, so without
    /// this a value set in one Play session survives into the next one. Same guard as
    /// NemesisEvents.ResetStatics.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        lowFrequencyEnabled = DefaultEnabled;
        OnChanged = null;
    }

    /// <summary>Raised whenever the setting changes. AmbienceDriftLayer does not need it — it polls — but a future HUD hint might.</summary>
    public static event Action<bool> OnChanged;

    /// <summary>
    /// False when the player has turned the low-frequency ambience off. The drift layer ramps every
    /// comfortGated track to silence while this is false.
    /// </summary>
    public static bool LowFrequencyEnabled
    {
        get => lowFrequencyEnabled;
        set
        {
            if (lowFrequencyEnabled == value) return;
            lowFrequencyEnabled = value;
            OnChanged?.Invoke(value);
        }
    }

    /// <summary>Reads the persisted preference. Used by the applier at startup and on Apply.</summary>
    public static bool ReadFromPrefs() =>
        PlayerPrefs.GetInt(PrefsKey, DefaultEnabled ? 1 : 0) != 0;
}
