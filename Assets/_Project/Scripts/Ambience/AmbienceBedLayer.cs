using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Ambience Layer 1 — the constant factory bed: the air movement, building resonance, old
/// ventilation and faint electrical hum that say "large empty space" underneath everything else.
///
/// Structurally a descendant of NemesisAudio, which is this project's working reference for owned
/// looping AudioSources with a crossfade. Three deliberate differences:
///
///   1. spatialBlend = 0 (2D). A room tone is not positional — it must not attenuate or pan as the
///      player walks, because it represents the space the player is standing in.
///   2. N crossfade slots instead of one, so a profile can run two loops of COPRIME LENGTH at
///      once. Two clips of 37 s and 53 s have a composite period of 1961 s — about 33 minutes — so
///      the loop becomes undetectable. This matters because a loop is recognised by its overall
///      contour long before its transients, so removing recognisable events from the clip (which
///      the audio spec correctly asks for) is necessary but not sufficient on its own.
///   3. Time.unscaledDeltaTime for the crossfade. See the Update comment.
///
/// Preserved from NemesisAudio, including the reasoning:
///   - Sources are created in code as children, never dragged in.
///   - Roles are swapped rather than reassigned, so no source is ever cut abruptly.
///   - A slot with no clip crossfades to silence instead of stopping.
///   - The same-clip guard. Here it is load-bearing rather than an optimisation: crossfading a clip
///     against ITSELF comb-filters audibly, and two adjacent zones that share a bed while differing
///     only in event density is the common case.
///
/// pitch is never touched on any source in this layer. It scales playback rate, which changes the
/// loop period, and there is no reason to want that on a bed.
/// </summary>
public class AmbienceBedLayer : MonoBehaviour
{
    /// <summary>
    /// One crossfade channel: two sources that swap roles on every profile change. Runtime state
    /// only — deliberately not [Serializable], because nothing here should be authored by hand.
    /// </summary>
    private class Slot
    {
        public AudioSource A;
        public AudioSource B;

        public AudioSource Active;   // fading in, or already at full level
        public AudioSource Fading;   // on its way out

        public float ActiveTarget;
        public float FadingStart;
        public float Progress = 1f;
        public float Duration = 1f;
    }

    [Header("Slots")]
    [Tooltip("How many bed loops can play at once. 2 is the recommended default so profiles can " +
             "use a coprime pair — see the class summary. Each slot costs two AudioSources.")]
    [SerializeField, Range(1, 3)] private int bedSlots = 2;

    [Header("Fades")]
    [Tooltip("Seconds for the bed to rise from silence when the level starts. A bed that snaps in " +
             "at full level is the first thing a player notices about the audio.")]
    [SerializeField, Min(0f)] private float startupFadeSeconds = 4f;

    private Slot[] slots;
    private AmbienceBusTable buses;

    private float volumeScale = 1f;      // driven by SetVolumeScale, for the future tension layer
    private float masterEnvelope;        // startup fade in, FadeOut on the way out
    private float masterTarget = 1f;
    private float masterRate;            // per second

    private bool warnedAboutSlotOverflow;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// The target level of the first bed slot, before the master envelope and volume scale. The
    /// drift layer reads this so the pink noise can be expressed as a fraction of the bed and
    /// follow it automatically whenever the bed is rebalanced.
    /// </summary>
    public float ReferenceVolume => slots != null && slots.Length > 0 ? slots[0].ActiveTarget : 0f;

    /// <summary>
    /// Routes the sources to the mixer. Called by AmbienceController in Start, after AudioManager
    /// has built its own groups in Awake.
    /// </summary>
    public void Initialize(AmbienceBusTable busTable)
    {
        buses = busTable;

        AudioMixerGroup group = buses != null ? buses.For(EAmbienceBus.Bed) : null;

        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].A.outputAudioMixerGroup = group;
            slots[i].B.outputAudioMixerGroup = group;
        }

        // Rise from silence rather than starting at full level.
        masterEnvelope = 0f;
        masterTarget = 1f;
        masterRate = startupFadeSeconds > 0.01f ? 1f / startupFadeSeconds : 1000f;
    }

    /// <summary>
    /// Crossfades every slot to the tracks and levels of <paramref name="profile"/>. A null profile
    /// fades the whole layer to silence without stopping anything, which is the correct behaviour
    /// for an area that is meant to be dead quiet.
    /// </summary>
    public void ApplyProfile(SO_AmbienceProfile profile)
    {
        float duration = profile != null ? profile.TransitionDuration : 1f;

        for (int i = 0; i < slots.Length; i++)
        {
            AudioClip clip = profile != null ? profile.BedTrackForSlot(i) : null;
            float volume = profile != null && clip != null ? profile.BedVolumeForSlot(i) : 0f;

            ApplyToSlot(slots[i], clip, volume, duration);
        }

        WarnIfProfileHasMoreTracksThanSlots(profile);
    }

    /// <summary>
    /// Scales every bed level. Reserved for the enemy-proximity layer, which will duck the bed as
    /// the Nemesis closes in. 1 is the neutral value and nothing calls this yet.
    /// </summary>
    public void SetVolumeScale(float scale) => volumeScale = Mathf.Max(0f, scale);

    /// <summary>Fades the whole layer out over <paramref name="seconds"/>, e.g. for a chase-music takeover.</summary>
    public void FadeOut(float seconds)
    {
        masterTarget = 0f;
        masterRate = seconds > 0.01f ? 1f / seconds : 1000f;
    }

    /// <summary>Fades the whole layer back in over <paramref name="seconds"/>.</summary>
    public void FadeIn(float seconds)
    {
        masterTarget = 1f;
        masterRate = seconds > 0.01f ? 1f / seconds : 1000f;
    }

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        slots = new Slot[Mathf.Max(1, bedSlots)];

        for (int i = 0; i < slots.Length; i++)
        {
            Slot slot = new Slot
            {
                A = CreateSource($"BedSlot{i}_A"),
                B = CreateSource($"BedSlot{i}_B")
            };

            slot.Active = slot.A;
            slot.Fading = slot.B;

            slots[i] = slot;
        }
    }

    private void Update()
    {
        // unscaledDeltaTime, not deltaTime, and this is the one place in the ambience system where
        // that choice is load-bearing in both directions:
        //   - A crossfade frozen at 40 % because a modal UI is open is a bug the player hears.
        //   - The level can finish loading while timeScale is still 0 during a UI transition, and
        //     with scaled time the startup fade would never begin at all.
        // The rule across the whole system: volume envelopes must always complete, timers must not.
        float delta = Time.unscaledDeltaTime;

        masterEnvelope = Mathf.MoveTowards(masterEnvelope, masterTarget, masterRate * delta);

        for (int i = 0; i < slots.Length; i++)
            UpdateSlot(slots[i], delta);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private AudioSource CreateSource(string sourceName)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0f;
        source.spatialBlend = 0f;        // 2D — the bed is the room the player is in, not a point in it.
        source.dopplerLevel = 0f;
        source.ignoreListenerPause = false;

        return source;
    }

    private void ApplyToSlot(Slot slot, AudioClip clip, float volume, float duration)
    {
        // Same clip already running: retarget the level and let it keep playing. Restarting it
        // would jump back to sample zero, and crossfading it against itself would comb-filter.
        if (clip != null && slot.Active.clip == clip && slot.Active.isPlaying)
        {
            slot.ActiveTarget = volume;
            return;
        }

        // Swap roles: whatever was audible is now the one on its way out.
        (slot.Active, slot.Fading) = (slot.Fading, slot.Active);

        slot.FadingStart = slot.Fading.volume;

        slot.Active.clip = clip;
        slot.Active.volume = 0f;
        slot.ActiveTarget = clip != null ? volume : 0f;

        if (clip != null) slot.Active.Play();

        slot.Progress = 0f;
        slot.Duration = duration;
    }

    private void UpdateSlot(Slot slot, float delta)
    {
        if (slot.Progress < 1f)
        {
            slot.Progress = slot.Duration > 0f
                ? Mathf.Clamp01(slot.Progress + delta / slot.Duration)
                : 1f;

            if (slot.Progress >= 1f && slot.Fading.isPlaying)
            {
                slot.Fading.Stop();
                slot.Fading.clip = null;
            }
        }

        float envelope = masterEnvelope * volumeScale;

        slot.Active.volume = slot.ActiveTarget * slot.Progress * envelope;

        if (slot.Fading.isPlaying)
            slot.Fading.volume = slot.FadingStart * (1f - slot.Progress) * envelope;
    }

    private void WarnIfProfileHasMoreTracksThanSlots(SO_AmbienceProfile profile)
    {
        if (warnedAboutSlotOverflow || profile == null || profile.BedTracks == null) return;
        if (profile.BedTracks.Length <= slots.Length) return;

        warnedAboutSlotOverflow = true;
        Debug.LogWarning($"[{nameof(AmbienceBedLayer)}] Profile '{profile.DisplayName}' has " +
                         $"{profile.BedTracks.Length} bed tracks but this layer only has " +
                         $"{slots.Length} slots, so the extra tracks are silent. Raise bedSlots " +
                         $"or trim the profile.", this);
    }
}
