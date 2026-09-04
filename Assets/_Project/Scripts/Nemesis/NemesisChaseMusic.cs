using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Chase music: fades a dedicated 2D AudioSource in while the Nemesis is hunting the player, and
/// back out once it stops, ducking the ambience bed/drift layers underneath for the takeover.
///
/// Wires up two gaps the audio system already left open for exactly this:
///   - NemesisAudio: "reactive music is not wired... the state signal it would need is already
///     public as NemesisEvents.OnStateChanged."
///   - AmbienceController: "the hooks it would drive are SetTensionScalars and FadeOutAll below,
///     which currently have no callers." Only FadeOutAll/FadeInAll are used here — those two are
///     documented as "e.g. for a chase-music takeover", which is exactly this. SetTensionScalars
///     drives the separate, still-unbuilt proximity tension driver and is left untouched.
///
/// Driven by OnChaseStarted/OnChaseEnded rather than OnStateChanged directly: that is the same
/// "being hunted" set NemesisStateManager already computes for the red vignette (Chasing, plus
/// Catch while the capture is unresolved), so the music does not duck out for the one beat the
/// Nemesis is grabbing the player.
///
/// Structurally a simplified NemesisAudio/AmbienceBedLayer: one owned AudioSource instead of a
/// crossfade pair, because there is only ever one clip and two states (audible / silent) rather
/// than a swap between several.
///
/// Setup:
///   1. Empty GameObject in the gameplay scene (e.g. next to AmbienceController).
///   2. Add this component.
///   3. Drag the chase music clip in. outputGroup and ambienceController are optional — left
///      empty they resolve themselves at runtime from AudioManager and the scene.
/// </summary>
public class NemesisChaseMusic : MonoBehaviour
{
    [Header("Clip")]
    [Tooltip("Chase music track. Loops for as long as the Nemesis is hunting the player.")]
    [SerializeField] private AudioClip chaseMusicClip;

    [Tooltip("Volume the track fades up to.")]
    [SerializeField, Range(0f, 1f)] private float maxVolume = 1f;

    [Header("Fade")]
    [Tooltip("Seconds for a full fade in or out, in either direction.")]
    [SerializeField, Min(0.05f)] private float fadeDuration = 2f;

    [Header("Routing")]
    [Tooltip("Leave EMPTY. It then resolves to AudioManager's Music bus, which is where this " +
             "belongs: it is a score cue, not a sound the monster makes, and the Music slider is " +
             "what the player reaches for to turn it down. Ambience follows that same slider.\n\n" +
             "It was wired to the Nemesis bus in the level once, which made it ride the SFX slider " +
             "instead and ignore the music setting entirely.")]
    [SerializeField] private AudioMixerGroup outputGroup;

    [Header("Ambience takeover")]
    [Tooltip("Optional. Ducks the ambience bed/drift layers out while the chase music plays and " +
             "back in once it ends. Auto-found in the scene if left empty.")]
    [SerializeField] private AmbienceController ambienceController;

    private AudioSource source;
    private float currentVolume;
    private float volumeTarget;

    private void Awake()
    {
        source = CreateSource();

        // Awake/OnDestroy and not OnEnable/OnDisable, per docs/CLAUDE.md: a static delegate
        // outlives the GameObject's enabled state. OnChaseStarted/Ended are a PAIR with no
        // catch-up, so an enabled-scoped subscription can miss one half and leave this stuck
        // playing chase music through a walk, or silent through a chase.
        NemesisEvents.OnChaseStarted += HandleChaseStarted;
        NemesisEvents.OnChaseEnded += HandleChaseEnded;
    }

    private void OnDestroy()
    {
        NemesisEvents.OnChaseStarted -= HandleChaseStarted;
        NemesisEvents.OnChaseEnded -= HandleChaseEnded;
    }

    private void Start()
    {
        // Resolved in Start and not Awake: AudioManager sets its groups up in its own Awake.
        if (outputGroup == null && AudioManager.Exists) outputGroup = AudioManager.Instance.MusicGroup;
        source.outputAudioMixerGroup = outputGroup;
    }

    // Stays enabled-scoped: this is a scene lookup, not a subscription, and re-running it on
    // re-enable is how it recovers a controller that was unloaded with its scene.
    private void OnEnable() => AcquireAmbienceController();

    private AudioSource CreateSource()
    {
        GameObject go = new GameObject("ChaseMusic");
        go.transform.SetParent(transform, false);

        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.volume = 0f;
        src.spatialBlend = 0f; // 2D: music, not a positional sound.

        return src;
    }

    private void AcquireAmbienceController()
    {
        if (ambienceController != null) return;

        // FindAnyObjectByType and not FindFirstObjectByType: the latter is deprecated because it
        // orders by instance ID, and that ordering is worth nothing here — a scene only ever has
        // one ambience controller, so "any" is "the one".
        ambienceController = FindAnyObjectByType<AmbienceController>();

        if (ambienceController == null)
            Debug.LogWarning($"[{nameof(NemesisChaseMusic)}] No {nameof(AmbienceController)} found " +
                             "in the scene. Chase music will still play, but the ambience will not " +
                             "duck out for it.", this);
    }

    private void HandleChaseStarted()
    {
        if (chaseMusicClip == null) return;

        volumeTarget = maxVolume;

        if (source.clip != chaseMusicClip) source.clip = chaseMusicClip;
        if (!source.isPlaying) source.Play();

        if (ambienceController != null) ambienceController.FadeOutAll(fadeDuration);
    }

    private void HandleChaseEnded()
    {
        volumeTarget = 0f;

        if (ambienceController != null) ambienceController.FadeInAll(fadeDuration);
    }

    private void Update()
    {
        // Unscaled, not scaled: this fade is paired with AmbienceController's own (also unscaled)
        // takeover fade, and a pause mid-transition must not leave one of the two frozen while the
        // other keeps moving — that is what would turn a paused chase into dead silence.
        float rate = fadeDuration > 0f ? maxVolume / fadeDuration : maxVolume;
        currentVolume = Mathf.MoveTowards(currentVolume, volumeTarget, rate * Time.unscaledDeltaTime);
        source.volume = currentVolume;

        if (currentVolume <= 0f && volumeTarget <= 0f && source.isPlaying)
        {
            source.Stop();
            source.clip = null;
        }
    }
}
