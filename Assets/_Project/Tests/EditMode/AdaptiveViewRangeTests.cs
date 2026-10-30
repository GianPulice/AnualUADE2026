using NUnit.Framework;

/// <summary>
/// How far the Nemesis's eyes reach (04/10): the base range for one that has not seen the player, a
/// longer one while it holds them in sight, and one that grows while it hunts a player it lost.
/// Values as shipped: view range 7 m, hold and hunt ×2, two seconds to grow and two to settle.
/// </summary>
public class AdaptiveViewRangeTests
{
    private const float Base = 7f;
    private const float Hold = 2f;
    private const float Hunt = 2f;
    private const float Grow = 2f;
    private const float Settle = 2f;
    private const float Tolerance = 1e-4f;

    private static float Step(AdaptiveViewRange range, float seconds, bool seeing, bool hunting, bool atRest) =>
        range.Step(seconds, seeing, hunting, atRest, Hold, Hunt, Grow, Settle);

    /// <summary>A Nemesis that saw the player and has just lost them while chasing.</summary>
    private static AdaptiveViewRange JustLost()
    {
        var range = new AdaptiveViewRange();
        Step(range, 0.1f, seeing: true, hunting: true, atRest: false);
        Step(range, 0f, seeing: false, hunting: true, atRest: false);
        return range;
    }

    // ── The base range: what stealth is balanced on ──────────────────────────

    [Test]
    public void Unaware_OnPatrol_IsTheBaseRange()
    {
        var range = new AdaptiveViewRange();
        for (int i = 0; i < 50; i++) Step(range, 0.1f, seeing: false, hunting: false, atRest: true);

        Assert.AreEqual(1f, range.Scale);
        Assert.AreEqual(Base, AdaptiveViewRange.Range(Base, range.Scale));
        Assert.AreEqual(AdaptiveViewRange.EReason.Base, range.Reason);
    }

    [Test]
    public void ANoise_NeverEarnsTheHunt()
    {
        // A search that grew out of a footstep, or a lift ride towards a noise: a hunting state with
        // no sighting behind it. Sneaking past a Nemesis that only heard you stays as hard as it was.
        var range = new AdaptiveViewRange();
        for (int i = 0; i < 100; i++) Step(range, 0.1f, seeing: false, hunting: true, atRest: false);

        Assert.AreEqual(1f, range.Scale);
        Assert.IsFalse(range.HuntEarned);
        Assert.AreEqual(AdaptiveViewRange.EReason.Base, range.Reason);
    }

    [Test]
    public void WalkingToANoise_AfterNothing_IsTheBaseRange()
    {
        // Investigating: neither hunting nor at rest.
        var range = new AdaptiveViewRange();
        for (int i = 0; i < 50; i++) Step(range, 0.1f, seeing: false, hunting: false, atRest: false);

        Assert.AreEqual(1f, range.Scale);
    }

    // ── Hold ─────────────────────────────────────────────────────────────────

    [Test]
    public void Seeing_HoldsFurtherOut_AtOnce()
    {
        var range = new AdaptiveViewRange();
        Step(range, 0.02f, seeing: true, hunting: false, atRest: true);

        Assert.AreEqual(Hold, range.Scale, Tolerance);
        Assert.AreEqual(14f, AdaptiveViewRange.Range(Base, range.Scale), Tolerance);
        Assert.AreEqual(AdaptiveViewRange.EReason.Hold, range.Reason);
    }

    [Test]
    public void SeeingFromAnyState_Holds()
    {
        // A player in view on a catwalk it cannot reach: it never enters Chasing, and it still holds.
        var range = new AdaptiveViewRange();
        Step(range, 0.1f, seeing: true, hunting: false, atRest: false);

        Assert.AreEqual(Hold, range.Scale, Tolerance);
    }

    // ── Hunt ─────────────────────────────────────────────────────────────────

    [Test]
    public void LosingSight_DropsToTheBase()
    {
        // Breaking line of sight is worth something: the hold ends with the sighting, and the hunt
        // has to grow the range again from the base.
        AdaptiveViewRange range = JustLost();

        Assert.AreEqual(1f, range.Scale, Tolerance);
        Assert.IsTrue(range.HuntEarned);
    }

    [Test]
    public void Hunt_GrowsWithTheTimeWithoutSeeing()
    {
        AdaptiveViewRange range = JustLost();

        Step(range, 0.5f, seeing: false, hunting: true, atRest: false);
        Assert.AreEqual(1.25f, range.Scale, Tolerance);
        Assert.AreEqual(AdaptiveViewRange.EReason.Hunt, range.Reason);

        Step(range, 0.5f, seeing: false, hunting: true, atRest: false);
        Assert.AreEqual(1.5f, range.Scale, Tolerance);
    }

    [Test]
    public void Hunt_ReachesItsScale_AndStaysThere()
    {
        AdaptiveViewRange range = JustLost();
        for (int i = 0; i < 20; i++) Step(range, 0.1f, seeing: false, hunting: true, atRest: false);

        Assert.AreEqual(Hunt, range.Scale, Tolerance);

        // A search that goes on for a minute by ear is the same hunt.
        for (int i = 0; i < 600; i++) Step(range, 0.1f, seeing: false, hunting: true, atRest: false);

        Assert.AreEqual(Hunt, range.Scale, Tolerance);
        Assert.AreEqual(14f, AdaptiveViewRange.Range(Base, range.Scale), Tolerance);
    }

    [Test]
    public void SeeingAgain_Holds_AndTheNextLossStartsFromTheBase()
    {
        AdaptiveViewRange range = JustLost();
        for (int i = 0; i < 20; i++) Step(range, 0.1f, seeing: false, hunting: true, atRest: false);

        Step(range, 0.1f, seeing: true, hunting: true, atRest: false);
        Assert.AreEqual(Hold, range.Scale, Tolerance);
        Assert.AreEqual(AdaptiveViewRange.EReason.Hold, range.Reason);

        Step(range, 0f, seeing: false, hunting: true, atRest: false);
        Assert.AreEqual(1f, range.Scale, Tolerance, "a pillar at twelve metres buys the regrow time");
    }

    [Test]
    public void GrowTimeZero_JumpsToTheHuntScale()
    {
        var range = new AdaptiveViewRange();
        range.Step(0.1f, true, true, false, Hold, Hunt, 0f, Settle);
        range.Step(0f, false, true, false, Hold, Hunt, 0f, Settle);

        Assert.AreEqual(Hunt, range.Scale, Tolerance);
    }

    // ── The hunt ends ────────────────────────────────────────────────────────

    [Test]
    public void HuntOver_SettlesBack_InsteadOfSnapping()
    {
        AdaptiveViewRange range = JustLost();
        for (int i = 0; i < 20; i++) Step(range, 0.1f, seeing: false, hunting: true, atRest: false);

        Step(range, 0.5f, seeing: false, hunting: false, atRest: true);
        Assert.AreEqual(1.75f, range.Scale, Tolerance);
        Assert.AreEqual(AdaptiveViewRange.EReason.Settling, range.Reason);

        for (int i = 0; i < 20; i++) Step(range, 0.1f, seeing: false, hunting: false, atRest: true);
        Assert.AreEqual(1f, range.Scale, Tolerance);
        Assert.AreEqual(AdaptiveViewRange.EReason.Base, range.Reason);
    }

    [Test]
    public void BackOnPatrol_DropsWhatTheSightingEarned()
    {
        AdaptiveViewRange range = JustLost();
        Step(range, 0.1f, seeing: false, hunting: false, atRest: true);

        Assert.IsFalse(range.HuntEarned);

        // The next search comes from a noise: nothing earned, base range.
        for (int i = 0; i < 50; i++) Step(range, 0.1f, seeing: false, hunting: true, atRest: false);
        Assert.AreEqual(1f, range.Scale, Tolerance);
    }

    [Test]
    public void ADecoyMidHunt_DoesNotLoseIt()
    {
        // Searching → Investigating (a lead took its attention) → Searching: the same hunt.
        AdaptiveViewRange range = JustLost();
        for (int i = 0; i < 20; i++) Step(range, 0.1f, seeing: false, hunting: true, atRest: false);

        Step(range, 1f, seeing: false, hunting: false, atRest: false);
        Assert.AreEqual(1.5f, range.Scale, Tolerance, "it eases while it looks at something else");
        Assert.IsTrue(range.HuntEarned);

        Step(range, 1f, seeing: false, hunting: true, atRest: false);
        Assert.AreEqual(Hunt, range.Scale, Tolerance, "and picks up from where it had got to");
    }

    [Test]
    public void SettleTimeZero_SnapsBack()
    {
        var range = new AdaptiveViewRange();
        range.Step(0.1f, true, true, false, Hold, Hunt, Grow, 0f);
        for (int i = 0; i < 30; i++) range.Step(0.1f, false, true, false, Hold, Hunt, Grow, 0f);

        range.Step(0.01f, false, false, true, Hold, Hunt, Grow, 0f);

        Assert.AreEqual(1f, range.Scale, Tolerance);
    }

    [Test]
    public void Reset_ForgetsEverything()
    {
        AdaptiveViewRange range = JustLost();
        for (int i = 0; i < 20; i++) Step(range, 0.1f, seeing: false, hunting: true, atRest: false);

        range.Reset();

        Assert.AreEqual(1f, range.Scale);
        Assert.AreEqual(0f, range.HuntLevel);
        Assert.IsFalse(range.HuntEarned);
        Assert.AreEqual(AdaptiveViewRange.EReason.Base, range.Reason);
    }

    // ── Switched off, it is the sensor it was ────────────────────────────────

    [Test]
    public void ScalesAtOne_IsExactlyTheOldRange()
    {
        var range = new AdaptiveViewRange();

        // Seen, lost, hunted for ten seconds, back on patrol: the range never moves off the base.
        range.Step(0.1f, true, true, false, 1f, 1f, Grow, Settle);
        Assert.AreEqual(Base, AdaptiveViewRange.Range(Base, range.Scale));

        for (int i = 0; i < 100; i++)
        {
            range.Step(0.1f, false, true, false, 1f, 1f, Grow, Settle);
            Assert.AreEqual(Base, AdaptiveViewRange.Range(Base, range.Scale));
            Assert.AreEqual(AdaptiveViewRange.EReason.Base, range.Reason);
        }

        range.Step(0.1f, false, false, true, 1f, 1f, Grow, Settle);
        Assert.AreEqual(Base, AdaptiveViewRange.Range(Base, range.Scale));
    }

    [Test]
    public void AScaleUnderOne_NeverShortensTheRange()
    {
        Assert.AreEqual(1f, AdaptiveViewRange.ScaleFor(true, 0f, 0.5f, 0.5f));
        Assert.AreEqual(1f, AdaptiveViewRange.ScaleFor(false, 1f, 0.5f, 0.5f));
        Assert.AreEqual(Base, AdaptiveViewRange.Range(Base, 0.25f));
    }

    // ── The pieces ───────────────────────────────────────────────────────────

    [Test]
    public void HuntLevel_FillsWhileHunting_DrainsOtherwise_EmptiesOnSight()
    {
        Assert.AreEqual(0.5f, AdaptiveViewRange.StepHuntLevel(0.25f, 0.5f, false, true, Grow, Settle), Tolerance);
        Assert.AreEqual(0.25f, AdaptiveViewRange.StepHuntLevel(0.5f, 0.5f, false, false, Grow, Settle), Tolerance);
        Assert.AreEqual(0f, AdaptiveViewRange.StepHuntLevel(0.9f, 0.5f, true, true, Grow, Settle), Tolerance);
        Assert.AreEqual(1f, AdaptiveViewRange.StepHuntLevel(0.9f, 5f, false, true, Grow, Settle), Tolerance);
        Assert.AreEqual(0f, AdaptiveViewRange.StepHuntLevel(0.1f, 5f, false, false, Grow, Settle), Tolerance);
    }

    [Test]
    public void StaysEarned_BySeeing_UntilAtRest()
    {
        Assert.IsTrue(AdaptiveViewRange.StaysEarned(false, seeing: true, atRest: true));
        Assert.IsTrue(AdaptiveViewRange.StaysEarned(true, seeing: false, atRest: false));
        Assert.IsFalse(AdaptiveViewRange.StaysEarned(true, seeing: false, atRest: true));
        Assert.IsFalse(AdaptiveViewRange.StaysEarned(false, seeing: false, atRest: false));
    }

    [Test]
    public void SecondsToReach_SaysWhenAHuntSeesThatFar()
    {
        Assert.AreEqual(0f, AdaptiveViewRange.SecondsToReach(5f, Base, Hunt, Grow), Tolerance);
        Assert.AreEqual(1f, AdaptiveViewRange.SecondsToReach(10.5f, Base, Hunt, Grow), Tolerance);
        Assert.AreEqual(2f, AdaptiveViewRange.SecondsToReach(14f, Base, Hunt, Grow), Tolerance);
        Assert.IsTrue(float.IsPositiveInfinity(AdaptiveViewRange.SecondsToReach(15f, Base, Hunt, Grow)));
        Assert.AreEqual(0f, AdaptiveViewRange.SecondsToReach(12f, Base, Hunt, 0f), Tolerance);
        Assert.IsTrue(float.IsPositiveInfinity(AdaptiveViewRange.SecondsToReach(8f, Base, 1f, Grow)));
    }

    [Test]
    public void SecondsToReach_AgreesWithTheStep()
    {
        // 10.5 m is ×1.5: the tester says one second, and one second of hunting is ×1.5.
        AdaptiveViewRange range = JustLost();
        Step(range, AdaptiveViewRange.SecondsToReach(10.5f, Base, Hunt, Grow), false, true, false);

        Assert.AreEqual(10.5f, AdaptiveViewRange.Range(Base, range.Scale), Tolerance);
    }
}
