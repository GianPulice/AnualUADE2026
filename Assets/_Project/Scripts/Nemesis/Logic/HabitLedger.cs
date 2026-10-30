using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The books behind PlayerHabitTracker: what has been counted, how it drains and what it unlocks
/// (plan §5, Phase 3), plus the per-spot usage meter (plan §17.6, D23, Phase 2D).
///
/// No scene, no singleton and no clock: the caller says what time it is. That is what lets the
/// thresholds be tested in EditMode, and it keeps the ledger honest about its one job — it knows
/// only what it was told. Deciding what deserves counting (R1: escapes, not attempts) is the
/// tracker's business; this only keeps the score.
///
/// DECAY IS LAZY. Nothing ticks. Every value remembers when it last went up, and reading it works
/// out how much has drained since. A value nobody looks at costs nothing, and a paused game drains
/// nothing, because the caller passes scaled time.
///
/// Spots are keyed by id and not by reference: the tracker outlives the level (it is in the Data
/// scene), and a HidingSpot does not.
/// </summary>
public class HabitLedger
{
    /// <summary>One used spot, as <see cref="CollectSpots"/> reports it.</summary>
    public readonly struct SpotReading
    {
        public readonly string SpotId;
        public readonly float Meter;
        public readonly int Uses;
        public readonly int Escapes;

        public SpotReading(string spotId, float meter, int uses, int escapes)
        {
            SpotId = spotId;
            Meter = meter;
            Uses = uses;
            Escapes = escapes;
        }
    }

    private struct Tally
    {
        public float Value;
        public float RaisedAt;
        public int Total;
    }

    private struct SpotTally
    {
        public float Value;
        public float RaisedAt;
        public int Uses;
        public int Escapes;
    }

    private static readonly Comparison<SpotReading> ByMeterDescending =
        (a, b) => b.Meter.CompareTo(a.Meter);

    private readonly Dictionary<EExploitKind, Tally> exploits = new Dictionary<EExploitKind, Tally>();
    private readonly Dictionary<string, SpotTally> spots =
        new Dictionary<string, SpotTally>(StringComparer.Ordinal);
    private readonly HashSet<ECounterplay> ran = new HashSet<ECounterplay>();

    private IHabitRules rules;

    public HabitLedger(IHabitRules rules) => Rules = rules;

    /// <summary>The numbers to score with. Swappable — a difficulty setting (D8) would swap them —
    /// without losing what has been counted.</summary>
    public IHabitRules Rules
    {
        get => rules;
        set => rules = value ?? throw new ArgumentNullException(nameof(value));
    }

    // ── Exploits ────────────────────────────────────────────────────────────

    /// <summary>Counts one more <paramref name="kind"/> at <paramref name="now"/>; returns the count
    /// after it.</summary>
    public float RegisterExploit(EExploitKind kind, float now)
    {
        exploits.TryGetValue(kind, out Tally tally);

        tally.Value = CurrentValue(tally, now) + 1f;
        tally.RaisedAt = now;
        tally.Total++;

        exploits[kind] = tally;
        return tally.Value;
    }

    /// <summary>The count of <paramref name="kind"/> at <paramref name="now"/>, drain applied.
    /// </summary>
    public float GetExploitCount(EExploitKind kind, float now) =>
        exploits.TryGetValue(kind, out Tally tally) ? CurrentValue(tally, now) : 0f;

    /// <summary>How many times <paramref name="kind"/> was counted this session, drain ignored.
    /// For the log and the HUD, where "has this happened at all" is its own question.</summary>
    public int GetExploitTotal(EExploitKind kind) =>
        exploits.TryGetValue(kind, out Tally tally) ? tally.Total : 0;

    // ── Spots ───────────────────────────────────────────────────────────────

    /// <summary>The player hid in <paramref name="spotId"/>: its meter goes up by a use. Returns
    /// the meter after it.</summary>
    public float AddSpotUse(string spotId, float now)
    {
        if (string.IsNullOrEmpty(spotId)) return 0f;

        spots.TryGetValue(spotId, out SpotTally tally);

        tally.Value = CurrentValue(tally, now) + rules.SpotUsePoints;
        tally.RaisedAt = now;
        tally.Uses++;

        spots[spotId] = tally;
        return tally.Value;
    }

    /// <summary>The player got away from <paramref name="spotId"/> with the Nemesis hunting nearby:
    /// the extra point of D23, and one more escape on the spot's record. Returns the meter after it.
    /// </summary>
    public float AddSpotEscape(string spotId, float now)
    {
        if (string.IsNullOrEmpty(spotId)) return 0f;

        spots.TryGetValue(spotId, out SpotTally tally);

        tally.Value = CurrentValue(tally, now) + rules.SpotHuntedBonus;
        tally.RaisedAt = now;
        tally.Escapes++;

        spots[spotId] = tally;
        return tally.Value;
    }

    /// <summary>The meter of <paramref name="spotId"/> at <paramref name="now"/>, drain applied;
    /// 0 for a spot never used.</summary>
    public float GetSpotMeter(string spotId, float now) =>
        spotId != null && spots.TryGetValue(spotId, out SpotTally tally) ? CurrentValue(tally, now) : 0f;

    /// <summary>Times the player hid in <paramref name="spotId"/> this session.</summary>
    public int GetSpotUses(string spotId) =>
        spotId != null && spots.TryGetValue(spotId, out SpotTally tally) ? tally.Uses : 0;

    /// <summary>Times the player got away from <paramref name="spotId"/> with the Nemesis hunting
    /// nearby, this session.</summary>
    public int GetSpotEscapes(string spotId) =>
        spotId != null && spots.TryGetValue(spotId, out SpotTally tally) ? tally.Escapes : 0;

    /// <summary>Whether <paramref name="spotId"/> is used enough to be checked first (C2).</summary>
    public bool IsPrioritySpot(string spotId, float now) =>
        MeetsThreshold(GetSpotMeter(spotId, now), rules.SpotPriorityThreshold);

    /// <summary>Whether <paramref name="spotId"/> is used enough to be torn apart (plan §3.6).
    /// </summary>
    public bool IsBurnable(string spotId, float now) =>
        MeetsThreshold(GetSpotMeter(spotId, now), rules.SpotBurnThreshold);

    /// <summary>Chance, 0..1, of opening <paramref name="spotId"/> while checking the area it is in
    /// (D23: meter × chance per point, capped). 0 for a spot never used or fully drained.</summary>
    public float GetOpenChance(string spotId, float now)
    {
        float meter = GetSpotMeter(spotId, now);
        if (meter <= 0f) return 0f;

        return Mathf.Clamp01(Mathf.Min(rules.SpotOpenChanceCap, meter * rules.SpotOpenChancePerPoint));
    }

    /// <summary>Fills <paramref name="results"/> with every spot whose meter has not drained to
    /// zero, highest first. Returns how many.</summary>
    public int CollectSpots(List<SpotReading> results, float now)
    {
        if (results == null) return 0;
        results.Clear();

        foreach (KeyValuePair<string, SpotTally> entry in spots)
        {
            float meter = CurrentValue(entry.Value, now);
            if (meter <= 0f) continue;

            results.Add(new SpotReading(entry.Key, meter, entry.Value.Uses, entry.Value.Escapes));
        }

        results.Sort(ByMeterDescending);
        return results.Count;
    }

    // ── Counterplays ────────────────────────────────────────────────────────

    /// <summary>
    /// Whether any row unlocks <paramref name="counterplay"/> at <paramref name="now"/>.
    ///
    /// Rows only. The three meter-scored counterplays (CheckHidingSpots, PrioritizeSuspiciousSpots,
    /// BurnHidingSpot) are answered per spot, by the spot methods above, and read false here unless
    /// a designer gives them a row of their own.
    /// </summary>
    public bool IsUnlocked(ECounterplay counterplay, float now)
    {
        IReadOnlyList<CounterplayRule> rows = rules.Rules;
        if (rows == null) return false;

        for (int i = 0; i < rows.Count; i++)
        {
            CounterplayRule row = rows[i];
            if (row == null || row.Unlocks != counterplay) continue;

            if (row.IsMetBy(GetExploitCount(row.Kind, now))) return true;
        }

        return false;
    }

    /// <summary>The chance, 0..1, that <paramref name="counterplay"/> happens when its moment comes:
    /// the likeliest of the rows that unlock it, 0 while none does.</summary>
    public float GetChance(ECounterplay counterplay, float now)
    {
        IReadOnlyList<CounterplayRule> rows = rules.Rules;
        if (rows == null) return 0f;

        float best = 0f;
        for (int i = 0; i < rows.Count; i++)
        {
            CounterplayRule row = rows[i];
            if (row == null || row.Unlocks != counterplay) continue;

            best = Mathf.Max(best, row.ChanceAt(GetExploitCount(row.Kind, now), rules.ChanceCap));
        }

        return best;
    }

    /// <summary>Whether <paramref name="counterplay"/> has already run once this session (R3).
    /// </summary>
    public bool HasRun(ECounterplay counterplay) => ran.Contains(counterplay);

    /// <summary>Records that <paramref name="counterplay"/> ran. True the first time only — the run
    /// R3 requires the player to be able to see or hear.</summary>
    public bool MarkRun(ECounterplay counterplay) => ran.Add(counterplay);

    /// <summary>Forgets everything: New Game.</summary>
    public void Clear()
    {
        exploits.Clear();
        spots.Clear();
        ran.Clear();
    }

    // ── Decay ───────────────────────────────────────────────────────────────

    private float CurrentValue(Tally tally, float now) =>
        Drained(tally.Value, tally.RaisedAt, now, rules.HabitDecayDelayMinutes, rules.HabitDecayPerMinute);

    private float CurrentValue(SpotTally tally, float now) =>
        Drained(tally.Value, tally.RaisedAt, now, rules.SpotDecayDelayMinutes, rules.SpotDecayPerMinute);

    /// <summary>
    /// <paramref name="value"/> as it stands at <paramref name="now"/>: untouched for
    /// <paramref name="delayMinutes"/> after it last went up, then draining linearly, never below
    /// zero.
    ///
    /// The delay is R7 read literally — a habit that goes N minutes without being repeated starts to
    /// go down — and it is also what keeps a count from slipping back under its threshold the moment
    /// it reaches it: with a drain from the first second, a third escape would unlock a counterplay
    /// for a few seconds and then quietly lock it again.
    /// </summary>
    private static float Drained(float value, float raisedAt, float now, float delayMinutes,
                                 float perMinute)
    {
        if (value <= 0f) return 0f;
        if (perMinute <= 0f) return value;

        float idleMinutes = (now - raisedAt) / 60f - Mathf.Max(0f, delayMinutes);
        if (idleMinutes <= 0f) return value;

        return Mathf.Max(0f, value - perMinute * idleMinutes);
    }

    /// <summary>A threshold at or below zero means "never" here, not "always": an unset threshold
    /// must not make every spot burnable.</summary>
    private static bool MeetsThreshold(float value, float threshold) => threshold > 0f && value >= threshold;
}
