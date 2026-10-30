/// <summary>
/// A yes-or-no answer that only changes its mind once the new answer has held for a while.
///
/// WHY IT EXISTS (plan §19.4, T1). "The route to the belief does not get there" is read by the two
/// rungs that keep the Nemesis chasing, and it comes off a path query: a player crossing the edge of
/// the NavMesh, a belief that snaps to the far side of a railing for one query, the cache being
/// dropped on a state entry. One bad answer took the chase away with the player in plain view and
/// the next good one gave it back, which read in the traces as Chasing for exactly the dwell window,
/// over and over. A verdict that has to hold before it counts ends that trade in both directions:
/// a chase is not dropped for one bad query, and a search of somewhere it cannot reach is not turned
/// into a chase by one good one.
///
/// THE FIRST ANSWER IS TAKEN AS IT COMES, and so is the first one after nobody asked for a while.
/// The delay is for an answer that CHANGES while it is being watched; a Nemesis that has just laid
/// eyes on a player standing on a ledge it cannot reach must not chase for the length of the settle
/// time before working that out.
///
/// PURE: a clock and this moment's answer in, the settled one out, in WIRED.Nemesis.Logic so
/// EditMode tests can reach it. A plain class because it has to remember what it last settled on.
/// NemesisPathOracle owns the one instance.
/// </summary>
public sealed class SettledVerdict
{
    /// <summary>The answer as it stands. False until the first <see cref="Step"/>.</summary>
    public bool Value { get; private set; }

    private bool hasValue;
    private bool disagreeing;
    private float disagreeingSince;
    private float lastAsked;

    /// <summary>Forgets everything: the next answer is taken as it comes.</summary>
    public void Reset()
    {
        Value = false;
        hasValue = false;
        disagreeing = false;
    }

    /// <summary>
    /// One reading.
    /// </summary>
    /// <param name="now">The clock, in seconds. Only differences matter.</param>
    /// <param name="raw">This moment's answer.</param>
    /// <param name="settleTime">Seconds a different answer has to hold, without a break, before it
    /// replaces the settled one. 0 or less: the raw answer, always.</param>
    /// <param name="staleAfter">A gap in the asking longer than this means what was settled is no
    /// longer known: the next answer is taken as it comes.</param>
    /// <returns>The settled answer.</returns>
    public bool Step(float now, bool raw, float settleTime, float staleAfter)
    {
        bool stale = !hasValue || now - lastAsked > staleAfter;
        lastAsked = now;

        if (stale || settleTime <= 0f)
        {
            Value = raw;
            hasValue = true;
            disagreeing = false;
            return Value;
        }

        if (raw == Value)
        {
            // Back in agreement: a later disagreement starts its count from nothing.
            disagreeing = false;
            return Value;
        }

        if (!disagreeing)
        {
            disagreeing = true;
            disagreeingSince = now;
        }

        if (now - disagreeingSince >= settleTime)
        {
            Value = raw;
            disagreeing = false;
        }

        return Value;
    }
}
