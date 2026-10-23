using NUnit.Framework;

/// <summary>
/// The weight the cluster patrol gives a zone for the value the search left behind (plan §19.5, E3).
/// </summary>
public class PatrolMapValueTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void NoValue_LeavesTheWeightAlone()
    {
        Assert.AreEqual(1f, PatrolMapValue.Weight(0f, 3f, 0.1f), Tolerance);
    }

    [Test]
    public void FullShare_GetsTheFullStrength()
    {
        Assert.AreEqual(3f, PatrolMapValue.Weight(0.1f, 3f, 0.1f), Tolerance);
    }

    [Test]
    public void MoreThanFullShare_DoesNotStackPastTheStrength()
    {
        Assert.AreEqual(3f, PatrolMapValue.Weight(0.8f, 3f, 0.1f), Tolerance);
    }

    [Test]
    public void HalfShare_IsHalfwayBetween()
    {
        Assert.AreEqual(2f, PatrolMapValue.Weight(0.05f, 3f, 0.1f), Tolerance);
    }

    [Test]
    public void StrengthOfOne_TurnsItOff()
    {
        Assert.AreEqual(1f, PatrolMapValue.Weight(0.5f, 1f, 0.1f), Tolerance);
    }
}
