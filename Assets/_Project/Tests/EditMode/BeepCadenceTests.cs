using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The module countdown's beep pace: 30 s → 10 s up to amber, 10 s → 5 s up to red, then an
/// exponential fall to the urgent 0.5 s, which holds to the end. All times are seconds LEFT.
/// </summary>
public class BeepCadenceTests
{
    private const float Tolerance = 1e-3f;

    // The shipping values: M2 / M3 (180 s). Amber at 25 % = 45 s; red at max(10 %, 30 s) = 30 s.
    private static BeepCadence Short() => new BeepCadence(180f, 45f, 30f, 10f, 30f, 10f, 5f, 0.5f);

    // M1 (900 s). Amber at 225 s; red at max(90 s, 30 s) = 90 s.
    private static BeepCadence Long() => new BeepCadence(900f, 225f, 90f, 10f, 30f, 10f, 5f, 0.5f);

    // ── The numbers the design asks for ──────────────────────────────────────

    [Test]
    public void Start_IsTheSlowestBeep()
    {
        Assert.AreEqual(30f, Short().IntervalAt(180f), Tolerance);
        Assert.AreEqual(30f, Long().IntervalAt(900f), Tolerance);
    }

    [Test]
    public void Amber_IsOneBeepEveryTenSeconds()
    {
        Assert.AreEqual(10f, Short().IntervalAt(45f), Tolerance);
        Assert.AreEqual(10f, Long().IntervalAt(225f), Tolerance);
    }

    [Test]
    public void Red_IsOneBeepEveryFiveSeconds()
    {
        Assert.AreEqual(5f, Short().IntervalAt(30f), Tolerance);
        Assert.AreEqual(5f, Long().IntervalAt(90f), Tolerance);
    }

    [Test]
    public void Urgent_IsHalfASecond_AllTheWayDown()
    {
        BeepCadence c = Short();
        Assert.AreEqual(0.5f, c.IntervalAt(10f), Tolerance);
        Assert.AreEqual(0.5f, c.IntervalAt(5f), Tolerance);
        Assert.AreEqual(0.5f, c.IntervalAt(0f), Tolerance);
    }

    // ── Shape ────────────────────────────────────────────────────────────────

    [Test]
    public void BeforeAmber_ShrinksInAStraightLine()
    {
        // Halfway between the start (180 s) and amber (45 s): halfway between 30 s and 10 s.
        Assert.AreEqual(20f, Short().IntervalAt(112.5f), Tolerance);
    }

    [Test]
    public void AmberToRed_ShrinksInAStraightLine()
    {
        Assert.AreEqual(7.5f, Short().IntervalAt(37.5f), Tolerance);
    }

    [Test]
    public void RedToUrgent_IsExponential_NotLinear()
    {
        // Halfway through the stage an exponential fall sits at the geometric mean of its ends
        // (sqrt(5 × 0.5) ≈ 1.58 s), well under the straight line's 2.75 s: it falls fast first.
        float mid = Short().IntervalAt(20f);
        Assert.AreEqual(Mathf.Sqrt(5f * 0.5f), mid, Tolerance);
        Assert.Less(mid, 2.75f);
    }

    [Test]
    public void TheIntervalNeverGrowsAsTheModuleRunsOut()
    {
        foreach (BeepCadence c in new[] { Short(), Long() })
        {
            float previous = float.MaxValue;
            for (float left = c.Duration; left >= 0f; left -= 0.25f)
            {
                float interval = c.IntervalAt(left);
                Assert.LessOrEqual(interval, previous + Tolerance, $"it grew at {left} s left");
                previous = interval;
            }
        }
    }

    [Test]
    public void ContinuousAcrossEveryBoundary()
    {
        const float Eps = 0.01f;
        foreach (BeepCadence c in new[] { Short(), Long() })
        {
            foreach (float boundary in new[] { c.AmberAt, c.RedAt, c.UrgentAt })
            {
                // Both sides of a boundary must land close: no step in the pace to hear.
                float before = c.IntervalAt(boundary + Eps);
                float after = c.IntervalAt(boundary - Eps);
                Assert.AreEqual(before, after, 0.05f, $"a step at {boundary} s left");
            }
        }
    }

    // ── The whole countdown, beep by beep ────────────────────────────────────

    [Test]
    public void ShortModule_BeepCountIsSane()
    {
        // The schedule the beeper runs: the next beep one interval below the last one.
        BeepCadence c = Short();
        int beeps = 0;
        int urgent = 0;
        for (float at = 180f - c.IntervalAt(180f); at > 0f; at -= c.IntervalAt(at))
        {
            beeps++;
            if (at <= 10f) urgent++;
            Assert.Less(beeps, 200, "runaway schedule");
        }

        // About 7 slow beeps, 3 amber, a dozen red and the last 20 urgent ones (10 s at 0.5 s).
        Assert.GreaterOrEqual(urgent, 19);
        Assert.Less(beeps, 60);
    }

    // ── Degenerate stages ────────────────────────────────────────────────────

    [Test]
    public void NoRedStage_StillHasACurve()
    {
        // Red = 0 (never): amber to the end, then the urgent tail.
        BeepCadence c = new BeepCadence(180f, 45f, 0f, 10f, 30f, 10f, 5f, 0.5f);
        Assert.AreEqual(30f, c.IntervalAt(180f), Tolerance);
        Assert.AreEqual(0.5f, c.IntervalAt(10f), Tolerance);
        Assert.Greater(c.IntervalAt(20f), 0.5f);
    }

    [Test]
    public void RedInsideTheUrgentTail_SkipsTheExponential()
    {
        // Red at 8 s, urgent from 10 s: the urgent rate already holds there.
        BeepCadence c = new BeepCadence(180f, 45f, 8f, 10f, 30f, 10f, 5f, 0.5f);
        Assert.AreEqual(0.5f, c.IntervalAt(8f), Tolerance);
        Assert.AreEqual(0.5f, c.IntervalAt(9.9f), Tolerance);
    }

    [Test]
    public void StagesPastTheModule_AreHeldInside()
    {
        // A module shorter than its own stages: no NaN, no interval outside the configured range.
        BeepCadence c = new BeepCadence(20f, 45f, 30f, 10f, 30f, 10f, 5f, 0.5f);
        for (float left = 20f; left >= 0f; left -= 0.5f)
        {
            float interval = c.IntervalAt(left);
            Assert.IsFalse(float.IsNaN(interval));
            Assert.That(interval, Is.InRange(0.5f - Tolerance, 30f + Tolerance));
        }
    }
}
