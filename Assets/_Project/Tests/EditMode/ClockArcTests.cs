using NUnit.Framework;

/// <summary>
/// The hit test of the Central Puzzle 2 stabilization checks: is the needle inside the success
/// zone? The needle keeps turning lap after lap and a zone can straddle twelve o'clock.
/// </summary>
public class ClockArcTests
{
    [Test]
    public void InsidePlainArc_Hits()
    {
        Assert.IsTrue(ClockArc.Contains(100f, 90f, 30f));
        Assert.IsTrue(ClockArc.Contains(90f, 90f, 30f));
        Assert.IsTrue(ClockArc.Contains(120f, 90f, 30f));
    }

    [Test]
    public void OutsidePlainArc_Misses()
    {
        Assert.IsFalse(ClockArc.Contains(89f, 90f, 30f));
        Assert.IsFalse(ClockArc.Contains(121f, 90f, 30f));
        Assert.IsFalse(ClockArc.Contains(300f, 90f, 30f));
    }

    [Test]
    public void ArcAcrossTwelve_HitsOnBothSides()
    {
        Assert.IsTrue(ClockArc.Contains(350f, 340f, 40f));
        Assert.IsTrue(ClockArc.Contains(10f, 340f, 40f));
        Assert.IsFalse(ClockArc.Contains(30f, 340f, 40f));
        Assert.IsFalse(ClockArc.Contains(330f, 340f, 40f));
    }

    [Test]
    public void LaterLaps_AreTheSameAngle()
    {
        Assert.IsTrue(ClockArc.Contains(100f + 360f * 3f, 90f, 30f));
        Assert.IsFalse(ClockArc.Contains(200f + 720f, 90f, 30f));
        Assert.IsTrue(ClockArc.Contains(-260f, 90f, 30f));   // -260 = 100
    }

    [Test]
    public void DegenerateWidths()
    {
        Assert.IsFalse(ClockArc.Contains(90f, 90f, 0f));
        Assert.IsTrue(ClockArc.Contains(5f, 90f, 360f));
    }

    [Test]
    public void Normalize_WrapsIntoOneLap()
    {
        Assert.AreEqual(10f, ClockArc.Normalize(370f), 1e-4f);
        Assert.AreEqual(350f, ClockArc.Normalize(-10f), 1e-4f);
        Assert.AreEqual(0f, ClockArc.Normalize(720f), 1e-4f);
    }
}
