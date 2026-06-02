using UnityEngine;

/// <summary>
/// Aplica resolución, modo de ventana, FPS limit y VSync desde PlayerPrefs al engine.
/// Los índices mapean en el mismo orden que los arrays serializados en SettingsPanelScreenView:
///   - Resolución: 1920x1080, 2560x1440, 3840x2160, 1366x768, 1280x720
///   - Modo:       ExclusiveFullScreen, Windowed, FullScreenWindow
///   - FPS:        -1 (sin limite), 30, 60, 120, 144
///
/// IMPORTANTE: Screen.SetResolution es no-op en Play Mode del Editor; testear en build standalone.
/// VSync > 0 ignora targetFrameRate.
///
/// Colocar este componente en un GameObject persistente (ej. el del AudioManager o un bootstrap).
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

        // Guard anti-flicker: solo cambiar resolución/modo si difiere del estado actual.
        if (Screen.width != w || Screen.height != h || Screen.fullScreenMode != mode)
            Screen.SetResolution(w, h, mode);

        QualitySettings.vSyncCount = vsync ? 1 : 0;
        Application.targetFrameRate = FpsTargets[f];
    }
}
