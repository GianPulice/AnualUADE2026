using NUnit.Framework;

/// <summary>
/// The search's two judgements about a place on the possibility map (Plan-Busqueda-Nemesis §3.4, Fase
/// 2b): what it is worth going to — value ÷ (1 + seconds to walk there), the number the pick rolls
/// by — and whether the place it is already walking to has kept its value. Plain numbers in, an
/// answer out. They take the place of the disc's cases in SearchSweepRulesTests (keep / follow /
/// re-centre): there is no disc left to be inside or outside of.
/// </summary>
public class SearchPickRulesTests
{
    private const float Tolerance = 1e-5f;
    private const float Keep = 0.35f;   // SO_NemesisData.SearchMapRepickShare as shipped

    // ── Worth: what the roll weighs a place by ───────────────────────────────

    [Test]
    public void Worth_IsValueOverOnePlusTheWalk()
    {
        Assert.AreEqual(0.1f, SearchPickRules.Worth(0.5f, 4f), Tolerance);
    }

    [Test]
    public void Worth_RightBesideIt_IsTheValueItself()
    {
        // The +1: no walk at all does not divide by nothing.
        Assert.AreEqual(0.3f, SearchPickRules.Worth(0.3f, 0f), Tolerance);
    }

    [Test]
    public void Worth_SoonerIsBetter_ForTheSameValue()
    {
        Assert.Greater(SearchPickRules.Worth(0.2f, 2f), SearchPickRules.Worth(0.2f, 6f));
    }

    [Test]
    public void Worth_ALikelyPlaceFurtherOff_BeatsASliverNextDoor()
    {
        // 40 % of the value six seconds away against 5 % one second away.
        Assert.Greater(SearchPickRules.Worth(0.4f, 6f), SearchPickRules.Worth(0.05f, 1f));
    }

    [Test]
    public void Worth_WhereItCannotWalk_IsNothing()
    {
        // No path on foot is infinite seconds: it never enters the roll, whatever it holds.
        Assert.AreEqual(0f, SearchPickRules.Worth(0.9f, float.PositiveInfinity), Tolerance);
    }

    [Test]
    public void Worth_NoValue_IsNothing()
    {
        Assert.AreEqual(0f, SearchPickRules.Worth(0f, 1f), Tolerance);
        Assert.AreEqual(0f, SearchPickRules.Worth(float.NaN, 1f), Tolerance);
    }

    // ── LostItsValue: is the place it is walking to still worth getting to? ──

    [Test]
    public void LostItsValue_StillHoldingMostOfIt_KeepsWalking()
    {
        Assert.IsFalse(SearchPickRules.LostItsValue(0.25f, 0.4f, Keep));
    }

    [Test]
    public void LostItsValue_SeenEmptyFromADistance_PicksAgain()
    {
        // It looked down the corridor and the map emptied the place before it got there.
        Assert.IsTrue(SearchPickRules.LostItsValue(0f, 0.4f, Keep));
    }

    [Test]
    public void LostItsValue_TheValueFlowingOn_IsNotAReasonByItself()
    {
        // The hysteresis: the value spreads on every tick, and a place that thinned out a little is
        // still the place it was going to. Only losing most of it counts.
        float picked = 0.4f;
        for (float now = picked; now > picked * Keep + 0.01f; now -= 0.02f)
            Assert.IsFalse(SearchPickRules.LostItsValue(now, picked, Keep), $"dropped at {now:0.00}");

        Assert.IsTrue(SearchPickRules.LostItsValue(picked * Keep - 0.01f, picked, Keep));
    }

    [Test]
    public void LostItsValue_MeasuredAgainstWhatThatPlaceHad_NotAgainstTheBestPlace()
    {
        // A place picked at 6 % and still at 5 % is kept, even though 5 % would be "lost" for a place
        // picked at 40 %. The next pick starts from its own value.
        Assert.IsFalse(SearchPickRules.LostItsValue(0.05f, 0.06f, Keep));
        Assert.IsTrue(SearchPickRules.LostItsValue(0.05f, 0.4f, Keep));
    }

    [Test]
    public void LostItsValue_APlaceNotPickedOffTheMap_HasNothingToLose()
    {
        // The last-resort scatter, a hiding spot: no value when picked, no value to lose.
        Assert.IsFalse(SearchPickRules.LostItsValue(0f, 0f, Keep));
    }

    [Test]
    public void LostItsValue_KeepAtZero_AlwaysWalksToTheEnd()
    {
        Assert.IsFalse(SearchPickRules.LostItsValue(0f, 0.4f, 0f));
    }

    // ── The heading: the pick a chase hands over with leans towards where the player was going ──

    private const float Boost = 4f;   // SO_NemesisData.SearchMapChaseHeadingBoost as shipped

    private static readonly UnityEngine.Vector3 LastSeen = new UnityEngine.Vector3(10f, 0f, 5f);
    private static readonly UnityEngine.Vector3 GoingEast = new UnityEngine.Vector3(4.5f, 0f, 0f);

    private static float Alignment(float dx, float dz, float dy = 0f) =>
        SearchPickRules.HeadingAlignment(LastSeen, GoingEast, LastSeen + new UnityEngine.Vector3(dx, dy, dz));

    [Test]
    public void Alignment_DeadAhead_IsOne() => Assert.AreEqual(1f, Alignment(6f, 0f), Tolerance);

    [Test]
    public void Alignment_OffToTheSide_IsZero() => Assert.AreEqual(0f, Alignment(0f, 6f), Tolerance);

    [Test]
    public void Alignment_StraightBack_IsMinusOne() => Assert.AreEqual(-1f, Alignment(-6f, 0f), Tolerance);

    [Test]
    public void Alignment_IsFlat_AFloorUpIsNotAhead()
    {
        // Straight above the spot: no direction along the floor at all.
        Assert.AreEqual(0f, Alignment(0f, 0f, 4f), Tolerance);

        // Ahead and a floor up counts as ahead, not as "partly ahead".
        Assert.AreEqual(1f, Alignment(6f, 0f, 4f), Tolerance);
    }

    [Test]
    public void Alignment_WithNoHeading_HasNoOpinion()
    {
        var place = LastSeen + new UnityEngine.Vector3(6f, 0f, 0f);
        Assert.AreEqual(0f, SearchPickRules.HeadingAlignment(LastSeen, UnityEngine.Vector3.zero, place), Tolerance);
    }

    [Test]
    public void HeadingWeight_AheadSideAndBack()
    {
        Assert.AreEqual(Boost, SearchPickRules.HeadingWeight(1f, Boost), Tolerance);
        Assert.AreEqual(1f, SearchPickRules.HeadingWeight(0f, Boost), Tolerance);
        Assert.AreEqual(1f / Boost, SearchPickRules.HeadingWeight(-1f, Boost), Tolerance);
    }

    /// <summary>The corner of the playtest: a place two steps to the side against one four times the
    /// walk away down the corridor the player took. By worth alone the near one wins the roll more
    /// often than not; leant on the heading, the one ahead does.</summary>
    [Test]
    public void HeadingWeight_TurnsTheCornerRoll_TowardsWhereTheyWent()
    {
        float beside = SearchPickRules.Worth(0.25f, 1f);
        float ahead = SearchPickRules.Worth(0.35f, 4f);
        Assert.Greater(beside, ahead, "the case is not the one from the playtest");

        float besideLeant = beside * SearchPickRules.HeadingWeight(0f, Boost);
        float aheadLeant = ahead * SearchPickRules.HeadingWeight(1f, Boost);
        Assert.Greater(aheadLeant, besideLeant);
    }

    [Test]
    public void HeadingWeight_NeverTakesAPlaceOutOfTheRoll()
    {
        Assert.Greater(SearchPickRules.HeadingWeight(-1f, 10f), 0f);
    }

    [Test]
    public void HeadingWeight_BoostOfOne_IsOff()
    {
        Assert.AreEqual(1f, SearchPickRules.HeadingWeight(1f, 1f), Tolerance);
        Assert.AreEqual(1f, SearchPickRules.HeadingWeight(-1f, 1f), Tolerance);
        Assert.AreEqual(1f, SearchPickRules.HeadingWeight(-1f, 0.5f), Tolerance);
    }
}
