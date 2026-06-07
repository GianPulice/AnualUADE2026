using UnityEngine;

/// <summary>
/// Aplica la preferencia "Audio in background" leyendo Settings_AudioInBackground.
/// Maneja Application.runInBackground (juego sigue corriendo sin foco) y
/// AudioListener.pause (audio audible sin foco). Los dos van juntos en Standalone;
/// en Mobile, runInBackground no tiene efecto.
///
/// Colocar este componente en un GameObject persistente (ej. el del AudioManager).
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
