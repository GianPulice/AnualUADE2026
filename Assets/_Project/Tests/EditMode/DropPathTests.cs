using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The shape of a drop between floors (plan §15.3, Fase 8): the arc it flies, and for a tall drop
/// the hang before it. Pure geometry, so every case is a few lines and needs no scene. What these
/// pin down is what a playtest can only show as "it clipped through the floor" or "it teleported".
/// </summary>
public class DropPathTests
{
    private const float Tolerance = 1e-3f;
    private const float Gravity = 12f;

    private static readonly DropTuning Tuning = DropTuning.Default;

    [Test]
    public void Arc_StartsWhereItLeavesAndEndsWhereItLands()
    {
        Vector3 from = new Vector3(1f, 5f, 2f);
        Vector3 to = new Vector3(2.5f, 1.2f, 2f);

        DropArc arc = DropArc.Create(from, to, Gravity, 0.3f, 0.35f);

        AssertNear(from, arc.PointAt(0f));
        AssertNear(to, arc.PointAt(arc.Duration));
    }

    [Test]
    public void Arc_FromRest_LastsTheFreeFallTime()
    {
        const float height = 3f;

        DropArc arc = DropArc.Create(Vector3.up * height, Vector3.zero, Gravity, 0f, 0f);

        Assert.AreEqual(Mathf.Sqrt(2f * height / Gravity), arc.Duration, Tolerance);
        Assert.AreEqual(0f, arc.LaunchSpeed, Tolerance);
        Assert.AreEqual(0f, arc.ApexHeight, Tolerance);
    }

    [Test]
    public void Arc_RisesExactlyTheApexHeightBeforeFalling()
    {
        const float apex = 0.3f;
        Vector3 from = new Vector3(0f, 2f, 0f);

        DropArc arc = DropArc.Create(from, new Vector3(0f, 0f, 1.5f), Gravity, apex, 0f);

        float highest = float.NegativeInfinity;
        for (int i = 0; i <= 200; i++) highest = Mathf.Max(highest, arc.PointAt(arc.Duration * i / 200f).y);

        Assert.AreEqual(from.y + apex, highest, 0.01f);
        Assert.AreEqual(apex, arc.ApexHeight, Tolerance);
    }

    [Test]
    public void Arc_ShorterThanTheMinimum_IsStretchedAndStillLands()
    {
        Vector3 from = new Vector3(0f, 0.2f, 0f);
        Vector3 to = new Vector3(0f, 0f, 1f);

        DropArc arc = DropArc.Create(from, to, Gravity, 0f, 0.5f);

        Assert.AreEqual(0.5f, arc.Duration, Tolerance);
        Assert.Greater(arc.LaunchSpeed, 0f, "a stretched fall has to rise first to last longer");
        AssertNear(to, arc.PointAt(arc.Duration));
    }

    [Test]
    public void Arc_MovesAtConstantSpeedAcrossTheFloor()
    {
        Vector3 from = new Vector3(0f, 4f, 0f);
        Vector3 to = new Vector3(2f, 0f, 1f);

        DropArc arc = DropArc.Create(from, to, Gravity, 0.3f, 0f);
        Vector3 half = arc.PointAt(arc.Duration * 0.5f);

        Assert.AreEqual(1f, half.x, Tolerance);
        Assert.AreEqual(0.5f, half.z, Tolerance);
    }

    [Test]
    public void Arc_ClampsTimeOutsideTheFlight()
    {
        Vector3 from = new Vector3(0f, 3f, 0f);
        Vector3 to = new Vector3(0f, 0f, 1f);

        DropArc arc = DropArc.Create(from, to, Gravity, 0f, 0f);

        AssertNear(from, arc.PointAt(-1f));
        AssertNear(to, arc.PointAt(arc.Duration + 1f));
    }

    [Test]
    public void Kind_IsDecidedByHeightAlone()
    {
        Assert.AreEqual(EDropKind.Hop, DropPath.KindFor(2.49f, 2.5f));
        Assert.AreEqual(EDropKind.Hang, DropPath.KindFor(2.5f, 2.5f));
        Assert.AreEqual(EDropKind.Hang, DropPath.KindFor(4.8f, 2.5f));
    }

    [Test]
    public void Hop_IsOneArcFromWhereItStands()
    {
        Vector3 start = new Vector3(0f, 2f, 0f);
        Vector3 end = new Vector3(0f, 0f, 1.6f);

        DropPath path = DropPath.Create(start, start + Vector3.forward * 0.4f, end, Vector3.forward, Tuning);

        Assert.AreEqual(EDropKind.Hop, path.Kind);
        AssertNear(start, path.Arc.From);
        AssertNear(end, path.Arc.To);
        Assert.AreEqual(Tuning.HopApexHeight, path.Arc.ApexHeight, Tolerance);
    }

    [Test]
    public void Hang_CrouchesAtTheEdgeThenHangsOutsideItBeforeFalling()
    {
        Vector3 start = new Vector3(0f, 4f, 0f);
        Vector3 edge = new Vector3(0f, 3.7f, 0.5f);   // Found on the mesh at another height: ignored.
        Vector3 end = new Vector3(0f, 0f, 1.8f);

        DropPath path = DropPath.Create(start, edge, end, Vector3.forward, Tuning);

        Assert.AreEqual(EDropKind.Hang, path.Kind);
        AssertNear(Vector3.forward, path.Facing);

        // Crouched on the floor it came from, its back one body radius short of the edge.
        AssertNear(new Vector3(0f, 4f, 0.5f - Tuning.BodyRadius), path.EdgeStand);

        // Hanging just outside the edge, the hang depth below it.
        AssertNear(new Vector3(0f, 4f - Tuning.HangDepth, 0.5f + Tuning.BodyRadius), path.HangPoint);

        // And the fall starts there, from rest.
        AssertNear(path.HangPoint, path.Arc.From);
        AssertNear(end, path.Arc.To);
        Assert.AreEqual(0f, path.Arc.ApexHeight, Tolerance);
    }

    [Test]
    public void Hang_DeeperThanTheDrop_StillFallsAtTheEnd()
    {
        Vector3 start = new Vector3(0f, 2.6f, 0f);
        Vector3 end = new Vector3(0f, 0f, 1.5f);

        // A hang depth typed longer than the drop itself: hanging would put the feet on the floor.
        DropTuning deep = new DropTuning(2.5f, 0.3f, Gravity, 0.35f, 3f, 0.3f);
        DropPath path = DropPath.Create(start, new Vector3(0f, 2.6f, 0.4f), end, Vector3.forward, deep);

        Assert.AreEqual(EDropKind.Hang, path.Kind);
        Assert.AreEqual(DropPath.MinFallAfterHang, path.HangPoint.y - end.y, Tolerance);
    }

    [Test]
    public void LandingStraightBelow_FacesTheFallbackDirection()
    {
        Vector3 start = new Vector3(0f, 3f, 0f);
        Vector3 end = new Vector3(0f, 0f, 0f);

        DropPath path = DropPath.Create(start, start, end, Vector3.left, Tuning);

        AssertNear(Vector3.left, path.Facing);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.x, actual.x, Tolerance, $"x of {actual}");
        Assert.AreEqual(expected.y, actual.y, Tolerance, $"y of {actual}");
        Assert.AreEqual(expected.z, actual.z, Tolerance, $"z of {actual}");
    }
}
