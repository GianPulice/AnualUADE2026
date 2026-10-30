using System;
using UnityEngine;

/// <summary>
/// One row of SO_CounterplayRules (plan §5.2): enough of one exploit unlocks one counterplay, and
/// from then on it is a bet, never a switch — a deterministic anti-cheese gets learned exactly like
/// the cheese it answers.
///
/// A plain serialisable class and not a type nested in the ScriptableObject, so the arithmetic can
/// sit in WIRED.Nemesis.Logic beside <see cref="HabitLedger"/> and be tested in EditMode: a test
/// assembly cannot see Assembly-CSharp, which is where the ScriptableObject lives.
/// </summary>
[Serializable]
public class CounterplayRule
{
    [Tooltip("What is counted.")]
    [SerializeField] private EExploitKind kind;

    [Tooltip("How many of it unlock the counterplay. Counts decay (Habit Decay), so this is how " +
             "many RECENT ones, not how many ever.")]
    [SerializeField, Min(1)] private int threshold = 3;

    [Tooltip("What the count unlocks. Two rows may unlock the same counterplay (ZoneDefense): the " +
             "likelier of the two wins.")]
    [SerializeField] private ECounterplay unlocks;

    [Tooltip("Chance, 0..1, that the counterplay happens right at the threshold.")]
    [SerializeField, Range(0f, 1f)] private float chanceAtUnlock = 0.35f;

    [Tooltip("Chance added per use beyond the threshold. Chance Cap stops it short of a certainty.")]
    [SerializeField, Range(0f, 1f)] private float chancePerExtraUse = 0.1f;

    public CounterplayRule() { }

    public CounterplayRule(EExploitKind kind, int threshold, ECounterplay unlocks,
                           float chanceAtUnlock, float chancePerExtraUse)
    {
        this.kind = kind;
        this.threshold = threshold;
        this.unlocks = unlocks;
        this.chanceAtUnlock = chanceAtUnlock;
        this.chancePerExtraUse = chancePerExtraUse;
    }

    public EExploitKind Kind => kind;
    public int Threshold => threshold;
    public ECounterplay Unlocks => unlocks;
    public float ChanceAtUnlock => chanceAtUnlock;
    public float ChancePerExtraUse => chancePerExtraUse;

    /// <summary>
    /// Whether <paramref name="count"/> reaches the threshold. A threshold below 1 is read as 1:
    /// a row at 0 would be unlocked before the player had done anything, which is never what a
    /// designer meant (the validator reports it too).
    /// </summary>
    public bool IsMetBy(float count) => count >= Mathf.Max(1, threshold);

    /// <summary>The chance this row gives at <paramref name="count"/>, capped at
    /// <paramref name="cap"/>; 0 below the threshold.</summary>
    public float ChanceAt(float count, float cap)
    {
        if (!IsMetBy(count)) return 0f;

        float extra = count - Mathf.Max(1, threshold);
        return Mathf.Clamp01(Mathf.Min(cap, chanceAtUnlock + chancePerExtraUse * extra));
    }
}
