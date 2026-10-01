using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Which escalation tier applies to a completed-puzzle count, and what a tier does to a number
/// (plan Fase 7).
/// </summary>
public class EscalationRulesTests
{
    private const float Tolerance = 1e-4f;

    private static List<EscalationTier> SpecTiers() => new List<EscalationTier>
    {
        new EscalationTier(0, 1f, 1f, 1f, 0f),
        new EscalationTier(2, 1.1f, 1f, 1f, 0.25f),
        new EscalationTier(3, 1.15f, 1.1f, 1f, 0.4f),
    };

    [Test]
    public void TierIndex_FollowsTheCompletedPuzzles()
    {
        List<EscalationTier> tiers = SpecTiers();

        Assert.AreEqual(0, EscalationRules.TierIndex(tiers, 0));
        Assert.AreEqual(0, EscalationRules.TierIndex(tiers, 1));
        Assert.AreEqual(1, EscalationRules.TierIndex(tiers, 2));
        Assert.AreEqual(2, EscalationRules.TierIndex(tiers, 3));
        Assert.AreEqual(2, EscalationRules.TierIndex(tiers, 12));
    }

    [Test]
    public void TierIndex_DoesNotDependOnTheOrderOfTheList()
    {
        List<EscalationTier> tiers = SpecTiers();
        tiers.Reverse();

        Assert.AreEqual(2, EscalationRules.TierIndex(tiers, 1));
        Assert.AreEqual(0, EscalationRules.TierIndex(tiers, 3));
    }

    [Test]
    public void TierIndex_OnATie_TheLaterRowWins()
    {
        List<EscalationTier> tiers = new List<EscalationTier>
        {
            new EscalationTier(2, 1.1f, 1f, 1f, 0f),
            new EscalationTier(2, 1.2f, 1f, 1f, 0f),
        };

        Assert.AreEqual(1, EscalationRules.TierIndex(tiers, 2));
    }

    [Test]
    public void TierIndex_WithNothingReached_IsMinusOne()
    {
        List<EscalationTier> tiers = new List<EscalationTier> { new EscalationTier(2, 1.1f, 1f, 1f, 0f) };

        Assert.AreEqual(-1, EscalationRules.TierIndex(tiers, 1));
        Assert.AreEqual(-1, EscalationRules.TierIndex(new List<EscalationTier>(), 5));
        Assert.AreEqual(-1, EscalationRules.TierIndex(null, 5));
    }

    [Test]
    public void TierIndex_SkipsNullRows()
    {
        List<EscalationTier> tiers = new List<EscalationTier> { null, new EscalationTier(1, 1.1f, 1f, 1f, 0f) };

        Assert.AreEqual(1, EscalationRules.TierIndex(tiers, 1));
    }

    [Test]
    public void IsIdentity_OnlyWhenNothingChanges()
    {
        Assert.IsTrue(EscalationRules.IsIdentity(new EscalationTier(0, 1f, 1f, 1f, 0f)));
        Assert.IsTrue(EscalationRules.IsIdentity(null));
        Assert.IsFalse(EscalationRules.IsIdentity(new EscalationTier(0, 1.1f, 1f, 1f, 0f)));
        Assert.IsFalse(EscalationRules.IsIdentity(new EscalationTier(0, 1f, 1f, 0.8f, 0f)));
        Assert.IsFalse(EscalationRules.IsIdentity(new EscalationTier(0, 1f, 1f, 1f, 0.25f)));
    }

    [Test]
    public void RouteChance_IsRaisedToTheFloor_NeverLowered()
    {
        Assert.AreEqual(0.25f, EscalationRules.RouteChance(0.15f, 0.25f), Tolerance);
        Assert.AreEqual(0.5f, EscalationRules.RouteChance(0.5f, 0.25f), Tolerance);
        Assert.AreEqual(0.15f, EscalationRules.RouteChance(0.15f, 0f), Tolerance);
    }

    [Test]
    public void Scale_MultipliesAndNeverGoesNegative()
    {
        Assert.AreEqual(8.05f, EscalationRules.Scale(7f, 1.15f), Tolerance);
        Assert.AreEqual(0f, EscalationRules.Scale(7f, -1f), Tolerance);
    }
}
