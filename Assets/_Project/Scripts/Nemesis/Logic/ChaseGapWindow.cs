using UnityEngine;

/// <summary>
/// The measurement window behind NemesisChaseProgress: "in this many seconds, did the chase get
/// this much closer?" (plan C4, the loop round a table). Pure arithmetic, so the rules that decide
/// what counts as progress are tested in EditMode instead of being read off an F9 log; the
/// component keeps what needs a scene (the NavMesh query, the sight gate, the state it is in).
///
/// JUDGED AGAINST THE START, NOT THE BEST READING. A running minimum ratchets: a player looping a
/// column gets a metre closer on the near side of every lap and a metre further on the far side, and
/// against a ratchet those near sides add up to progress that was never made. Against the
/// distance the window OPENED at, the loop reads as what it is: the number keeps coming back to
/// where it started.
///
/// A PATH JUMP IS NOT PROGRESS (bug report 30/09). The NavMesh distance can change by tens of
/// metres between two consecutive samples without anybody moving: the player stepped onto a
/// walkway whose way up is the far end of the floor, a door sealed, a link switched on. One sample
/// had the player 2.9 m away in a straight line and 23.2 m away over the NavMesh, and the window
/// read "shortened by -20 m", a stall nobody caused. Two bodies cannot cover that in the 0.4 s
/// between samples, so a step bigger than the threshold is the PATH changing: the sample is not
/// judged, it becomes the new baseline.
///
/// A WINDOW THAT HAS NOT BEEN FED FOR A WHOLE WINDOW IS STALE. While the Nemesis does not see the
/// player (or its body is busy with a door or the lift) the window is paused, not cleared, so a chase
/// that dips out of sight for a moment picks up where it was. But a pause longer than the window
/// itself leaves a baseline from another place and another time: resuming against it would judge
/// the new position against a distance measured before the player had even moved, and call it a
/// stall (or a rescue) on the first sample. <see cref="DiscardIfStale"/> drops it instead.
/// </summary>
public class ChaseGapWindow
{
    /// <summary>What <see cref="Sample"/> made of a measurement.</summary>
    public enum ESample
    {
        /// <summary>There was no window: this reading opened one. Nothing was judged.</summary>
        Opened,

        /// <summary>The window is open and the distance has not come down far enough yet.</summary>
        Waiting,

        /// <summary>The chase closed the distance (or is within reach): a new window opened from
        /// here. The stall, if there was one, is over.</summary>
        Closed,

        /// <summary>The distance jumped further than a body can move between two samples: the path
        /// changed. Nothing was judged; a new window opened from here.</summary>
        PathJump,
    }

    /// <summary>Seconds the window has gone without being fed or aged. See
    /// <see cref="DiscardIfStale"/>.</summary>
    private float idle;

    /// <summary>Whether a window is open. False until the first measurement of a chase lands.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>Path distance when the window opened: the reference it is judged against.</summary>
    public float StartDistance { get; private set; }

    /// <summary>The most recent measurement, or infinity before there is one. A path jump
    /// updates it too: it is what the Nemesis has to run NOW, whether or not it was judged.</summary>
    public float LastDistance { get; private set; } = float.PositiveInfinity;

    /// <summary>Seconds the window has been aged since it opened.</summary>
    public float Elapsed { get; private set; }

    /// <summary>Metres this window has closed; negative when the Nemesis has lost ground. 0 with
    /// no window.</summary>
    public float Progress =>
        IsOpen && !float.IsPositiveInfinity(LastDistance) ? StartDistance - LastDistance : 0f;

    /// <summary>Seconds left before the window is judged against <paramref name="length"/>.
    /// Infinity when none is open, so "not measuring" cannot be mistaken for "about to fire".
    /// </summary>
    public float Remaining(float length) => IsOpen ? Mathf.Max(0f, length - Elapsed) : float.PositiveInfinity;

    /// <summary>
    /// Whether <paramref name="current"/> counts as having closed the distance from
    /// <paramref name="start"/>: down by <paramref name="minProgress"/> metres, or within
    /// <paramref name="catchReach"/> of the player.
    ///
    /// ARM'S LENGTH COUNTS AS PROGRESS. From a start already inside a metre and a half the
    /// threshold cannot be met at all, and a Nemesis that has reached the player has not stalled:
    /// whatever is keeping the grab from firing (the post-capture cooldown, a height difference) is
    /// not a lap round a table, and flanking would only walk it away from someone it is standing next
    /// to.
    /// </summary>
    public static bool HasClosed(float start, float current, float minProgress, float catchReach) =>
        start - current >= minProgress || current <= catchReach;

    /// <summary>
    /// Whether the distance moved by more than <paramref name="maxStep"/> between two consecutive
    /// measurements, in either direction. A <paramref name="maxStep"/> at or below zero turns the
    /// rule off; an unknown <paramref name="previous"/> (none yet) is never a jump.
    ///
    /// BOTH DIRECTIONS. A distance that suddenly gets 20 m SHORTER is as much a path change as one
    /// that gets 20 m longer, and counting it as "closed" would clear a real stall because a link
    /// opened somewhere.
    /// </summary>
    public static bool IsPathJump(float previous, float current, float maxStep)
    {
        if (maxStep <= 0f) return false;
        if (float.IsInfinity(previous) || float.IsNaN(previous)) return false;

        return Mathf.Abs(current - previous) > maxStep;
    }

    /// <summary>
    /// Folds one measurement in, opening a window when there is none. The order of the rules
    /// matters: a jump is checked BEFORE closure, so a path that suddenly gets 20 m shorter is
    /// re-baselined and not read as a rescue.
    /// </summary>
    public ESample Sample(float distance, float minProgress, float catchReach, float maxStep)
    {
        float previous = LastDistance;
        LastDistance = distance;

        if (!IsOpen)
        {
            Open(distance);
            return ESample.Opened;
        }

        if (IsPathJump(previous, distance, maxStep))
        {
            Open(distance);
            return ESample.PathJump;
        }

        if (!HasClosed(StartDistance, distance, minProgress, catchReach)) return ESample.Waiting;

        // Genuinely closing. The next window starts here, from the shorter distance, so a chase
        // that keeps gaining ground keeps resetting its own clock.
        Open(distance);
        return ESample.Closed;
    }

    /// <summary>
    /// Ages the window by <paramref name="deltaTime"/>; true once it has lasted
    /// <paramref name="length"/> seconds and is due to be judged. False with no window.
    /// </summary>
    public bool Age(float deltaTime, float length)
    {
        if (!IsOpen) return false;

        idle = 0f;
        Elapsed += deltaTime;
        return Elapsed >= length;
    }

    /// <summary>The window was judged and failed: a new one opens from the latest reading, so a
    /// player who keeps looping keeps being judged, one window at a time.</summary>
    public void Restart()
    {
        if (!IsOpen || float.IsPositiveInfinity(LastDistance)) return;

        Open(LastDistance);
    }

    /// <summary>A tick in which the window could not be fed or aged (sight lost, no path, body held
    /// elsewhere). Only counts toward staleness.</summary>
    public void NoteIdle(float deltaTime)
    {
        if (IsOpen) idle += deltaTime;
    }

    /// <summary>
    /// Drops the window if it has gone unfed for more than <paramref name="length"/> seconds (the
    /// window's own length: a baseline older than a whole window is from another time). True when it
    /// did. A short pause (a door being opened, a corner) keeps it.
    /// </summary>
    public bool DiscardIfStale(float length)
    {
        if (!IsOpen || idle <= length) return false;

        Clear();
        return true;
    }

    /// <summary>Ends the episode: no window, no remembered distance.</summary>
    public void Clear()
    {
        IsOpen = false;
        StartDistance = 0f;
        LastDistance = float.PositiveInfinity;
        Elapsed = 0f;
        idle = 0f;
    }

    private void Open(float distance)
    {
        IsOpen = true;
        StartDistance = distance;
        Elapsed = 0f;
        idle = 0f;
    }
}
