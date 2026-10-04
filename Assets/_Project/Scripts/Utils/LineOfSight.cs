using UnityEngine;

/// <summary>
/// The three questions a vision test is made of - is it close enough, is it inside the cone, is
/// there something in the way - in one place.
///
/// WHY THIS EXISTS
///
/// The trio was written out by hand in four separate files: <see cref="FieldOfView"/> twice (the
/// proximity check and the cone sweep), <see cref="FieldOfListening"/> three times (sound blockers,
/// floors, and the occlusion test four other systems borrow), plus the capture's reach test on
/// NemesisStateManager and the spawn-point visibility test on NemesisController. Six copies of
/// "normalise, measure, raycast" is six chances to get the distance argument subtly wrong in one
/// of them, and an occlusion raycast that is wrong does not throw - it just quietly sees through
/// a wall, or stops seeing through a doorway.
///
/// WHAT IS DELIBERATELY NOT HERE
///
/// <b>Nothing flattens Y.</b> The angle test measures in full 3D, which is what
/// <see cref="FieldOfView"/> has always done: a player on a catwalk directly overhead is outside
/// a 90 degree cone and has to stay outside it. NemesisGizmos flattens when it DRAWS the cone,
/// because a cone drawn tilted on a ramp is unreadable, but that is a drawing concern and it does
/// not belong in the test.
///
/// <b>No gizmo drawing and no runtime cone mesh.</b> <see cref="NemesisGizmos"/> already draws
/// every range to scale with its metre value, and a cone visible to the player in-game is a design
/// decision this project has not taken.
/// </summary>
public static class LineOfSight
{
    // -- Range ---------------------------------------------------------------

    public static bool CheckRange(Vector3 origin, Vector3 point, float range)
    {
        return (point - origin).sqrMagnitude <= range * range;
    }

    public static bool CheckRange(Transform self, Transform target, float range)
    {
        if (self == null || target == null) return false;

        return CheckRange(self.position, target.position, range);
    }

    // -- Cone ----------------------------------------------------------------

    /// <summary>
    /// Whether the point falls inside a cone of <paramref name="angle"/> degrees TOTAL width
    /// centred on <paramref name="front"/>.
    ///
    /// Halved against the front vector, matching how every caller in the project reads its own
    /// angle: a "90 degree cone" is 45 to each side, not 90 to each side. Getting that wrong
    /// doubles the sensor and is invisible in the inspector.
    /// </summary>
    public static bool CheckAngle(Vector3 origin, Vector3 point, Vector3 front, float angle)
    {
        Vector3 toPoint = point - origin;
        if (toPoint.sqrMagnitude < 0.000001f) return true;   // Standing on it: no direction to test.

        return Vector3.Angle(front, toPoint) <= angle * 0.5f;
    }

    public static bool CheckAngle(Transform self, Transform target, float angle)
    {
        if (self == null || target == null) return false;

        return CheckAngle(self.position, target.position, self.forward, angle);
    }

    /// <summary>
    /// Cone test around an ARBITRARY front vector rather than the transform's own forward.
    ///
    /// This overload is what lets the Nemesis look somewhere other than where its body is pointing.
    /// The NavMeshAgent owns the body's rotation - it turns towards wherever it is walking - so
    /// without a separate look direction the monster physically cannot glance down a corridor it
    /// is not currently walking into. See <see cref="NemesisLookAround"/>.
    /// </summary>
    public static bool CheckAngle(Transform self, Transform target, Vector3 front, float angle)
    {
        if (self == null || target == null) return false;

        return CheckAngle(self.position, target.position, front, angle);
    }

    // -- Occlusion -----------------------------------------------------------

    /// <summary>
    /// Whether the way from <paramref name="origin"/> to <paramref name="point"/> is CLEAR.
    ///
    /// Note the polarity: true means "can see", not "is blocked". Both readings exist in this
    /// project - FieldOfListening.IsOccludedByWall answers the opposite question - and mixing them
    /// up inverts a sensor silently, so the name says which one this is.
    ///
    /// TRIGGERS NEVER BLOCK THE VIEW. The project has Queries Hit Triggers ON, and the level puts
    /// trigger volumes on Default — ambience zones, light zones, tutorial hints, checkpoints — so a
    /// raycast that does not say otherwise stops at the invisible edge of every one of them. Looking
    /// from one ambience zone into the next, through an open doorway, read as a wall (WIR-020).
    /// </summary>
    public static bool CheckView(Vector3 origin, Vector3 point, LayerMask obstacleMask)
    {
        Vector3 toPoint = point - origin;
        float distance = toPoint.magnitude;
        if (distance <= 0.0001f) return true;

        return !Physics.Raycast(origin, toPoint / distance, distance, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    public static bool CheckView(Transform self, Transform target, LayerMask obstacleMask)
    {
        if (self == null || target == null) return false;

        return CheckView(self.position, target.position, obstacleMask);
    }

    // -- Combined ------------------------------------------------------------

    public static bool LOS(Transform self, Transform target, float range, float angle,
                           LayerMask obstacleMask)
    {
        return CheckRange(self, target, range) &&
               CheckAngle(self, target, angle) &&
               CheckView(self, target, obstacleMask);
    }

    /// <summary>
    /// The real test, against a collider rather than a point: three samples up the target's
    /// bounds - feet, centre and head - and it counts as seen if ANY of them is both inside the
    /// cone and unoccluded.
    ///
    /// THE THREE SAMPLES ARE NOT AN OPTIMISATION TO SKIP. A single ray at the centre of mass is
    /// the difference between a player crouched behind a crate with their head showing being seen
    /// and being invisible, and between a player whose feet show under a shelf being seen and
    /// being invisible. Collapsing this to one ray narrows detection everywhere at once and
    /// nothing errors - the monster just gets quietly worse at its job. This method exists so the
    /// consolidation onto shared helpers could not do that by accident.
    ///
    /// The angle and the occlusion are tested TOGETHER, per sample, and that is also load-bearing:
    /// testing them separately would let a head that is inside the cone pass the angle while a
    /// foot that is outside it passes the raycast, and report a target nothing actually saw.
    ///
    /// TWO NESTED CONES, AND THE INNER ONE IS ASKED OF EVERY SAMPLE (04/10). The sensor has a focus
    /// cone inside its vision cone, and which of the two the target is in used to be decided
    /// afterwards, off whichever sample got through FIRST — the feet. The angle is measured in 3D
    /// from an eye two metres up, so the feet of someone standing two metres dead ahead are 44
    /// degrees below the gaze: outside an 80 degree focus cone, with their head squarely in the
    /// middle of it. They read as "something in the corner of its eye" and the focus cone, drawn as
    /// a flat wedge by every gizmo, was in practice a wedge that got narrower the closer you stood
    /// and vanished under 2.3 m. The rule for the inner cone is now the rule for the outer one: any
    /// sample that is inside it and unoccluded counts.
    /// </summary>
    /// <param name="front">Where the eye is looking. Not necessarily the body's forward.</param>
    /// <param name="angle">Total width of the outer cone, in degrees: what the target has to be
    /// inside to be seen at all.</param>
    /// <param name="innerAngle">Total width of the inner cone, in degrees. At or above
    /// <paramref name="angle"/> there is no band between the two and every sample that is seen is
    /// inside it.</param>
    /// <param name="minDistance">Inside this distance both cones are ignored - "it is right next to
    /// me" - but the occlusion raycast still applies.</param>
    /// <param name="seenPoint">The sample that got through, for a caller that needs to know WHERE
    /// on the target it saw: the one inside the inner cone when there is one, otherwise the first
    /// seen through the outer one. Only meaningful when this returns true.</param>
    /// <param name="insideInner">Whether any sample was seen through the inner cone (or inside
    /// <paramref name="minDistance"/>).</param>
    public static bool CheckConeSampled(Vector3 origin, Vector3 front, Collider target, float angle,
                                        float innerAngle, float minDistance, LayerMask obstacleMask,
                                        out Vector3 seenPoint, out bool insideInner)
    {
        seenPoint = Vector3.zero;
        insideInner = false;
        if (target == null) return false;

        Bounds bounds = target.bounds;
        bool seen = false;

        // -1, 0, +1 times 90% of the half-height: feet, centre, head, pulled slightly inside the
        // bounds so the outer two do not graze the surface they sit on.
        for (int j = -1; j < 2; j++)
        {
            Vector3 point = bounds.center + new Vector3(0f, j * bounds.extents.y * 0.9f, 0f);

            Vector3 toPoint = point - origin;
            float distance = toPoint.magnitude;
            if (distance <= 0.0001f)
            {
                seenPoint = point;
                insideInner = true;
                return true;
            }

            float offAxis = Vector3.Angle(front, toPoint);
            bool rightNextToIt = distance <= minDistance;
            if (!rightNextToIt && offAxis > angle * 0.5f) continue;

            // Triggers ignored, same as CheckView and for the same reason.
            if (Physics.Raycast(origin, toPoint / distance, distance, obstacleMask, QueryTriggerInteraction.Ignore)) continue;

            // Seen through the inner cone: nothing a later sample could add.
            if (rightNextToIt || offAxis <= innerAngle * 0.5f)
            {
                seenPoint = point;
                insideInner = true;
                return true;
            }

            // Seen through the outer band only. The first such sample stands for the target (as it
            // always did), and the rest still get their chance at the inner cone.
            if (seen) continue;

            seen = true;
            seenPoint = point;
        }

        return seen;
    }

    /// <summary>
    /// <see cref="CheckConeSampled"/> for a sensor with no vertical limit: a vertical WEDGE instead
    /// of a cone. Only the horizontal angle off <paramref name="front"/> is tested, measured in the
    /// plane perpendicular to <paramref name="up"/>; range and occlusion stay full 3D, per sample.
    ///
    /// This is the one place in the class that flattens, and it does it on purpose: it exists for
    /// the security cameras, whose design is that height never hides the player - crouched,
    /// on a catwalk or straight underneath, they are caught the moment an edge of the sweep
    /// crosses them. It does not loosen the Nemesis: FieldOfView keeps the cone, and "nothing
    /// flattens Y" above still holds for it.
    ///
    /// Same three samples up the bounds as the cone, for the same reason, and the wedge, range
    /// and occlusion are tested together per sample, for the same reason too.
    /// </summary>
    /// <param name="up">The axis the wedge stands along - the mount's up, not necessarily world
    /// up.</param>
    /// <param name="angle">Total horizontal width of the wedge, in degrees.</param>
    /// <param name="range">How far the wedge reaches, measured in 3D from the origin.</param>
    public static bool CheckWedgeSampled(Vector3 origin, Vector3 front, Vector3 up, Collider target,
                                         float angle, float range, LayerMask obstacleMask,
                                         out Vector3 seenPoint)
    {
        seenPoint = Vector3.zero;
        if (target == null) return false;

        // Looking straight along the up axis there is no horizontal heading to measure from.
        Vector3 flatFront = Vector3.ProjectOnPlane(front, up);
        if (flatFront.sqrMagnitude < 0.000001f) return false;

        Bounds bounds = target.bounds;
        float rangeSqr = range * range;

        for (int j = -1; j < 2; j++)
        {
            Vector3 point = bounds.center + new Vector3(0f, j * bounds.extents.y * 0.9f, 0f);

            Vector3 toPoint = point - origin;
            float sqrDistance = toPoint.sqrMagnitude;
            if (sqrDistance > rangeSqr) continue;

            if (sqrDistance <= 0.00000001f)
            {
                seenPoint = point;
                return true;
            }

            // A point straight above or below the origin has no horizontal angle: inside the wedge.
            Vector3 flatToPoint = Vector3.ProjectOnPlane(toPoint, up);
            bool withinWedge = flatToPoint.sqrMagnitude < 0.000001f ||
                               Vector3.Angle(flatFront, flatToPoint) <= angle * 0.5f;
            if (!withinWedge) continue;

            // Triggers ignored, same as CheckView and for the same reason.
            float distance = Mathf.Sqrt(sqrDistance);
            if (Physics.Raycast(origin, toPoint / distance, distance, obstacleMask, QueryTriggerInteraction.Ignore)) continue;

            seenPoint = point;
            return true;
        }

        return false;
    }
}
