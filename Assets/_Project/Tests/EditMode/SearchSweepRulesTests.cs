using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The search's two questions and its sweep size (plan §18.5 A, Fase 2B part 2): is there anything
/// new, is it the same place it is already sweeping, and how wide to sweep. Plain numbers in, a
/// verdict out, so every case is a few lines and needs no scene.
/// </summary>
public class SearchSweepRulesTests
{
    private const float Tolerance = 1e-4f;
    private const float FloorBand = 2.5f;

    private static readonly Vector3 Centre = new Vector3(10f, 0f, 10f);

    // ── Question 1: anything new? ────────────────────────────────────────────

    [Test]
    public void Judge_SameSequence_Keeps()
    {
        var verdict = SearchSweepRules.Judge(7, 7, true, Centre, 4f, Centre + Vector3.right * 20f, FloorBand);

        // Even evidence far away is ignored if it is not NEW: the sequence did not move.
        Assert.AreEqual(SearchSweepRules.EVerdict.Keep, verdict);
    }

    [Test]
    public void Judge_NewEvidenceWithoutSweep_Recenters()
    {
        var verdict = SearchSweepRules.Judge(0, 1, false, Vector3.zero, 0f, Centre, FloorBand);

        Assert.AreEqual(SearchSweepRules.EVerdict.Recenter, verdict);
    }

    // ── Question 4: the same place? ──────────────────────────────────────────

    [Test]
    public void Judge_NewEvidenceInsideDisc_Updates()
    {
        var verdict = SearchSweepRules.Judge(1, 2, true, Centre, 4f, Centre + new Vector3(2f, 0f, 2f), FloorBand);

        Assert.AreEqual(SearchSweepRules.EVerdict.Update, verdict);
    }

    [Test]
    public void Judge_NewEvidenceOutsideDisc_Recenters()
    {
        var verdict = SearchSweepRules.Judge(1, 2, true, Centre, 4f, Centre + new Vector3(5f, 0f, 0f), FloorBand);

        Assert.AreEqual(SearchSweepRules.EVerdict.Recenter, verdict);
    }

    [Test]
    public void Judge_SameSpotOneFloorUp_Recenters()
    {
        // Straight above the centre, a storey up: the flat distance is zero, and it is still
        // somewhere else.
        var verdict = SearchSweepRules.Judge(1, 2, true, Centre, 4f, Centre + Vector3.up * 5f, FloorBand);

        Assert.AreEqual(SearchSweepRules.EVerdict.Recenter, verdict);
    }

    [Test]
    public void Judge_ContinuousFootstepsInsideTheDisc_NeverRecenter()
    {
        // The case question 4 exists for: evidence every 0.1 s, each a little further along, with
        // the centre following it. Ten sequence changes, zero restarts.
        Vector3 centre = Centre;
        int consumed = 0;

        for (int sequence = 1; sequence <= 10; sequence++)
        {
            Vector3 step = centre + new Vector3(0.25f, 0f, 0f);
            var verdict = SearchSweepRules.Judge(consumed, sequence, true, centre, 4f, step, FloorBand);

            Assert.AreEqual(SearchSweepRules.EVerdict.Update, verdict, $"sequence {sequence}");

            consumed = sequence;
            centre = step;   // What the search does on Update: the centre follows the evidence.
        }
    }

    // ── IsInside ─────────────────────────────────────────────────────────────

    [Test]
    public void IsInside_OnTheRim_CountsAsInside()
    {
        Assert.IsTrue(SearchSweepRules.IsInside(Centre, 4f, Centre + new Vector3(4f, 0f, 0f), FloorBand));
    }

    [Test]
    public void IsInside_SmallStepUp_IsStillTheSameFloor()
    {
        Assert.IsTrue(SearchSweepRules.IsInside(Centre, 4f, Centre + new Vector3(1f, 1f, 0f), FloorBand));
    }

    // ── Sweep size ───────────────────────────────────────────────────────────

    [Test]
    public void SweepRadius_Sighting_ClampsUpToTheMinimum()
    {
        // A sighting is a point (0.5 m): +1 m margin is 1.5, below the 3 m floor.
        Assert.AreEqual(3f, SearchSweepRules.SweepRadius(0.5f, 1f, 3f, 8f), Tolerance);
    }

    [Test]
    public void SweepRadius_NoiseThroughAWall_IsWider()
    {
        Assert.AreEqual(5.5f, SearchSweepRules.SweepRadius(4.5f, 1f, 3f, 8f), Tolerance);
    }

    [Test]
    public void SweepRadius_VagueEvidence_ClampsToTheMaximum()
    {
        Assert.AreEqual(8f, SearchSweepRules.SweepRadius(20f, 1f, 3f, 8f), Tolerance);
    }

    [Test]
    public void SweepRadius_NoEvidenceRadius_UsesTheMaximum()
    {
        Assert.AreEqual(8f, SearchSweepRules.SweepRadius(float.PositiveInfinity, 1f, 3f, 8f), Tolerance);
    }

    [Test]
    public void SweepRadius_MaximumBelowMinimum_UsesTheMinimum()
    {
        Assert.AreEqual(3f, SearchSweepRules.SweepRadius(10f, 1f, 3f, 2f), Tolerance);
    }

    [Test]
    public void Widen_GrowsByTheStepAndStopsAtTheMaximum()
    {
        Assert.AreEqual(5.5f, SearchSweepRules.Widen(3f, 2.5f, 8f), Tolerance);
        Assert.AreEqual(8f, SearchSweepRules.Widen(7f, 2.5f, 8f), Tolerance);
    }
}
