using NUnit.Framework;

/// <summary>When a hiding spot is suspected, from the map's value inside it and the habit
/// (plan §19.5, D38/D41).</summary>
public class SpotOpenRulesTests
{
    private const float Tolerance = 1e-4f;
    private const float FullShare = 0.05f;
    private const float NeverMin = 0.25f;
    private const float NeverChance = 0.3f;

    private static float Chance(float share, float habit) =>
        SpotOpenRules.Chance(share, habit, FullShare, NeverMin, NeverChance);

    [Test]
    public void UsedSpot_TheMapHasCleared_NeverOpens()
    {
        Assert.AreEqual(0f, Chance(0f, 0.85f), Tolerance);
    }

    [Test]
    public void UsedSpot_WithEnoughValue_KeepsItsHabitChance()
    {
        Assert.AreEqual(0.5f, Chance(0.05f, 0.5f), Tolerance);
        Assert.AreEqual(0.5f, Chance(0.6f, 0.5f), Tolerance);
    }

    [Test]
    public void UsedSpot_WithLittleValue_IsScaledDown()
    {
        Assert.AreEqual(0.25f, Chance(0.025f, 0.5f), Tolerance);
    }

    [Test]
    public void NeverUsedSpot_BelowTheHighThreshold_DoesNotOpen()
    {
        Assert.AreEqual(0f, Chance(0.24f, 0f), Tolerance);
    }

    [Test]
    public void NeverUsedSpot_AtTheThreshold_OpensByTheFixedChance()
    {
        Assert.AreEqual(NeverChance, Chance(0.25f, 0f), Tolerance);
        Assert.AreEqual(NeverChance, Chance(0.9f, 0f), Tolerance);
    }

    [Test]
    public void ZeroFullShare_MeansTheHabitAlone()
    {
        Assert.AreEqual(0.4f, SpotOpenRules.Chance(0.01f, 0.4f, 0f, NeverMin, NeverChance), Tolerance);
    }
}
