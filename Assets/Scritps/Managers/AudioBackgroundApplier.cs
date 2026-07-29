using UnityEngine;

/// <summary>
/// Applies the "Audio in background" preference by reading Settings_AudioInBackground.
/// Handles Application.runInBackground (the game keeps running without focus) and
/// AudioListener.pause (audio stays audible without focus). Both go together on Standalone;
/// on Mobile, runInBackground has no effect.
///
/// Place this component on a persistent GameObject (e.g. the AudioManager's).
/// </summary>
public class AudioBackgroundApplier : MonoBehaviour
{
    private const string KEY_AUDIO_BG = "Settings_AudioInBackground";

    private void Awake()
    {
        SettingsModel.OnSettingsApplied += Apply;
        Apply();
    }

    private void OnDestroy() => SettingsModel.OnSettingsApplied -= Apply;

    private static bool Wanted() => PlayerPrefs.GetInt(KEY_AUDIO_BG, 0) != 0;

    private void Apply() => Application.runInBackground = Wanted();

    private void OnApplicationFocus(bool hasFocus) => AudioListener.pause = !hasFocus && !Wanted();
}
