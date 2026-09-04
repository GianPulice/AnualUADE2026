using System.Threading;
using Cysharp.Threading.Tasks;
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

    [Tooltip("Seconds the world takes to fade out when the pause menu opens. Only the fade OUT is " +
             "ramped: unpausing restores the audio instantly, because that is the player's own " +
             "action and a fade-in there makes the menu feel slow to leave.\n\n" +
             "0 restores the old behaviour, an instant cut.")]
    [SerializeField, Min(0f)] private float pauseFadeDuration = 0.15f;

    /// <summary>Cancels the in-flight fade. Recreated per fade rather than reused, because a
    /// CancellationTokenSource cannot be reset once cancelled.</summary>
    private CancellationTokenSource fadeCts;
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
        CancelFade();
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
    ///
    /// <b>Silencing is faded, restoring is instant.</b> Cutting the world off in a single frame
    /// reads as a bug — the ear notices a hard gate far more than a 150 ms slope — so the pause
    /// path ducks first and only sets the flag once the duck is down. Coming back needs no ramp:
    /// unpausing is the player's own action and they expect the world at once, and a fade-in there
    /// would just make the menu feel sluggish to leave.
    ///
    /// Focus loss is NOT faded. The window is already gone by the time it is known, so a fade
    /// would run against a listener nobody can hear.
    /// </summary>
    private void RefreshListenerPause()
    {
        bool shouldPause = gamePaused || (!hasFocus && !Wanted());

        CancelFade();

        if (!shouldPause)
        {
            AudioListener.pause = false;
            SetDuck(1f);
            return;
        }

        if (!gamePaused || pauseFadeDuration <= 0f)
        {
            SetDuck(1f);              // nothing to ramp down to; the flag does the silencing
            AudioListener.pause = true;
            return;
        }

        fadeCts = new CancellationTokenSource();
        FadeOutThenPauseAsync(fadeCts.Token).Forget();
    }

    /// <summary>
    /// Ramps the duck to zero, then hands over to AudioListener.pause, which is what actually
    /// freezes playback so a sound does not advance behind the menu.
    ///
    /// UniTask and not a coroutine, per docs/CLAUDE.md § Async — and it earns it here: the fade has
    /// to survive Time.timeScale = 0, which is exactly what DelayType.UnscaledDeltaTime is for, and
    /// it has to be cancellable the instant the player unpauses mid-fade.
    ///
    /// The token also comes from GetCancellationTokenOnDestroy via CancelFade, so a scene unload
    /// mid-fade cannot leave a task writing into a destroyed AudioManager.
    /// </summary>
    private async UniTaskVoid FadeOutThenPauseAsync(CancellationToken token)
    {
        float elapsed = 0f;

        while (elapsed < pauseFadeDuration)
        {
            // Cancelled means the player unpaused before the fade finished. Returning without
            // touching the duck is correct: RefreshListenerPause has already restored it to 1.
            if (token.IsCancellationRequested) return;

            elapsed += Time.unscaledDeltaTime;
            SetDuck(1f - Mathf.Clamp01(elapsed / pauseFadeDuration));

            await UniTask.Yield(PlayerLoopTiming.Update, token, cancelImmediately: true)
                         .SuppressCancellationThrow();
        }

        if (token.IsCancellationRequested) return;

        SetDuck(0f);
        AudioListener.pause = true;
    }

    /// <summary>Stops any fade in flight. Safe to call when none is running.</summary>
    private void CancelFade()
    {
        if (fadeCts == null) return;

        fadeCts.Cancel();
        fadeCts.Dispose();
        fadeCts = null;
    }

    private static void SetDuck(float value)
    {
        if (AudioManager.Exists) AudioManager.Instance.PauseDuck = value;
    }
}
