using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders the Architect's voice disguise offline so it can be judged by ear without entering Play:
/// runs a few real lines from the line bank through every <see cref="SO_VoiceDisguiseSettings"/>
/// preset in the project, with the same <see cref="VoiceDisguiseProcessor"/> the game uses, and
/// writes 16-bit PCM WAVs next to the untouched original for comparison.
///
/// The WAVs go to AudioPreview/ArchitectVoice/ in the project root, outside Assets so Unity does not
/// import them. Needs the clips to be Decompress On Load (the project's default for the Architect).
///
/// Tools ▸ Architect ▸ Render Voice Disguise Previews.
/// </summary>
public static class ArchitectVoiceDisguisePreview
{
    private const string LogTag = "[ArchitectVoiceDisguisePreview]";
    private const string BankPath = "Assets/_Project/ScriptableObjects/Architect/SO_ArchitectLineBank.asset";

    // The shifter delays the voice by up to its window: without this the processed render would end
    // with the last 50 ms cut off.
    private const float TailSeconds = 0.25f;

    private readonly struct Sample
    {
        public readonly string Label;
        public readonly string Text;
        public readonly AudioClip Clip;

        public Sample(string label, string text, AudioClip clip)
        {
            Label = label;
            Text = text;
            Clip = clip;
        }
    }

    [MenuItem("Tools/Architect/Render Voice Disguise Previews")]
    public static void RenderPreviews()
    {
        SO_ArchitectLineBank bank = AssetDatabase.LoadAssetAtPath<SO_ArchitectLineBank>(BankPath);
        if (bank == null)
        {
            Debug.LogError($"{LogTag} No line bank at {BankPath}.");
            return;
        }

        Sample[] samples =
        {
            PickSample(bank, ArchitectLineID.WakeUpMoment1, 0, "WakeUp1"),
            PickSample(bank, ArchitectLineID.Captured, 2, "Captured"),
            PickSample(bank, ArchitectLineID.Idle, 3, "Idle"),
        };

        SO_VoiceDisguiseSettings[] presets = FindPresets();
        if (presets.Length == 0)
        {
            Debug.LogError($"{LogTag} No SO_VoiceDisguiseSettings asset in the project.");
            return;
        }

        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "AudioPreview", "ArchitectVoice");
        Directory.CreateDirectory(folder);

        int written = 0;
        foreach (Sample sample in samples)
        {
            if (sample.Clip == null) continue;

            if (!TryRead(sample.Clip, out float[] data))
            {
                Debug.LogWarning($"{LogTag} Could not read '{sample.Clip.name}' ({sample.Label}). " +
                                 "Its load type must be Decompress On Load.", sample.Clip);
                continue;
            }

            int channels = sample.Clip.channels;
            int frequency = sample.Clip.frequency;

            string original = Path.Combine(folder, $"{sample.Label}__original.wav");
            WriteWav(original, data, channels, frequency);
            Debug.Log($"{LogTag} {original}  (\"{sample.Text}\")");
            written++;

            foreach (SO_VoiceDisguiseSettings preset in presets)
            {
                float[] processed = Disguise(data, channels, frequency, preset);

                string path = Path.Combine(folder, $"{sample.Label}__{preset.name}.wav");
                WriteWav(path, processed, channels, frequency);
                Debug.Log($"{LogTag} {path}");
                written++;
            }
        }

        Debug.Log($"{LogTag} {written} files in {folder}");
    }

    // ── Rendering ────────────────────────────────────────────────────────────────────────

    private static float[] Disguise(float[] data, int channels, int frequency, SO_VoiceDisguiseSettings preset)
    {
        int tailSamples = Mathf.CeilToInt(TailSeconds * frequency) * channels;
        float[] buffer = new float[data.Length + tailSamples];
        Array.Copy(data, buffer, data.Length);

        VoiceDisguiseProcessor processor = new VoiceDisguiseProcessor(frequency, Mathf.Max(channels, 1));
        processor.SetSettings(preset.CreateProcessorSettings());
        processor.Process(buffer, channels);
        return buffer;
    }

    private static bool TryRead(AudioClip clip, out float[] data)
    {
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();

        data = new float[clip.samples * clip.channels];
        return clip.GetData(data, 0);
    }

    // ── Assets ───────────────────────────────────────────────────────────────────────────

    private static Sample PickSample(SO_ArchitectLineBank bank, ArchitectLineID id, int preferredVariant, string label)
    {
        SO_ArchitectLineBank.Line line = bank.Find(id);
        if (line == null || line.clips == null || line.clips.Length == 0)
        {
            Debug.LogWarning($"{LogTag} {id} has no clips in the bank.");
            return new Sample(label, string.Empty, null);
        }

        int variant = preferredVariant;
        if (variant >= line.clips.Length || line.clips[variant] == null)
        {
            variant = Array.FindIndex(line.clips, c => c != null);
            if (variant < 0)
            {
                Debug.LogWarning($"{LogTag} {id} has no clips in the bank.");
                return new Sample(label, string.Empty, null);
            }
        }

        string text = variant < line.variants.Length ? ArchitectLinePages.Joined(line.variants[variant]) : string.Empty;
        return new Sample($"{label}_v{variant}", text, line.clips[variant]);
    }

    private static SO_VoiceDisguiseSettings[] FindPresets()
    {
        string[] guids = AssetDatabase.FindAssets("t:SO_VoiceDisguiseSettings");
        SO_VoiceDisguiseSettings[] presets = new SO_VoiceDisguiseSettings[guids.Length];

        for (int i = 0; i < guids.Length; i++)
        {
            presets[i] = AssetDatabase.LoadAssetAtPath<SO_VoiceDisguiseSettings>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
        }

        Array.Sort(presets, (a, b) => string.CompareOrdinal(a.name, b.name));
        return presets;
    }

    // ── WAV ──────────────────────────────────────────────────────────────────────────────

    private static void WriteWav(string path, float[] samples, int channels, int sampleRate)
    {
        int dataBytes = samples.Length * 2;

        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                           // PCM chunk size.
            writer.Write((short)1);                     // PCM.
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * 2);    // Bytes per second.
            writer.Write((short)(channels * 2));        // Bytes per frame.
            writer.Write((short)16);

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
            foreach (float sample in samples)
            {
                int value = Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * 32767f);
                writer.Write((short)value);
            }
        }
    }
}
