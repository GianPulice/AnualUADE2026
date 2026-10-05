using NUnit.Framework;

/// <summary>
/// The answer the ladder reads for "the route to the belief does not get there": it changes only
/// when the new answer has held, so one bad path query cannot take a chase away (plan §19.4, T1).
/// </summary>
public class SettledVerdictTests
{
    private const float Settle = 0.75f;
    private const float Stale = 0.75f;
    private const float Frame = 0.05f;

    /// <summary>Asks every frame from <paramref name="from"/> for <paramref name="seconds"/>, with
    /// the same raw answer, and returns the last settled one and the time it stopped at.</summary>
    private static bool Hold(SettledVerdict verdict, ref float now, float seconds, bool raw)
    {
        bool value = verdict.Value;

        for (float elapsed = 0f; elapsed < seconds - 0.0001f; elapsed += Frame)
        {
            value = verdict.Step(now, raw, Settle, Stale);
            now += Frame;
        }

        return value;
    }

    [Test]
    public void TheFirstAnswer_IsTakenAsItComes()
    {
        Assert.IsTrue(new SettledVerdict().Step(10f, true, Settle, Stale));
        Assert.IsFalse(new SettledVerdict().Step(10f, false, Settle, Stale));
    }

    /// <summary>The case it exists for: chasing a reachable player, one path query fails.</summary>
    [Test]
    public void OneBadAnswer_DoesNotTakeTheChaseAway()
    {
        var verdict = new SettledVerdict();
        float now = 0f;

        Hold(verdict, ref now, 2f, false);
        Assert.IsFalse(Hold(verdict, ref now, 0.4f, true), "a 0.4 s blip settled");
        Assert.IsFalse(Hold(verdict, ref now, 1f, false));
    }

    /// <summary>And the other way: searching round a ledge it cannot reach, one query says it can.
    /// </summary>
    [Test]
    public void OneGoodAnswer_DoesNotStartAChase()
    {
        var verdict = new SettledVerdict();
        float now = 0f;

        Hold(verdict, ref now, 2f, true);
        Assert.IsTrue(Hold(verdict, ref now, 0.4f, false), "a 0.4 s blip settled");
        Assert.IsTrue(Hold(verdict, ref now, 1f, true));
    }

    [Test]
    public void AnAnswerThatHolds_Replaces_TheSettledOne()
    {
        var verdict = new SettledVerdict();
        float now = 0f;

        Hold(verdict, ref now, 1f, false);
        Assert.IsFalse(Hold(verdict, ref now, Settle - Frame, true), "it settled early");
        Assert.IsTrue(Hold(verdict, ref now, 3 * Frame, true), "it never settled");
    }

    /// <summary>Two blips with agreement in between are two blips, not one long disagreement.</summary>
    [Test]
    public void Blips_DoNotAddUp()
    {
        var verdict = new SettledVerdict();
        float now = 0f;

        Hold(verdict, ref now, 1f, false);

        for (int i = 0; i < 6; i++)
        {
            Assert.IsFalse(Hold(verdict, ref now, 0.5f, true), $"blip {i} settled");
            Hold(verdict, ref now, 0.1f, false);
        }

        Assert.IsFalse(verdict.Value);
    }

    /// <summary>Nobody asked for a while (sight was lost, it rode the lift): what was settled is not
    /// known any more, and the first answer back is not held to the delay.</summary>
    [Test]
    public void AfterAGapInTheAsking_TheFirstAnswerIsTakenAsItComes()
    {
        var verdict = new SettledVerdict();
        float now = 0f;

        Hold(verdict, ref now, 1f, false);
        now += Stale + 0.5f;

        Assert.IsTrue(verdict.Step(now, true, Settle, Stale));
    }

    [Test]
    public void AShortGapInTheAsking_KeepsTheSettledAnswer()
    {
        var verdict = new SettledVerdict();
        float now = 0f;

        Hold(verdict, ref now, 1f, false);
        now += Stale * 0.5f;

        Assert.IsFalse(verdict.Step(now, true, Settle, Stale));
    }

    [Test]
    public void WithNoSettleTime_ItIsTheRawAnswer()
    {
        var verdict = new SettledVerdict();

        Assert.IsFalse(verdict.Step(0f, false, 0f, Stale));
        Assert.IsTrue(verdict.Step(0.05f, true, 0f, Stale));
        Assert.IsFalse(verdict.Step(0.1f, false, 0f, Stale));
    }

    [Test]
    public void Reset_ForgetsWhatWasSettled()
    {
        var verdict = new SettledVerdict();
        float now = 0f;

        Hold(verdict, ref now, 1f, true);
        verdict.Reset();

        Assert.IsFalse(verdict.Value);
        Assert.IsFalse(verdict.Step(now, false, Settle, Stale), "the answer after a reset was held back");
    }
}
