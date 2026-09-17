using UnityEngine;

/// <summary>
/// The static crackle layered under every hover sound (<see cref="UISelectableSound"/>): a short
/// burst of hiss with a few pops, like a CRT catching the signal for an instant.
///
/// Built in code, not a clip on disk: a handful of variants are generated once (the first hover)
/// and each hover picks one with a slightly different pitch, so no two sound identical. Tune it
/// with the constants below.
/// </summary>
public static class UIHoverStatic
{
    private const int SampleRate = 44100;
    private const int Variants = 4;

    /// <summary>Length of the burst, seconds. The hover clip itself is ~0.13 s.</summary>
    private const float Duration = 0.11f;

    /// <summary>Volume of the layer on top of the hover (0..1). The mixer's UI bus still applies.</summary>
    private const float Volume = 0.22f;

    private const float PitchJitter = 0.12f;

    /// <summary>How many pops (single loud samples) per burst, on top of the hiss.</summary>
    private const int Pops = 6;

    private static AudioClip[] clips;

    public static void Play()
    {
        if (!AudioManager.Exists) return;
        // Null check on the element too: with domain reload off, the array outlives the clips.
        if (clips == null || clips[0] == null) Build();

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        AudioManager.Instance.PlayUIClip(clip, Volume, 1f + Random.Range(-PitchJitter, PitchJitter));
    }

    private static void Build()
    {
        clips = new AudioClip[Variants];
        System.Random rng = new System.Random(0x5717C);
        int length = Mathf.CeilToInt(Duration * SampleRate);

        for (int v = 0; v < Variants; v++)
        {
            float[] data = new float[length];
            float previousIn = 0f, previousOut = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = (float)i / length;

                // Snaps in (3 ms) and dies off fast: a crackle, not a fade.
                float envelope = Mathf.Min(1f, i / (0.003f * SampleRate)) * Mathf.Exp(-4.5f * t);

                // One-pole high-pass on white noise: thin hiss, no rumble under the hover clip.
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hiss = 0.85f * (previousOut + white - previousIn);
                previousIn = white;
                previousOut = hiss;

                data[i] = hiss * envelope;
            }

            for (int p = 0; p < Pops; p++)
            {
                int at = rng.Next(0, length);
                data[at] = Mathf.Clamp((float)(rng.NextDouble() * 2.0 - 1.0) * 1.6f, -1f, 1f) * Mathf.Exp(-3f * at / (float)length);
            }

            AudioClip clip = AudioClip.Create($"UIHoverStatic_{v}", length, 1, SampleRate, false);
            clip.SetData(data, 0);
            clips[v] = clip;
        }
    }
}
