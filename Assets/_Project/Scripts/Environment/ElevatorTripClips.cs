using System;
using UnityEngine;

/// <summary>
/// One travel recording cut into the three pieces a lift needs to sound continuous: the run-up
/// before the repeating stretch (<see cref="Intro"/>), the stretch itself rebuilt so that it repeats
/// without a click (<see cref="Loop"/>), and whatever follows it, which is the stop
/// (<see cref="Outro"/>).
///
/// Why the loop is rebuilt and not just played from Loop Start again: the waveform at Loop End and
/// at Loop Start are two unrelated values, so jumping from one to the other is a step in the signal,
/// and a step is heard as a click on every pass. Here the last <c>seam</c> seconds of the loop are
/// crossfaded into the material that sits right before Loop Start. The loop then ends on the sample
/// that precedes its own first one in the original recording, so the wrap is continuous.
///
/// Why the intro and outro are separate clips: an AudioSource can only loop a whole clip. The three
/// pieces are chained on the DSP clock by <see cref="ElevatorTravelSound"/>, which is sample-exact,
/// unlike seeking <c>AudioSource.time</c> from Update.
///
/// Owns the clips it creates; <see cref="Release"/> destroys them.
/// </summary>
public sealed class ElevatorTripClips
{
    /// <summary>Start of the recording up to Loop Start. Null when Loop Start is 0.</summary>
    public AudioClip Intro { get; }

    /// <summary>Loop Start to Loop End, with the seam blended in. Play it with loop = true.</summary>
    public AudioClip Loop { get; }

    /// <summary>Loop End to the end of the recording, faded in. Null when Loop End is the end.</summary>
    public AudioClip Outro { get; }

    private ElevatorTripClips(AudioClip intro, AudioClip loop, AudioClip outro)
    {
        Intro = intro;
        Loop = loop;
        Outro = outro;
    }

    /// <summary>
    /// Cuts <paramref name="source"/>. Returns null when there is nothing to cut: no clip, no
    /// stretch between the two points, or samples that cannot be read (the clip is not loaded, or
    /// its Load Type is Streaming).
    /// </summary>
    /// <param name="seamSeconds">Length of the crossfade that closes the loop. Cut short to what the
    /// recording has before Loop Start, and to half the loop.</param>
    /// <param name="outroFadeInSeconds">Fade-in on the outro. It starts while the loop is still
    /// fading out, so it hides that the two are not in phase.</param>
    public static ElevatorTripClips Build(AudioClip source, float loopStart, float loopEnd,
                                          float seamSeconds, float outroFadeInSeconds)
    {
        if (source == null) return null;

        if (source.loadState == AudioDataLoadState.Unloaded) source.LoadAudioData();

        int channels = source.channels;
        int rate = source.frequency;
        int total = source.samples;

        int start = Mathf.Clamp(Mathf.RoundToInt(loopStart * rate), 0, total);
        int end = Mathf.Clamp(Mathf.RoundToInt(loopEnd * rate), 0, total);
        if (end <= start) return null;

        var data = new float[total * channels];
        if (!source.GetData(data, 0)) return null;

        int length = end - start;
        int seam = Mathf.Min(Mathf.RoundToInt(seamSeconds * rate), start, length / 2);

        var loop = new float[length * channels];
        Array.Copy(data, start * channels, loop, 0, loop.Length);

        // Equal power: the two stretches are unrelated material, so a linear fade would dip in the
        // middle of the seam.
        for (int i = 0; i < seam; i++)
        {
            float angle = (i + 0.5f) / seam * Mathf.PI * 0.5f;
            float fromLoop = Mathf.Cos(angle);
            float fromBefore = Mathf.Sin(angle);

            int dst = (length - seam + i) * channels;
            int before = (start - seam + i) * channels;
            for (int c = 0; c < channels; c++)
                loop[dst + c] = loop[dst + c] * fromLoop + data[before + c] * fromBefore;
        }

        AudioClip intro = start > 0 ? MakeClip(source.name + "_intro", data, start, channels, rate) : null;
        AudioClip body = MakeClip(source.name + "_loop", loop, length, channels, rate);

        AudioClip outro = null;
        if (end < total)
        {
            int outroLength = total - end;
            var tail = new float[outroLength * channels];
            Array.Copy(data, end * channels, tail, 0, tail.Length);

            int fade = Mathf.Min(Mathf.RoundToInt(outroFadeInSeconds * rate), outroLength);
            for (int i = 0; i < fade; i++)
            {
                float gain = (float)i / fade;
                for (int c = 0; c < channels; c++) tail[i * channels + c] *= gain;
            }

            outro = MakeClip(source.name + "_outro", tail, outroLength, channels, rate);
        }

        return new ElevatorTripClips(intro, body, outro);
    }

    /// <summary>Destroys the generated clips.</summary>
    public void Release()
    {
        if (Intro != null) UnityEngine.Object.Destroy(Intro);
        if (Loop != null) UnityEngine.Object.Destroy(Loop);
        if (Outro != null) UnityEngine.Object.Destroy(Outro);
    }

    /// <summary>A clip of the first <paramref name="samples"/> samples (per channel) of <paramref name="data"/>.</summary>
    private static AudioClip MakeClip(string name, float[] data, int samples, int channels, int rate)
    {
        var clip = AudioClip.Create(name, samples, channels, rate, false);

        if (data.Length == samples * channels)
        {
            clip.SetData(data, 0);
            return clip;
        }

        var slice = new float[samples * channels];
        Array.Copy(data, slice, slice.Length);
        clip.SetData(slice, 0);
        return clip;
    }
}
