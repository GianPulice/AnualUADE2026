using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Why a vision sweep came back without the player: the reason the trace writes down.
/// </summary>
public class SightMissTests
{
    [Test]
    public void NothingInRange_IsOutOfRange() =>
        Assert.AreEqual(SightMiss.EReason.OutOfRange, SightMiss.Classify(false, false, false));

    [Test]
    public void InRange_NoSampleInsideTheCone_IsOutOfCone() =>
        Assert.AreEqual(SightMiss.EReason.OutOfCone, SightMiss.Classify(true, false, false));

    [Test]
    public void InRange_InsideTheCone_NotSeen_IsOccluded() =>
        Assert.AreEqual(SightMiss.EReason.Occluded, SightMiss.Classify(true, true, false));

    [Test]
    public void SeenInTheOuterBandOnly_IsPeriphery() =>
        Assert.AreEqual(SightMiss.EReason.Periphery, SightMiss.Classify(true, true, true));

    /// <summary>One collider of theirs behind a wall and another in the corner of its eye: the
    /// glimpse is what the sweep has, not the wall.</summary>
    [Test]
    public void APeripheralCandidate_WinsOverTheOnesThatFailed()
    {
        Assert.AreEqual(SightMiss.EReason.Periphery, SightMiss.Classify(true, false, true));
    }

    [Test]
    public void Seeing_WritesADash() => Assert.AreEqual("-", SightMiss.Token(SightMiss.EReason.None));

    /// <summary>A reason added to the enum without a token would read as "-" in the trace: as though
    /// it were seeing the player.</summary>
    [Test]
    public void EveryReason_HasItsOwnToken()
    {
        var seen = new HashSet<string>();

        foreach (SightMiss.EReason reason in Enum.GetValues(typeof(SightMiss.EReason)))
        {
            string token = SightMiss.Token(reason);

            Assert.IsFalse(string.IsNullOrEmpty(token), $"{reason} has no token");
            Assert.IsTrue(seen.Add(token), $"{reason} shares the token '{token}'");
            if (reason != SightMiss.EReason.None) Assert.AreNotEqual("-", token, $"{reason} reads as seeing");
        }
    }
}
