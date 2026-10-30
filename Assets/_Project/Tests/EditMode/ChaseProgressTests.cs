using NUnit.Framework;

/// <summary>
/// The pure half of NemesisChaseProgress (plan C4, bug report 30/09): what counts as closing the
/// distance, the NavMesh jump that is the path changing and not the player, a window left unfed for
/// too long, and how many ChaseStalled one chase is worth. Defaults of SO_NemesisData: window 4 s,
/// 1.5 m of progress, arm's reach 1 m, jump 10 m.
/// </summary>
public class ChaseProgressTests
{
    private const float Window = 4f;
    private const float MinProgress = 1.5f;
    private const float CatchReach = 1f;
    private const float JumpLimit = 10f;
    private const float Tolerance = 1e-4f;

    private ChaseGapWindow window;
    private ChaseStallCounter counter;

    [SetUp]
    public void SetUp()
    {
        window = new ChaseGapWindow();
        counter = new ChaseStallCounter();
    }

    private ChaseGapWindow.ESample Sample(float distance) =>
        window.Sample(distance, MinProgress, CatchReach, JumpLimit);

    // ── Closing the distance ────────────────────────────────────────────────

    [Test]
    public void HasClosed_NeedsTheMinimumProgress()
    {
        Assert.IsFalse(ChaseGapWindow.HasClosed(10f, 8.6f, MinProgress, CatchReach));
        Assert.IsTrue(ChaseGapWindow.HasClosed(10f, 8.5f, MinProgress, CatchReach));
    }

    [Test]
    public void HasClosed_ArmsLengthAlwaysCounts()
    {
        // Already inside the threshold from the start: it cannot close 1.5 m, and has not stalled.
        Assert.IsTrue(ChaseGapWindow.HasClosed(1.2f, 1f, MinProgress, CatchReach));
        Assert.IsFalse(ChaseGapWindow.HasClosed(1.4f, 1.3f, MinProgress, CatchReach));
    }

    [Test]
    public void HasClosed_LosingGroundIsNeverProgress()
    {
        Assert.IsFalse(ChaseGapWindow.HasClosed(10f, 14f, MinProgress, CatchReach));
    }

    // ── The window ──────────────────────────────────────────────────────────

    [Test]
    public void FirstSample_OpensTheWindow_AndJudgesNothing()
    {
        Assert.IsFalse(window.IsOpen);

        Assert.AreEqual(ChaseGapWindow.ESample.Opened, Sample(12f));

        Assert.IsTrue(window.IsOpen);
        Assert.AreEqual(12f, window.StartDistance, Tolerance);
        Assert.AreEqual(0f, window.Progress, Tolerance);
    }

    [Test]
    public void ALoop_IsJudgedAgainstTheStart_NotTheBestReading()
    {
        Sample(10f);

        // Near side of the lap, far side, near side again: it never closes 1.5 m from the start,
        // however many metres it gets closer than the far side was.
        Assert.AreEqual(ChaseGapWindow.ESample.Waiting, Sample(9f));
        Assert.AreEqual(ChaseGapWindow.ESample.Waiting, Sample(11f));
        Assert.AreEqual(ChaseGapWindow.ESample.Waiting, Sample(9.2f));

        Assert.AreEqual(0.8f, window.Progress, Tolerance);
    }

    [Test]
    public void Closing_ReopensTheWindowFromTheShorterDistance()
    {
        Sample(10f);
        window.Age(2f, Window);

        Assert.AreEqual(ChaseGapWindow.ESample.Closed, Sample(8.4f));

        Assert.AreEqual(8.4f, window.StartDistance, Tolerance);
        Assert.AreEqual(0f, window.Elapsed, Tolerance);
    }

    [Test]
    public void Age_ExpiresAfterTheWindowLength_AndNeverWithoutOne()
    {
        Assert.IsFalse(window.Age(100f, Window));

        Sample(10f);

        Assert.IsFalse(window.Age(3.9f, Window));
        Assert.IsTrue(window.Age(0.2f, Window));
        Assert.AreEqual(0f, window.Remaining(Window), Tolerance);
    }

    [Test]
    public void Remaining_IsInfinityWithNoWindow()
    {
        Assert.IsTrue(float.IsPositiveInfinity(window.Remaining(Window)));
    }

    [Test]
    public void Restart_OpensAFreshWindowFromTheLatestReading()
    {
        Sample(10f);
        Sample(10.4f);
        window.Age(Window, Window);

        window.Restart();

        Assert.AreEqual(10.4f, window.StartDistance, Tolerance);
        Assert.AreEqual(0f, window.Elapsed, Tolerance);
        Assert.AreEqual(0f, window.Progress, Tolerance);
    }

    // ── NavMesh jumps (bug 2) ───────────────────────────────────────────────

    [Test]
    public void IsPathJump_BothDirections_PastTheLimit()
    {
        Assert.IsTrue(ChaseGapWindow.IsPathJump(2.9f, 23.2f, JumpLimit));
        Assert.IsTrue(ChaseGapWindow.IsPathJump(23.2f, 2.9f, JumpLimit));
        Assert.IsFalse(ChaseGapWindow.IsPathJump(10f, 19.9f, JumpLimit));
    }

    [Test]
    public void IsPathJump_NoPreviousReading_IsNeverAJump()
    {
        Assert.IsFalse(ChaseGapWindow.IsPathJump(float.PositiveInfinity, 40f, JumpLimit));
    }

    [Test]
    public void IsPathJump_ALimitAtZeroTurnsTheRuleOff()
    {
        Assert.IsFalse(ChaseGapWindow.IsPathJump(2f, 50f, 0f));
    }

    [Test]
    public void AJump_IsNotJudged_ItIsTheNewBaseline()
    {
        // The report: 2.9 m in a straight line, 23.2 m over the NavMesh, in one sample.
        Sample(3f);
        window.Age(3f, Window);

        Assert.AreEqual(ChaseGapWindow.ESample.PathJump, Sample(23.2f));

        // Not "shortened by -20 m": the window starts over from where the path now is.
        Assert.AreEqual(23.2f, window.StartDistance, Tolerance);
        Assert.AreEqual(0f, window.Progress, Tolerance);
        Assert.AreEqual(0f, window.Elapsed, Tolerance);
    }

    [Test]
    public void AJumpDown_IsNotARescue_EitherDirection()
    {
        Sample(23f);

        Assert.AreEqual(ChaseGapWindow.ESample.PathJump, Sample(3f));
        Assert.AreEqual(3f, window.StartDistance, Tolerance);
    }

    [Test]
    public void AfterAJump_NormalSamplesAreJudgedFromTheNewBaseline()
    {
        Sample(3f);
        Sample(23.2f);

        Assert.AreEqual(ChaseGapWindow.ESample.Waiting, Sample(23.0f));
        Assert.AreEqual(ChaseGapWindow.ESample.Closed, Sample(21.5f));
    }

    [Test]
    public void ARealRunningStep_IsNotAJump()
    {
        // Sprint 4.5 m/s plus chase 3.0 m/s over a 0.4 s sample is 3 m: nowhere near the limit.
        Sample(10f);

        Assert.AreEqual(ChaseGapWindow.ESample.Waiting, Sample(13f));
        Assert.AreEqual(-3f, window.Progress, Tolerance);
    }

    // ── A window left unfed (bug 3) ─────────────────────────────────────────

    [Test]
    public void AShortPause_KeepsTheWindow()
    {
        Sample(10f);
        window.Age(2f, Window);

        // A door opened: one second of nothing.
        window.NoteIdle(1f);

        Assert.IsFalse(window.DiscardIfStale(Window));
        Assert.IsTrue(window.IsOpen);
        Assert.AreEqual(2f, window.Elapsed, Tolerance);
    }

    [Test]
    public void APauseLongerThanTheWindow_DropsIt()
    {
        Sample(10f);
        window.Age(3.7f, Window);

        // Lost sight for a good while: the 10 m baseline belongs to a place the player left.
        for (int i = 0; i < 25; i++) window.NoteIdle(0.2f);

        Assert.IsTrue(window.DiscardIfStale(Window));
        Assert.IsFalse(window.IsOpen);

        // The next reading opens a window instead of being judged against the old one: it cannot
        // stall on its first sample, which is what resuming the old one did.
        Assert.AreEqual(ChaseGapWindow.ESample.Opened, Sample(16f));
    }

    [Test]
    public void AgeingResetsTheIdleClock()
    {
        Sample(10f);
        window.NoteIdle(3.5f);
        window.Age(0.016f, Window);
        window.NoteIdle(3.5f);

        Assert.IsFalse(window.DiscardIfStale(Window));
    }

    [Test]
    public void NoteIdle_WithNoWindow_DoesNothing()
    {
        window.NoteIdle(100f);

        Assert.IsFalse(window.DiscardIfStale(Window));
    }

    [Test]
    public void Clear_ForgetsTheWindowAndTheLastDistance()
    {
        Sample(10f);

        window.Clear();

        Assert.IsFalse(window.IsOpen);
        Assert.IsTrue(float.IsPositiveInfinity(window.LastDistance));
        Assert.AreEqual(0f, window.Progress, Tolerance);

        // And a jump is not read against the forgotten distance.
        Assert.AreEqual(ChaseGapWindow.ESample.Opened, Sample(40f));
    }

    // ── One stall per chase (bug 1) ─────────────────────────────────────────

    [Test]
    public void ALongChase_CountsOnce_HoweverManyWindowsStall()
    {
        float now = 0f;
        int counted = 0;

        // A 50 s chase stalling a window every ~4 s: 12 windows, the report's 8 and then some.
        for (int i = 0; i < 12; i++)
        {
            now += 4f;
            counter.NoteChasing(now, 10f);
            if (counter.RegisterStalledWindow()) counted++;
        }

        Assert.AreEqual(1, counted);
        Assert.AreEqual(12, counter.WindowsThisChase);
        Assert.IsTrue(counter.CountedThisChase);
    }

    [Test]
    public void TheFirstStalledWindow_IsTheOneThatCounts()
    {
        counter.NoteChasing(0f, 10f);

        Assert.IsTrue(counter.RegisterStalledWindow());
        Assert.IsFalse(counter.RegisterStalledWindow());
    }

    [Test]
    public void AFlickerOutOfChasing_IsStillTheSameChase()
    {
        counter.NoteChasing(0f, 10f);
        counter.RegisterStalledWindow();

        // Chasing, a 3 s search because the sight broke, chasing again.
        counter.NoteChasing(3f, 10f);
        counter.NoteChasing(6.5f, 10f);

        Assert.IsFalse(counter.RegisterStalledWindow());
    }

    [Test]
    public void ALongSearchBetween_MakesTheSecondLoopAnotherChase()
    {
        counter.NoteChasing(0f, 10f);
        counter.RegisterStalledWindow();

        // Out of Chasing for 40 s, then chasing again.
        counter.NoteChasing(45f, 10f);

        Assert.AreEqual(0, counter.WindowsThisChase);
        Assert.IsTrue(counter.RegisterStalledWindow());
    }

    [Test]
    public void ARegroupTimeOfZero_EveryExitIsANewChase()
    {
        counter.NoteChasing(0f, 0f);
        counter.RegisterStalledWindow();

        counter.NoteChasing(0.5f, 0f);

        Assert.IsTrue(counter.RegisterStalledWindow());
    }

    [Test]
    public void AGapRightAtTheRegroupTime_IsStillTheSameChase()
    {
        counter.NoteChasing(0f, 10f);
        counter.RegisterStalledWindow();

        counter.NoteChasing(10f, 10f);

        Assert.IsFalse(counter.RegisterStalledWindow());
    }

    [Test]
    public void ACapture_EndsTheChaseAtOnce()
    {
        counter.NoteChasing(0f, 10f);
        counter.RegisterStalledWindow();

        counter.EndChase();
        counter.NoteChasing(2f, 10f);

        Assert.AreEqual(0, counter.WindowsThisChase);
        Assert.IsFalse(counter.CountedThisChase);
        Assert.IsTrue(counter.RegisterStalledWindow());
    }

    [Test]
    public void AChaseWithNoStall_CountsNothing()
    {
        counter.NoteChasing(0f, 10f);
        counter.NoteChasing(30f, 10f);

        Assert.AreEqual(0, counter.WindowsThisChase);
        Assert.IsFalse(counter.CountedThisChase);
    }
}
