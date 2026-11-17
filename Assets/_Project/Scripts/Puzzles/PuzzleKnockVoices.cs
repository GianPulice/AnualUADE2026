using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AudioSources of their own for the boiler puzzle's knocks (Central Puzzle 2 — SP1), created on
/// demand as children of their owner (the boiler, a lever) and routed to the SFX bus.
///
/// Why not <see cref="AudioManager.PlaySFX(AudioClip, Vector3, float, float, float, float)"/>: its
/// sources come from a shared pool, and a pooled source frozen by the pause (AudioListener.pause)
/// reports isPlaying = false — the first pause-menu sound (hover, click, closing the menu) takes it
/// over and the knock never comes back. These are never lent out, so the pause freezes them and
/// unpausing resumes them from where they were, in step with the sequence (scaled time).
///
/// Same settings as the pooled 3D one-shot: linear falloff from 1 m to the max distance given.
/// </summary>
public sealed class PuzzleKnockVoices
{
    private readonly Transform owner;
    private readonly List<AudioSource> voices = new List<AudioSource>();

    public PuzzleKnockVoices(Transform owner)
    {
        this.owner = owner;
    }

    public void Play(AudioClip clip, float volume, float maxDistance)
    {
        if (clip == null || owner == null || !AudioManager.Exists) return;

        AudioSource src = FreeVoice();
        src.outputAudioMixerGroup = AudioManager.Instance.SfxGroup;
        src.clip = clip;
        src.loop = false;
        src.volume = Mathf.Clamp01(volume);
        src.pitch = 1f;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 1f;
        src.maxDistance = Mathf.Max(maxDistance, 1.1f);
        src.Play();
    }

    /// <summary>Cuts every knock (the owner was disabled: what it was playing dies with it).</summary>
    public void StopAll()
    {
        foreach (AudioSource src in voices)
            if (src != null) src.Stop();
    }

    // Knocks overlap in the boiler's sequence (the next one starts halfway through the last one),
    // so there can be a few at once. Nothing plays here while the game is paused (the sequence and
    // the levers wait on scaled time), so a frozen voice is never mistaken for a free one.
    private AudioSource FreeVoice()
    {
        foreach (AudioSource src in voices)
            if (src != null && !src.isPlaying) return src;

        var go = new GameObject($"KnockVoice_{voices.Count}");
        go.transform.SetParent(owner, false);
        var voice = go.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.ignoreListenerPause = false;   // the pause has to freeze it
        voices.Add(voice);
        return voice;
    }
}
