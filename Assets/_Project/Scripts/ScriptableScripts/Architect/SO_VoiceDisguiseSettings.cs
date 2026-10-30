using UnityEngine;

/// <summary>
/// How the Architect's voice is disguised: the knobs of <see cref="VoiceDisguiseProcessor"/>, saved
/// as an asset so presets can be swapped and tuned without touching code. The goal is a person
/// hiding behind a voice changer, not a clean narrator: a lower, rougher voice squeezed through a
/// small speaker.
///
/// Read by <see cref="ArchitectVoiceDisguise"/>. Editing an asset while the game runs applies on
/// the next frame (<see cref="Version"/> bumps on every inspector edit). Presets live next to the
/// line bank: SO_VoiceDisguise_Scrambler (strong) and SO_VoiceDisguise_Radio (lighter).
/// </summary>
[CreateAssetMenu(fileName = "SO_VoiceDisguiseSettings", menuName = "Scriptable Objects/Architect/Voice Disguise Settings")]
public class SO_VoiceDisguiseSettings : ScriptableObject
{
    [Tooltip("Off: the voice plays clean, as recorded.")]
    [SerializeField] private bool enabled = true;

    [Header("Pitch")]
    [Tooltip("Playback-speed ratio of the pitch shift, duration unchanged. 1 = no shift, 0.8 = about " +
             "4 semitones down, 0.5 = an octave down. Lower sounds heavier but more artefacts creep in.")]
    [SerializeField, Range(0.5f, 1.5f)] private float pitchRatio = 0.82f;

    [Tooltip("Crossfade window of the pitch shifter, in milliseconds. Short windows buzz, long ones echo. " +
             "40-60 is the usual sweet spot; the warble it leaves is part of the sound.")]
    [SerializeField, Range(20f, 100f)] private float pitchWindowMs = 50f;

    [Header("Ring modulator")]
    [Tooltip("Carrier frequency in Hz. Tens of Hz roughen the voice into a growl; hundreds start to " +
             "sound like a robot.")]
    [SerializeField, Range(0f, 400f)] private float ringFrequencyHz = 45f;

    [Tooltip("How much of the ring-modulated voice is mixed in. 0 = off.")]
    [SerializeField, Range(0f, 1f)] private float ringMix = 0.35f;

    [Header("Band-pass")]
    [Tooltip("High-pass cutoff in Hz: cuts the body of the voice.")]
    [SerializeField, Range(20f, 2000f)] private float highPassHz = 320f;

    [Tooltip("Low-pass cutoff in Hz: cuts the air. Keep it above the high-pass.")]
    [SerializeField, Range(1000f, 12000f)] private float lowPassHz = 3200f;

    [Header("Digital grit")]
    [Tooltip("Bits kept per sample. Fewer bits = grittier. 16 = off.")]
    [SerializeField, Range(4, 16)] private int bitDepth = 10;

    [Tooltip("Sample-and-hold: every Nth sample is repeated N times. 1 = off, 2 = half the sample rate.")]
    [SerializeField, Range(1, 8)] private int holdFactor = 2;

    [Header("Output")]
    [Tooltip("Makes up for what the filters take away. The mixer's Voice group volume still applies on top.")]
    [SerializeField, Range(-24f, 12f)] private float outputGainDb = 0f;

    // Not serialized: a counter the consumer only compares against what it last read.
    [System.NonSerialized] private int version = 1;

    public bool Enabled => enabled;

    /// <summary>Bumps every time the asset is edited in the inspector, so the consumer knows to re-read it.</summary>
    public int Version => version;

    /// <summary>
    /// A fresh immutable snapshot for the audio thread. Allocates: call it when <see cref="Version"/>
    /// changes, never per frame or from the audio callback.
    /// </summary>
    public VoiceDisguiseProcessor.Settings CreateProcessorSettings()
    {
        return new VoiceDisguiseProcessor.Settings
        {
            PitchRatio = pitchRatio,
            PitchWindowMs = pitchWindowMs,
            RingFrequencyHz = ringFrequencyHz,
            RingMix = ringMix,
            HighPassHz = highPassHz,
            LowPassHz = lowPassHz,
            BitDepth = bitDepth,
            HoldFactor = holdFactor,
            OutputGain = Mathf.Pow(10f, outputGainDb / 20f),
        };
    }

    private void OnValidate()
    {
        // A high-pass at or above the low-pass would leave nothing to hear.
        if (highPassHz > lowPassHz - 100f) highPassHz = Mathf.Max(20f, lowPassHz - 100f);

        version++;
    }
}
