using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generated stand-ins for the boiler knocks (Central Puzzle 2 — SP1) until audio delivers the real
/// ones: a short metallic thud per lever, each at its own pitch so the levers can be told apart by
/// ear. Built once per lever and cached; no file on disk.
/// </summary>
public static class PlaceholderKnockClips
{
    private const int SampleRate = 44100;
    private const float Seconds = 0.35f;

    // Pitch of lever 1; every next lever is a few semitones higher.
    private const float BaseFrequency = 70f;
    private const float StepRatio = 1.26f;

    private static readonly Dictionary<int, AudioClip> cache = new Dictionary<int, AudioClip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cache.Clear();

    public static AudioClip Get(int lever)
    {
        lever = Mathf.Max(1, lever);
        if (cache.TryGetValue(lever, out AudioClip clip) && clip != null) return clip;

        clip = Build(lever);
        cache[lever] = clip;
        return clip;
    }

    private static AudioClip Build(int lever)
    {
        int length = Mathf.CeilToInt(SampleRate * Seconds);
        float[] data = new float[length];

        float frequency = BaseFrequency * Mathf.Pow(StepRatio, lever - 1);
        System.Random noise = new System.Random(lever * 7919);

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float body = Mathf.Exp(-t * 14f);                    // the thud decays fast
            float click = Mathf.Exp(-t * 90f);                   // the hammer hitting metal
            float tone = Mathf.Sin(2f * Mathf.PI * frequency * t) +
                         0.45f * Mathf.Sin(2f * Mathf.PI * frequency * 2.76f * t);  // inharmonic: metal
            float hit = (float)(noise.NextDouble() * 2.0 - 1.0);
            data[i] = Mathf.Clamp(tone * body * 0.6f + hit * click * 0.5f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create($"PlaceholderKnock_Lever{lever}", length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
