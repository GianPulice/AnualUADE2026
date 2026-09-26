using System;
using UnityEngine;

/// <summary>
/// Tuning of a skill check sequence, Dead by Daylight style (Central Puzzle 2 — Ventilation Hub).
///
/// A sequence is a list of checks played back to back. Each check: a warning "ding", the ring pops
/// up with a success zone somewhere on it, and the needle sweeps ONE lap clockwise from twelve
/// o'clock. [E] inside the zone passes, inside its leading "perfect" slice passes and gives time back
/// to the module; [E] anywhere else — or no press before the lap ends — is a miss: time is taken off
/// the module and the sequence ends there, the overlay closing on it. The next try starts again from
/// the first check: the only way through is every check hit in a row.
///
/// The steps go from easy to hard (faster needle, narrower zone), so the last checks are the test.
///
/// Angles follow a clock face: 0 = twelve o'clock, growing clockwise — the same convention as
/// <see cref="UIRingArc"/>, so the data maps to the drawing one to one.
/// </summary>
[CreateAssetMenu(fileName = "SO_SkillCheckData", menuName = "Scriptable Objects/Puzzles/Skill Check Data")]
public class SO_SkillCheckData : ScriptableObject
{
    [Serializable]
    public struct SkillCheckStep
    {
        [Tooltip("Seconds the needle takes for its one lap. Lower = harder.")]
        [Min(0.2f)] public float sweepDuration;

        [Tooltip("Width of the success zone, in degrees.")]
        [Range(5f, 180f)] public float successZoneDegrees;

        [Tooltip("Width of the perfect slice at the START of the success zone (the part the needle " +
                 "reaches first), in degrees. 0 = no perfect zone on this check.")]
        [Range(0f, 60f)] public float perfectZoneDegrees;

        [Tooltip("Seconds taken off the active module's timer on a miss.")]
        [Min(0f)] public float failTimePenalty;

        [Tooltip("Seconds given back to the active module's timer on a perfect hit (never past its " +
                 "full duration).")]
        [Min(0f)] public float perfectTimeBonus;
    }

    [Serializable]
    public struct ZoneSector
    {
        [Tooltip("Where the sector starts, in degrees (0 = twelve o'clock, clockwise).")]
        [Range(0f, 360f)] public float startDegrees;

        [Tooltip("Where the sector ends. The success zone fits inside the sector when it can; a zone " +
                 "wider than the sector starts at the sector's start (so the perfect slice always " +
                 "lands inside it), pulled back if needed so it never runs past twelve o'clock.")]
        [Range(0f, 360f)] public float endDegrees;

        [Tooltip("Relative chance of the zone landing in this sector (RouletteSelection). 0 = never, " +
                 "unless every sector is 0 — then they are all equally likely.")]
        [Min(0f)] public float weight;
    }

    [Header("Checks (played in order, easiest first)")]
    public SkillCheckStep[] steps =
    {
        new SkillCheckStep { sweepDuration = 1.45f, successZoneDegrees = 90f, perfectZoneDegrees = 14f, failTimePenalty = 5f, perfectTimeBonus = 3f },
        new SkillCheckStep { sweepDuration = 1.15f, successZoneDegrees = 60f, perfectZoneDegrees = 10f, failTimePenalty = 5f, perfectTimeBonus = 3f },
        new SkillCheckStep { sweepDuration = 0.95f, successZoneDegrees = 40f, perfectZoneDegrees = 6f, failTimePenalty = 5f, perfectTimeBonus = 3f },
        new SkillCheckStep { sweepDuration = 0.8f, successZoneDegrees = 26f, perfectZoneDegrees = 4f, failTimePenalty = 5f, perfectTimeBonus = 3f },
    };

    [Header("Where the zone lands")]
    [Tooltip("Candidate sectors for the success zone, picked by weight every attempt. None of the " +
             "defaults comes near twelve o'clock: the needle starts there, and the player needs time " +
             "to react, as in DBD.")]
    public ZoneSector[] zoneSectors =
    {
        new ZoneSector { startDegrees = 110f, endDegrees = 180f, weight = 3f }, // right
        new ZoneSector { startDegrees = 180f, endDegrees = 270f, weight = 2f }, // bottom
        new ZoneSector { startDegrees = 270f, endDegrees = 340f, weight = 1f }, // left
    };

    [Header("Rhythm (seconds)")]
    [Tooltip("From the warning ding to the needle starting to move. [E] is ignored in this window.")]
    [Min(0f)] public float warningLeadTime = 0.6f;

    [Tooltip("How long the result (GOOD / PERFECT / MISS) holds on screen. After a MISS the overlay " +
             "closes as soon as it is over.")]
    [Min(0f)] public float resultHoldTime = 0.35f;

    [Tooltip("Random pause between one check's result and the next warning.")]
    [Min(0f)] public float gapBetweenChecksMin = 0.4f;
    [Min(0f)] public float gapBetweenChecksMax = 0.9f;

    [Tooltip("How long the final STABILIZED message holds before the overlay closes.")]
    [Min(0f)] public float completeHoldTime = 0.8f;

    [Header("Audio (placeholders until audio delivers the skill check set)")]
    public AudioClip warningClip;
    public AudioClip goodClip;
    public AudioClip perfectClip;
    public AudioClip missClip;
    public AudioClip completeClip;
    [Range(0f, 1f)] public float volume = 0.9f;

    public int TotalSteps => steps != null ? steps.Length : 0;

    private void OnValidate()
    {
        if (gapBetweenChecksMax < gapBetweenChecksMin) gapBetweenChecksMax = gapBetweenChecksMin;

        if (zoneSectors == null) return;
        for (int i = 0; i < zoneSectors.Length; i++)
        {
            if (zoneSectors[i].endDegrees < zoneSectors[i].startDegrees)
                zoneSectors[i].endDegrees = zoneSectors[i].startDegrees;
        }
    }
}
