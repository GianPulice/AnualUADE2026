using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// The books behind PlayerHabitTracker (plan Fase 3 and the spot meter of Fase 2D): counts, their
/// drain, what they unlock and at what chance, and the per-spot usage meter. Pure arithmetic with
/// the clock passed in, so every case is a few lines and needs no scene.
/// </summary>
public class HabitLedgerTests
{
    private const float Minute = 60f;
    private const float Tolerance = 1e-4f;

    /// <summary>The values of plan §12, settable per test.</summary>
    private sealed class TestRules : IHabitRules
    {
        public readonly List<CounterplayRule> Rows = new List<CounterplayRule>();

        public IReadOnlyList<CounterplayRule> Rules => Rows;
        public float ChanceCap { get; set; } = 0.85f;
        public float HabitDecayDelayMinutes { get; set; } = 5f;
        public float HabitDecayPerMinute { get; set; } = 0.1f;
        public float SpotUsePoints { get; set; } = 1f;
        public float SpotHuntedBonus { get; set; } = 1f;
        public float SpotDecayDelayMinutes { get; set; }
        public float SpotDecayPerMinute { get; set; } = 0.1f;
        public float SpotPriorityThreshold { get; set; } = 2f;
        public float SpotBurnThreshold { get; set; } = 4f;
        public float SpotOpenChancePerPoint { get; set; } = 0.25f;
        public float SpotOpenChanceCap { get; set; } = 0.85f;
    }

    private TestRules rules;
    private HabitLedger ledger;

    [SetUp]
    public void SetUp()
    {
        rules = new TestRules();
        ledger = new HabitLedger(rules);
    }

    // ── Exploit counts ──────────────────────────────────────────────────────

    [Test]
    public void RegisterExploit_CountsUpAndKeepsATotal()
    {
        ledger.RegisterExploit(EExploitKind.ChaseStalled, 0f);
        float count = ledger.RegisterExploit(EExploitKind.ChaseStalled, 10f);

        Assert.AreEqual(2f, count, Tolerance);
        Assert.AreEqual(2, ledger.GetExploitTotal(EExploitKind.ChaseStalled));
        Assert.AreEqual(0f, ledger.GetExploitCount(EExploitKind.SafeZoneEscape, 10f), Tolerance);
    }

    [Test]
    public void ExploitCount_HoldsThroughTheDelay_ThenDrainsPerMinute()
    {
        ledger.RegisterExploit(EExploitKind.EscapedWhileHidden, 0f);
        ledger.RegisterExploit(EExploitKind.EscapedWhileHidden, 0f);

        Assert.AreEqual(2f, ledger.GetExploitCount(EExploitKind.EscapedWhileHidden, 5f * Minute), Tolerance);
        Assert.AreEqual(1.5f, ledger.GetExploitCount(EExploitKind.EscapedWhileHidden, 10f * Minute), Tolerance);
    }

    [Test]
    public void ExploitCount_NeverDrainsBelowZero_AndTheTotalRemains()
    {
        ledger.RegisterExploit(EExploitKind.SafeZoneEscape, 0f);

        Assert.AreEqual(0f, ledger.GetExploitCount(EExploitKind.SafeZoneEscape, 120f * Minute), Tolerance);
        Assert.AreEqual(1, ledger.GetExploitTotal(EExploitKind.SafeZoneEscape));
    }

    [Test]
    public void RegisterExploit_DrainsFirst_AndRestartsTheDelay()
    {
        ledger.RegisterExploit(EExploitKind.ChaseStalled, 0f);

        // 10 minutes later: 5 of delay, 5 of drain (0.5), then +1.
        float count = ledger.RegisterExploit(EExploitKind.ChaseStalled, 10f * Minute);
        Assert.AreEqual(1.5f, count, Tolerance);

        // The delay counts from the new registration, so 5 more minutes drain nothing.
        Assert.AreEqual(1.5f, ledger.GetExploitCount(EExploitKind.ChaseStalled, 15f * Minute), Tolerance);
    }

    // ── Counterplays ────────────────────────────────────────────────────────

    [Test]
    public void Counterplay_LockedBelowTheThreshold_ChanceAtUnlockOnIt()
    {
        rules.Rows.Add(new CounterplayRule(EExploitKind.EscapedWhileHidden, 3, ECounterplay.ExitAmbush, 0.35f, 0.1f));

        ledger.RegisterExploit(EExploitKind.EscapedWhileHidden, 0f);
        ledger.RegisterExploit(EExploitKind.EscapedWhileHidden, 1f);

        Assert.IsFalse(ledger.IsUnlocked(ECounterplay.ExitAmbush, 1f));
        Assert.AreEqual(0f, ledger.GetChance(ECounterplay.ExitAmbush, 1f), Tolerance);

        ledger.RegisterExploit(EExploitKind.EscapedWhileHidden, 2f);

        Assert.IsTrue(ledger.IsUnlocked(ECounterplay.ExitAmbush, 2f));
        Assert.AreEqual(0.35f, ledger.GetChance(ECounterplay.ExitAmbush, 2f), Tolerance);
    }

    [Test]
    public void Counterplay_ChanceGrowsPerExtraUse_UpToTheCap()
    {
        rules.Rows.Add(new CounterplayRule(EExploitKind.ChaseStalled, 1, ECounterplay.ChaseFlank, 0.35f, 0.1f));

        for (int i = 0; i < 3; i++) ledger.RegisterExploit(EExploitKind.ChaseStalled, 0f);
        Assert.AreEqual(0.55f, ledger.GetChance(ECounterplay.ChaseFlank, 0f), Tolerance);

        for (int i = 0; i < 20; i++) ledger.RegisterExploit(EExploitKind.ChaseStalled, 0f);
        Assert.AreEqual(0.85f, ledger.GetChance(ECounterplay.ChaseFlank, 0f), Tolerance);
    }

    [Test]
    public void Counterplay_TwoRowsForOneUnlock_TheLikelierWins()
    {
        rules.Rows.Add(new CounterplayRule(EExploitKind.ChaseStalled, 2, ECounterplay.ZoneDefense, 0.35f, 0.1f));
        rules.Rows.Add(new CounterplayRule(EExploitKind.SafeZoneEscape, 2, ECounterplay.ZoneDefense, 0.35f, 0.1f));

        ledger.RegisterExploit(EExploitKind.SafeZoneEscape, 0f);
        ledger.RegisterExploit(EExploitKind.SafeZoneEscape, 0f);

        Assert.IsTrue(ledger.IsUnlocked(ECounterplay.ZoneDefense, 0f));
        Assert.AreEqual(0.35f, ledger.GetChance(ECounterplay.ZoneDefense, 0f), Tolerance);

        for (int i = 0; i < 4; i++) ledger.RegisterExploit(EExploitKind.ChaseStalled, 0f);
        Assert.AreEqual(0.55f, ledger.GetChance(ECounterplay.ZoneDefense, 0f), Tolerance);
    }

    [Test]
    public void ChaseStalled_OnePerChase_UnlocksTheFlankOnly_TwoChasesUnlockZoneDefense()
    {
        // The default rows of SO_CounterplayRules. The ledger counts occurrences, so what a
        // "chase that stalled" is worth is decided before it gets here (ChaseStallCounter): one
        // register per chase. A 50 s loop used to register 8 and walk straight past both rows.
        rules.Rows.Add(new CounterplayRule(EExploitKind.ChaseStalled, 1, ECounterplay.ChaseFlank, 0.35f, 0.1f));
        rules.Rows.Add(new CounterplayRule(EExploitKind.ChaseStalled, 2, ECounterplay.ZoneDefense, 0.35f, 0.1f));

        ledger.RegisterExploit(EExploitKind.ChaseStalled, 0f);

        Assert.IsTrue(ledger.IsUnlocked(ECounterplay.ChaseFlank, 0f));
        Assert.AreEqual(0.35f, ledger.GetChance(ECounterplay.ChaseFlank, 0f), Tolerance);
        Assert.IsFalse(ledger.IsUnlocked(ECounterplay.ZoneDefense, 0f));

        ledger.RegisterExploit(EExploitKind.ChaseStalled, 5f * Minute);

        Assert.IsTrue(ledger.IsUnlocked(ECounterplay.ZoneDefense, 5f * Minute));
        Assert.AreEqual(0.45f, ledger.GetChance(ECounterplay.ChaseFlank, 5f * Minute), Tolerance);
    }

    [Test]
    public void Counterplay_LocksAgainOnceTheCountDrainsUnderTheThreshold()
    {
        rules.Rows.Add(new CounterplayRule(EExploitKind.EscapedWhileHidden, 3, ECounterplay.ExitAmbush, 0.35f, 0.1f));
        for (int i = 0; i < 3; i++) ledger.RegisterExploit(EExploitKind.EscapedWhileHidden, 0f);

        Assert.IsTrue(ledger.IsUnlocked(ECounterplay.ExitAmbush, 5f * Minute));
        Assert.IsFalse(ledger.IsUnlocked(ECounterplay.ExitAmbush, 6f * Minute));
    }

    [Test]
    public void Counterplay_AThresholdOfZeroIsReadAsOne()
    {
        rules.Rows.Add(new CounterplayRule(EExploitKind.SafeZoneEscape, 0, ECounterplay.ZoneDefense, 0.35f, 0.1f));

        Assert.IsFalse(ledger.IsUnlocked(ECounterplay.ZoneDefense, 0f));

        ledger.RegisterExploit(EExploitKind.SafeZoneEscape, 0f);
        Assert.IsTrue(ledger.IsUnlocked(ECounterplay.ZoneDefense, 0f));
    }

    [Test]
    public void MeterScoredCounterplays_AreAnsweredPerSpot_NotByARow()
    {
        for (int i = 0; i < 5; i++) ledger.AddSpotUse("locker_01", 0f);

        Assert.IsTrue(ledger.IsBurnable("locker_01", 0f));
        Assert.IsFalse(ledger.IsUnlocked(ECounterplay.BurnHidingSpot, 0f));
    }

    // ── Spot meter ──────────────────────────────────────────────────────────

    [Test]
    public void SpotMeter_AUseAndAnEscape_AddUp()
    {
        Assert.AreEqual(1f, ledger.AddSpotUse("locker_01", 0f), Tolerance);
        Assert.AreEqual(2f, ledger.AddSpotEscape("locker_01", 0f), Tolerance);

        Assert.AreEqual(1, ledger.GetSpotUses("locker_01"));
        Assert.AreEqual(1, ledger.GetSpotEscapes("locker_01"));
    }

    [Test]
    public void SpotMeter_DrainsPerMinute_FromTheFirstMinute()
    {
        ledger.AddSpotUse("table_02", 0f);
        ledger.AddSpotEscape("table_02", 0f);

        Assert.AreEqual(1.5f, ledger.GetSpotMeter("table_02", 5f * Minute), Tolerance);
        Assert.AreEqual(0f, ledger.GetSpotMeter("table_02", 30f * Minute), Tolerance);
    }

    [Test]
    public void SpotMeter_PriorityAtTwo_BurnableAtFour()
    {
        ledger.AddSpotUse("locker_01", 0f);
        Assert.IsFalse(ledger.IsPrioritySpot("locker_01", 0f));

        ledger.AddSpotEscape("locker_01", 0f);
        Assert.IsTrue(ledger.IsPrioritySpot("locker_01", 0f));
        Assert.IsFalse(ledger.IsBurnable("locker_01", 0f));

        ledger.AddSpotUse("locker_01", 0f);
        ledger.AddSpotEscape("locker_01", 0f);
        Assert.IsTrue(ledger.IsBurnable("locker_01", 0f));
    }

    [Test]
    public void SpotThresholdAtZero_MeansNever()
    {
        rules.SpotBurnThreshold = 0f;
        ledger.AddSpotUse("locker_01", 0f);

        Assert.IsFalse(ledger.IsBurnable("locker_01", 0f));
        Assert.IsFalse(ledger.IsBurnable("never_used", 0f));
    }

    [Test]
    public void OpenChance_ScalesWithTheMeter_UpToTheCap()
    {
        Assert.AreEqual(0f, ledger.GetOpenChance("never_used", 0f), Tolerance);

        ledger.AddSpotUse("locker_01", 0f);
        Assert.AreEqual(0.25f, ledger.GetOpenChance("locker_01", 0f), Tolerance);

        for (int i = 0; i < 5; i++) ledger.AddSpotUse("locker_01", 0f);
        Assert.AreEqual(0.85f, ledger.GetOpenChance("locker_01", 0f), Tolerance);
    }

    [Test]
    public void Spots_AreKeptApartById_CaseSensitive()
    {
        ledger.AddSpotUse("locker_01", 0f);
        ledger.AddSpotUse("locker_02", 0f);
        ledger.AddSpotEscape("locker_02", 0f);

        Assert.AreEqual(1f, ledger.GetSpotMeter("locker_01", 0f), Tolerance);
        Assert.AreEqual(2f, ledger.GetSpotMeter("locker_02", 0f), Tolerance);

        // Same comparison as Tools/Player/Validate Hiding Spots: ids differing in case are two spots.
        Assert.AreEqual(0f, ledger.GetSpotMeter("Locker_01", 0f), Tolerance);
    }

    [Test]
    public void EmptySpotId_IsIgnored()
    {
        Assert.AreEqual(0f, ledger.AddSpotUse("", 0f), Tolerance);
        Assert.AreEqual(0f, ledger.AddSpotEscape(null, 0f), Tolerance);
        Assert.AreEqual(0f, ledger.GetSpotMeter(null, 0f), Tolerance);
        Assert.AreEqual(0, ledger.CollectSpots(new List<HabitLedger.SpotReading>(), 0f));
    }

    [Test]
    public void CollectSpots_MostUsedFirst_SkipsTheDrained()
    {
        ledger.AddSpotUse("old", 0f);
        ledger.AddSpotUse("table_02", 19f * Minute);
        ledger.AddSpotUse("locker_01", 19f * Minute);
        ledger.AddSpotEscape("locker_01", 19f * Minute);

        List<HabitLedger.SpotReading> readings = new List<HabitLedger.SpotReading>();
        int count = ledger.CollectSpots(readings, 20f * Minute);

        Assert.AreEqual(2, count);
        Assert.AreEqual("locker_01", readings[0].SpotId);
        Assert.AreEqual(1.9f, readings[0].Meter, Tolerance);
        Assert.AreEqual(1, readings[0].Escapes);
        Assert.AreEqual("table_02", readings[1].SpotId);
    }

    // ── R3 and New Game ─────────────────────────────────────────────────────

    [Test]
    public void MarkRun_IsTrueTheFirstTimeOnly()
    {
        Assert.IsFalse(ledger.HasRun(ECounterplay.CheckHidingSpots));
        Assert.IsTrue(ledger.MarkRun(ECounterplay.CheckHidingSpots));
        Assert.IsFalse(ledger.MarkRun(ECounterplay.CheckHidingSpots));
        Assert.IsTrue(ledger.HasRun(ECounterplay.CheckHidingSpots));
    }

    [Test]
    public void Clear_ForgetsCountsSpotsAndRuns()
    {
        ledger.RegisterExploit(EExploitKind.ChaseStalled, 0f);
        ledger.AddSpotUse("locker_01", 0f);
        ledger.MarkRun(ECounterplay.ChaseFlank);

        ledger.Clear();

        Assert.AreEqual(0f, ledger.GetExploitCount(EExploitKind.ChaseStalled, 0f), Tolerance);
        Assert.AreEqual(0, ledger.GetExploitTotal(EExploitKind.ChaseStalled));
        Assert.AreEqual(0f, ledger.GetSpotMeter("locker_01", 0f), Tolerance);
        Assert.AreEqual(0, ledger.GetSpotUses("locker_01"));
        Assert.IsFalse(ledger.HasRun(ECounterplay.ChaseFlank));
    }

    [Test]
    public void SwappingTheRules_KeepsTheCounts()
    {
        ledger.RegisterExploit(EExploitKind.ChaseStalled, 0f);

        TestRules harder = new TestRules();
        harder.Rows.Add(new CounterplayRule(EExploitKind.ChaseStalled, 1, ECounterplay.ChaseFlank, 0.5f, 0.1f));
        ledger.Rules = harder;

        Assert.AreEqual(0.5f, ledger.GetChance(ECounterplay.ChaseFlank, 0f), Tolerance);
    }

    [Test]
    public void NullRules_AreRefused()
    {
        Assert.Throws<System.ArgumentNullException>(() => new HabitLedger(null));
    }
}
