using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The sound of the cabin travelling: one clip for a trip up, another for a trip down, each with a
/// stretch of it that repeats until the cabin arrives.
///
/// Why it is not <c>AudioManager.PlaySFX(id, position)</c>: that plays at a fixed point and the
/// cabin moves, so the sound would stay at the landing. This owns AudioSources that ride along
/// with the cabin.
///
/// Why the loop is a stretch and not the whole clip: a lift needs a start, a steady hum and a stop,
/// and one recording carries all three. <see cref="ElevatorTripClips"/> cuts it at <c>loopStart</c>
/// and <c>loopEnd</c> into an intro, a loop that wraps without a click and an outro. The intro and
/// the loop are chained on the DSP clock, so the hum starts on the exact sample the intro ends. On
/// arrival the loop fades out while the outro, the stop, fades in over the same instant, wherever in
/// its cycle the loop is: waiting for the cycle to end would delay the stop by up to a loop length.
///
/// Driven by <see cref="MovingPlatform.OnRideStarted"/> / <see cref="MovingPlatform.OnRideCompleted"/>,
/// so it sounds the same whoever sent the cabin: the player, a call panel or the Nemesis.
/// </summary>
[RequireComponent(typeof(MovingPlatform))]
public class ElevatorTravelSound : MonoBehaviour
{
    [Serializable]
    public class Trip
    {
        [Tooltip("SO_SoundData of the trip. Its clip, volume and 3D range are used. Leave empty for " +
                 "a silent trip. The clip must be readable: Load Type Decompress On Load, not " +
                 "Streaming.")]
        [SoundId] public string soundId = string.Empty;

        [Tooltip("Seconds into the clip where the repeating stretch begins. The loop is closed by " +
                 "blending its end into the clip just before this point, so leave a few tenths of " +
                 "a second of clip ahead of it. Tune by ear, also while playing.")]
        [Min(0f)] public float loopStart = 0.35f;

        [Tooltip("Seconds into the clip where the repeating stretch ends. What comes after this " +
                 "point is the stop, played once on arrival. Not past the clip's length. Not above " +
                 "Loop Start = no loop, the clip just plays once.")]
        [Min(0f)] public float loopEnd = 1.15f;
    }

    [SerializeField] private Trip goingUp = new Trip();
    [SerializeField] private Trip goingDown = new Trip();

    /// <summary>Shorter than this and a loop is a buzz, not a hum.</summary>
    private const float MinLoopLength = 0.05f;

    /// <summary>Head start given to anything scheduled on the DSP clock, which cannot be in the past.</summary>
    private const double StartLead = 0.03;

    /// <summary>Crossfade that closes the loop, see <see cref="ElevatorTripClips"/>.</summary>
    private const float SeamSeconds = 0.08f;

    /// <summary>Crossfade from the loop to the stop on arrival.</summary>
    private const float ReleaseSeconds = 0.08f;

    /// <summary>A trip sent while the previous stop is still ringing fades that stop instead of cutting it.</summary>
    private const float CutFadeSeconds = 0.05f;

    /// <summary>Slack before a finished voice is reused, so its last samples are not cut.</summary>
    private const double ReuseMargin = 0.05;

    /// <summary>One AudioSource playing one piece of a trip.</summary>
    private class Voice
    {
        public AudioSource source;
        public float volume;

        /// <summary>DSP time from which the voice is free. Infinity while a loop runs.</summary>
        public double busyUntil;

        public double fadeStart;

        /// <summary>0 = not fading.</summary>
        public float fadeLength;
    }

    /// <summary>The cut of one trip, and what it was cut from, to notice when it needs redoing.</summary>
    private class Cut
    {
        public AudioClip clip;
        public float loopStart;
        public float loopEnd;
        public ElevatorTripClips clips;
    }

    private MovingPlatform platform;

    /// <summary>Never plays: <see cref="AudioManager.PlayLoop"/> fills it with the sound's clip, bus and 3D range.</summary>
    private AudioSource routing;

    private readonly List<Voice> voices = new List<Voice>();
    private readonly Dictionary<Trip, Cut> cuts = new Dictionary<Trip, Cut>();

    private ElevatorTripClips riding;
    private Voice loopVoice;
    private double loopStartDsp;

    private void Awake()
    {
        platform = GetComponent<MovingPlatform>();
        routing = CreateSource("TravelAudioRouting");
    }

    private void OnEnable()
    {
        platform.OnRideStarted += HandleRideStarted;
        platform.OnRideCompleted += HandleRideCompleted;
    }

    private void OnDisable()
    {
        platform.OnRideStarted -= HandleRideStarted;
        platform.OnRideCompleted -= HandleRideCompleted;

        riding = null;
        loopVoice = null;

        foreach (Voice v in voices)
        {
            v.source.Stop();
            v.busyUntil = 0;
            v.fadeLength = 0f;
        }
    }

    private void OnDestroy()
    {
        foreach (Cut cut in cuts.Values) cut.clips?.Release();
        cuts.Clear();
    }

    private void HandleRideStarted(bool up)
    {
        Trip trip = up ? goingUp : goingDown;
        if (trip == null || string.IsNullOrWhiteSpace(trip.soundId) || !AudioManager.Exists) return;

        FadeOutEverything(CutFadeSeconds);

        // PlayLoop is what routes a source to the bus of the sound's category and copies its volume
        // and 3D range, and it also starts it. Here it only fills the routing source, whose clip
        // and settings the voices then take: muted and stopped in the same frame so nothing leaks.
        routing.clip = null;
        routing.mute = true;
        AudioManager.Instance.PlayLoop(trip.soundId, routing);
        routing.Stop();
        routing.mute = false;

        AudioClip clip = routing.clip;
        if (clip == null) return;

        double t = AudioSettings.dspTime + StartLead;

        ElevatorTripClips cut = GetClips(trip, clip);
        if (cut == null)
        {
            // No loop to build: it is either not asked for or cannot be. The clip plays once.
            Play(clip, t, false);
            return;
        }

        if (cut.Intro != null)
        {
            Play(cut.Intro, t, false);
            t += cut.Intro.samples / (double)cut.Intro.frequency;
        }

        riding = cut;
        loopStartDsp = t;
        loopVoice = Play(cut.Loop, t, true);
    }

    private void HandleRideCompleted()
    {
        if (riding == null || loopVoice == null) return;

        // If the trip was shorter than the intro the loop has not begun. Let it begin, then release.
        double t = Math.Max(AudioSettings.dspTime + StartLead, loopStartDsp);

        if (riding.Outro != null) Play(riding.Outro, t, false);
        Fade(loopVoice, t, ReleaseSeconds);

        riding = null;
        loopVoice = null;
    }

    private void Update()
    {
        double now = AudioSettings.dspTime;

        foreach (Voice v in voices)
        {
            if (v.fadeLength <= 0f) continue;

            float k = Mathf.Clamp01((float)((now - v.fadeStart) / v.fadeLength));
            v.source.volume = v.volume * (1f - k);
            if (k < 1f) continue;

            v.source.Stop();
            v.fadeLength = 0f;
            v.busyUntil = now;
        }
    }

    /// <summary>
    /// The cut of <paramref name="trip"/>, or null when it has no loop. Rebuilt when the clip or the
    /// loop points changed since it was made, so tuning them in the Inspector while playing works.
    /// </summary>
    private ElevatorTripClips GetClips(Trip trip, AudioClip clip)
    {
        if (trip.loopEnd - trip.loopStart < MinLoopLength) return null;

        float end = Mathf.Min(trip.loopEnd, clip.length);

        if (cuts.TryGetValue(trip, out Cut cached))
        {
            if (cached.clip == clip && cached.loopStart == trip.loopStart && cached.loopEnd == end)
                return cached.clips;

            cached.clips?.Release();
            cuts.Remove(trip);
        }

        ElevatorTripClips clips = ElevatorTripClips.Build(clip, trip.loopStart, end, SeamSeconds, ReleaseSeconds);
        if (clips == null)
            Debug.LogWarning($"[ElevatorTravelSound] Cannot loop '{clip.name}' on {name}: its samples " +
                             "are not readable. Set its Load Type to Decompress On Load. It plays once.", this);

        cuts[trip] = new Cut { clip = clip, loopStart = trip.loopStart, loopEnd = end, clips = clips };
        return clips;
    }

    private Voice Play(AudioClip clip, double startDsp, bool loop)
    {
        Voice v = FreeVoice();
        AudioSource s = v.source;

        s.clip = clip;
        s.loop = loop;
        s.outputAudioMixerGroup = routing.outputAudioMixerGroup;
        s.ignoreListenerPause = routing.ignoreListenerPause;
        s.volume = routing.volume;
        s.rolloffMode = routing.rolloffMode;
        s.minDistance = routing.minDistance;
        s.maxDistance = routing.maxDistance;
        s.PlayScheduled(startDsp);

        v.volume = routing.volume;
        v.fadeLength = 0f;
        v.busyUntil = loop ? double.PositiveInfinity
                           : startDsp + clip.samples / (double)clip.frequency + ReuseMargin;
        return v;
    }

    private void Fade(Voice v, double startDsp, float length)
    {
        v.fadeStart = startDsp;
        v.fadeLength = length;
        v.busyUntil = startDsp + length + ReuseMargin;
    }

    private void FadeOutEverything(float length)
    {
        double now = AudioSettings.dspTime;

        foreach (Voice v in voices)
        {
            if (v.busyUntil <= now || v.fadeLength > 0f) continue;
            Fade(v, now, length);
        }

        riding = null;
        loopVoice = null;
    }

    private Voice FreeVoice()
    {
        double now = AudioSettings.dspTime;

        foreach (Voice v in voices)
            if (v.busyUntil <= now) return v;

        var created = new Voice { source = CreateSource("TravelAudio") };
        voices.Add(created);
        return created;
    }

    /// <summary>
    /// A child so the source rides with the cabin, and 3D so distance means something. Doppler is
    /// off: the cabin moves at walking pace and a pitch bend on a hum just sounds broken.
    /// </summary>
    private AudioSource CreateSource(string objectName)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 1f;
        s.dopplerLevel = 0f;
        return s;
    }
}
