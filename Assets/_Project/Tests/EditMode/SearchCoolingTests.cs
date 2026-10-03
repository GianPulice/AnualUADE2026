using NUnit.Framework;

/// <summary>
/// When a search is over (plan §18.5 B, Fase 2B part 3): the minimum, the cap, "looked everywhere"
/// and the silence window scaled by the quality of the last evidence. Values from plan §12.
/// </summary>
public class SearchCoolingTests
{
    private const float MinTime = 6f;
    private const float Window = 8f;
    private const float Cap = 30f;
    private const float Tolerance = 1e-4f;

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
}
