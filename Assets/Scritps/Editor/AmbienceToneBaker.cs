using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the procedurally generatable parts of the factory ambience to .wav assets:
/// the pink and brown noise textures (Layer 3), the low-frequency drones (Layer 4), a placeholder
/// factory bed (Layer 1) and a handful of placeholder one-shots (Layer 2).
///
/// Run from: Tools/Audio/Bake Ambience Texture Clips.
///
/// WHY BAKE RATHER THAN GENERATE AT RUNTIME
/// AudioClip.Create + OnAudioRead would avoid the disk cost and nothing else. A baked clip is an
/// ordinary asset: the designer can open it in an editor, see the waveform, verify the loop seam,
/// audition it in the Project window, and later replace it with a real recording without touching
/// a line of code — which is the entire point of making these layers procedural. Per-clip import
/// settings (PCM vs Vorbis, sample rate, load type) are also impossible on a runtime clip, and
/// they are not optional here: a perceptual codec treats noise as its worst case and its block
/// padding destroys the sample-exact seam this tool works to produce. OnAudioRead would also run
/// generator code on the audio thread forever, for three tracks, where an allocation causes a
/// dropout.
///
/// DETERMINISM
/// Everything is driven by a fixed seed, so re-baking produces byte-identical files and git does
/// not churn. Change Seed if you want a different noise character.
///
/// SEAMLESS LOOPS — the two techniques used here
///
/// 1. Cycle alignment, for the sine-based drones. With a buffer of L samples, any frequency that
///    is an integer multiple of sampleRate/L completes a whole number of cycles inside the buffer
///    and therefore wraps perfectly. At 60 seconds that step is 1/60 Hz regardless of sample rate,
///    which is fine enough to place any partial you would actually want. This is what makes
///    detuned partials free: 32.0 and 31.7 Hz are both legal, so the drone can beat slowly without
///    any crossfade at all. The generator stores integer cycle COUNTS rather than frequencies, so
///    the alignment is exact by construction instead of by rounding.
///
/// 2. Seam crossfade, for the noise-based clips, where no such alignment exists. The generator
///    produces N + S samples and folds the last S over the first S. Because the first output
///    sample is dominated by buffer[N] — which naturally follows buffer[N-1] — the wrap is
///    continuous.
///
///    The crossfade is EQUAL POWER (sqrt), not linear. Two uncorrelated noise signals summed with
///    linear gains lose about 3 dB in the middle of the fade, which is an audible hole once per
///    loop. This is the single easiest thing to get wrong here.
/// </summary>
public static class AmbienceToneBaker
{
    private const string OutputFolder = "Assets/Audios/Ambience/Generated";

    private const int Seed = 1337;

    // Noise and the bed keep full bandwidth.
    private const int FullSampleRate = 44100;

    // The drones are baked at a low rate on purpose. Nyquist at 11025 Hz is 5.5 kHz, still fifty
    // times the highest partial, the sampleRate/L step is unchanged, and a 60 s mono 16-bit file
    // drops from 5.3 MB to 1.3 MB.
    private const int SubSampleRate = 11025;

    // ──────────────────────────────────────────────────────────────────────────
    // Menu entry
    // ──────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Audio/Bake Ambience Texture Clips")]
    public static void BakeAll()
    {
        EnsureFolder(OutputFolder);

        // Deliberately NOT wrapped in StartAssetEditing/StopAssetEditing: that batch mode defers
        // imports, so the AssetImporter.GetAtPath call in ConfigureImporter would come back null
        // and every clip would silently keep the project's default (Vorbis) compression.
        BakeNoiseTextures();
        BakePlaceholderBed();
        BakeSubDrones();
        BakePlaceholderOneShots();

        AssetDatabase.Refresh();

        Debug.Log($"[AmbienceToneBaker] Baked ambience clips into {OutputFolder}.\n" +
                  "  Layer 3  PinkNoise_20s, BrownNoise_20s        (assign one to AmbienceDriftLayer)\n" +
                  "  Layer 4  Sub_17Hz_60s, Sub_32Hz_60s           (assign both to AmbienceDriftLayer)\n" +
                  "  Layer 1  PLACEHOLDER_Bed_A_37s, _Bed_B_53s    (coprime pair — assign BOTH to a profile)\n" +
                  "  Layer 2  PLACEHOLDER_OneShot_*                (5 crude stand-ins, one or two per tier)\n" +
                  "Everything prefixed PLACEHOLDER_ is a stand-in for tuning the system, not " +
                  "shippable content. Replace those with real recordings; the generated noise and " +
                  "drones are final.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Layer 3 — pink and brown noise
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Both noise colours come out of a single white-noise run so they share a character.
    ///
    /// Pink is what the audio spec asks for. Brown is baked alongside it because pink still reads
    /// as hiss when there is no other high-frequency content in the mix, and this game has a PSX
    /// aesthetic with very little of it — brown (-6 dB/octave instead of -3) sits underneath far
    /// more invisibly. Try both on the Texture track and keep whichever disappears better.
    /// </summary>
    private static void BakeNoiseTextures()
    {
        const float durationSeconds = 20f;
        const float seamSeconds = 1f;

        int outLength  = (int)(durationSeconds * FullSampleRate);
        int seamLength = (int)(seamSeconds * FullSampleRate);
        int genLength  = outLength + seamLength;

        float[] pink  = new float[genLength];
        float[] brown = new float[genLength];

        System.Random rng = new System.Random(Seed);

        // Paul Kellet's economy pink-noise filter. Chosen over Voss-McCartney: Voss needs a bank
        // of counters and produces a stepped spectrum, while this is seven multiply-adds and is
        // flat to about +/-0.05 dB from 20 Hz to 20 kHz.
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;

        // Brown noise: one leaky integrator over the same white samples.
        double brownLast = 0;

        for (int i = 0; i < genLength; i++)
        {
            double white = rng.NextDouble() * 2.0 - 1.0;

            b0 = 0.99886 * b0 + white * 0.0555179;
            b1 = 0.99332 * b1 + white * 0.0750759;
            b2 = 0.96900 * b2 + white * 0.1538520;
            b3 = 0.86650 * b3 + white * 0.3104856;
            b4 = 0.55000 * b4 + white * 0.5329522;
            b5 = -0.7616 * b5 - white * 0.0168980;

            pink[i] = (float)((b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362) * 0.11);

            b6 = white * 0.115926;

            brownLast = (brownLast + 0.02 * white) / 1.02;
            brown[i] = (float)(brownLast * 3.5);
        }

        // A gentle one-pole lowpass so the pink does not read as tape hiss. The spec wants a
        // texture the player never consciously identifies, and audible high frequencies are what
        // gives noise away.
        OnePoleLowpass(pink, 6000f, FullSampleRate);

        float[] pinkOut  = CrossfadeSeam(pink,  outLength, seamLength);
        float[] brownOut = CrossfadeSeam(brown, outLength, seamLength);

        NormalizeToPeakDb(pinkOut,  -6f);
        NormalizeToPeakDb(brownOut, -6f);

        WriteClip("PinkNoise_20s",  pinkOut,  FullSampleRate);
        WriteClip("BrownNoise_20s", brownOut, FullSampleRate);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Layer 1 — placeholder factory bed
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two placeholder beds of coprime length (37 s and 53 s). Played together their composite
    /// period is the least common multiple, 1961 s — about 33 minutes — so the loop is effectively
    /// undetectable even though each individual clip is short.
    ///
    /// The recipe is brown noise through three resonant peaks, which is a crude imitation of a
    /// large empty room: broadband air movement plus the handful of modal frequencies a big
    /// concrete-and-steel box rings at. It is deliberately dull — no transients, no recognisable
    /// events — which is exactly what the spec asks of the bed, and it is enough to tune levels,
    /// zone crossfades and the balance against the one-shots.
    ///
    /// It is NOT shippable. A real bed wants recorded ventilation, building resonance and distant
    /// structure. Replace it and delete these files.
    /// </summary>
    private static void BakePlaceholderBed()
    {
        // Coprime lengths: gcd(37, 53) = 1 because both are prime.
        BakeOneBed("PLACEHOLDER_Bed_A_37s", 37f, Seed + 11, new[] { 74f, 183f, 431f });
        BakeOneBed("PLACEHOLDER_Bed_B_53s", 53f, Seed + 29, new[] { 96f, 247f, 512f });
    }

    private static void BakeOneBed(string clipName, float durationSeconds, int seed, float[] peaks)
    {
        const float seamSeconds = 1.5f;

        int outLength  = (int)(durationSeconds * FullSampleRate);
        int seamLength = (int)(seamSeconds * FullSampleRate);
        int genLength  = outLength + seamLength;

        float[] buffer = new float[genLength];
        System.Random rng = new System.Random(seed);

        double brownLast = 0;
        for (int i = 0; i < genLength; i++)
        {
            double white = rng.NextDouble() * 2.0 - 1.0;
            brownLast = (brownLast + 0.02 * white) / 1.02;
            buffer[i] = (float)(brownLast * 3.5);
        }

        // Sum the dry brown with three narrow resonances. The dry part is the air, the resonances
        // are the room.
        float[] resonated = new float[genLength];
        for (int p = 0; p < peaks.Length; p++)
        {
            float[] band = (float[])buffer.Clone();
            Resonator(band, peaks[p], 0.9965, FullSampleRate);

            float weight = 1f / (p + 1f);
            for (int i = 0; i < genLength; i++) resonated[i] += band[i] * weight;
        }

        for (int i = 0; i < genLength; i++)
            buffer[i] = buffer[i] * 0.55f + resonated[i] * 0.45f;

        OnePoleLowpass(buffer, 3500f, FullSampleRate);

        float[] output = CrossfadeSeam(buffer, outLength, seamLength);

        // A very slow tremolo so the bed is not perfectly static. An integer number of cycles over
        // the output length keeps it seamless.
        ApplyPeriodicTremolo(output, cycles: 3, depth: 0.14f);

        NormalizeToPeakDb(output, -9f);

        WriteClip(clipName, output, FullSampleRate);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Layer 4 — low-frequency drones
    // ──────────────────────────────────────────────────────────────────────────

    private static void BakeSubDrones()
    {
        const float durationSeconds = 60f;

        // 32 Hz industrial drone. This is the layer that carries the perceptible weight, because
        // it is the lowest range consumer hardware actually reproduces. The 64 Hz octave is the
        // safety net: it is the only partial here a laptop speaker has any chance with.
        BakeDrone(
            clipName: "Sub_32Hz_60s",
            durationSeconds: durationSeconds,
            peakDb: -8f,
            partials: new[]
            {
                new Partial(32.0f, 1.00f),   // fundamental
                new Partial(31.7f, 0.55f),   // detune — this is what makes it breathe
                new Partial(48.1f, 0.22f),   // near-fifth, industrial body
                new Partial(64.0f, 0.14f)    // octave, audible on small speakers
            },
            lfos: new[]
            {
                new Lfo(1, 0.12f),
                new Lfo(3, 0.06f)
            },
            seed: Seed + 101);

        // 17 Hz infrasound.
        //
        // HONEST NOTE, because this layer is easy to misunderstand: 17 Hz is below the roughly
        // 20 Hz human hearing floor and is reproduced by no laptop speaker, no earbud and very few
        // headphones. A file containing only 17 Hz is, on most hardware, a silent file that costs a
        // voice.
        //
        // The 34 Hz partial is therefore deliberate. It is quiet enough that the layer still reads
        // as pressure rather than as a tone, but it means the track does something on every device
        // instead of nothing on almost all of them. It stops being infrasound-pure; that is the
        // trade, and it is the right one.
        //
        // The real defence against this layer misbehaving is level, not spectrum: keep the
        // Ambience/Sub fader at -12 dB or lower and verify on the worst speakers available. High
        // level low frequency on a small driver produces intermodulation distortion — the sub
        // modulates the midrange, so footsteps and dialogue sound gritty — not "pressure".
        BakeDrone(
            clipName: "Sub_17Hz_60s",
            durationSeconds: durationSeconds,
            peakDb: -10f,
            partials: new[]
            {
                new Partial(17.0f, 1.00f),
                new Partial(16.8f, 0.40f),
                new Partial(34.0f, 0.10f)    // see the note above — this is what makes it audible at all
            },
            lfos: new[]
            {
                new Lfo(1, 0.10f),
                new Lfo(2, 0.05f)
            },
            seed: Seed + 211);
    }

    private readonly struct Partial
    {
        public readonly float Frequency;
        public readonly float Amplitude;

        public Partial(float frequency, float amplitude)
        {
            Frequency = frequency;
            Amplitude = amplitude;
        }
    }

    /// <summary>
    /// An amplitude modulation term. <see cref="Cycles"/> is an integer number of cycles over the
    /// whole buffer, which is what keeps the modulation seamless — a modulator with a fractional
    /// period would reintroduce the discontinuity the cycle-aligned partials avoid.
    /// </summary>
    private readonly struct Lfo
    {
        public readonly int Cycles;
        public readonly float Depth;

        public Lfo(int cycles, float depth)
        {
            Cycles = cycles;
            Depth = depth;
        }
    }

    private static void BakeDrone(string clipName, float durationSeconds, float peakDb,
                                  Partial[] partials, Lfo[] lfos, int seed)
    {
        int length = (int)(durationSeconds * SubSampleRate);
        float[] buffer = new float[length];

        System.Random rng = new System.Random(seed);

        foreach (Partial partial in partials)
        {
            // Store the integer cycle count, not the frequency. The requested frequency is snapped
            // to the nearest whole number of cycles in the buffer, which makes the wrap exact by
            // construction: at i == length the phase is exactly 2*pi*cycles.
            int cycles = Mathf.Max(1, Mathf.RoundToInt(partial.Frequency * durationSeconds));
            float actualHz = cycles / durationSeconds;

            if (!Mathf.Approximately(actualHz, partial.Frequency))
            {
                Debug.Log($"[AmbienceToneBaker] {clipName}: partial {partial.Frequency:F2} Hz " +
                          $"snapped to {actualHz:F4} Hz ({cycles} cycles) so the loop stays seamless.");
            }

            // Random start phase so the partials do not all begin at zero, which would make the
            // sum open on a transient. Legal because each partial is individually periodic in the
            // buffer regardless of its phase offset.
            double phase = rng.NextDouble() * 2.0 * Math.PI;
            double step  = 2.0 * Math.PI * cycles / length;

            for (int i = 0; i < length; i++)
                buffer[i] += (float)(partial.Amplitude * Math.Sin(step * i + phase));
        }

        foreach (Lfo lfo in lfos)
            ApplyPeriodicTremolo(buffer, lfo.Cycles, lfo.Depth);

        NormalizeToPeakDb(buffer, peakDb);

        WriteClip(clipName, buffer, SubSampleRate);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Layer 2 — placeholder one-shots
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Five crude synthetic one-shots so the event scheduler, the tier weighting, the repetition
    /// history and the 3D placement can all be tuned and verified before any real audio has been
    /// sourced. Convincing environmental one-shots cannot be synthesised this cheaply — these are
    /// caricatures, and they are labelled as such.
    ///
    /// Suggested tier placement: drip and debris in COMMON, pipe and creak in UNCOMMON, boom in
    /// RARE. That gives at least one entry per tier so the weighted roll has somewhere to land.
    /// </summary>
    private static void BakePlaceholderOneShots()
    {
        BakeDrip();
        BakeNoiseBurst("PLACEHOLDER_OneShot_Debris", 0.55f, 1800f, 0.9985, 8f, Seed + 301, -7f);
        BakeDampedSine("PLACEHOLDER_OneShot_PipeHit", 1.3f, 142f, 3.2f, Seed + 311, -7f);
        BakeCreak();
        BakeNoiseBurst("PLACEHOLDER_OneShot_DistantBoom", 2.6f, 110f, 0.9992, 1.6f, Seed + 331, -6f);
    }

    /// <summary>A short sine with a fast downward pitch sweep and a quick decay.</summary>
    private static void BakeDrip()
    {
        const float durationSeconds = 0.28f;
        int length = (int)(durationSeconds * FullSampleRate);
        float[] buffer = new float[length];

        double phase = 0;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)length;
            double hz = Mathf.Lerp(1450f, 520f, Mathf.Sqrt(t));
            phase += 2.0 * Math.PI * hz / FullSampleRate;

            float envelope = Mathf.Exp(-11f * t);
            buffer[i] = (float)(Math.Sin(phase) * envelope);
        }

        ApplyAttackFade(buffer, FullSampleRate);
        NormalizeToPeakDb(buffer, -8f);
        WriteClip("PLACEHOLDER_OneShot_Drip", buffer, FullSampleRate);
    }

    /// <summary>Band-passed noise with an exponential decay — a stand-in for anything impact-like.</summary>
    private static void BakeNoiseBurst(string clipName, float durationSeconds, float centreHz,
                                       double resonance, float decayRate, int seed, float peakDb)
    {
        int length = (int)(durationSeconds * FullSampleRate);
        float[] buffer = new float[length];

        System.Random rng = new System.Random(seed);
        for (int i = 0; i < length; i++)
            buffer[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        Resonator(buffer, centreHz, resonance, FullSampleRate);

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)length;
            buffer[i] *= Mathf.Exp(-decayRate * t);
        }

        ApplyAttackFade(buffer, FullSampleRate);
        NormalizeToPeakDb(buffer, peakDb);
        WriteClip(clipName, buffer, FullSampleRate);
    }

    /// <summary>A damped sine with an inharmonic partial — roughly a struck metal pipe.</summary>
    private static void BakeDampedSine(string clipName, float durationSeconds, float baseHz,
                                       float decayRate, int seed, float peakDb)
    {
        int length = (int)(durationSeconds * FullSampleRate);
        float[] buffer = new float[length];

        System.Random rng = new System.Random(seed);
        double phaseA = rng.NextDouble() * 2.0 * Math.PI;
        double phaseB = rng.NextDouble() * 2.0 * Math.PI;

        double stepA = 2.0 * Math.PI * baseHz / FullSampleRate;
        // 2.76x rather than 2x: metal bars ring inharmonically, and an exact octave sounds tonal.
        double stepB = 2.0 * Math.PI * baseHz * 2.76 / FullSampleRate;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)length;
            float envA = Mathf.Exp(-decayRate * t);
            float envB = Mathf.Exp(-decayRate * 2.4f * t);

            buffer[i] = (float)(Math.Sin(stepA * i + phaseA) * envA * 0.8 +
                                Math.Sin(stepB * i + phaseB) * envB * 0.35);
        }

        ApplyAttackFade(buffer, FullSampleRate);
        NormalizeToPeakDb(buffer, peakDb);
        WriteClip(clipName, buffer, FullSampleRate);
    }

    /// <summary>Slowly amplitude-modulated resonant noise — the "creaking structure" caricature.</summary>
    private static void BakeCreak()
    {
        const float durationSeconds = 1.9f;
        int length = (int)(durationSeconds * FullSampleRate);
        float[] buffer = new float[length];

        System.Random rng = new System.Random(Seed + 321);
        for (int i = 0; i < length; i++)
            buffer[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        Resonator(buffer, 380f, 0.9990, FullSampleRate);

        // A stuttering modulation is what reads as "creak" rather than "hiss".
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)length;
            double stutter = 0.5 + 0.5 * Math.Sin(2.0 * Math.PI * 19.0 * t + Math.Sin(2.0 * Math.PI * 3.0 * t) * 2.0);
            float envelope = Mathf.Sin(t * Mathf.PI);           // slow swell in and out
            buffer[i] *= (float)(stutter * envelope);
        }

        ApplyAttackFade(buffer, FullSampleRate);
        NormalizeToPeakDb(buffer, -8f);
        WriteClip("PLACEHOLDER_OneShot_Creak", buffer, FullSampleRate);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DSP helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Folds the tail of <paramref name="source"/> over its head to make a seamless loop of
    /// <paramref name="outLength"/> samples.
    ///
    /// The crossfade is equal power (sqrt), not linear. Summing two uncorrelated noise signals with
    /// linear gains loses about 3 dB in the middle of the fade, which is an audible dip once per
    /// loop — the exact artefact this function exists to avoid.
    /// </summary>
    private static float[] CrossfadeSeam(float[] source, int outLength, int seamLength)
    {
        float[] output = new float[outLength];

        Array.Copy(source, 0, output, 0, outLength);

        for (int i = 0; i < seamLength; i++)
        {
            float t = i / (float)seamLength;
            float gainIn  = Mathf.Sqrt(t);
            float gainOut = Mathf.Sqrt(1f - t);
            output[i] = source[i] * gainIn + source[outLength + i] * gainOut;
        }

        return output;
    }

    /// <summary>
    /// Multiplies the buffer by (1 + depth * sin) with an integer number of cycles over its whole
    /// length, so the modulation itself wraps seamlessly.
    /// </summary>
    private static void ApplyPeriodicTremolo(float[] buffer, int cycles, float depth)
    {
        if (cycles <= 0 || depth <= 0f) return;

        double step = 2.0 * Math.PI * cycles / buffer.Length;
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= (float)(1.0 + depth * Math.Sin(step * i));
    }

    /// <summary>One-pole lowpass, in place.</summary>
    private static void OnePoleLowpass(float[] buffer, float cutoffHz, int sampleRate)
    {
        double a = 1.0 - Math.Exp(-2.0 * Math.PI * cutoffHz / sampleRate);
        double state = 0;

        for (int i = 0; i < buffer.Length; i++)
        {
            state += a * (buffer[i] - state);
            buffer[i] = (float)state;
        }
    }

    /// <summary>
    /// Two-pole resonator, in place. <paramref name="resonance"/> is the pole radius: closer to 1
    /// is a narrower, longer-ringing peak. Bandwidth is roughly (1 - r) * sampleRate / pi.
    /// </summary>
    private static void Resonator(float[] buffer, float centreHz, double resonance, int sampleRate)
    {
        double w  = 2.0 * Math.PI * centreHz / sampleRate;
        double a1 = 2.0 * resonance * Math.Cos(w);
        double a2 = -resonance * resonance;
        double gain = 1.0 - resonance;

        double y1 = 0, y2 = 0;

        for (int i = 0; i < buffer.Length; i++)
        {
            double y = gain * buffer[i] + a1 * y1 + a2 * y2;
            y2 = y1;
            y1 = y;
            buffer[i] = (float)y;
        }
    }

    /// <summary>
    /// A 2 ms fade at the head of a one-shot. Without it, a buffer that happens to start away from
    /// zero clicks on every playback.
    /// </summary>
    private static void ApplyAttackFade(float[] buffer, int sampleRate)
    {
        int fade = Mathf.Min(buffer.Length, (int)(0.002f * sampleRate));
        for (int i = 0; i < fade; i++)
            buffer[i] *= i / (float)fade;
    }

    /// <summary>
    /// Scales the buffer so its loudest sample sits at <paramref name="targetDb"/> dBFS. Never
    /// normalise these to 0 dBFS — the headroom is what keeps the sub layer from clipping the
    /// Ambience bus once several layers sum.
    /// </summary>
    private static void NormalizeToPeakDb(float[] buffer, float targetDb)
    {
        float peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float magnitude = Mathf.Abs(buffer[i]);
            if (magnitude > peak) peak = magnitude;
        }

        if (peak <= 1e-6f) return;

        float target = Mathf.Pow(10f, targetDb / 20f);
        float scale = target / peak;

        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= scale;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Asset writing
    // ──────────────────────────────────────────────────────────────────────────

    private static void WriteClip(string clipName, float[] samples, int sampleRate)
    {
        string path = $"{OutputFolder}/{clipName}.wav";
        string systemPath = AssetPathToSystemPath(path);

        WriteWavMono16(systemPath, samples, sampleRate);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        ConfigureImporter(path, sampleRate);
    }

    /// <summary>
    /// Writes a mono 16-bit PCM RIFF/WAVE file. BinaryWriter is little-endian on every platform
    /// Unity targets, which is what WAV wants.
    /// </summary>
    private static void WriteWavMono16(string systemPath, float[] samples, int sampleRate)
    {
        const int channels = 1;
        const int bitsPerSample = 16;

        int dataSize = samples.Length * channels * (bitsPerSample / 8);

        Directory.CreateDirectory(Path.GetDirectoryName(systemPath));

        using (FileStream stream = new FileStream(systemPath, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            // Chunk ids are written as raw bytes, not as char[]: BinaryWriter.Write(char[]) runs the
            // characters through its encoding, which is the wrong contract for a binary tag even
            // though it happens to work for ASCII.
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                                       // fmt chunk size
            writer.Write((short)1);                                 // 1 = uncompressed PCM
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * (bitsPerSample / 8));  // byte rate
            writer.Write((short)(channels * (bitsPerSample / 8)));      // block align
            writer.Write((short)bitsPerSample);

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                writer.Write((short)Mathf.RoundToInt(clamped * 32767f));
            }
        }
    }

    /// <summary>
    /// Sets the import settings the generated clips need. Doing this here is what makes the tool
    /// complete — otherwise a designer has to set six fields by hand on every clip, and getting one
    /// of them wrong is silent.
    ///
    /// PCM, never Vorbis: a perceptual codec treats noise as its worst case, and its block padding
    /// destroys the sample-exact seam. (Vorbis IS the right choice for a real recorded bed — a 45 s
    /// stereo PCM file is 7.9 MB against roughly 600 KB compressed, and a signal with no transients
    /// by design hides the seam risk. That trade only applies to sourced content, not to these.)
    ///
    /// forceToMono is left OFF deliberately: these files are already mono, so there is nothing to
    /// downmix, and that also sidesteps the importer's normalize pass — which the project has
    /// enabled by default and which would undo the deliberate peak levels set above.
    ///
    /// loadInBackground off: the bed and the drones have to be running at level start, and a
    /// background load is an audibly silent first second.
    /// </summary>
    private static void ConfigureImporter(string assetPath, int sampleRate)
    {
        AudioImporter importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[AmbienceToneBaker] No AudioImporter at '{assetPath}'. " +
                             "Its import settings were left at the project defaults — check " +
                             "Compression Format is PCM by hand.");
            return;
        }

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;

        // preloadAudioData lives on the sample settings, not on the importer: the top-level
        // AudioImporter.preloadAudioData was deprecated in 2022.2 in favour of this one. If this
        // line ever fails to compile on a future Unity version, the replacement is on the importer's
        // per-platform settings rather than here.
        settings.preloadAudioData = true;

        importer.defaultSampleSettings = settings;
        importer.forceToMono = false;
        importer.loadInBackground = false;
        importer.ambisonic = false;

        importer.SaveAndReimport();
    }

    /// <summary>
    /// Converts an "Assets/..." project path to an absolute filesystem path.
    ///
    /// Built from Application.dataPath rather than Path.GetFullPath, which would resolve against
    /// the process working directory. That happens to be the project root today, but it is not a
    /// documented guarantee and it is not worth depending on.
    /// </summary>
    private static string AssetPathToSystemPath(string assetPath)
    {
        const string assetsPrefix = "Assets/";

        if (!assetPath.StartsWith(assetsPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Expected a path under 'Assets/', got '{assetPath}'.", nameof(assetPath));
        }

        // Application.dataPath is "<project>/Assets", so the prefix is dropped before joining.
        return Path.Combine(Application.dataPath, assetPath.Substring(assetsPrefix.Length))
                   .Replace('\\', '/');
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
