using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Per-state looping audio for the Nemesis, with crossfades and wall occlusion.
///
/// Two AudioSources are kept and swapped: on a state change the incoming loop fades in while the
/// outgoing one fades out over <see cref="crossfadeDuration"/>. Nothing is ever cut abruptly —
/// a state with no clip configured crossfades to silence rather than stopping.
///
/// Occlusion attenuates, it does not mute: a wall between the Nemesis and the player drops the
/// volume to <see cref="occludedVolumeMultiplier"/>, and that multiplier itself is eased so
/// walking past a doorway does not click.
///
/// KNOWN GAP (Tier 2.6 point 4): reactive music is not wired. AudioManager exposes PlayMusic(id)
/// but has no per-state hooks and no crossfade on the music source, so driving it from here
/// would mean rewriting the music path. Left for whoever owns the music system — the state
/// signal it would need is already public as NemesisEvents.OnStateChanged.
/// </summary>
public class NemesisAudio : MonoBehaviour
{
    [Serializable]
    private class StateLoop
    {
        public NemesisStateManager.ENemesisState state;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    [Header("Loops")]
    [Tooltip("One entry per state. States with no entry, or with an empty clip, fade to silence.")]
    [SerializeField] private StateLoop[] stateLoops = Array.Empty<StateLoop>();

    [Tooltip("Seconds to crossfade between two state loops. Spec range: 0.3 - 0.5.")]
    [SerializeField, Range(0.05f, 2f)] private float crossfadeDuration = 0.4f;

    [Header("Routing")]
    [Tooltip("Optional. If empty it is taken from AudioManager's Nemesis bus at runtime.")]
    [SerializeField] private AudioMixerGroup outputGroup;

    [Header("3D falloff")]
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 25f;

    [Header("Occlusion")]
    [SerializeField] private bool occlusionEnabled = true;

    [Tooltip("Optional. If empty it is taken from the NemesisStateManager in the parents. " +
             "Reuses its obstacleMask raycast instead of a second one here.")]
    [SerializeField] private FieldOfListening fieldOfListening;

    [Tooltip("Volume multiplier while a wall stands between the Nemesis and the player. " +
             "Attenuation, never a cut: 0 would silence it completely.")]
    [SerializeField, Range(0f, 1f)] private float occludedVolumeMultiplier = 0.35f;

    [Tooltip("How fast the occlusion multiplier eases towards its target, per second.")]
    [SerializeField, Min(0.1f)] private float occlusionEaseSpeed = 3f;

    private AudioSource sourceA;
    private AudioSource sourceB;

    private AudioSource activeSource;   // fading in / already at full
    private AudioSource fadingSource;   // fading out

    private float activeTargetVolume;
    private float fadingStartVolume;
    private float crossfadeProgress = 1f;

    private float occlusionMultiplier = 1f;

    private void Awake()
    {
        if (fieldOfListening == null)
        {
            NemesisStateManager manager = GetComponentInParent<NemesisStateManager>();
            if (manager != null) fieldOfListening = manager.FieldOfListening;
        }

        sourceA = CreateSource("NemesisLoopA");
        sourceB = CreateSource("NemesisLoopB");
        activeSource = sourceA;
        fadingSource = sourceB;
    }

    private void Start()
    {
        // Resolved in Start and not Awake: AudioManager sets its groups up in its own Awake.
        if (outputGroup == null && AudioManager.Exists) outputGroup = AudioManager.Instance.NemesisGroup;

        sourceA.outputAudioMixerGroup = outputGroup;
        sourceB.outputAudioMixerGroup = outputGroup;
    }

    private void OnEnable()  => NemesisEvents.OnStateChanged += HandleStateChanged;
    private void OnDisable() => NemesisEvents.OnStateChanged -= HandleStateChanged;

    private AudioSource CreateSource(string sourceName)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0f;
        source.spatialBlend = 1f;                       // Fully 3D.
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;

        return source;
    }

    private void HandleStateChanged(NemesisStateManager.ENemesisState state)
    {
        AudioClip clip = null;
        float volume = 0f;

        foreach (StateLoop loop in stateLoops)
        {
            if (loop == null || loop.state != state) continue;
            clip = loop.clip;
            volume = loop.volume;
            break;
        }

        // Same clip already playing (e.g. Chasing -> Catch sharing a loop): let it run rather
        // than restarting it from sample zero, which would be audible.
        if (clip != null && activeSource.clip == clip && activeSource.isPlaying)
        {
            activeTargetVolume = volume;
            return;
        }

        StartCrossfade(clip, volume);
    }

    private void StartCrossfade(AudioClip clip, float volume)
    {
        // Swap roles: whatever was audible is now the one on its way out.
        (activeSource, fadingSource) = (fadingSource, activeSource);

        fadingStartVolume = fadingSource.volume;

        activeSource.clip = clip;
        activeSource.volume = 0f;
        activeTargetVolume = clip != null ? volume : 0f;

        if (clip != null) activeSource.Play();

        crossfadeProgress = 0f;
    }

    private void Update()
    {
        UpdateOcclusion();
        UpdateCrossfade();
    }

    private void UpdateOcclusion()
    {
        float target = 1f;

        if (occlusionEnabled && fieldOfListening != null && PlayerRegistry.HasPlayer)
        {
            Vector3 listener = PlayerRegistry.CurrentTransform.position;
            if (fieldOfListening.IsOccludedByWall(listener, transform.position))
                target = occludedVolumeMultiplier;
        }

        // Eased, never snapped: a hard jump on crossing a doorway clicks.
        occlusionMultiplier = Mathf.MoveTowards(occlusionMultiplier, target,
                                                occlusionEaseSpeed * Time.deltaTime);
    }

    private void UpdateCrossfade()
    {
        if (crossfadeProgress < 1f)
        {
            crossfadeProgress = crossfadeDuration > 0f
                ? Mathf.Clamp01(crossfadeProgress + Time.deltaTime / crossfadeDuration)
                : 1f;

            if (crossfadeProgress >= 1f && fadingSource.isPlaying)
            {
                fadingSource.Stop();
                fadingSource.clip = null;
            }
        }

        activeSource.volume = activeTargetVolume * crossfadeProgress * occlusionMultiplier;

        if (fadingSource.isPlaying)
            fadingSource.volume = fadingStartVolume * (1f - crossfadeProgress) * occlusionMultiplier;
    }
}
