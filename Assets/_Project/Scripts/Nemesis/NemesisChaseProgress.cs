using UnityEngine;

/// <summary>
/// Whether the chase is actually getting anywhere.
///
/// WHAT IT IS FOR
///
/// The player sprints at 4.5 m/s and the Nemesis chases at 3.0, so a loop around a column, a table
/// or a block of shelves is a chase the Nemesis can never win: every lap renews the sighting, so
/// "lo está viendo" holds <see cref="NemesisStateManager.ENemesisState.Chasing"/> forever, and
/// <see cref="NemesisStuckEscape"/> never fires because the body IS moving — briskly, in a circle.
/// Nothing in the system said "I am not closing the distance", so nothing could react to it. This
/// is that sentence, measured.
///
/// WHAT IT DOES NOT DO, AND THAT IS THE POINT
///
/// It measures and it publishes. It does not choose a route (that is still
/// <see cref="NemesisPursuit"/>, which reads <see cref="IsChaseStagnant"/> to penalise the way the
/// player came and widen its detour budget), it does not write NextState (only
/// <see cref="NemesisDecision"/> does, through the <c>IsChaseStagnant</c> predicate), and it moves
/// nothing. The answer to a stall is never "run faster": the speed gap is the design, and a
/// monster that visibly accelerates when you outsmart it reads as the game cheating.
///
/// WHY THE DISTANCE IS OVER THE NAVMESH
///
/// <c>Vector3.Distance</c> to the belief is not a chase's progress in a level with floors: standing
/// on the walkway directly above the player reads as four metres while being half a storey of
/// running away, and it would report a chase closing in whenever the player moved DOWN. The number
/// that means "am I catching up" is how far the Nemesis still has to run, which is
/// <see cref="NemesisNav"/>'s job.
///
/// SETUP: goes on the Nemesis root, next to <see cref="NemesisStateManager"/>, which finds it, adds
/// it when missing and ticks it. Nothing to author — every number it reads lives on
/// <see cref="SO_NemesisData"/>.
/// </summary>
public class NemesisChaseProgress : MonoBehaviour
{
    // Tuning lives on SO_NemesisData, reached through the state manager, exactly as
    // NemesisStuckEscape does it: nothing is serialised here, so this component has no scene
    // wiring anyone could get wrong or forget to copy onto a prefab variant.
    private const float FallbackWindow = 4f;
    private const float FallbackMinProgress = 1.5f;
    private const float FallbackSampleInterval = 0.4f;
    private const float FallbackSightGrace = 2.5f;
    private const float FallbackCatchReach = 1f;

    private NemesisStateManager stateManager;

    /// <summary>Whether a measurement window is open. False while the Nemesis is not chasing, and
    /// until the first measurement of a chase lands.</summary>
    private bool hasWindow;

    /// <summary>Whether the latest path query could be answered. A window only ages while it is
    /// being fed real measurements — see <see cref="Tick"/>.</summary>
    private bool measurable;

    private float windowElapsed;
    private float sampleTimer;

    /// <summary>Path distance to the belief when the current window opened. The reference the
    /// window is judged against — see <see cref="Sample"/> for why it is the window's START and
    /// not the best reading so far.</summary>
    private float windowStartDistance;

    /// <summary>The most recent measurement, or infinity before there is one. For the HUD: the
    /// pair (start, now) is what makes "it is not closing" readable rather than asserted.</summary>
    public float LastDistance { get; private set; } = float.PositiveInfinity;

    /// <summary>
    /// The chase has spent a whole window without closing the distance, and has not closed it
    /// since.
    ///
    /// A LATCH AND NOT A PULSE, which is what lets anything act on it. The counterplay is "come
    /// round the other side for as long as this lasts", so a flag that was true for the single
    /// frame the window expired would be read by the pursuit's throttled replan roughly never.
    /// It clears on real progress, or when the chase ends.
    /// </summary>
    public bool IsChaseStagnant { get; private set; }

    /// <summary>
    /// How many windows have expired without progress this session — one <c>ChaseStalled</c>
    /// each, in the vocabulary of the exploit table.
    ///
    /// Public and counted now, acted on later: the habit tracker is what will turn a count into an
    /// unlocked counterplay, and it does not exist yet. Until it does this is a number to read off
    /// the HUD while playing, which is the whole reason thresholds get calibrated from data
    /// instead of guessed. It survives the end of a chase on purpose — a counter that reset every
    /// time the Nemesis lost you would only ever say 0 or 1.
    /// </summary>
    public int ChaseStalledCount { get; private set; }

    /// <summary>Whether a window is open at all. The HUD needs to tell "measuring and fine" apart
    /// from "not measuring", which the numbers below cannot say on their own.</summary>
    public bool IsMeasuring => hasWindow;

    /// <summary>Metres the current window has managed to close; negative when the Nemesis has
    /// actually lost ground. For the HUD.</summary>
    public float WindowProgress =>
        hasWindow && !float.IsPositiveInfinity(LastDistance)
            ? windowStartDistance - LastDistance
            : 0f;

    /// <summary>Seconds left before the current window is judged. Infinity when none is open, so
    /// a caller cannot mistake "not measuring" for "about to fire".</summary>
    public float WindowRemaining =>
        hasWindow ? Mathf.Max(0f, Window - windowElapsed) : float.PositiveInfinity;

    /// <summary>Called by <see cref="NemesisStateManager"/> during its Awake, so this is wired
    /// before any tick — same contract as the other siblings.</summary>
    public void Initialize(NemesisStateManager manager) => stateManager = manager;

    private SO_NemesisData Data => stateManager != null ? stateManager.NemesisData : null;

    /// <summary>Seconds a chase gets to close <see cref="MinProgress"/>. See
    /// <see cref="SO_NemesisData.ChaseProgressWindow"/>.</summary>
    private float Window
    {
        get
        {
            SO_NemesisData data = Data;
            return data != null ? Mathf.Max(0.5f, data.ChaseProgressWindow) : FallbackWindow;
        }
    }

    /// <summary>Metres of path that count as having closed the distance.</summary>
    private float MinProgress
    {
        get
        {
            SO_NemesisData data = Data;
            return data != null ? Mathf.Max(0.05f, data.ChaseMinProgress) : FallbackMinProgress;
        }
    }

    /// <summary>
    /// How often the path query behind the measurement is allowed to run.
    ///
    /// Deliberately NOT a knob of its own: it is the same "how often do we pay for a CalculatePath"
    /// number the route verdict is throttled by, and a second one would mean two places to tune
    /// one cost. What it measures cannot change meaningfully inside 0.4 s anyway — the whole point
    /// is a trend over seconds.
    /// </summary>
    private float SampleInterval
    {
        get
        {
            SO_NemesisData data = Data;
            return data != null ? Mathf.Max(0.05f, data.RouteVerdictInterval) : FallbackSampleInterval;
        }
    }

    /// <summary>
    /// One frame of the measurement. Called by <see cref="NemesisStateManager"/> BEFORE the
    /// decision layer runs, so the ladder reads this frame's verdict rather than last frame's —
    /// the same ordering rule the sensor flags are sampled under.
    /// </summary>
    public void Tick()
    {
        if (stateManager == null) return;

        // Only a chase can stall. Leaving Chasing ends the episode outright: coming back later is
        // a new chase, and inheriting a stale verdict would have the pursuit flanking a player it
        // has only just caught sight of.
        if (stateManager.CurrentStateKey != NemesisStateManager.ENemesisState.Chasing)
        {
            Clear();
            return;
        }

        // NOT DURING THE ESCAPE, and cleared rather than paused. NemesisEscapePursuit raises the
        // chase floor and then paces the Nemesis against the player's own sprint — easing off
        // when it is close and pressing when they pull away — so the gap holding steady there is
        // the pace WORKING, by design, not a player exploiting a table. Measured, it would read
        // as a stall every window, the pursuit would start detouring through patrol waypoints in
        // the middle of the escape corridor, and every escape would pour false ChaseStalled into
        // the count the habit thresholds get calibrated from. Clearing keeps the escape chase
        // byte for byte what it was before this component existed.
        NemesisDecision decision = stateManager.Decision;
        if (decision != null && decision.ChaseFloor)
        {
            Clear();
            return;
        }

        // PAUSED, NOT CLEARED, while something other than the chase owns the body — see
        // IsBodyHeldElsewhere — or while there is nothing worth measuring (TryGetMeasuredBelief).
        // A pause keeps the window, so a chase that dips out of it for a moment picks up where it
        // was instead of starting over.
        if (IsBodyHeldElsewhere()) return;
        if (!TryGetMeasuredBelief(out Vector3 belief)) return;

        // Throttled on its own timer even when the query fails, and that is the reason the timer
        // is checked alone: an unreachable belief must cost one CalculatePath per interval, not
        // one per frame for as long as it stays unreachable.
        sampleTimer -= Time.deltaTime;
        if (sampleTimer <= 0f)
        {
            sampleTimer = SampleInterval;

            // A partial path is not a distance. It stops at whatever separates the two, so its
            // length is the distance to the wall, and watching that sit still would read as a
            // stall when it is really a sealed door — which the pursuit already handles as "no
            // complete route" and is not the loop this exists for.
            measurable = NemesisNav.TryGetPathDistance(transform.position, belief,
                                                       out float distance);
            if (measurable) Sample(distance);
        }

        // Only a window fed real measurements may age. Otherwise the clock would run on a
        // distance frozen at its last good reading and report a stall nobody measured.
        if (!hasWindow || !measurable) return;

        windowElapsed += Time.deltaTime;
        if (windowElapsed < Window) return;

        Stall();
    }

    /// <summary>
    /// Folds one measurement into the current window, opening one if there is none.
    ///
    /// JUDGED AGAINST THE WINDOW'S START AND NOT AGAINST THE BEST READING SO FAR. A running
    /// minimum ratchets: a player looping a column gets a metre closer on the near side of every
    /// lap and a metre further on the far side, and against a ratchet those near sides add up to
    /// progress that was never made. Against a fixed reference the loop reads as what it is — the
    /// distance keeps coming back to where it started.
    /// </summary>
    private void Sample(float distance)
    {
        LastDistance = distance;

        if (!hasWindow)
        {
            OpenWindow(distance);
            return;
        }

        // ARM'S LENGTH COUNTS AS PROGRESS. From a start already inside a metre and a half the
        // threshold cannot be met at all, and a Nemesis that has reached the player has not
        // stalled: whatever is keeping the grab from firing — the post-capture cooldown, a height
        // difference — is not a lap round a table, and flanking would only walk it away from
        // someone it is standing next to.
        bool closed = windowStartDistance - distance >= MinProgress || distance <= CatchReach;
        if (!closed) return;

        // Genuinely closing. The episode is over — the next window starts here, from the shorter
        // distance, so a chase that keeps gaining ground keeps resetting its own clock.
        IsChaseStagnant = false;
        OpenWindow(distance);
    }

    private float CatchReach
    {
        get
        {
            SO_NemesisData data = Data;
            return data != null ? data.CatchMaxReach : FallbackCatchReach;
        }
    }

    private void OpenWindow(float distance)
    {
        hasWindow = true;
        windowElapsed = 0f;
        windowStartDistance = distance;
    }

    /// <summary>
    /// A whole window gone without closing the distance.
    ///
    /// Logged rather than only counted, because what it reports is a verdict on the LEVEL as much
    /// as on the tuning: a stall or two over a long run is a player using cover well; the same
    /// column reporting one every four seconds is a piece of geometry that beats the chase
    /// outright.
    /// </summary>
    private void Stall()
    {
        ChaseStalledCount++;
        IsChaseStagnant = true;

        Debug.Log($"[{nameof(NemesisChaseProgress)}] Chase stalled: closed " +
                  $"{windowStartDistance - LastDistance:F2} m of the {MinProgress:F2} m it needed " +
                  $"in {Window:F1} s (NavMesh distance {windowStartDistance:F1} m -> " +
                  $"{LastDistance:F1} m). ChaseStalled this session: {ChaseStalledCount}.", this);

        // A new window from here rather than a latch that fires once: a player who keeps looping
        // keeps being counted, which is what makes the count worth calibrating against.
        OpenWindow(LastDistance);
    }

    /// <summary>
    /// The belief to measure against, when there is a chase worth measuring.
    ///
    /// GATED ON THE SIGHTING'S AGE, MEASURED AGAINST THE FRESHEST BELIEF. The gate is "it has
    /// seen them recently": a chase running on hearing alone is a chase about to become a search,
    /// and judging its progress would only be judging the approach to a noise. The threshold is
    /// <see cref="SO_NemesisData.VisionLossGracePeriod"/> rather than a knob of its own, because it
    /// is already the number that decides how long a lost sighting keeps a chase alive.
    ///
    /// The DISTANCE is to whichever sense caught them last — the same belief the pursuit is
    /// steering by — and that is not a detail. Running round a full-height column breaks line of
    /// sight on every lap and hearing takes over for a second or so; measuring against the frozen
    /// sighting there would read the far side of every lap as the player standing still behind
    /// the column. The noise is stamped at the player's own position, so it is as good a reading
    /// as a sighting for this; what it cannot be is the only evidence the chase has.
    /// </summary>
    private bool TryGetMeasuredBelief(out Vector3 belief)
    {
        belief = Vector3.zero;

        FieldOfView view = stateManager.FieldOfView;
        if (view == null) return false;

        SO_NemesisData data = Data;
        float grace = data != null ? data.VisionLossGracePeriod : FallbackSightGrace;

        if (view.TimeSinceLastSighting > grace) return false;

        return stateManager.TryGetBelief(out belief);
    }

    /// <summary>
    /// Whether the body is currently not the chase's to move: switched off for the lift ride,
    /// frozen by the Director's staged entrance (<see cref="NemesisStateManager.SetExternalHold"/>),
    /// or opening a door / crossing a link (the stuck watchdog's own suppression, raised for
    /// exactly "something is moving the Nemesis outside the NavMeshAgent").
    ///
    /// None of those is the player looping anything. Letting the window age through them would
    /// count a door the Nemesis stopped to open as ground the player won, which is the cheese
    /// detector blaming the player for the level.
    /// </summary>
    private bool IsBodyHeldElsewhere()
    {
        if (!stateManager.IsAgentReady) return true;
        if (stateManager.NavAgent.isStopped) return true;

        return stateManager.IsStuckDetectionSuppressed;
    }

    /// <summary>Ends the episode. <see cref="ChaseStalledCount"/> deliberately survives it.
    /// </summary>
    private void Clear()
    {
        hasWindow = false;
        measurable = false;
        windowElapsed = 0f;

        // Zero, so the first frame of the next chase measures straight away instead of waiting
        // out an interval left over from the previous one.
        sampleTimer = 0f;

        LastDistance = float.PositiveInfinity;
        IsChaseStagnant = false;
    }
}
