using UnityEngine;

/// <summary>
/// Angles on a clock face — 0 = twelve o'clock, growing clockwise, in degrees — the convention
/// UIRingArc draws in. Pure maths so it can be tested: whether a needle is inside an arc that may
/// run past twelve o'clock, and where an arc may start so it fits a range.
/// </summary>
public static class ClockArc
{
    /// <summary>An angle brought into [0, 360).</summary>
    public static float Normalize(float degrees)
    {
        float a = degrees % 360f;
        return a < 0f ? a + 360f : a;
    }

    /// <summary>
    /// True when <paramref name="angle"/> lies on the arc that starts at <paramref name="start"/>
    /// and covers <paramref name="width"/> degrees clockwise, both edges included. Works for arcs
    /// that cross twelve o'clock and for angles of any number of laps.
    /// </summary>
    public static bool Contains(float angle, float start, float width)
    {
        if (width <= 0f) return false;
        if (width >= 360f) return true;
        float into = Normalize(angle - start);
        return into <= width;
    }
}
