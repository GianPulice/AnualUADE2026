using UnityEngine;

/// <summary>
/// The two questions the search asks before it moves its sweep, and how big the sweep is (plan §18.5 A,
/// Fase 2B part 2). The first two questions of the arbiter the plan builds up to (§17.4):
///
///   1. Is there anything new? The belief's sequence number moved.
///   4. Is it the same thing it is already after? New evidence inside the disc being swept UPDATES
///      the sweep (the centre follows it, nothing restarts); evidence outside it RE-CENTRES it.
///
/// WHY QUESTION 4 IS NOT OPTIONAL. The belief's sequence goes up with every piece of evidence folded
/// in, which with the player seen or heard is every 0.1 s sweep of the senses. "Re-aim when the
/// sequence changes" on its own is re-aiming ten times a second — the old per-frame re-targeting in a
/// new coat. Question 4 is what turns a stream of footsteps into one moving area instead of a
/// hundred restarts.
///
/// PURE, AND ON PURPOSE. No scene, no state manager, no Assembly-CSharp type: it lives in
/// WIRED.Nemesis.Logic so EditMode tests can reach it (that assembly cannot see Assembly-CSharp).
/// NemesisSearchingState feeds it plain numbers and acts on the answer.
/// </summary>
public static class SearchSweepRules
{
    /// <summary>What to do with the sweep after looking at the belief this frame.</summary>
    public enum EVerdict
    {
        /// <summary>Nothing new (question 1): carry on.</summary>
        Keep,

        /// <summary>New evidence inside the current disc (question 4): move the centre, keep the
        /// radius, what was swept, the destination and any pause.</summary>
        Update,

        /// <summary>New evidence somewhere else, or no sweep yet: centre a new disc on it.</summary>
        Recenter,
    }

    /// <summary>
    /// Questions 1 and 4. <paramref name="consumedSequence"/> is the belief sequence the sweep last
    /// acted on; <paramref name="floorBand"/> the height past which two points are on different
    /// floors (SO_NemesisData.FloorHeightThreshold): the same spot one storey up is somewhere else.
    /// </summary>
    public static EVerdict Judge(int consumedSequence, int sequence, bool hasSweep, Vector3 centre,
                                 float radius, Vector3 evidence, float floorBand)
    {
        if (sequence == consumedSequence) return EVerdict.Keep;
        if (!hasSweep) return EVerdict.Recenter;

        return IsInside(centre, radius, evidence, floorBand) ? EVerdict.Update : EVerdict.Recenter;
    }

    /// <summary>
    /// Whether a point falls inside a sweep disc: flat distance within the radius, and on the same
    /// floor. Flat because the disc is drawn on the floor and a stair or a ramp inside a room is still
    /// the room; the height band is what keeps the storey above out.
    /// </summary>
    public static bool IsInside(Vector3 centre, float radius, Vector3 point, float floorBand)
    {
        if (Mathf.Abs(point.y - centre.y) > floorBand) return false;

        float dx = point.x - centre.x;
        float dz = point.z - centre.z;
        return dx * dx + dz * dz <= radius * radius;
    }

    /// <summary>
    /// How wide to sweep around a piece of evidence: its own precision plus a margin, clamped.
    ///
    /// The EVIDENCE's radius and not the belief's: the belief grows at the player's top speed from the
    /// moment the evidence came in (NemesisBelief.RadiusAt), so by the time the Nemesis has walked to
    /// the spot it is already at the maximum, and every sweep would come out the same size. The
    /// evidence's radius is what says a sighting is a point and a footstep through a wall is a room.
    /// </summary>
    public static float SweepRadius(float evidenceRadius, float margin, float min, float max)
    {
        if (max < min) max = min;
        if (float.IsNaN(evidenceRadius) || float.IsInfinity(evidenceRadius)) return max;

        return Mathf.Clamp(evidenceRadius + margin, min, max);
    }

    /// <summary>
    /// The next size up for a disc it has already covered: one step wider, never past the maximum.
    /// Where the player could have got to keeps growing while nothing new is heard, and the sweep
    /// follows that outwards instead of walking the same few points again.
    /// </summary>
    public static float Widen(float radius, float step, float max)
    {
        return Mathf.Min(max, radius + Mathf.Max(0f, step));
    }
}
