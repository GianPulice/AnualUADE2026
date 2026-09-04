using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Owned 3D AudioSources for the ambient one-shots.
///
/// WHY NOT AudioManager's SFX POOL
/// PlayInternal cannot do this job, for four reasons, and the third is fatal:
///
///   1. It hardcodes src.volume = 1f, so the per-playback volume jitter is impossible.
///   2. It never sets pitch, so the pitch jitter is impossible — and worse, a pooled source keeps
///      whatever pitch the previous caller left on it, so playback is not even deterministic.
///   3. It never sets rolloffMode or min/maxDistance. A fresh AudioSource is Logarithmic with
///      minDistance = 1, which means gain = 1/d:
///
///        distance   Logarithmic min=1 (SFX pool)   Linear min=6 max=45 (here)
///          8 m            0.125                          0.949
///         20 m            0.050                          0.641
///         30 m            0.033                          0.385
///
///      The SFX pool spreads the spec's 8-30 m range across 30 dB, so anything past about 15 m is
///      gone and the whole placement system is pointless. Linear with explicit distances spreads it
///      across 7.8 dB, which is a usable range. Only an owned pool can set that.
///   4. Sharing the 20-source pool means a distant clang gets cut mid-clip by a pickup sound, and
///      there is no handle to stop or fade anything.
///
/// CONSEQUENCE FOR THE DESIGNER: with linear rolloff, the bed has to sit low enough that an event
/// 25 m away is clearly audible. Start the bed's first slot around 0.30-0.35.
///
/// OCCLUSION IS MUFFLING, NOT JUST ATTENUATION. Each pooled source carries an AudioLowPassFilter,
/// enabled only for occluded playbacks. Attenuation alone sounds FAR; muffling sounds BLOCKED, and
/// the spec wants the second one.
/// </summary>
public class AmbienceEventPool : MonoBehaviour
{
    private class PooledSource
    {
        public AudioSource Source;
        public AudioLowPassFilter Lowpass;
    }

    [Header("Pool")]
    [Tooltip("Number of one-shots that can overlap. Six is generous for a system that fires about " +
             "twice a minute — it exists to cover a long clip still ringing when the next one lands.")]
    [SerializeField, Range(1, 16)] private int poolSize = 6;

    [Header("Per-playback variation")]
    [Tooltip("Volume varies by +/- this fraction. The spec asks for 10-15%; enough that two " +
             "playbacks of the same clip are never identical, little enough that it never reads as " +
             "a different sound.")]
    [SerializeField, Range(0f, 0.5f)] private float volumeJitter = 0.13f;

    [Tooltip("Pitch varies by +/- this fraction. Applies ONLY here: pitch scales playback rate, " +
             "which is harmless on a one-shot but would change the loop period of a bed or drone.")]
    [SerializeField, Range(0f, 0.5f)] private float pitchJitter = 0.04f;

    [Header("Occlusion")]
    [Tooltip("Volume multiplier for a sound coming through a surface. Deliberately conservative to " +
             "start with — it is far easier to add muffling by ear than to discover a whole " +
             "category of event has been inaudible for a month.")]
    [SerializeField, Range(0f, 1f)] private float occludedVolumeMultiplier = 0.6f;

    [Tooltip("Lowpass cutoff applied to occluded playbacks. This is what actually sells 'behind a " +
             "wall'. Lower is more muffled.")]
    [SerializeField, Range(200f, 8000f)] private float occludedLowpassCutoff = 1400f;

    private PooledSource[] pool;
    private AmbienceBusTable buses;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Creates and routes the sources. Called by AmbienceController in Start.</summary>
    public void Initialize(AmbienceBusTable busTable)
    {
        buses = busTable;

        AudioMixerGroup group = buses != null ? buses.For(EAmbienceBus.Events) : null;

        pool = new PooledSource[Mathf.Max(1, poolSize)];

        for (int i = 0; i < pool.Length; i++)
        {
            pool[i] = CreatePooledSource(i);
            pool[i].Source.outputAudioMixerGroup = group;
        }
    }

    /// <summary>
    /// Plays <paramref name="entry"/> at <paramref name="placement"/>.
    ///
    /// Returns false when every source is busy. The event is then SKIPPED — the pool is deliberately
    /// never grown (that is how a project ends up with forty voices) and never steals a playing
    /// source (ambience is not important enough to cut something else off).
    /// </summary>
    public bool TryPlay(SO_AmbienceEventBank.Entry entry, AmbiencePlacement placement,
                        out float volume, out float pitch)
    {
        volume = 0f;
        pitch = 1f;

        if (entry == null || entry.clip == null) return false;
        if (pool == null) return false;

        PooledSource pooled = GetFreeSource();
        if (pooled == null) return false;

        volume = entry.volume
                 * Random.Range(1f - volumeJitter, 1f + volumeJitter)
                 * (placement.Occluded ? occludedVolumeMultiplier : 1f);

        pitch = Random.Range(1f - pitchJitter, 1f + pitchJitter);

        AudioSource source = pooled.Source;

        source.clip = entry.clip;
        source.volume = volume;
        source.pitch = pitch;
        source.spread = entry.spread;
        source.minDistance = entry.minDistance;
        source.maxDistance = Mathf.Max(entry.minDistance + 0.1f, entry.maxDistance);
        source.transform.position = placement.Position;

        // Set every playback, not just when occluded: the source may have been left muffled by the
        // previous event that used it.
        pooled.Lowpass.enabled = placement.Occluded;
        pooled.Lowpass.cutoffFrequency = occludedLowpassCutoff;

        source.Play();
        return true;
    }

    /// <summary>Stops every one-shot immediately. For a hard cut into chase audio.</summary>
    public void StopAll()
    {
        if (pool == null) return;

        for (int i = 0; i < pool.Length; i++)
            if (pool[i].Source.isPlaying) pool[i].Source.Stop();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private PooledSource CreatePooledSource(int index)
    {
        GameObject go = new GameObject($"AmbienceOneShot_{index}");
        go.transform.SetParent(transform, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;                        // Fully 3D.
        source.rolloffMode = AudioRolloffMode.Linear;    // See the class summary — this is the point.
        source.ignoreListenerPause = false;

        // The emitters are static but the LISTENER moves. Doppler on a distant clang while walking
        // is an audible pitch artefact, and there is no world in which it is wanted here.
        source.dopplerLevel = 0f;

        AudioLowPassFilter lowpass = go.AddComponent<AudioLowPassFilter>();
        lowpass.cutoffFrequency = occludedLowpassCutoff;
        lowpass.enabled = false;

        return new PooledSource { Source = source, Lowpass = lowpass };
    }

    private PooledSource GetFreeSource()
    {
        for (int i = 0; i < pool.Length; i++)
            if (!pool[i].Source.isPlaying) return pool[i];

        return null;
    }
}
