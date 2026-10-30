using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Where the Nemesis's eyes point during a chase (04/10): on the player it sees, and, once it has
/// lost them, the way it saw them go — swinging further into the corner the closer it gets, so it
/// arrives already looking down the corridor they left by.
///
/// The corner used throughout: the player ran north up a corridor and turned WEST at z = 10, where
/// the Nemesis lost them. The Nemesis follows from the south with its eye two metres up.
/// </summary>
public class ChaseGazeTests
{
    private const float LookAhead = 6f;

    /// <summary>Tolerance on an angle. Loose on purpose: Vector3.Angle goes through an arc cosine,
    /// which near 0 degrees turns one unit of float rounding into a few hundredths of a degree.
    /// </summary>
    private const float Degrees = 0.1f;

    private static readonly Vector3 LostAt = new Vector3(0f, 0f, 10f);
    private static readonly Vector3 RunningWest = new Vector3(-4.5f, 0f, 0f);
    private static readonly Vector3 RunningNorth = new Vector3(0f, 0f, 4.5f);

    private static Vector3 EyeAt(float z) => new Vector3(0f, 2f, z);

    private static float DegreesOffNorth(Vector3 eye, Vector3 velocity)
    {
        Assert.IsTrue(ChaseGaze.TryGetAim(eye, LostAt, velocity, LookAhead, out Vector3 aim));
        return Vector3.Angle(Vector3.forward, aim);
    }

    // ── Looking where they went ──────────────────────────────────────────────

    [Test]
    public void StraightCorridor_LooksStraightOn()
    {
        Assert.AreEqual(0f, DegreesOffNorth(EyeAt(0f), RunningNorth), Degrees);
    }

    [Test]
    public void Corner_TheGazeSwingsFurtherIn_TheCloserItGets()
    {
        float far = DegreesOffNorth(EyeAt(0f), RunningWest);
        float near = DegreesOffNorth(EyeAt(7f), RunningWest);
        float almost = DegreesOffNorth(EyeAt(9f), RunningWest);

        // Ten metres out the point is 6 m to the side of a 10 m run: about 31 degrees, well inside
        // what the cone already covers. A metre out it is all but sideways.
        Assert.AreEqual(Mathf.Atan2(6f, 10f) * Mathf.Rad2Deg, far, Degrees);
        Assert.Greater(near, far);
        Assert.Greater(almost, near);
        Assert.Greater(almost, 80f);
    }

    [Test]
    public void Corner_AtTheLostSpot_LooksDownTheWayTheyLeft()
    {
        Assert.IsTrue(ChaseGaze.TryGetAim(EyeAt(10f), LostAt, RunningWest, LookAhead, out Vector3 aim));

        Assert.AreEqual(0f, Vector3.Angle(Vector3.left, aim), Degrees);
    }

    [Test]
    public void Corner_TurnsToTheSideTheyWent()
    {
        Assert.IsTrue(ChaseGaze.TryGetAim(EyeAt(5f), LostAt, RunningWest, LookAhead, out Vector3 west));
        Assert.IsTrue(ChaseGaze.TryGetAim(EyeAt(5f), LostAt, -RunningWest, LookAhead, out Vector3 east));

        Assert.Less(west.x, 0f);
        Assert.Greater(east.x, 0f);
    }

    [Test]
    public void Aim_IsFlatAndNormalised()
    {
        Assert.IsTrue(ChaseGaze.TryGetAim(EyeAt(3f), LostAt + Vector3.up * 1.5f, RunningWest, LookAhead,
                                          out Vector3 aim));

        Assert.AreEqual(0f, aim.y, 1e-5f, "it looks left and right along the floor, like the scan");
        Assert.AreEqual(1f, aim.magnitude, 1e-4f);
    }

    [Test]
    public void AimPoint_IsTheLookAheadPastTheLostSpot_AlongTheHeading()
    {
        Vector3 point = ChaseGaze.AimPoint(LostAt, RunningWest, LookAhead);

        Assert.AreEqual(-6f, point.x, 1e-4f);
        Assert.AreEqual(10f, point.z, 1e-4f);
    }

    // ── Nowhere in particular to look ────────────────────────────────────────

    [Test]
    public void NoObservedHeading_NoAim()
    {
        Assert.IsFalse(ChaseGaze.TryGetAim(EyeAt(0f), LostAt, Vector3.zero, LookAhead, out _));
    }

    [Test]
    public void TooSlowToBeAHeading_NoAim()
    {
        // The jitter of one sighting against the next, or a player seen standing.
        Vector3 drifting = new Vector3(0.3f, 0f, 0f);

        Assert.IsFalse(ChaseGaze.TryGetAim(EyeAt(0f), LostAt, drifting, LookAhead, out _));
    }

    [Test]
    public void FallingIsNotAHeading()
    {
        Assert.IsFalse(ChaseGaze.TryGetAim(EyeAt(0f), LostAt, Vector3.down * 6f, LookAhead, out _));
    }

    [Test]
    public void LookAheadZero_IsOff()
    {
        Assert.IsFalse(ChaseGaze.TryGetAim(EyeAt(0f), LostAt, RunningWest, 0f, out _));
    }

    // ── Eyes on the player ───────────────────────────────────────────────────

    [Test]
    public void DirectionTo_IsFlat()
    {
        Assert.IsTrue(ChaseGaze.TryGetDirectionTo(EyeAt(0f), new Vector3(3f, 0f, 3f), out Vector3 direction));

        Assert.AreEqual(0f, direction.y, 1e-5f);
        Assert.AreEqual(45f, Vector3.Angle(Vector3.forward, direction), Degrees);
    }

    [Test]
    public void DirectionTo_StraightBelow_IsNoDirection()
    {
        Assert.IsFalse(ChaseGaze.TryGetDirectionTo(EyeAt(4f), new Vector3(0f, 0f, 4f), out _));
    }

    // ── Turning ──────────────────────────────────────────────────────────────

    [Test]
    public void Turn_GoesNoFurtherThanTheStep()
    {
        Vector3 turned = ChaseGaze.Turn(Vector3.forward, Vector3.right, 30f);

        Assert.AreEqual(30f, Vector3.Angle(Vector3.forward, turned), Degrees);
        Assert.AreEqual(60f, Vector3.Angle(Vector3.right, turned), Degrees);
    }

    [Test]
    public void Turn_StopsAtTheTarget()
    {
        Vector3 turned = ChaseGaze.Turn(Vector3.forward, Vector3.right, 200f);

        Assert.AreEqual(0f, Vector3.Angle(Vector3.right, turned), Degrees);
    }

    [Test]
    public void Turn_TakesTheShortWayRound()
    {
        Vector3 turned = ChaseGaze.Turn(Vector3.forward, Vector3.left, 45f);

        Assert.Less(turned.x, 0f);
        Assert.AreEqual(45f, Vector3.Angle(Vector3.forward, turned), Degrees);
    }

    [Test]
    public void Turn_StraightBehind_StaysOnTheFloor()
    {
        // The case Vector3.RotateTowards may take over the top.
        Vector3 gaze = Vector3.forward;
        for (int i = 0; i < 4; i++)
        {
            gaze = ChaseGaze.Turn(gaze, Vector3.back, 45f);
            Assert.AreEqual(0f, gaze.y, 1e-5f);
            Assert.AreEqual(1f, gaze.magnitude, 1e-4f);
        }

        Assert.AreEqual(0f, Vector3.Angle(Vector3.back, gaze), Degrees);
    }

    [Test]
    public void Turn_IgnoresAnyTilt()
    {
        Vector3 tilted = new Vector3(0f, 0.6f, 1f);
        Vector3 turned = ChaseGaze.Turn(tilted, Vector3.right + Vector3.up, 10f);

        Assert.AreEqual(0f, turned.y, 1e-5f);
        Assert.AreEqual(10f, Vector3.Angle(Vector3.forward, turned), Degrees);
    }

    [Test]
    public void Turn_WithNoTarget_KeepsLookingWhereItWas()
    {
        Vector3 turned = ChaseGaze.Turn(Vector3.forward, Vector3.zero, 90f);

        Assert.AreEqual(0f, Vector3.Angle(Vector3.forward, turned), Degrees);
    }

    [Test]
    public void Angle_IsMeasuredAlongTheFloor()
    {
        Assert.AreEqual(90f, ChaseGaze.Angle(new Vector3(0f, 5f, 1f), new Vector3(1f, -3f, 0f)), Degrees);
        Assert.AreEqual(0f, ChaseGaze.Angle(Vector3.up, Vector3.forward), Degrees);
    }
}
