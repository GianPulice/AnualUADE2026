using System;
using System.Threading;

/// <summary>
/// The DSP behind the Architect's "hidden voice": it takes a clean recorded line and makes it sound
/// like someone talking through a voice changer, so it stops reading as a synthetic narrator and
/// starts reading as a person who does not want to be recognised. Pure C# on interleaved float
/// buffers, no UnityEngine, so the same code runs on the audio thread
/// (<see cref="ArchitectVoiceDisguise"/>) and offline in the editor (preview WAV renders).
///
/// Chain, per channel, in this order:
///   1. Pitch shift DOWN without changing the duration: a two-tap delay line whose read heads sweep
///      the line at a different speed than it is written, crossfaded with a Hann window so the tap
///      that wraps around is silent at the jump. The leftover warble is part of the sound.
///   2. Ring modulator: the voice times a low sine, mixed with the dry signal. Low frequencies
///      (tens of Hz) make a rough, buzzing growl rather than a Dalek.
///   3. Band-pass: a 2nd-order high-pass and low-pass (RBJ cookbook, Butterworth Q) that cut the
///      body and the air, leaving a small speaker's band.
///   4. Bit crush plus sample-and-hold: quantises the level and repeats every Nth sample.
///   5. Output gain, then a soft limiter that rounds anything above 0.8 off towards 1, so the
///      make-up gain never hard-clips (the band-pass takes level away and the preset gives it back).
///
/// Threading: <see cref="SetSettings"/> and <see cref="RequestReset"/> are safe to call from any
/// thread (the main thread, while the audio thread is inside <see cref="Process"/>); everything
/// else belongs to whichever thread runs <see cref="Process"/>. Settings objects are never mutated
/// after being handed over, which is what makes the handover lock-free. Process allocates nothing
/// and flushes tiny values to zero so a silent tail never runs the CPU through denormals.
/// </summary>
public sealed class VoiceDisguiseProcessor
{
    /// <summary>
    /// One immutable snapshot of the disguise parameters. Build a new instance to change anything:
    /// the audio thread picks it up by reference on its next buffer.
    /// </summary>
    public sealed class Settings
    {
        /// <summary>Playback-speed ratio of the shift. 1 = off, 0.8 = about 4 semitones down.</summary>
        public float PitchRatio = 1f;

        /// <summary>Length of the pitch shifter's crossfade window, in milliseconds.</summary>
        public float PitchWindowMs = 50f;

        /// <summary>Ring modulator carrier, in Hz.</summary>
        public float RingFrequencyHz = 40f;

        /// <summary>0 = dry voice, 1 = fully ring-modulated.</summary>
        public float RingMix = 0f;

        /// <summary>High-pass cutoff in Hz. 0 or less = off.</summary>
        public float HighPassHz = 0f;

        /// <summary>Low-pass cutoff in Hz. 0 or less, or at/above Nyquist, = off.</summary>
        public float LowPassHz = 0f;

        /// <summary>Bits kept per sample. 16 or more = off.</summary>
        public int BitDepth = 16;

        /// <summary>Every Nth sample is repeated N times. 1 = off.</summary>
        public int HoldFactor = 1;

        /// <summary>Linear output gain.</summary>
        public float OutputGain = 1f;
    }

    public const float MinWindowMs = 10f;
    public const float MaxWindowMs = 100f;

    // Butterworth: the flattest passband a 2nd-order section can have, no resonant bump at the edges.
    private const double ButterworthQ = 0.70710678118654752;
    private const float TwoPi = 6.28318530718f;

    // Above this level the output is eased towards 1 instead of being clipped flat.
    private const float SoftKnee = 0.8f;

    // Anything this small is inaudible (-300 dB) and only exists to be a denormal.
    private const float DenormalFloor = 1e-15f;

    private struct BiquadCoefficients
    {
        public float B0, B1, B2, A1, A2;
    }

    private sealed class ChannelState
    {
        public float[] Delay;
        public int Write;
        public float PitchPhase;
        public float RingPhase;
        public float Hp1, Hp2;
        public float Lp1, Lp2;
        public int HoldCounter;
        public float Held;

        public void Clear()
        {
            Array.Clear(Delay, 0, Delay.Length);
            Write = 0;
            PitchPhase = 0f;
            RingPhase = 0f;
            Hp1 = Hp2 = Lp1 = Lp2 = 0f;
            HoldCounter = 0;
            Held = 0f;
        }
    }

    private readonly int sampleRate;
    private readonly ChannelState[] states;

    // Handover from the thread that sets things up to the one that processes.
    private Settings pending;
    private int resetRequested;

    // Derived from the applied settings, only touched by the processing thread.
    private Settings applied;
    private bool identity = true;
    private bool pitchActive;
    private float windowSamples = 1f;
    private float phaseStep;
    private bool ringActive;
    private float ringStep;
    private float ringDry = 1f;
    private float ringWet;
    private bool hpActive;
    private BiquadCoefficients hp;
    private bool lpActive;
    private BiquadCoefficients lp;
    private bool crushActive;
    private float crushLevels = 1f;
    private float crushInverse = 1f;
    private int hold = 1;
    private float gain = 1f;

    public int SampleRate => sampleRate;
    public int MaxChannels => states.Length;

    /// <summary>
    /// Allocates the delay lines for <paramref name="maxChannels"/> channels, once. Channels beyond
    /// that count pass through untouched. Call it on the main thread, never from the audio callback.
    /// </summary>
    public VoiceDisguiseProcessor(int sampleRate, int maxChannels = 2)
    {
        this.sampleRate = Math.Max(sampleRate, 8000);
        int channelCount = Math.Max(maxChannels, 1);

        // Room for the longest window plus the two samples the interpolator reads past it.
        int delayLength = (int)Math.Ceiling(MaxWindowMs * 0.001 * this.sampleRate) + 8;

        states = new ChannelState[channelCount];
        for (int c = 0; c < channelCount; c++)
            states[c] = new ChannelState { Delay = new float[delayLength] };

        SetSettings(new Settings());
    }

    /// <summary>Hands over a new snapshot. Taken by the processing thread at the start of its next buffer.</summary>
    public void SetSettings(Settings settings)
    {
        if (settings == null) settings = new Settings();
        Volatile.Write(ref pending, settings);
    }

    /// <summary>
    /// Clears the delay lines and filter memory at the start of the next buffer, so the tail of one
    /// line never leaks into the start of the next.
    /// </summary>
    public void RequestReset() => Volatile.Write(ref resetRequested, 1);

    /// <summary>Processes an interleaved buffer in place.</summary>
    public void Process(float[] data, int channels)
    {
        if (data == null || channels < 1) return;

        if (Interlocked.Exchange(ref resetRequested, 0) != 0)
        {
            for (int c = 0; c < states.Length; c++) states[c].Clear();
        }

        Settings latest = Volatile.Read(ref pending);
        if (!ReferenceEquals(latest, applied)) Apply(latest);

        if (identity) return;

        int frames = data.Length / channels;
        int active = Math.Min(channels, states.Length);

        for (int f = 0; f < frames; f++)
        {
            int frameStart = f * channels;
            for (int c = 0; c < active; c++)
            {
                ChannelState st = states[c];
                float x = data[frameStart + c];

                // A NaN or a runaway value would poison every filter state for good.
                if (!(x > -8f && x < 8f)) x = 0f;

                // 1) Pitch shift. The sample is written first, so a delay of 0 reads it back.
                st.Delay[st.Write] = x;
                if (pitchActive)
                {
                    float p1 = st.PitchPhase;
                    float p2 = p1 + 0.5f;
                    if (p2 >= 1f) p2 -= 1f;

                    // Hann crossfade: g1 + (1 - g1) = 1, and each tap is silent when its delay jumps.
                    float g1 = 0.5f - 0.5f * (float)Math.Cos(TwoPi * p1);
                    float tap1 = ReadDelayed(st.Delay, st.Write, p1 * windowSamples);
                    float tap2 = ReadDelayed(st.Delay, st.Write, p2 * windowSamples);
                    x = tap1 * g1 + tap2 * (1f - g1);

                    p1 += phaseStep;
                    if (p1 >= 1f) p1 -= 1f;
                    else if (p1 < 0f) p1 += 1f;
                    st.PitchPhase = p1;
                }
                if (++st.Write >= st.Delay.Length) st.Write = 0;

                // 2) Ring modulation.
                if (ringActive)
                {
                    float carrier = (float)Math.Sin(TwoPi * st.RingPhase);
                    x *= ringDry + ringWet * carrier;

                    st.RingPhase += ringStep;
                    if (st.RingPhase >= 1f) st.RingPhase -= 1f;
                }

                // 3) Band-pass, transposed direct form II.
                if (hpActive)
                {
                    float y = hp.B0 * x + st.Hp1;
                    st.Hp1 = Flush(hp.B1 * x - hp.A1 * y + st.Hp2);
                    st.Hp2 = Flush(hp.B2 * x - hp.A2 * y);
                    x = y;
                }
                if (lpActive)
                {
                    float y = lp.B0 * x + st.Lp1;
                    st.Lp1 = Flush(lp.B1 * x - lp.A1 * y + st.Lp2);
                    st.Lp2 = Flush(lp.B2 * x - lp.A2 * y);
                    x = y;
                }

                // 4) Bit crush and sample-and-hold: quantise on the sample that gets held.
                if (crushActive || hold > 1)
                {
                    if (st.HoldCounter <= 0)
                    {
                        float held = x;
                        if (crushActive)
                        {
                            // Signed integer steps: -levels .. levels - 1, as many as the bit depth allows.
                            float step = (float)Math.Floor(x * crushLevels + 0.5f);
                            if (step > crushLevels - 1f) step = crushLevels - 1f;
                            else if (step < -crushLevels) step = -crushLevels;
                            held = step * crushInverse;
                        }
                        st.Held = held;
                        st.HoldCounter = hold;
                    }
                    st.HoldCounter--;
                    x = st.Held;
                }

                // 5) Output.
                x *= gain;
                float magnitude = x < 0f ? -x : x;
                if (magnitude > SoftKnee)
                {
                    float limited = SoftKnee + (1f - SoftKnee) * (float)Math.Tanh((magnitude - SoftKnee) / (1f - SoftKnee));
                    x = x < 0f ? -limited : limited;
                }
                data[frameStart + c] = x;
            }
        }
    }

    // ── Setup ────────────────────────────────────────────────────────────────────────────

    private void Apply(Settings s)
    {
        applied = s;
        float nyquist = sampleRate * 0.5f;

        float ratio = Clamp(s.PitchRatio, 0.5f, 2f);
        pitchActive = Math.Abs(ratio - 1f) > 0.001f;
        windowSamples = Clamp(s.PitchWindowMs, MinWindowMs, MaxWindowMs) * 0.001f * sampleRate;

        // The read heads move at 'ratio' samples per sample, the writer at 1: the delay drifts by
        // (1 - ratio) each sample and the phase is that drift as a fraction of the window.
        phaseStep = (1f - ratio) / windowSamples;

        float mix = Clamp(s.RingMix, 0f, 1f);
        ringActive = mix > 0.001f && s.RingFrequencyHz > 0.01f;
        ringStep = Clamp(s.RingFrequencyHz, 0f, nyquist * 0.9f) / sampleRate;
        ringWet = mix;
        ringDry = 1f - mix;

        float lowPass = s.LowPassHz;
        lpActive = lowPass > 1f && lowPass < nyquist * 0.9f;
        float highPass = s.HighPassHz;
        hpActive = highPass > 1f;

        if (hpActive)
        {
            // A high-pass above the low-pass would leave nothing: pull it under.
            if (lpActive && highPass >= lowPass) highPass = lowPass * 0.5f;
            highPass = Math.Min(highPass, nyquist * 0.9f);
            hp = DesignBiquad(true, highPass, sampleRate);
        }
        if (lpActive) lp = DesignBiquad(false, lowPass, sampleRate);

        int bits = s.BitDepth;
        crushActive = bits >= 1 && bits < 16;
        if (crushActive)
        {
            crushLevels = (float)Math.Pow(2.0, bits - 1);
            crushInverse = 1f / crushLevels;
        }

        hold = Math.Max(1, Math.Min(s.HoldFactor, 64));
        gain = Clamp(s.OutputGain, 0f, 8f);

        identity = !pitchActive && !ringActive && !hpActive && !lpActive && !crushActive &&
                   hold <= 1 && Math.Abs(gain - 1f) < 0.0001f;
    }

    /// <summary>RBJ cookbook 2nd-order high-pass or low-pass, normalised so a0 = 1.</summary>
    private static BiquadCoefficients DesignBiquad(bool highPass, float cutoffHz, int sampleRate)
    {
        double w0 = 2.0 * Math.PI * cutoffHz / sampleRate;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * ButterworthQ);
        double a0 = 1.0 + alpha;

        double b0, b1;
        if (highPass)
        {
            b0 = (1.0 + cos) * 0.5;
            b1 = -(1.0 + cos);
        }
        else
        {
            b0 = (1.0 - cos) * 0.5;
            b1 = 1.0 - cos;
        }

        return new BiquadCoefficients
        {
            B0 = (float)(b0 / a0),
            B1 = (float)(b1 / a0),
            B2 = (float)(b0 / a0),
            A1 = (float)(-2.0 * cos / a0),
            A2 = (float)((1.0 - alpha) / a0),
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The sample <paramref name="delay"/> samples before <paramref name="write"/> (0 = the one just
    /// written), linearly interpolated between the two neighbours.
    /// </summary>
    private static float ReadDelayed(float[] buffer, int write, float delay)
    {
        int whole = (int)delay;
        float fraction = delay - whole;

        int newer = write - whole;
        if (newer < 0) newer += buffer.Length;
        int older = newer - 1;
        if (older < 0) older += buffer.Length;

        float a = buffer[newer];
        float b = buffer[older];
        return a + (b - a) * fraction;
    }

    private static float Flush(float value) =>
        value > -DenormalFloor && value < DenormalFloor ? 0f : value;

    private static float Clamp(float value, float min, float max) =>
        value < min ? min : value > max ? max : value;
}
