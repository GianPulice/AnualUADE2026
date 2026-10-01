using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The questions of the focus (plan §17.4, Fase 2B part 4), checked against the cases of §13 they
/// exist for: 25 and 28–33, 37. Values from §12 (FocusArbiter.Tuning.Default).
/// </summary>
public class FocusArbiterTests
{
    private static readonly FocusArbiter.Tuning Tuning = FocusArbiter.Tuning.Default;

    private static FocusArbiter.Candidate Player(float radius, float age, float distance, bool sight = false) =>
        new FocusArbiter.Candidate
        {
            Kind = FocusArbiter.EKind.Player, BaseValue = 1f, Radius = radius, Age = age,
            Distance = distance, Habituation = 1f, IsSight = sight,
        };

    private static FocusArbiter.Candidate Lead(int id, float baseValue, float distance, Vector3 position = default,
                                               float habituation = 1f) =>
        new FocusArbiter.Candidate
        {
            Kind = FocusArbiter.EKind.Lead, Identity = id, BaseValue = baseValue, Radius = 0.5f, Age = 0f,
            Distance = distance, Position = position, Habituation = habituation,
        };

    private static FocusArbiter.Decision Decide(in FocusArbiter.Candidate current, in FocusArbiter.Candidate candidate,
                                                float chosenAge = 10f, float sinceSwitch = 10f, bool busy = false,
                                                bool sums = false) =>
        FocusArbiter.Decide(current, chosenAge, sinceSwitch, candidate, busy, sums, Tuning);

    // ── 2: seeing the player ────────────────────────────────────────────────

    [Test]
    public void Sight_SwitchesFromALead_EvenMidBreak()
    {
        // Case 37: breaking the radio, it sees you.
        var d = Decide(Lead(1, 0.6f, 1f), Player(0.5f, 0f, 5f, sight: true), chosenAge: 0f, busy: true);
        Assert.AreEqual(FocusArbiter.EVerdict.Switch, d.Verdict);
        Assert.AreEqual(FocusArbiter.EReason.SeesPlayer, d.Reason);
    }

    [Test]
    public void Sight_WhileOnThePlayer_OnlyUpdates()
    {
        var d = Decide(Player(0.5f, 1f, 5f), Player(0.5f, 0f, 5f, sight: true));
        Assert.AreEqual(FocusArbiter.EVerdict.Update, d.Verdict);
    }

    // ── 3: busy ─────────────────────────────────────────────────────────────

    [Test]
    public void Busy_KeepsTheRadio_AgainstAFootstep()
    {
        var d = Decide(Lead(1, 0.6f, 1f), Player(2.5f, 0f, 10f), busy: true);
        Assert.AreEqual(FocusArbiter.EVerdict.Keep, d.Verdict);
        Assert.AreEqual(FocusArbiter.EReason.Busy, d.Reason);
    }

    // ── 4: the same thing ───────────────────────────────────────────────────

    [Test]
    public void SameDecoy_Updates()
    {
        var d = Decide(Lead(7, 0.6f, 10f), Lead(7, 0.6f, 9f));
        Assert.AreEqual(FocusArbiter.EVerdict.Update, d.Verdict);
        Assert.AreEqual(FocusArbiter.EReason.SameThing, d.Reason);
    }

    [Test]
    public void AnonymousNoise_WithinThreeMetres_IsTheSamePlace()
    {
        var d = Decide(Lead(0, 0.4f, 10f, new Vector3(0f, 0f, 0f)), Lead(0, 0.4f, 10f, new Vector3(2f, 0f, 0f)));
        Assert.AreEqual(FocusArbiter.EVerdict.Update, d.Verdict);
    }

    [Test]
    public void TwoDifferentDecoys_WithinThreeMetres_AreTwoThings()
    {
        // The breaker smashes the focus decoy, and habituation is per decoy: a radio beside the chains
        // must not ride on the chains' identity.
        var d = Decide(Lead(1, 0.45f, 10f, new Vector3(0f, 0f, 0f)), Lead(2, 0.6f, 10f, new Vector3(2f, 0f, 0f)));
        Assert.AreNotEqual(FocusArbiter.EReason.SameThing, d.Reason);
    }

    [Test]
    public void Decoy_NextToAnAnonymousNoise_IsTheSamePlace()
    {
        var d = Decide(Lead(0, 0.4f, 10f, new Vector3(0f, 0f, 0f)), Lead(3, 0.6f, 10f, new Vector3(2f, 0f, 0f)));
        Assert.AreEqual(FocusArbiter.EVerdict.Update, d.Verdict);
    }

    [Test]
    public void PlayerEvidence_WhileOnThePlayer_Updates()
    {
        var d = Decide(Player(0.5f, 3f, 5f), Player(2.5f, 0f, 12f));
        Assert.AreEqual(FocusArbiter.EVerdict.Update, d.Verdict);
    }

    // ── 5: discarded, and a lead that sums ──────────────────────────────────

    [Test]
    public void BrokenRadio_IsIgnored()
    {
        FocusArbiter.Candidate radio = Lead(3, 0.6f, 5f);
        radio.Discarded = true;
        var d = Decide(FocusArbiter.Candidate.None, radio);
        Assert.AreEqual(FocusArbiter.EVerdict.Ignore, d.Verdict);
        Assert.AreEqual(FocusArbiter.EReason.Discarded, d.Reason);
    }

    [Test]
    public void LeadWhereItBelievesYouAre_Sums_KeepsThePlayer()
    {
        // Case 33.
        var d = Decide(Player(4f, 5f, 8f), Lead(2, 0.6f, 8f), sums: true);
        Assert.AreEqual(FocusArbiter.EVerdict.Keep, d.Verdict);
        Assert.AreEqual(FocusArbiter.EReason.SumsWithBelief, d.Reason);
    }

    // ── 7 to 11: values ─────────────────────────────────────────────────────

    [Test]
    public void AlarmFarAway_WithNothingElse_IsWorthGoing()
    {
        // Case 25: never sensed you; the alarm goes off across the level.
        var d = Decide(FocusArbiter.Candidate.None, Lead(4, 0.7f, 60f));
        Assert.AreEqual(FocusArbiter.EVerdict.Switch, d.Verdict);
    }

    [Test]
    public void Radio_BeatsABeliefTenSecondsOld()
    {
        // Case 31: lost you ten seconds ago; the radio sounds thirty metres away.
        var d = Decide(Player(0.5f, 10f, 5f), Lead(1, 0.6f, 30f));
        Assert.AreEqual(FocusArbiter.EVerdict.Switch, d.Verdict, $"radio {d.CandidateValue} vs belief {d.CurrentValue}");
    }

    [Test]
    public void Radio_BeatsACold_Search_EvenStandingOnTheBelief()
    {
        // Case 31 as it happens: the search circles the belief, a metre from it. "Almost there" is
        // not a thing for the belief.
        var d = Decide(Player(0.5f, 10f, 1f), Lead(1, 0.6f, 30f));
        Assert.AreEqual(FocusArbiter.EVerdict.Switch, d.Verdict, $"radio {d.CandidateValue} vs belief {d.CurrentValue}");
    }

    [Test]
    public void Radio_DoesNotBeatABeliefThreeSecondsOld()
    {
        // §17.5: lost you a moment ago and the belief is precise — the decoy waits.
        var d = Decide(Player(0.5f, 3f, 5f), Lead(1, 0.6f, 30f));
        Assert.AreEqual(FocusArbiter.EVerdict.Keep, d.Verdict);
    }

    [Test]
    public void PlayerStep_BeatsTheChains_EvenRightAfterChoosingThem()
    {
        // Case 28: investigating the chains, a soft step on the other side of the room.
        var d = Decide(Lead(5, 0.45f, 6f), Player(2.5f, 0f, 10f), chosenAge: 0.5f, sinceSwitch: 0.5f);
        Assert.AreEqual(FocusArbiter.EVerdict.Switch, d.Verdict, $"step {d.CandidateValue} vs chains {d.CurrentValue}");
    }

    [Test]
    public void TwoSimilarNoises_DoNotFlipFlop()
    {
        // Case 29: the chains were chosen a second ago; the radio on the other side is only a bit more.
        var d = Decide(Lead(5, 0.45f, 12f, LeftSide), Lead(6, 0.5f, 12f, RightSide), chosenAge: 1f, sinceSwitch: 1f);
        Assert.AreEqual(FocusArbiter.EVerdict.Keep, d.Verdict);
    }

    // Two sides of a room: far enough apart not to be the same place (question 4 is 3 m).
    private static readonly Vector3 LeftSide = new Vector3(-8f, 0f, 0f);
    private static readonly Vector3 RightSide = new Vector3(8f, 0f, 0f);

    [Test]
    public void ClearlyBetterLead_StillWaitsOutTheAntiDither()
    {
        var d = Decide(Lead(5, 0.2f, 12f, LeftSide), Lead(6, 0.7f, 5f, RightSide), chosenAge: 5f, sinceSwitch: 1f);
        Assert.AreEqual(FocusArbiter.EVerdict.Keep, d.Verdict);
        Assert.AreEqual(FocusArbiter.EReason.TooSoon, d.Reason);
    }

    [Test]
    public void Chains_ThirdTimeFromAcrossTheLevel_AreBeneathNotice()
    {
        // Case 32: ×0.6 per fruitless visit. First and second still go, the third does not.
        const float Far = 40f;
        Assert.AreEqual(FocusArbiter.EVerdict.Switch, Decide(FocusArbiter.Candidate.None, Lead(5, 0.45f, Far, habituation: 1f)).Verdict);
        Assert.AreEqual(FocusArbiter.EVerdict.Switch, Decide(FocusArbiter.Candidate.None, Lead(5, 0.45f, Far, habituation: 0.6f)).Verdict);

        var third = Decide(FocusArbiter.Candidate.None, Lead(5, 0.45f, Far, habituation: 0.36f));
        Assert.AreEqual(FocusArbiter.EVerdict.Ignore, third.Verdict);
        Assert.AreEqual(FocusArbiter.EReason.BelowAttention, third.Reason);
    }

    [Test]
    public void Unreachable_WeighsLess_ButIsNotVetoed()
    {
        FocusArbiter.Candidate reachable = Lead(1, 0.6f, 20f);
        FocusArbiter.Candidate unreachable = reachable;
        unreachable.Distance = float.PositiveInfinity;

        float a = FocusArbiter.Value(reachable, Tuning);
        float b = FocusArbiter.Value(unreachable, Tuning);
        Assert.Less(b, a);
        Assert.Greater(b, 0f);
    }

    [Test]
    public void AlmostThere_KeepsTheCurrentLead()
    {
        // Two metres from the chains; a slightly better noise elsewhere does not pull it away.
        var d = Decide(Lead(5, 0.45f, 2f, LeftSide), Lead(6, 0.55f, 10f, RightSide));
        Assert.AreEqual(FocusArbiter.EVerdict.Keep, d.Verdict);
    }

    [Test]
    public void Questions_AreNumberedAsInThePlan()
    {
        Assert.AreEqual(2, FocusArbiter.QuestionOf(FocusArbiter.EReason.SeesPlayer));
        Assert.AreEqual(4, FocusArbiter.QuestionOf(FocusArbiter.EReason.SameThing));
        Assert.AreEqual(9, FocusArbiter.QuestionOf(FocusArbiter.EReason.NotBetterByMargin));
        Assert.AreEqual(11, FocusArbiter.QuestionOf(FocusArbiter.EReason.Better));
    }
}
