using NUnit.Framework;

/// <summary>
/// The Nemesis's three line-of-sight zones and its suspicion meter: focus sees, the periphery
/// suspects and can become a sighting, the rear only ever makes it turn round to look.
/// </summary>
public class VisionZonesTests
{
    private const float ViewAngle = 170f;
    private const float FocusAngle = 80f;
    private const float ViewRange = 7f;
    private const float RearRange = 3f;

    private static VisionZones.EZone Zone(float angle, float distance) =>
        VisionZones.Classify(angle, distance, ViewAngle, FocusAngle, ViewRange, RearRange);

    // ── Zones ────────────────────────────────────────────────────────────────

    [Test]
    public void DeadAhead_IsFocus() => Assert.AreEqual(VisionZones.EZone.Focus, Zone(0f, 5f));

    [Test]
    public void FocusEdge_IsStillFocus() => Assert.AreEqual(VisionZones.EZone.Focus, Zone(40f, 5f));

    [Test]
    public void PastTheFocus_IsPeripheral() => Assert.AreEqual(VisionZones.EZone.Peripheral, Zone(41f, 5f));

    [Test]
    public void ConeEdge_IsStillPeripheral() => Assert.AreEqual(VisionZones.EZone.Peripheral, Zone(85f, 5f));

    [Test]
    public void BeyondViewRange_InFront_IsNothing() => Assert.AreEqual(VisionZones.EZone.None, Zone(0f, 7.5f));

    [Test]
    public void JustBehindTheCone_Close_IsRear() => Assert.AreEqual(VisionZones.EZone.Rear, Zone(86f, 2f));

    [Test]
    public void StraightBehind_Close_IsRear() => Assert.AreEqual(VisionZones.EZone.Rear, Zone(180f, 3f));

    [Test]
    public void StraightBehind_Far_IsNothing() => Assert.AreEqual(VisionZones.EZone.None, Zone(180f, 3.5f));

    [Test]
    public void RearRangeZero_SwitchesTheRearOff()
    {
        Assert.AreEqual(VisionZones.EZone.None,
                        VisionZones.Classify(180f, 1f, ViewAngle, FocusAngle, ViewRange, 0f));
    }

    [Test]
    public void NegativeAngle_IsTheOtherSide() => Assert.AreEqual(VisionZones.EZone.Peripheral, Zone(-60f, 5f));

    // ── Rate ─────────────────────────────────────────────────────────────────

    [Test]
    public void BuildRate_CloserIsFaster()
    {
        Assert.Greater(VisionZones.BuildRate(0.9f, 1.2f), VisionZones.BuildRate(0.1f, 1.2f));
        Assert.Greater(VisionZones.BuildRate(0f, 1.2f), 0f, "the edge of the range still gets there");
    }

    [Test]
    public void Closeness_OneAtTheEye_ZeroAtTheRange()
    {
        Assert.AreEqual(1f, VisionZones.Closeness(0f, 3f), 1e-5f);
        Assert.AreEqual(0f, VisionZones.Closeness(3f, 3f), 1e-5f);
        Assert.AreEqual(0f, VisionZones.Closeness(10f, 3f), 1e-5f);
    }

    // ── The senses adding up ─────────────────────────────────────────────────

    private static readonly UnityEngine.Vector3 Belief = new UnityEngine.Vector3(10f, 0f, 10f);

    [Test]
    public void Corroborates_GlimpseInsideAFreshBelief()
    {
        Assert.IsTrue(VisionZones.Corroborates(Belief + new UnityEngine.Vector3(2f, 0f, 1f), Belief, 4f, 1.5f, 8f, 2.5f));
    }

    [Test]
    public void Corroborates_NotOutsideTheRadius()
    {
        Assert.IsFalse(VisionZones.Corroborates(Belief + new UnityEngine.Vector3(6f, 0f, 0f), Belief, 4f, 1.5f, 8f, 2.5f));
    }

    [Test]
    public void Corroborates_NotWhenTheBeliefIsOld()
    {
        Assert.IsFalse(VisionZones.Corroborates(Belief, Belief, 40f, 9f, 8f, 2.5f));
    }

    [Test]
    public void Corroborates_NotWithoutABelief()
    {
        Assert.IsFalse(VisionZones.Corroborates(Belief, Belief, float.PositiveInfinity, float.PositiveInfinity, 8f, 2.5f));
    }

    [Test]
    public void Corroborates_NotOnAnotherFloor()
    {
        Assert.IsFalse(VisionZones.Corroborates(Belief + UnityEngine.Vector3.up * 4f, Belief, 40f, 1f, 8f, 2.5f));
    }

    [Test]
    public void Corroborates_WindowZero_IsOff()
    {
        Assert.IsFalse(VisionZones.Corroborates(Belief, Belief, 40f, 0f, 0f, 2.5f));
    }

    // ── The meter ────────────────────────────────────────────────────────────

    [Test]
    public void Meter_Eyes_FillToOne()
    {
        float meter = 0f;
        for (int i = 0; i < 100; i++) meter = VisionZones.StepMeter(meter, 0.1f, true, 1f, false, 0f, 0.5f, 0.9f);

        Assert.AreEqual(1f, meter, 1e-5f);
    }

    [Test]
    public void Meter_PresenceBehindAlone_NeverASighting()
    {
        float meter = 0f;
        for (int i = 0; i < 500; i++) meter = VisionZones.StepMeter(meter, 0.1f, false, 0f, true, 0.2f, 0.5f, 0.9f);

        Assert.AreEqual(0.9f, meter, 1e-5f);
    }

    [Test]
    public void Meter_PresenceBehind_CrossesTheSuspicionThreshold()
    {
        // Rear rate at mid range: a quarter of the peripheral one.
        float rate = VisionZones.BuildRate(0.5f, 1.2f) * 0.25f;
        float meter = 0f;
        float seconds = 0f;
        while (meter < 0.4f && seconds < 30f)
        {
            meter = VisionZones.StepMeter(meter, 0.1f, false, 0f, true, rate, 0.5f, 0.9f);
            seconds += 0.1f;
        }

        Assert.Less(seconds, 30f, "a presence behind it has to make it turn round eventually");
        Assert.Greater(seconds, 1f, "but much more slowly than a glimpse");
    }

    [Test]
    public void Meter_AboveTheCap_HoldsWhileTheSenseGoesOn()
    {
        // The eyes put it at 0.95, then lose the player while they are still felt behind it.
        float meter = VisionZones.StepMeter(0.95f, 0.1f, false, 0f, true, 0.2f, 0.5f, 0.9f);

        Assert.AreEqual(0.95f, meter, 1e-5f, "the cap limits what the sense adds, not what the eyes put");
    }

    [Test]
    public void Meter_NoContact_Drains()
    {
        float meter = VisionZones.StepMeter(0.6f, 0.2f, false, 0f, false, 0f, 0.5f, 0.9f);

        Assert.AreEqual(0.5f, meter, 1e-5f);
    }

    [Test]
    public void Meter_EyesAndPresence_AddUp()
    {
        float eyes = VisionZones.StepMeter(0f, 0.1f, true, 1f, false, 0f, 0.5f, 0.9f);
        float both = VisionZones.StepMeter(0f, 0.1f, true, 1f, true, 0.3f, 0.5f, 0.9f);

        Assert.Greater(both, eyes);
    }
}
