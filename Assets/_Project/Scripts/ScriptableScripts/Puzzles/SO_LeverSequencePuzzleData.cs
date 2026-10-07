using System;
using UnityEngine;

/// <summary>
/// Boiler room puzzle (Central Puzzle 2 — SP1, auditory memory). The boiler plays a sequence of
/// knocks; every lever of the panel in the next room makes one of those knocks, and pulling the
/// levers in the order heard solves it.
///
/// One asset drives both prefabs: the boiler plays <see cref="Sequence"/> (each step is a lever's
/// knock followed by a pause, so the rhythm is authored here too) and the lever panel checks the
/// order. Only the order is judged — the rhythm is there to help the player remember it.
/// </summary>
[CreateAssetMenu(fileName = "SO_LeverSequencePuzzleData", menuName = "Scriptable Objects/Puzzles/Lever Sequence Puzzle Data")]
public class SO_LeverSequencePuzzleData : ScriptableObject
{
    [Serializable]
    public struct Knock
    {
        [Tooltip("Lever whose knock plays here, counted from 1 (left to right on the panel).")]
        [Min(1)] public int lever;

        [Tooltip("Silence after this knock before the next one, in seconds. This is the rhythm.")]
        [Min(0f)] public float pauseAfter;
    }

    [Tooltip("Completed in PuzzleStateManager when the levers are pulled in the right order.")]
    [SerializeField] private string puzzleId;

    [Header("Levers")]
    [Tooltip("Levers on the panel. The prefab carries 6; the ones past this number are hidden.")]
    [SerializeField, Range(1, 6)] private int leverCount = 5;

    [Tooltip("The sequence the boiler plays and the panel expects, in order. A lever can appear " +
             "more than once.")]
    [SerializeField] private Knock[] sequence =
    {
        new Knock { lever = 2, pauseAfter = 0.9f },
        new Knock { lever = 4, pauseAfter = 0.35f },
        new Knock { lever = 1, pauseAfter = 1.2f },
        new Knock { lever = 3, pauseAfter = 0.25f },
        new Knock { lever = 3, pauseAfter = 0.6f },
    };

    [Header("Sounds")]
    [Tooltip("One knock per lever (element 0 = lever 1). An empty slot plays a generated placeholder " +
             "knock with its own pitch, until audio delivers the real ones.")]
    [SerializeField] private AudioClip[] leverKnockClips = new AudioClip[0];

    [Tooltip("Optional: a single clip with the whole sequence, designed by audio. When set, the " +
             "boiler plays it instead of building the sequence from the lever knocks.")]
    [SerializeField] private AudioClip boilerSequenceClip;

    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Tooltip("Distance at which the knocks fade out completely. Keep it shorter than the distance " +
             "between the boiler and the lever panel, so one cannot be heard from the other.")]
    [SerializeField, Min(1f)] private float maxHearingDistance = 18f;

    [Tooltip("Silence between pressing [E] on the boiler and its first knock.")]
    [SerializeField, Min(0f)] private float leadInSeconds = 0.8f;

    [SoundId, SerializeField] private string boilerStartSoundId = "sfx_interaction_valvula";
    [SoundId, SerializeField] private string wrongSequenceSoundId = "sfx_secuencia_incorrecta_panel_electrico";
    [SoundId, SerializeField] private string completedSoundId = "sfx_subpuzzle_1_completo";

    [Header("Prompts")]
    [SerializeField] private string boilerPrompt = "Activate the boiler";
    [SerializeField] private string boilerPlayingInfo = "Listen...";
    [Tooltip("{0} = lever number.")]
    [SerializeField] private string leverPrompt = "Pull lever {0}";

    [Header("Feel")]
    [Tooltip("Seconds a lever takes to go down and spring back up.")]
    [SerializeField, Min(0.05f)] private float leverPullSeconds = 0.4f;

    public string PuzzleId => puzzleId;
    public int LeverCount => leverCount;
    public Knock[] Sequence => sequence ?? Array.Empty<Knock>();
    public AudioClip BoilerSequenceClip => boilerSequenceClip;
    public float Volume => volume;
    public float MaxHearingDistance => maxHearingDistance;
    public float LeadInSeconds => leadInSeconds;
    public string BoilerStartSoundId => boilerStartSoundId;
    public string WrongSequenceSoundId => wrongSequenceSoundId;
    public string CompletedSoundId => completedSoundId;
    public string BoilerPrompt => boilerPrompt;
    public string BoilerPlayingInfo => boilerPlayingInfo;
    public string LeverPrompt => leverPrompt;
    public float LeverPullSeconds => leverPullSeconds;

    /// <summary>The knock of a lever (counted from 1): the authored clip, or the placeholder.</summary>
    public AudioClip GetKnockClip(int lever)
    {
        int index = lever - 1;
        if (leverKnockClips != null && index >= 0 && index < leverKnockClips.Length && leverKnockClips[index] != null)
            return leverKnockClips[index];
        return PlaceholderKnockClips.Get(lever);
    }

    private void OnValidate()
    {
        if (sequence == null) return;
        for (int i = 0; i < sequence.Length; i++)
            sequence[i].lever = Mathf.Clamp(sequence[i].lever, 1, leverCount);
    }
}
