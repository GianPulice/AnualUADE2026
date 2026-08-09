using UnityEngine;

/// <summary>
/// Applies resolution, window mode, FPS limit and VSync from PlayerPrefs to the engine.
/// The indices map in the same order as the serialized arrays in SettingsPanelScreenView:
///   - Resolution: 1920x1080, 2560x1440, 3840x2160, 1366x768, 1280x720
///   - Mode:       ExclusiveFullScreen, Windowed, FullScreenWindow
///   - FPS:        -1 (no limit), 30, 60, 120, 144
///
/// IMPORTANT: Screen.SetResolution is a no-op in the Editor's Play Mode; test in a standalone build.
/// VSync > 0 ignores targetFrameRate.
///
/// Place this component on a persistent GameObject (e.g. the AudioManager's, or a bootstrap one).
/// </summary>
public class ScreenSettingsApplier : MonoBehaviour
{
    private const string KEY_RESOLUTION  = "Settings_ResolutionIndex";
    private const string KEY_WINDOW_MODE = "Settings_WindowMode";
    private const string KEY_FPS_LIMIT   = "Settings_FPSLimit";
    private const string KEY_VSYNC       = "Settings_VSync";

    private static readonly (int w, int h)[] Resolutions =
    {
        (1920, 1080),
        (2560, 1440),
        (3840, 2160),
        (1366, 768),
        (1280, 720),
    };

    private static readonly FullScreenMode[] Modes =
    {
        FullScreenMode.ExclusiveFullScreen,
        FullScreenMode.Windowed,
        FullScreenMode.FullScreenWindow,
    };

    private static readonly int[] FpsTargets = { -1, 30, 60, 120, 144 };

    private void Awake()
    {
        SettingsModel.OnSettingsApplied += Apply;
        Apply();
    }

    private void OnDestroy() => SettingsModel.OnSettingsApplied -= Apply;

    private void Apply()
    {
        int r = Mathf.Clamp(PlayerPrefs.GetInt(KEY_RESOLUTION,  0), 0, Resolutions.Length - 1);
        int m = Mathf.Clamp(PlayerPrefs.GetInt(KEY_WINDOW_MODE, 0), 0, Modes.Length - 1);
        int f = Mathf.Clamp(PlayerPrefs.GetInt(KEY_FPS_LIMIT,   0), 0, FpsTargets.Length - 1);
        bool vsync = PlayerPrefs.GetInt(KEY_VSYNC, 1) != 0;

        var (w, h) = Resolutions[r];
        var mode = Modes[m];

        // Anti-flicker guard: only change resolution/mode if it differs from the current state.
        if (Screen.width != w || Screen.height != h || Screen.fullScreenMode != mode)
            Screen.SetResolution(w, h, mode);

        QualitySettings.vSyncCount = vsync ? 1 : 0;
        Application.targetFrameRate = FpsTargets[f];
    }
}
