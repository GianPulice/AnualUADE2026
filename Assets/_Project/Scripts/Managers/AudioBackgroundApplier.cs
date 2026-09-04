using UnityEngine;

/// <summary>
/// Applies the "Audio in background" preference by reading Settings_AudioInBackground.
/// Handles Application.runInBackground (the game keeps running without focus) and
/// AudioListener.pause (audio stays audible without focus). Both go together on Standalone;
/// on Mobile, runInBackground has no effect.
///
/// It is also the SINGLE owner of AudioListener.pause. That flag is driven by two independent
/// reasons for the world to go quiet — window focus (this preference) and the pause menu
/// (PauseManager.OnPauseStateChanged) — and both are folded into one write in
/// <see cref="RefreshListenerPause"/> so they cannot clobber each other (an alt-tab back into a
/// paused game must not un-mute the ambience).
///
/// Sounds that have to keep playing while the game is paused set AudioSource.ignoreListenerPause
/// on themselves — UI clicks and voice do it via AudioManager.PlayUI / PlayVoice, the inventory
/// audio-log does it in ItemDetailView. Everything else (ambience bed/drift, chase music, world
/// one-shots) stops on pause, which is the intent.
///
/// Place this component on a persistent GameObject (e.g. the AudioManager's).
/// </summary>
public class AudioBackgroundApplier : MonoBehaviour
{
    private const string KEY_AUDIO_BG = "Settings_AudioInBackground";

    private bool hasFocus = true;
    private bool gamePaused;

    private void Awake()
    {
        SettingsModel.OnSettingsApplied += Apply;

        // Awake/OnDestroy and not OnEnable/OnDisable, per docs/CLAUDE.md: OnPauseStateChanged is a
        // static delegate that outlives this GameObject's enabled state, and it has no catch-up —
        // an enabled-scoped subscription can miss the edge that unpauses the audio and leave the
        // world muted.
        PauseManager.OnPauseStateChanged += HandlePauseStateChanged;

        Apply();
        RefreshListenerPause();
    }

    private void OnDestroy()
    {
        SettingsModel.OnSettingsApplied -= Apply;
        PauseManager.OnPauseStateChanged -= HandlePauseStateChanged;
    }

    private static bool Wanted() => PlayerPrefs.GetInt(KEY_AUDIO_BG, 0) != 0;

    private void Apply() => Application.runInBackground = Wanted();

    private void HandlePauseStateChanged(PauseState state)
    {
        gamePaused = state == PauseState.Paused;
        RefreshListenerPause();
    }

    private void OnApplicationFocus(bool focus)
    {
        hasFocus = focus;
        RefreshListenerPause();
    }

    /// <summary>
    /// Recomputes AudioListener.pause from every reason the game audio should be silent: the pause
    /// menu is open, or the window lost focus and the player did not ask for audio in the
    /// background.
    /// </summary>
    private void RefreshListenerPause()
    {
        AudioListener.pause = gamePaused || (!hasFocus && !Wanted());
    }
}
