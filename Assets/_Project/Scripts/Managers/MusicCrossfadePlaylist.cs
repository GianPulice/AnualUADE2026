using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Endless music playlist where every transition is a crossfade, including the one that closes a
/// track onto its own start.
///
/// With tracks A and B and <c>playsPerTrack</c> = 2 it plays A A B B A A B B and so on. Each play is
/// the whole clip: the next one starts <c>crossfadeSeconds</c> before the current one ends and the
/// two overlap on an equal-power curve, so neither the loop of a track nor the hand-over to the
/// other one has a seam. A track that is only looped (a plain <c>AudioSource.loop</c>) cuts from its
/// last sample to its first; this blends them instead, which is what hides it.
///
/// Two AudioSources take turns. Timing is read off each source's own playback position
/// (<see cref="AudioSource.time"/>), not off the DSP clock: <c>AudioListener.pause</c> freezes the
/// sources but the DSP clock keeps going, and a schedule built on it would be wrong by the length of
/// the pause. Starting the next play from Update is up to a frame late, which inside a fade of
/// seconds cannot be heard.
///
/// It routes through the Music bus, so the Music slider and the mixer apply as for any other music,
/// and the per-clip trim of each SO_SoundData is its base volume. Created at runtime as a child of
/// the AudioManager (which survives scene changes) so a fade-out can finish after the scene that
/// asked for it is gone. Use <see cref="Create"/>; the first use is the main menu.
/// </summary>
public class MusicCrossfadePlaylist : MonoBehaviour
{
    private class Voice
    {
        public AudioSource source;
        public AudioClip clip;
        public float volume;
        public float fadeIn;
        public int startFrame;
        public bool nextStarted;
    }

    // Shortest crossfade. The next play starts when the newest one has this much left, so a value
    // near zero would make that test race the end of the clip.
    private const float MinCrossfade = 0.25f;

    // A crossfade longer than this fraction of the shortest clip would put three plays on two
    // sources at once.
    private const float MaxCrossfadeOfClip = 0.25f;

    private const float LoadTimeout = 5f;

    private readonly List<SO_SoundData> tracks = new();
    private readonly List<Voice> voices = new();
    private readonly AudioSource[] sources = new AudioSource[2];

    private int playsPerTrack = 2;
    private float crossfade = 4f;
    private float initialFadeIn = 2f;
    private int segmentCount;

    private bool running;
    private bool stopping;
    private float stopSpeed;
    private float masterGain = 1f;

    /// <summary>False once <see cref="FadeOutAndDestroy"/> has been called.</summary>
    public bool IsActive => !stopping;

    /// <summary>Makes the playlist object under <paramref name="parent"/>. Call <see cref="Begin"/> next.</summary>
    public static MusicCrossfadePlaylist Create(Transform parent, string objectName = "MusicPlaylist")
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        return go.AddComponent<MusicCrossfadePlaylist>();
    }

    /// <summary>
    /// Starts the playlist. Entries with no clip are skipped. <paramref name="crossfadeSeconds"/> is
    /// clamped to what the shortest clip can hold; the first play fades in over half of it, up to 2 s.
    /// </summary>
    public void Begin(IReadOnlyList<SO_SoundData> playlist, int playsPerTrack, float crossfadeSeconds)
    {
        if (running) return;
        running = true;

        if (playlist != null)
            foreach (SO_SoundData t in playlist)
                if (t != null && t.Clip != null) tracks.Add(t);

        this.playsPerTrack = Mathf.Max(1, playsPerTrack);
        crossfade = crossfadeSeconds;

        if (tracks.Count == 0)
        {
            Debug.LogWarning($"[{nameof(MusicCrossfadePlaylist)}] No playable track: nothing to play.", this);
            Destroy(gameObject);
            return;
        }

        AudioMixerGroup group = AudioManager.Exists ? AudioManager.Instance.MusicGroup : null;
        for (int i = 0; i < sources.Length; i++)
            sources[i] = CreateSource($"Voice_{i}", group);

        StartCoroutine(LoadThenStart());
    }

    /// <summary>
    /// Fades everything out on unscaled time and removes the object. Safe to call before the clips
    /// have loaded, and more than once.
    /// </summary>
    public void FadeOutAndDestroy(float seconds)
    {
        if (stopping) return;
        stopping = true;
        stopSpeed = 1f / Mathf.Max(seconds, 0.05f);

        if (voices.Count == 0) Destroy(gameObject);
    }

    private AudioSource CreateSource(string sourceName, AudioMixerGroup group)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);

        AudioSource s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = false;
        s.spatialBlend = 0f;
        s.priority = 0;
        s.volume = 0f;
        s.outputAudioMixerGroup = group;
        return s;
    }

    // The importer has these clips with Preload Audio Data off, and a clip played before it is
    // loaded starts late. Loading first costs a few frames of silence at the start, once.
    private IEnumerator LoadThenStart()
    {
        foreach (SO_SoundData t in tracks)
            if (t.Clip.loadState == AudioDataLoadState.Unloaded) t.Clip.LoadAudioData();

        float waited = 0f;
        while (!AllLoaded() && waited < LoadTimeout)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        tracks.RemoveAll(t => t.Clip.loadState == AudioDataLoadState.Failed);
        if (tracks.Count == 0)
        {
            Debug.LogWarning($"[{nameof(MusicCrossfadePlaylist)}] No track could be loaded.", this);
            Destroy(gameObject);
            yield break;
        }

        float shortest = float.MaxValue;
        foreach (SO_SoundData t in tracks) shortest = Mathf.Min(shortest, t.Clip.length);

        crossfade = Mathf.Clamp(crossfade, MinCrossfade, Mathf.Max(MinCrossfade, shortest * MaxCrossfadeOfClip));
        initialFadeIn = Mathf.Min(2f, crossfade * 0.5f);

        if (stopping) { Destroy(gameObject); yield break; }

        StartNext(first: true);
    }

    private bool AllLoaded()
    {
        foreach (SO_SoundData t in tracks)
            if (t.Clip.loadState == AudioDataLoadState.Loading || t.Clip.loadState == AudioDataLoadState.Unloaded)
                return false;
        return true;
    }

    private void StartNext(bool first = false)
    {
        int index = segmentCount++;
        SO_SoundData data = tracks[(index / playsPerTrack) % tracks.Count];
        AudioSource s = sources[index % sources.Length];

        if (s.isPlaying) s.Stop();
        s.clip = data.Clip;
        s.volume = 0f; // The envelope raises it from this frame on; starting at 0 avoids a pop.
        s.Play();

        voices.Add(new Voice
        {
            source = s,
            clip = data.Clip,
            volume = data.Volume,
            fadeIn = first ? initialFadeIn : crossfade,
            startFrame = Time.frameCount,
        });
    }

    private void Update()
    {
        // AudioListener.pause freezes the sources and their position with them, so skipping the
        // frame keeps the envelope and the audio in step. It is also what makes !isPlaying below
        // mean "ended" and not "paused".
        if (voices.Count == 0 || AudioListener.pause) return;

        if (stopping)
        {
            masterGain = Mathf.MoveTowards(masterGain, 0f, Time.unscaledDeltaTime * stopSpeed);
            if (masterGain <= 0f)
            {
                Destroy(gameObject);
                return;
            }
        }
        else
        {
            Voice newest = voices[voices.Count - 1];
            if (!newest.nextStarted && Time.frameCount > newest.startFrame)
            {
                bool inTail = newest.source.isPlaying && newest.clip.length - newest.source.time <= crossfade;

                // !isPlaying before the tail means it ended early (a long hitch, or the audio
                // device being reset). Carry on with the next play instead of going silent.
                if (inTail || !newest.source.isPlaying)
                {
                    newest.nextStarted = true;
                    StartNext();
                }
            }
        }

        for (int i = voices.Count - 1; i >= 0; i--)
        {
            Voice v = voices[i];

            if (v.nextStarted && !v.source.isPlaying && Time.frameCount > v.startFrame)
            {
                v.source.clip = null;
                voices.RemoveAt(i);
                continue;
            }

            float t = v.source.time;
            float fadeIn = v.fadeIn > 0f ? Mathf.Clamp01(t / v.fadeIn) : 1f;
            float fadeOut = Mathf.Clamp01((v.clip.length - t) / crossfade);

            // Equal power: the two plays of a crossfade add up to constant loudness, which a
            // linear pair does not (it sags about 3 dB in the middle for unrelated material).
            v.source.volume = v.volume * masterGain
                              * Mathf.Sin(fadeIn * Mathf.PI * 0.5f)
                              * Mathf.Sin(fadeOut * Mathf.PI * 0.5f);
        }
    }
}
