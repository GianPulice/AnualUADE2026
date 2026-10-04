using NUnit.Framework;

/// <summary>
/// When a search is over (plan §18.5 B, Fase 2B part 3): the minimum, the cap, "looked everywhere"
/// and the silence window scaled by the quality of the last evidence. Values from plan §12. And what
/// "looked everywhere" means since the possibility map decides where the search goes
/// (Plan-Busqueda-Nemesis §3.4): no place is worth the walk.
/// </summary>
public class SearchCoolingTests
{
    private const float MinTime = 6f;
    private const float Window = 8f;
    private const float Cap = 30f;
    private const float Tolerance = 1e-4f;
    private const float WorthThreshold = 0.015f;   // SO_NemesisData.SearchMapWorthThreshold as shipped

    // ── Quality ──────────────────────────────────────────────────────────────

    [Test]
    public void Quality_Sight_StretchesTheWindow()
    {
        Assert.AreEqual(1.25f, SearchCooling.Quality(true, false, 1.25f, 0.75f), Tolerance);
    }

    [Test]
    public void Quality_SightWinsOverMuffled()
    {
        // A sighting is never muffled; the flag only means something for a noise.
        Assert.AreEqual(1.25f, SearchCooling.Quality(true, true, 1.25f, 0.75f), Tolerance);
    }

    [Test]
    public void Quality_MuffledNoise_ShrinksIt()
    {
        Assert.AreEqual(0.75f, SearchCooling.Quality(false, true, 1.25f, 0.75f), Tolerance);
    }

    [Test]
    public void Quality_ClearNoise_LeavesItAlone()
    {
        Assert.AreEqual(1f, SearchCooling.Quality(false, false, 1.25f, 0.75f), Tolerance);
    }

    // ── IsWarm ───────────────────────────────────────────────────────────────

    [Test]
    public void IsWarm_UnderTheMinimum_EvenWithLongSilence()
    {
        Assert.IsTrue(SearchCooling.IsWarm(2f, 100f, MinTime, Window, 1f, Cap, true));
    }

    [Test]
    public void IsWarm_SilenceUnderTheWindow_KeepsSearching()
    {
        Assert.IsTrue(SearchCooling.IsWarm(10f, 7.9f, MinTime, Window, 1f, Cap, false));
    }

    [Test]
    public void IsWarm_SilenceOverTheWindow_CoolsDown()
    {
        Assert.IsFalse(SearchCooling.IsWarm(10f, 8.1f, MinTime, Window, 1f, Cap, false));
    }

    [Test]
    public void IsWarm_SightingStretchesTheWindow()
    {
        // 9 s of silence: cold after a noise (window 8), still warm after a sighting (8 × 1.25 = 10).
        Assert.IsFalse(SearchCooling.IsWarm(12f, 9f, MinTime, Window, 1f, Cap, false));
        Assert.IsTrue(SearchCooling.IsWarm(12f, 9f, MinTime, Window, 1.25f, Cap, false));
    }

    [Test]
    public void IsWarm_AtTheCap_CoolsDownWhateverItHears()
    {
        Assert.IsFalse(SearchCooling.IsWarm(30f, 0f, MinTime, Window, 1.25f, Cap, false));
    }

    [Test]
    public void IsWarm_NoCap_GoesOnWhileEvidenceKeepsComing()
    {
        // Ten minutes in, still hearing the player: no cap means no reason to stop.
        Assert.IsTrue(SearchCooling.IsWarm(600f, 0.5f, MinTime, Window, 1f, 0f, false));
    }

    [Test]
    public void IsWarm_NoCap_StillCoolsInSilence()
    {
        Assert.IsFalse(SearchCooling.IsWarm(600f, Window + 0.1f, MinTime, Window, 1f, 0f, false));
    }

    [Test]
    public void IsWarm_SearchedEverything_CoolsDownEarly()
    {
        Assert.IsFalse(SearchCooling.IsWarm(10f, 1f, MinTime, Window, 1f, Cap, true));
    }

    [Test]
    public void IsWarm_EscalatedSearch_UsesTheShorterCap()
    {
        // D26: a search that follows an empty investigation gets half the cap.
        Assert.IsFalse(SearchCooling.IsWarm(15f, 0f, MinTime, Window, 1f, Cap * 0.5f, false));
        Assert.IsTrue(SearchCooling.IsWarm(14f, 0f, MinTime, Window, 1f, Cap * 0.5f, false));
    }

    [Test]
    public void IsWarm_DirectorPersistence_ScalesWindowAndCap()
    {
        // Relax (×0.5): 5 s of silence is already too much. Rising sensitivity (×1.5): 11 s is not.
        Assert.IsFalse(SearchCooling.IsWarm(10f, 5f, MinTime, Window * 0.5f, 1f, Cap * 0.5f, false));
        Assert.IsTrue(SearchCooling.IsWarm(10f, 11f, MinTime, Window * 1.5f, 1f, Cap * 1.5f, false));
    }

    // ── "Revisé todo": nothing worth the walk ────────────────────────────────

    [Test]
    public void NothingWorthTheWalk_ALikelyPlaceNearby_IsWorthIt()
    {
        // Just lost in a corridor: half the value three seconds ahead.
        float worth = SearchPickRules.Worth(0.5f, 3f);

        Assert.IsFalse(SearchCooling.NothingWorthTheWalk(worth, WorthThreshold, 0f));
    }

    [Test]
    public void NothingWorthTheWalk_ValueSpreadThin_IsNot()
    {
        // The best place left holds 3 % of the value and is four seconds away: they could be anywhere.
        float worth = SearchPickRules.Worth(0.03f, 4f);

        Assert.IsTrue(SearchCooling.NothingWorthTheWalk(worth, WorthThreshold, 0f));
    }

    [Test]
    public void NothingWorthTheWalk_TheSameShare_IsWorthItNearAndNotFar()
    {
        // "So out of reach": 10 % of the value is worth three seconds of walk and not thirty.
        Assert.IsFalse(SearchCooling.NothingWorthTheWalk(SearchPickRules.Worth(0.1f, 3f), WorthThreshold, 0f));
        Assert.IsTrue(SearchCooling.NothingWorthTheWalk(SearchPickRules.Worth(0.1f, 30f), WorthThreshold, 0f));
    }

    [Test]
    public void NothingWorthTheWalk_NoPlaceItCanWalkTo_IsNot()
    {
        // All of it inside hiding spots, across the lift or on another island: no candidate at all.
        Assert.IsTrue(SearchCooling.NothingWorthTheWalk(0f, WorthThreshold, 0f));

        // Or a place holding everything, with no path to it.
        float worth = SearchPickRules.Worth(1f, float.PositiveInfinity);
        Assert.IsTrue(SearchCooling.NothingWorthTheWalk(worth, WorthThreshold, 0f));
    }

    [Test]
    public void NothingWorthTheWalk_MostOfItWentIntoTheHub_WhateverIsLeftOutside()
    {
        // Case 67 (C5): the sink holds the majority. The place still on the floor is the stretch in
        // front of the Hub's door, and by its own worth it would be walked to.
        float worth = SearchPickRules.Worth(0.35f, 1f);

        Assert.IsFalse(SearchCooling.NothingWorthTheWalk(worth, WorthThreshold, 0.3f));
        Assert.IsTrue(SearchCooling.NothingWorthTheWalk(worth, WorthThreshold, 0.6f));
        Assert.IsTrue(SearchCooling.NothingWorthTheWalk(worth, WorthThreshold, SearchCooling.SinkMajority));
    }

    [Test]
    public void NothingWorthTheWalk_ThresholdAtZero_OnlyTheHubEndsItThisWay()
    {
        Assert.IsFalse(SearchCooling.NothingWorthTheWalk(0f, 0f, 0f));
        Assert.IsFalse(SearchCooling.NothingWorthTheWalk(0.0001f, 0f, 0.2f));
        Assert.IsTrue(SearchCooling.NothingWorthTheWalk(0.0001f, 0f, 0.9f));
    }

    [Test]
    public void IsWarm_NothingWorthTheWalk_CoolsDownButNotUnderTheMinimum()
    {
        bool searchedEverything = SearchCooling.NothingWorthTheWalk(0f, WorthThreshold, 0f);

        // It always looks a little, even with nowhere to go; past the minimum it lets go, however
        // short the silence.
        Assert.IsTrue(SearchCooling.IsWarm(3f, 0.5f, MinTime, Window, 1f, 0f, searchedEverything));
        Assert.IsFalse(SearchCooling.IsWarm(7f, 0.5f, MinTime, Window, 1f, 0f, searchedEverything));
    }
}
