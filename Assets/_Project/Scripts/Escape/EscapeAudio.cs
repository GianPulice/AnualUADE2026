using UnityEngine;

/// <summary>
/// The two constant audio layers of the escape (Paso 7): the installation alarm on loop, and a
/// tension music layer that swells as the Nemesis closes in. One job: those two loops.
///
/// What it does NOT do, because the project already has it: the Nemesis's positional footsteps and
/// growls (NemesisAudio, on the Nemesis, 3D through its own bus — which during the escape is
/// gameplay information when the fog is closed) and the chase music (NemesisChaseMusic, which
/// starts on its own when the Nemesis begins to chase). The tension layer here is the score under
/// them, not a replacement.
///
/// The swell follows <see cref="NemesisEvents.OnProximityChanged"/>, the same 0..1 signal the red
/// vignette uses (0 = far, 1 = on top of the player).
///
/// The clips are in <see cref="SO_EscapeSequenceConfig"/>. A missing clip is silent, with a warning.
/// </summary>
public class EscapeAudio : MonoBehaviour
{
    private SO_EscapeSequenceConfig config;
    private AudioSource alarm;
    private AudioSource tension;

    private bool running;
    private float proximity;
    private float tensionVolume;
    private float tensionVelocity;

    public bool IsRunning => running;

    private void Awake() => NemesisEvents.OnProximityChanged += HandleProximity;

    private void OnDestroy() => NemesisEvents.OnProximityChanged -= HandleProximity;

    private void HandleProximity(float t) => proximity = Mathf.Clamp01(t);

    /// <summary>Starts both loops. Idempotent.</summary>
    public void Begin(SO_EscapeSequenceConfig escapeConfig)
    {
        if (running) return;

        config = escapeConfig;
        running = true;
        proximity = 0f;
        tensionVolume = config.TensionMinVolume;
        tensionVelocity = 0f;

        // Ambience bus for the alarm and Music for the score, so each rides the slider a player
        // would reach for. AudioManager builds its groups in its own Awake, hence resolved here.
        AudioManager audio = AudioManager.Exists ? AudioManager.Instance : null;

        alarm = StartLoop("EscapeAlarm", config.AlarmClip, config.AlarmVolume,
                          audio != null ? audio.AmbienceGroup : null);
        tension = StartLoop("EscapeTension", config.TensionClip, tensionVolume,
                            audio != null ? audio.MusicGroup : null);
    }

    /// <summary>Stops both loops.</summary>
    public void Stop()
    {
        running = false;
        if (alarm != null) alarm.Stop();
        if (tension != null) tension.Stop();
    }

    private AudioSource StartLoop(string objectName, AudioClip clip, float volume,
                                  UnityEngine.Audio.AudioMixerGroup group)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f;
        src.outputAudioMixerGroup = group;
        src.volume = volume;

        if (clip == null)
        {
            Debug.LogWarning($"[{nameof(EscapeAudio)}] No clip for '{objectName}' in the escape " +
                             "config: that layer stays silent.", this);
            return src;
        }

        src.clip = clip;
        src.Play();
        return src;
    }

    private void Update()
    {
        if (!running || config == null) return;

        if (alarm != null) alarm.volume = config.AlarmVolume;

        if (tension == null) return;

        float target = Mathf.Lerp(config.TensionMinVolume, config.TensionMaxVolume, proximity);

        // Unscaled: a pause must not freeze the swell halfway and let it jump on resume.
        tensionVolume = Mathf.SmoothDamp(tensionVolume, target, ref tensionVelocity,
                                         config.TensionSmoothing, Mathf.Infinity, Time.unscaledDeltaTime);
        tension.volume = tensionVolume;
    }
}
