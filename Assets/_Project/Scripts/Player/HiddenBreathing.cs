using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The player's breathing, and ONLY while hidden.
///
/// Deliberately scoped that way. The audio spec (§2, §2.1) asks for breathing in three places —
/// idle, under tension, and inside a hiding spot — but only the third one is a mechanic; the other
/// two are a loop that runs for the whole game and that the player stops hearing within a minute.
/// Keeping it to the hiding spot is what makes it land: the world goes quiet behind the hiding
/// lowpass, and the only thing left in the mix is the player breathing while the monster walks past.
///
/// IT MAKES NO NOISE FOR THE NEMESIS. In this project noise is a sphere the movement states own
/// (PlayerStateManager.AudioEmitingZone), not an event, and a second writer to its radius is how a
/// player ends up permanently loud for the rest of the run — see "Noise is a sphere, not an event"
/// in docs/CLAUDE.md. Wiring breathing into detection is a change to the hiding system, and it
/// belongs there, alongside the hold-breath input, not here.
///
/// WHAT DRIVES IT: <c>PlayerStateManager.IsHidden</c> — a real <see cref="HidingSpot"/> since
/// phase 1, or the F10 console's debug toggle in a scene with no spot built into it. The only
/// thing the hiding system added here is the per-type VOLUME multiplier below: a steel locker next
/// to your face is louder from the inside (spec's <c>closetBreathingMultiplier</c>), which is a mix
/// decision and therefore belongs to the component that owns the mix.
///
/// Setup: drop it on the player root, assign the loop. Everything else has a working default.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Audio/Hidden Breathing")]
public class HiddenBreathing : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Auto-resolved from the parents if left empty.")]
    [SerializeField] private PlayerStateManager player;

    [Header("Clips")]
    [Tooltip("The loop that plays while hidden and healthy.")]
    [SerializeField] private AudioClip hiddenLoop;

    [Tooltip("Optional. Replaces the loop above once M2 has exploded — the ragged, tense breathing " +
             "the module spec gives the chest penalty. Left empty, the normal loop is used " +
             "regardless of penalties.")]
    [SerializeField] private AudioClip hiddenLoopChestPenalty;

    [Header("Source")]
    [Tooltip("The AudioSource to breathe through. The player prefab already carries an unused one " +
             "on its root — wire that here rather than leaving a second, identical component on " +
             "the same object. Left empty, a child source is created at runtime instead.\n\n" +
             "Whichever it is, this component configures it: clip, loop, 3D falloff, bus and " +
             "volume are all overwritten in Awake, so nothing authored on it in the inspector " +
             "survives.")]
    [SerializeField] private AudioSource source;

    [Header("Mix")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

    [Tooltip("Seconds for a full fade, both directions. A hard cut on entering a hiding spot is " +
             "the one thing that reliably breaks the illusion.")]
    [SerializeField, Min(0.05f)] private float fadeDuration = 0.45f;

    [Tooltip("Fully 3D, like every other diegetic sound in the game. This is a third-person " +
             "camera: the AudioListener rides it a few metres behind the character, so the breath " +
             "is something you hear coming from the body you are looking at, not something inside " +
             "your own head. With the Linear rolloff below it is still at almost full volume at " +
             "that distance.")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;

    [Tooltip("Kept comfortably past the camera's orbit distance (~3.4 m) so the breath does not " +
             "start attenuating before it has even reached the listener.")]
    [SerializeField, Min(0f)] private float minDistance = 3f;

    [Tooltip("Linear, not the Unity default of 500 logarithmic. This is meant to be a close, " +
             "intimate sound the Nemesis's own footsteps can walk over — not something audible " +
             "from the far end of the level.")]
    [SerializeField, Min(0.1f)] private float maxDistance = 15f;

    [Tooltip("Optional. Left empty it resolves to AudioManager's Player bus at runtime.")]
    [SerializeField] private AudioMixerGroup outputGroupOverride;

    // ── Runtime ─────────────────────────────────────────────────────────────

    private float currentVolume;
    private bool warnedNoClip;

    private void Awake()
    {
        if (player == null) player = GetComponentInParent<PlayerStateManager>();

        if (source == null)
        {
            var go = new GameObject("Breathing");
            go.transform.SetParent(transform, false);
            source = go.AddComponent<AudioSource>();
        }

        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0f;
        source.spatialBlend = spatialBlend;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = Mathf.Max(maxDistance, minDistance + 0.1f);
    }

    private void Update()
    {
        bool shouldBreathe = player != null && player.IsHidden && !player.IsDisabled;

        if (shouldBreathe && !source.isPlaying) StartLoop();

        float target = shouldBreathe ? volume * SpotVolumeMultiplier() : 0f;

        // Unscaled: this fade is paired with the pause and the hiding lowpass, and a fade frozen
        // half way through by a modal is audible as a stuck drone. Same convention as
        // NemesisChaseMusic and the ambience layers.
        float rate = fadeDuration > 0f ? volume / fadeDuration : volume;
        currentVolume = Mathf.MoveTowards(currentVolume, target, rate * Time.unscaledDeltaTime);
        source.volume = currentVolume;

        if (!shouldBreathe && currentVolume <= 0f && source.isPlaying) source.Stop();
    }

    /// <summary>
    /// How much louder (or quieter) the breath is inside THIS kind of spot. 1 when hidden with no
    /// spot at all, which is what the F10 debug toggle does. Read every frame rather than cached
    /// on entry so the fade follows a spot that releases the player mid-breath.
    /// </summary>
    private float SpotVolumeMultiplier()
    {
        HidingSpot spot = player != null ? player.CurrentHidingSpot : null;
        if (spot == null || spot.Data == null) return 1f;
        return spot.Data.BreathingVolumeMultiplierFor(spot.Type);
    }

    private void StartLoop()
    {
        // Chosen per episode rather than per frame: swapping the clip mid-breath would restart it.
        AudioClip clip = hiddenLoop;
        if (player != null && player.ChestPenaltyActive && hiddenLoopChestPenalty != null)
            clip = hiddenLoopChestPenalty;

        if (clip == null)
        {
            if (!warnedNoClip)
            {
                warnedNoClip = true;
                Debug.LogWarning($"[{nameof(HiddenBreathing)}] '{name}' has no breathing clip, so " +
                                 "hiding is silent. Assign one.", this);
            }
            return;
        }

        EnsureRouting();

        source.clip = clip;
        // Random start offset so re-entering a hiding spot does not replay the same inhale every
        // time, which is what turns a loop into a recording.
        source.time = clip.length > 0.5f ? Random.Range(0f, clip.length - 0.25f) : 0f;
        source.Play();
    }

    private void EnsureRouting()
    {
        if (source.outputAudioMixerGroup != null) return;

        if (outputGroupOverride != null)
        {
            source.outputAudioMixerGroup = outputGroupOverride;
            return;
        }

        AudioManager manager = AudioManager.Instance;
        if (manager != null) source.outputAudioMixerGroup = manager.PlayerGroup;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maxDistance <= minDistance) maxDistance = minDistance + 0.1f;

        // Only live-update the source in play mode. Writing to it in edit mode would mark the
        // player prefab dirty every time one of these fields is nudged, since the source it is
        // pointed at is a component ON that prefab and not a runtime child.
        if (!Application.isPlaying || source == null) return;
        source.spatialBlend = spatialBlend;
        source.minDistance  = minDistance;
        source.maxDistance  = maxDistance;
    }
#endif
}
