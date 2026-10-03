using UnityEngine;

/// <summary>
/// How far apart the module countdown's beeps are, as a function of the time the module has left.
/// Slow at the start and quicker at every stage, so the beep itself tells how close the end is:
///
///   start → amber   the interval shrinks in a straight line from <c>startInterval</c> to
///                   <c>amberInterval</c> (30 s, 20 s... 10 s)
///   amber → red     in a straight line from <c>amberInterval</c> to <c>redInterval</c> (10 s → 5 s)
///   red → urgent    exponentially from <c>redInterval</c> to <c>urgentInterval</c>: it falls fast
///                   at first and settles into the urgent rate (5 s → 0.5 s)
///   urgent          <c>urgentInterval</c> flat, until the module ends
///
/// Amber and red are where the module's readout turns amber and red (see
/// <see cref="SO_PlayerCameraFeedConfig.ModuleTimerStage"/>), so the colour on screen and the pace
/// of the beep change together. The interval is continuous across every boundary.
///
/// PURE: plain numbers in, a number out, in WIRED.Utils so EditMode tests can reach it. All the
/// times are SECONDS LEFT, so they count down: a bigger value is earlier in the module.
/// </summary>
public readonly struct BeepCadence
{
    public readonly float Duration;
    public readonly float AmberAt;
    public readonly float RedAt;
    public readonly float UrgentAt;

    public readonly float StartInterval;
    public readonly float AmberInterval;
    public readonly float RedInterval;
    public readonly float UrgentInterval;

    /// <summary>
    /// Out-of-order stages are tolerated, not trusted: red is held inside the module, amber between
    /// red and the start, so a module too short for its stages still gets a sane curve.
    /// </summary>
    public BeepCadence(float duration, float amberAt, float redAt, float urgentAt,
                       float startInterval, float amberInterval, float redInterval, float urgentInterval)
    {
        Duration = Mathf.Max(0f, duration);
        RedAt = Mathf.Clamp(redAt, 0f, Duration);
        AmberAt = Mathf.Clamp(amberAt, RedAt, Duration);
        UrgentAt = Mathf.Max(0f, urgentAt);

        // The exponential needs a positive rate on both ends.
        StartInterval = Mathf.Max(0.05f, startInterval);
        AmberInterval = Mathf.Max(0.05f, amberInterval);
        RedInterval = Mathf.Max(0.05f, redInterval);
        UrgentInterval = Mathf.Max(0.05f, urgentInterval);
    }

    /// <summary>Seconds between one beep and the next when the module has <paramref name="left"/> left.</summary>
    public float IntervalAt(float left)
    {
        if (left <= UrgentAt) return UrgentInterval;

        if (left <= RedAt)
        {
            float t = Progress(left, RedAt, UrgentAt);
            return RedInterval * Mathf.Pow(UrgentInterval / RedInterval, t);
        }

        if (left <= AmberAt)
            return Mathf.Lerp(AmberInterval, RedInterval, Progress(left, AmberAt, RedAt));

        return Mathf.Lerp(StartInterval, AmberInterval, Progress(left, Duration, AmberAt));
    }

    /// <summary>0 at <paramref name="from"/>, 1 at <paramref name="to"/> (both seconds left, from the
    /// bigger to the smaller). A stage with no length is already over.</summary>
    private static float Progress(float left, float from, float to) =>
        from <= to ? 1f : Mathf.Clamp01((from - left) / (from - to));
}
