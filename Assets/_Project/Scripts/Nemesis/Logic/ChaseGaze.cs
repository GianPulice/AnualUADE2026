using UnityEngine;

/// <summary>
/// Where the Nemesis's EYES point during a chase, when that is not simply where its body is walking.
///
/// WHY (playtest 03/10: "en las esquinas me pierde muy fácil"). The vision cone was welded to the
/// body, and the body faces whatever the NavMeshAgent is walking at. A chase that loses sight walks
/// to the point where it last saw the player — so it arrived at the corner the player had turned
/// facing the WALL, with the corridor they went down ninety degrees off: outside the cone, or on its
/// very edge. It then lost them for good while standing at the mouth of the corridor they were in.
///
/// Two rules, and both only ever use what the eyes OBSERVED — the last sighting and the velocity
/// measured between sightings (FieldOfView.LastKnownVelocity) — never the player's transform:
///
///   - WHILE IT SEES THEM, the eyes stay on them (<see cref="TryGetDirectionTo"/>): the body can
///     turn to follow its path round a table without the cone swinging off the player it is looking
///     straight at.
///   - WHEN IT LOSES THEM, the eyes go where they went (<see cref="TryGetAim"/>): aimed at a point a
///     few metres past the lost spot along the heading it last saw. Geometry does the timing — far
///     from the lost spot that point is nearly straight ahead, and the closer it gets the further
///     the gaze swings into the turn, until at the lost spot itself it is looking straight down the
///     way they left. So by the time it arrives it is already looking the right way.
///
/// The gaze is turned at a capped rate (<see cref="Turn"/>) so it never snaps, and about the world's
/// up axis only: like the scan, it looks left and right along the floor.
///
/// It guesses, and that is the point: a player who turns the corner and then doubles back, or stops
/// dead behind it, is not where the eyes went. That is the counterplay, and the possibility map —
/// not this — is what reasons about everywhere else they could be.
///
/// PURE: vectors in, a direction out, in WIRED.Nemesis.Logic so EditMode tests can reach it.
/// NemesisLookAround is the one driver of FieldOfView.LookDirection and the only caller.
/// </summary>
public static class ChaseGaze
{
    /// <summary>Metres per second under which an observed velocity is not a heading: a player seen
    /// standing still, or the jitter of one sighting against the next. With no heading there is
    /// nowhere in particular to look, and the eyes stay with the body.</summary>
    public const float MinHeadingSpeed = 0.5f;

    private const float Tiny = 0.0001f;

    /// <summary>
    /// The flat direction from the eye to the point <paramref name="lookAhead"/> metres past where
    /// it lost the player, along the heading it last observed.
    /// </summary>
    /// <returns>false when there is no heading to follow (never seen moving, or too slowly), when
    /// the look-ahead is switched off (0), or when the eye is standing on that very point.</returns>
    public static bool TryGetAim(Vector3 eye, Vector3 lostAt, Vector3 observedVelocity, float lookAhead,
                                 out Vector3 aim)
    {
        aim = Vector3.zero;
        if (lookAhead <= 0f) return false;

        Vector3 heading = observedVelocity;
        heading.y = 0f;
        if (heading.sqrMagnitude < MinHeadingSpeed * MinHeadingSpeed) return false;

        return TryGetDirectionTo(eye, AimPoint(lostAt, observedVelocity, lookAhead), out aim);
    }

    /// <summary>The point <see cref="TryGetAim"/> looks at: for the gizmo, which draws what the eyes
    /// are aimed at rather than a second guess at it. Only meaningful when TryGetAim says yes.
    /// </summary>
    public static Vector3 AimPoint(Vector3 lostAt, Vector3 observedVelocity, float lookAhead)
    {
        Vector3 heading = observedVelocity;
        heading.y = 0f;
        if (heading.sqrMagnitude < Tiny) return lostAt;

        return lostAt + heading.normalized * Mathf.Max(0f, lookAhead);
    }

    /// <summary>The flat direction from the eye to a point. False when they are on top of each other
    /// (straight above or below counts): there is no left or right to look.</summary>
    public static bool TryGetDirectionTo(Vector3 eye, Vector3 point, out Vector3 direction)
    {
        direction = point - eye;
        direction.y = 0f;

        if (direction.sqrMagnitude < Tiny)
        {
            direction = Vector3.zero;
            return false;
        }

        direction.Normalize();
        return true;
    }

    /// <summary>
    /// Turns a gaze towards a target by at most <paramref name="maxDegrees"/>, about the world's up
    /// axis. Both are flattened first. A signed turn rather than Vector3.RotateTowards: straight
    /// behind, that one may pick any axis to go round — over the top included — and a gaze that
    /// flips through the ceiling is a cone that sees nothing for a frame.
    /// </summary>
    public static Vector3 Turn(Vector3 current, Vector3 target, float maxDegrees)
    {
        current.y = 0f;
        target.y = 0f;

        if (target.sqrMagnitude < Tiny) return current.sqrMagnitude < Tiny ? Vector3.zero : current.normalized;
        if (current.sqrMagnitude < Tiny) return target.normalized;

        float limit = Mathf.Max(0f, maxDegrees);
        float delta = Mathf.Clamp(Vector3.SignedAngle(current, target, Vector3.up), -limit, limit);

        return Quaternion.AngleAxis(delta, Vector3.up) * current.normalized;
    }

    /// <summary>Degrees between two directions along the floor, 0 to 180. 0 when either has no flat
    /// component to measure.</summary>
    public static float Angle(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        if (a.sqrMagnitude < Tiny || b.sqrMagnitude < Tiny) return 0f;

        return Vector3.Angle(a, b);
    }
}
