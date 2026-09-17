using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every line the Architect can say, with its variants, optional voice clips and the optional
/// system alert shown at the top of the screen while it plays.
///
/// The text is the final recording script from architect_voice_system_spec v1.2 and stays in
/// English: do not translate or rewrite it here. Clips are optional while the recordings do not
/// exist — without one, the line lasts as long as its text takes to read.
/// </summary>
[CreateAssetMenu(fileName = "SO_ArchitectLineBank", menuName = "Scriptable Objects/Architect/Line Bank")]
public class SO_ArchitectLineBank : ScriptableObject
{
    [Serializable]
    public class Line
    {
        public ArchitectLineID id;
        public ArchitectLineCategory category;

        [TextArea(1, 4)]
        [Tooltip("One per variant. Picked without repeats until every variant has played.")]
        public string[] variants = Array.Empty<string>();

        [Tooltip("Voice clip per variant, same order as the texts. Missing entries play text only.")]
        public AudioClip[] clips = Array.Empty<AudioClip>();

        [Tooltip("Short system message at the top of the screen while the line plays. Empty = none. " +
                 "{0} is the module label (M1, M2, M3) for module lines.")]
        public string alert = string.Empty;

        [Tooltip("Plays even with a menu, the inventory or a result screen open (spec §7: ARC_03 and " +
                 "ARC_10 are part of those moments).")]
        public bool playsOverMenus;
    }

    [SerializeField] private List<Line> lines = new List<Line>();

    [Header("Timing without a voice clip")]
    [Tooltip("Reading speed used to size a line that has no clip, in characters per second.")]
    [SerializeField, Min(1f)] private float readingCharsPerSecond = 14f;

    [Tooltip("Shortest time a line stays on screen.")]
    [SerializeField, Min(0.5f)] private float minLineDuration = 2f;

    [Tooltip("Extra seconds after the clip ends (or after the reading time), before the text fades.")]
    [SerializeField, Min(0f)] private float holdAfterLine = 0.6f;

    public IReadOnlyList<Line> Lines => lines;
    public float ReadingCharsPerSecond => readingCharsPerSecond;
    public float MinLineDuration => minLineDuration;
    public float HoldAfterLine => holdAfterLine;

    public Line Find(ArchitectLineID id)
    {
        foreach (Line line in lines)
            if (line != null && line.id == id) return line;
        return null;
    }

#if UNITY_EDITOR
    /// <summary>Editor-only: lets the setup tool fill a fresh bank from the spec.</summary>
    public List<Line> EditableLines => lines;
#endif
}
