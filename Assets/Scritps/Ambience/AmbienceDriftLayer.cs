using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Ambience Layers 3 and 4 — the subconscious floor underneath everything: the pink-noise texture
/// and the low-frequency drones.
///
/// The two spec layers collapse into one component because they are the same behaviour: a looping
/// 2D source whose volume slowly wanders towards a new random target. Splitting them would be three
/// near-identical scripts.
///
/// WHAT EACH TRACK IS FOR
///   Pink noise — a texture the player must never consciously identify. Its real job is to mask the
///     loop seam of the bed and glue the 3D one-shots into the same acoustic space. The acceptance
///     test is negative: mute it and listen for the bed's loop becoming more obvious. If muting it
///     changes nothing, it is too quiet to be worth a voice.
///   17 Hz — room pressure. Below the roughly 20 Hz hearing floor and reproduced by almost no
///     consumer hardware, which is why the baked clip carries a quiet 34 Hz partial: without it the
///     track is literally silent on most devices. See AmbienceToneBaker.
///   32 Hz drone — this is the one that actually carries the perceptible weight, because it is the
///     lowest range laptop speakers and earbuds have any chance with.
///
/// DRIFT CLIPS ARE NEVER RESTARTED
/// A zone change only retargets the profile scales. Restarting a noise loop is a click if the seam
/// is not perfect, restarting a drone is an audible thump, and neither buys anything — the player is
/// still in the same building.
/// </summary>
public class AmbienceDriftLayer : MonoBehaviour
{
    [Serializable]
    private class DriftTrack
    {
        [Tooltip("Editor readability only.")]
        public string label = "Pink noise";

        public AudioClip clip;

        public EAmbienceBus bus = EAmbienceBus.Texture;

        [Tooltip("When relativeToBed is on, this is a FRACTION of the bed's first-slot volume, so " +
                 "the track follows a rebalanced bed on its own — from the next drift target " +
                 "onwards, so up to driftIntervalRange seconds later. Otherwise it is an absolute " +
                 "source volume.\n\n" +
                 "The audio spec asks for pink noise at 3-8% of the bed; ship it at the TOP of that " +
                 "range. At the bottom, with the Texture bus also attenuated, it lands around " +
                 "-31 dB relative to the bed and does nothing at all — including nothing " +
                 "subconscious.")]
        [Range(0f, 1f)] public float level = 0.08f;

        public bool relativeToBed = true;

        [Header("Drift")]
        [Tooltip("The volume wanders inside level * [x, y].")]
        public Vector2 driftMultiplierRange = new Vector2(0.55f, 1.45f);

        [Tooltip("Seconds between picking a new drift target.")]
        public Vector2 driftIntervalRange = new Vector2(30f, 90f);

        [Tooltip("Seconds spent TRAVELLING to the new target. Keep this close to the interval so " +
                 "the movement is never perceptible as a change — the spec is explicit that there " +
                 "must be no sudden shifts.")]
        public Vector2 driftTravelRange = new Vector2(25f, 70f);

        [Header("Comfort")]
        [Tooltip("Silenced when the player turns off low-frequency ambience. Enable on every Sub " +
                 "track; leave off for the pink noise, which is not a comfort concern.")]
        public bool comfortGated = false;

        // ── Runtime state ────────────────────────────────────────────────────
        [NonSerialized] public AudioSource Source;
        [NonSerialized] public float Current;
        [NonSerialized] public float Target;
        [NonSerialized] public float Rate;
        [NonSerialized] public float NextPickCountdown;
    }

    [Header("Tracks")]
    [Tooltip("One entry per constant background texture. The baker produces exactly the three " +
             "clips this expects: PinkNoise_20s (or BrownNoise_20s), Sub_17Hz_60s and Sub_32Hz_60s.")]
    [SerializeField] private DriftTrack[] driftTracks = Array.Empty<DriftTrack>();

    [Header("Fades")]
    [Tooltip("Seconds for the whole layer to rise from silence at level start. Longer than the " +
             "bed's on purpose: a sub layer coming up quickly is a thump.")]
    [SerializeField, Min(0f)] private float startupFadeSeconds = 6f;

    [Tooltip("Seconds for a comfort-gated track to ramp to silence when the player turns " +
             "low-frequency ambience off, and back up when they turn it on.")]
    [SerializeField, Min(0.1f)] private float comfortRampSeconds = 3f;

    private AmbienceBedLayer bedLayer;
    private AmbienceBusTable buses;

    private float textureScale = 1f;
    private float subScale = 1f;

    private float masterEnvelope;
    private float masterTarget = 1f;
    private float masterRate;

    private float comfortEnvelope = 1f;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and routes the sources and starts every loop. Called by AmbienceController in Start.
    /// <paramref name="bed"/> may be null; tracks with relativeToBed set then fall back to treating
    /// their level as absolute.
    /// </summary>
    public void Initialize(AmbienceBusTable busTable, AmbienceBedLayer bed)
    {
        buses = busTable;
        bedLayer = bed;

        comfortEnvelope = AmbienceComfort.LowFrequencyEnabled ? 1f : 0f;

        for (int i = 0; i < driftTracks.Length; i++)
        {
            DriftTrack track = driftTracks[i];
            if (track == null) continue;

            track.Source = CreateSource($"Drift_{SanitizeName(track.label, i)}");
            track.Source.outputAudioMixerGroup = buses != null ? buses.For(track.bus) : null;
            track.Source.clip = track.clip;

            track.Current = 0f;

            // The first target is picked on the first Update, not here. AmbienceController calls
            // Initialize on every layer BEFORE applying the profile, so at this moment the bed layer
            // still reports a reference volume of 0 and a relativeToBed track would latch onto the
            // wrong level for its first drift cycle — up to 90 seconds.
            track.NextPickCountdown = 0f;

            if (track.clip != null) track.Source.Play();
            else
            {
                Debug.LogWarning($"[{nameof(AmbienceDriftLayer)}] Track '{track.label}' has no " +
                                 "clip and will be silent. Run Tools/Audio/Bake Ambience Texture " +
                                 "Clips and assign the generated files.", this);
            }
        }

        masterEnvelope = 0f;
        masterTarget = 1f;
        masterRate = startupFadeSeconds > 0.01f ? 1f / startupFadeSeconds : 1000f;
    }

    /// <summary>Retargets the per-area scales. Called on every profile change; never restarts a clip.</summary>
    public void ApplyProfile(SO_AmbienceProfile profile)
    {
        textureScale = profile != null ? profile.TextureScale : 1f;
        subScale = profile != null ? profile.SubScale : 1f;
    }

    /// <summary>
    /// Scales the low-frequency tracks on top of the profile's own subScale. Reserved for the
    /// enemy-proximity layer; 1 is neutral and nothing calls this yet.
    /// </summary>
    public void SetSubScale(float scale) => subScale = Mathf.Max(0f, scale);

    /// <summary>Fades the whole layer out over <paramref name="seconds"/>.</summary>
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

    private void Update()
    {
        // unscaledDeltaTime for the same reason as the bed layer: a volume envelope frozen halfway
        // because a modal is open is audible, and the level can load while timeScale is still 0.
        float delta = Time.unscaledDeltaTime;

        masterEnvelope = Mathf.MoveTowards(masterEnvelope, masterTarget, masterRate * delta);

        float comfortTarget = AmbienceComfort.LowFrequencyEnabled ? 1f : 0f;
        comfortEnvelope = Mathf.MoveTowards(comfortEnvelope, comfortTarget, delta / comfortRampSeconds);

        for (int i = 0; i < driftTracks.Length; i++)
            UpdateTrack(driftTracks[i], delta);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void UpdateTrack(DriftTrack track, float delta)
    {
        if (track == null || track.Source == null) return;

        track.NextPickCountdown -= delta;
        if (track.NextPickCountdown <= 0f) PickNewTarget(track);

        // MoveTowards with a precomputed rate, not Lerp towards a target: a Lerp asymptotes and can
        // never be said to have arrived in the requested number of seconds, which makes the
        // driftTravelRange field a lie.
        track.Current = Mathf.MoveTowards(track.Current, track.Target, track.Rate * delta);

        float busScale = track.bus == EAmbienceBus.Sub ? subScale : textureScale;
        float gate = track.comfortGated ? comfortEnvelope : 1f;

        track.Source.volume = track.Current * masterEnvelope * busScale * gate;
    }

    private void PickNewTarget(DriftTrack track)
    {
        float baseLevel = EffectiveLevel(track);

        float multiplier = UnityEngine.Random.Range(track.driftMultiplierRange.x,
                                                   track.driftMultiplierRange.y);
        track.Target = baseLevel * multiplier;

        float travel = Mathf.Max(0.1f, UnityEngine.Random.Range(track.driftTravelRange.x,
                                                               track.driftTravelRange.y));
        track.Rate = Mathf.Abs(track.Target - track.Current) / travel;

        track.NextPickCountdown = UnityEngine.Random.Range(track.driftIntervalRange.x,
                                                          track.driftIntervalRange.y);
    }

    private float EffectiveLevel(DriftTrack track)
    {
        if (!track.relativeToBed) return track.level;

        float bedReference = bedLayer != null ? bedLayer.ReferenceVolume : 0f;

        // If the bed is silent — no default profile, or an area with no bed — a relative track would
        // collapse to zero and never recover. Treating the level as absolute in that case keeps the
        // texture present, which is the more useful failure.
        return bedReference > 0.0001f ? bedReference * track.level : track.level;
    }

    private AudioSource CreateSource(string sourceName)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0f;
        source.spatialBlend = 0f;        // 2D — these are the space itself, not objects in it.
        source.dopplerLevel = 0f;
        source.pitch = 1f;               // Never varied: it would change the loop period and, on the
                                         // cycle-aligned sub clips, destroy the seamless seam.
        source.ignoreListenerPause = false;

        return source;
    }

    private static string SanitizeName(string label, int index)
    {
        if (string.IsNullOrWhiteSpace(label)) return index.ToString();
        return label.Replace(' ', '_');
    }

#if UNITY_EDITOR
    /// <summary>
    /// The sub layers cannot be verified by ear — that is the whole point of them. Use the
    /// Ambience/Sub VU meter in the AudioMixer window as ground truth instead: it shows level
    /// regardless of what the speakers can reproduce. This context menu exists because the comfort
    /// toggle has no Settings UI yet.
    /// </summary>
    [ContextMenu("Toggle Low-Freq Ambience")]
    private void DebugToggleLowFrequency()
    {
        AmbienceComfort.LowFrequencyEnabled = !AmbienceComfort.LowFrequencyEnabled;
        Debug.Log($"[{nameof(AmbienceDriftLayer)}] Low-frequency ambience is now " +
                  $"{(AmbienceComfort.LowFrequencyEnabled ? "ON" : "OFF")}. Watch the Ambience/Sub " +
                  $"meter ramp over {comfortRampSeconds:F1}s.", this);
    }
#endif
}
