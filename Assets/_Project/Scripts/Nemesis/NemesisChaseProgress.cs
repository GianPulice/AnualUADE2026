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
/// <see cref="NemesisNav"/>'s job. The flip side is that this number can jump without anybody
/// moving (the path changes), which is why a jump is re-baselined instead of judged — see
/// <see cref="ChaseGapWindow"/>.
///
/// TWO THINGS THAT USED TO BE ONE: STAGNANT AND COUNTED
///
/// <see cref="IsChaseStagnant"/> is a LATCH for the ladder and the pursuit: true from the first
/// window without progress until the chase really closes the distance or ends. It is what makes
/// the Nemesis come round the other side, so it has to stay up for as long as the loop does. What
/// the habit ledger gets is a different thing and is cut differently: ONE <c>ChaseStalled</c> per
/// chase, however many windows it stalls for (<see cref="ChaseStallCounter"/> has the why). Before
/// that cut each stalled window raised the event again, one 50 s chase added 8 and the flank
/// counterplay was unlocked at 85% inside five minutes.
///
/// WHY THE WINDOW SOMETIMES SITS STILL ("quedan 0,3 s" for two seconds, bug report 30/09)
///
/// On purpose. The window only AGES while it is being fed: in Chasing, with a sighting newer than
/// <see cref="SO_NemesisData.VisionLossGracePeriod"/>, with a path to the belief and with the body
/// free. Any of those missing PAUSES it (it is kept, not cleared, so a chase that dips out of sight
/// for a moment picks up where it was), and a paused window reads exactly what it read when it
/// stopped. The timer was right and the panel was wrong: it said "quedan 0,3 s" with nothing to say
/// it had stopped. <see cref="PauseReason"/> is that missing word. The one real bug in the pause
/// was a window kept for longer than it could mean anything: see
/// <see cref="ChaseGapWindow.DiscardIfStale"/>.
///
/// The pure rules (what counts as closed, what counts as a path jump, one stall per chase) live in
/// WIRED.Nemesis.Logic and are tested in EditMode; this component keeps the scene half: the path
/// query, the sight gate and the state it is in.
///
/// SETUP: goes on the Nemesis root, next to <see cref="NemesisStateManager"/>, which finds it, adds
/// it when missing and ticks it. Nothing to author — every number it reads lives on
/// <see cref="SO_NemesisData"/>.
/// </summary>
public class NemesisChaseProgress : MonoBehaviour
{
    /// <summary>Why a window that is open is not ageing this tick. For the HUD, which could not
    /// otherwise tell "measuring and fine" from "frozen".</summary>
    public enum EPauseReason
    {
        /// <summary>Measuring: the window is being fed and aged.</summary>
        None,

        /// <summary>No sighting newer than Vision Loss Grace Period: the chase is running on
        /// hearing, and judging its progress would only be judging the approach to a noise.</summary>
        SightLost,

        /// <summary>The NavMesh has no complete route to the belief right now.</summary>
        NoPath,

        /// <summary>Something other than the chase owns the body: a door, a link, the lift, the
        /// Director's hold.</summary>
        BodyHeld,
    }

    // Tuning lives on SO_NemesisData, reached through the state manager, exactly as
    // NemesisStuckEscape does it: nothing is serialised here, so this component has no scene
    // wiring anyone could get wrong or forget to copy onto a prefab variant.
    private const float FallbackWindow = 4f;
    private const float FallbackMinProgress = 1.5f;
    private const float FallbackSampleInterval = 0.4f;
    private const float FallbackSightGrace = 2.5f;
    private const float FallbackCatchReach = 1f;
    private const float FallbackPathJump = 10f;
    private const float FallbackRegroupTime = 10f;

    private NemesisStateManager stateManager;

    private readonly ChaseGapWindow window = new ChaseGapWindow();
    private readonly ChaseStallCounter stalls = new ChaseStallCounter();

    /// <summary>Whether the latest path query could be answered. A window only ages while it is
    /// being fed real measurements — see <see cref="Tick"/>.</summary>
    private bool measurable;

    private float sampleTimer;

    /// <summary>The most recent measurement, or infinity before there is one. For the HUD: the
    /// pair (start, now) is what makes "it is not closing" readable rather than asserted.</summary>
    public float LastDistance => window.LastDistance;

    /// <summary>
    /// The chase has spent a whole window without closing the distance, and has not closed it
    /// since.
    ///
    /// A LATCH AND NOT A PULSE, which is what lets anything act on it. The counterplay is "come
    /// round the other side for as long as this lasts", so a flag that was true for the single
    /// frame the window expired would be read by the pursuit's throttled replan roughly never.
    /// It clears on real progress, or when the chase ends. It does NOT depend on whether the stall
    /// was counted for the habits: it stays up through every window of a long loop.
    /// </summary>
    public bool IsChaseStagnant { get; private set; }

    /// <summary>
    /// How many chases have been counted as stalled this session: one <c>ChaseStalled</c> each, in
    /// the vocabulary of the exploit table, however many windows they stalled for. It is what
    /// PlayerHabitTracker has heard through <see cref="NemesisEvents.OnChaseStalled"/>, so after a
    /// New Game it differs from the tracker's own count (which is cleared); the tracker's is the one
    /// that unlocks anything, and lives in the Data scene so it survives the level.
    ///
    /// Survives the end of a chase on purpose — a counter that reset every time the Nemesis lost you
    /// would only ever say 0 or 1.
    /// </summary>
    public int ChaseStalledCount { get; private set; }

    /// <summary>Windows that expired without progress in the current chase (or the last one, until
    /// the next begins), counted or not. For the HUD: "8 windows, counted once" is what makes the
    /// per-chase rule readable, and a chase that reports a window every four seconds is a piece of
    /// geometry that beats it outright.</summary>
    public int StalledWindows => stalls.WindowsThisChase;

    /// <summary>Whether the current chase already gave its one <c>ChaseStalled</c>. For the HUD.
    /// </summary>
    public bool IsStallCounted => stalls.CountedThisChase;

    /// <summary>NavMesh jumps ignored this session (see <see cref="ChaseGapWindow"/>): each is
    /// a sample that would have been read as ground won or lost and was a change of path instead.
    /// For the HUD.</summary>
    public int PathJumpsIgnored { get; private set; }

    /// <summary>Why the window is not ageing this tick; <see cref="EPauseReason.None"/> when it is
    /// (or when there is no chase to measure).</summary>
    public EPauseReason PauseReason { get; private set; }

    /// <summary>Whether a window is open at all, ageing or paused. The HUD needs to tell
    /// "measuring and fine" from "not measuring", which the numbers below cannot say on their own;
    /// <see cref="PauseReason"/> tells a paused one apart.</summary>
    public bool IsMeasuring => window.IsOpen;

    /// <summary>Metres the current window has managed to close; negative when the Nemesis has
    /// actually lost ground. For the HUD.</summary>
    public float WindowProgress => window.Progress;

    /// <summary>Seconds left before the current window is judged. Infinity when none is open, so
    /// a caller cannot mistake "not measuring" for "about to fire". A paused window keeps reporting
    /// the same number until it resumes.</summary>
    public float WindowRemaining => window.Remaining(Window);

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

    /// <summary>Metres between two consecutive measurements past which the change is the path's and
    /// not anybody's movement. See <see cref="SO_NemesisData.ChasePathJumpDistance"/>.</summary>
    private float PathJumpDistance
    {
        get
        {
            SO_NemesisData data = Data;
            return data != null ? data.ChasePathJumpDistance : FallbackPathJump;
        }
    }

    /// <summary>Seconds out of Chasing that end a chase for the habit count. See
    /// <see cref="SO_NemesisData.ChaseStallRegroupTime"/>.</summary>
    private float RegroupTime
    {
        get
        {
            SO_NemesisData data = Data;
            return data != null ? data.ChaseStallRegroupTime : FallbackRegroupTime;
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

    private float CatchReach
    {
        get
        {
            SO_NemesisData data = Data;
            return data != null ? data.CatchMaxReach : FallbackCatchReach;
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
        NemesisStateManager.ENemesisState? key = stateManager.CurrentStateKey;
        if (key != NemesisStateManager.ENemesisState.Chasing)
        {
            // The one exit that closes the chase for the habit count too, without waiting out the
            // regroup time: a capture. Anything else (a lost sighting that turns into a search) may
            // come back to Chasing a moment later and is still the same chase.
            if (key == NemesisStateManager.ENemesisState.Catch) stalls.EndChase();

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

        // Before anything can pause or judge: a chase paused for lack of sight is still a chase,
        // and the regroup tolerance of the habit count runs off the last tick that saw it chasing.
        stalls.NoteChasing(Time.time, RegroupTime);

        float deltaTime = Time.deltaTime;

        // PAUSED, NOT CLEARED, while something other than the chase owns the body — see
        // IsBodyHeldElsewhere — or while there is nothing worth measuring (TryGetMeasuredBelief).
        // A pause keeps the window, so a chase that dips out of it for a moment picks up where it
        // was instead of starting over. Not for ever: a window left unfed for longer than its own
        // length is dropped below, because its baseline is from another place and another time.
        if (IsBodyHeldElsewhere())
        {
            Pause(EPauseReason.BodyHeld, deltaTime);
            return;
        }

        if (!TryGetMeasuredBelief(out Vector3 belief))
        {
            Pause(EPauseReason.SightLost, deltaTime);
            return;
        }

        // Back in the measurable case: drop a window the pause outlived, and measure at once
        // instead of waiting out the rest of the sample interval to open the new one.
        if (window.DiscardIfStale(Window)) sampleTimer = 0f;

        // Throttled on its own timer even when the query fails, and that is the reason the timer
        // is checked alone: an unreachable belief must cost one CalculatePath per interval, not
        // one per frame for as long as it stays unreachable.
        sampleTimer -= deltaTime;
        if (sampleTimer <= 0f)
        {
            sampleTimer = SampleInterval;

            // A partial path is not a distance. It stops at whatever separates the two, so its
            // length is the distance to the wall, and watching that sit still would read as a
            // stall when it is really a sealed door — which the pursuit already handles as "no
            // complete route" and is not the loop this exists for.
            measurable = NemesisNav.TryGetPathDistance(transform.position, belief,
                                                       out float distance);
            if (measurable) Fold(distance);
        }

        // Only a window fed real measurements may age. Otherwise the clock would run on a
        // distance frozen at its last good reading and report a stall nobody measured.
        if (!measurable)
        {
            Pause(EPauseReason.NoPath, deltaTime);
            return;
        }

        if (!window.IsOpen) return;

        PauseReason = EPauseReason.None;

        if (!window.Age(deltaTime, Window)) return;

        Stall();
    }

    /// <summary>
    /// Folds one measurement into the window and acts on what it made of it. The rules
    /// themselves (what is a jump, what is closed) are <see cref="ChaseGapWindow"/>'s.
    /// </summary>
    private void Fold(float distance)
    {
        float previous = window.LastDistance;

        switch (window.Sample(distance, MinProgress, CatchReach, PathJumpDistance))
        {
            case ChaseGapWindow.ESample.Closed:
                // Genuinely closing: the episode is over, and the window has already reopened
                // from the shorter distance.
                IsChaseStagnant = false;
                break;

            case ChaseGapWindow.ESample.PathJump:
                // Not judged, and the latch is left as it is: the path changing says nothing about
                // whether the player is looping. The window reopened from here.
                PathJumpsIgnored++;
                Debug.Log($"[{nameof(NemesisChaseProgress)}] NavMesh distance jumped " +
                          $"{previous:F1} m -> {distance:F1} m in one sample (limit " +
                          $"{PathJumpDistance:F1} m): the path changed, not the player. Sample " +
                          $"ignored and the window re-baselined. Jumps ignored this session: " +
                          $"{PathJumpsIgnored}.", this);
                break;
        }
    }

    /// <summary>
    /// A whole window gone without closing the distance.
    ///
    /// The latch goes up and stays up (see <see cref="IsChaseStagnant"/>); what is cut is the
    /// COUNT. Only the first stalled window of a chase is published as <c>ChaseStalled</c> — see
    /// <see cref="ChaseStallCounter"/> for why one per chase and not one per window.
    ///
    /// Every window is logged, counted or not, because what the log reports is a verdict on the
    /// LEVEL as much as on the tuning: a stall or two over a long run is a player using cover well;
    /// the same column reporting one every four seconds is a piece of geometry that beats the chase
    /// outright.
    /// </summary>
    private void Stall()
    {
        IsChaseStagnant = true;

        bool counts = stalls.RegisterStalledWindow();

        string verdict = $"closed {window.Progress:F2} m of the {MinProgress:F2} m it needed in " +
                         $"{Window:F1} s (NavMesh distance {window.StartDistance:F1} m -> " +
                         $"{window.LastDistance:F1} m)";

        if (counts)
        {
            ChaseStalledCount++;

            Debug.Log($"[{nameof(NemesisChaseProgress)}] Chase stalled: {verdict}. Counted for " +
                      $"the habits. ChaseStalled this session: {ChaseStalledCount}.", this);

            NemesisEvents.ChaseStalled();
        }
        else
        {
            Debug.Log($"[{nameof(NemesisChaseProgress)}] Chase still stalled (window " +
                      $"{stalls.WindowsThisChase} of this chase): {verdict}. Not counted again: " +
                      "one chase is one ChaseStalled.", this);
        }

        // A new window from here rather than a latch that fires once: a loop that goes on keeps
        // being judged, which is what keeps the latch honest (it drops the moment a window closes
        // the distance) and what the window count above is made of.
        window.Restart();
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
    ///
    /// When this says no, the window is PAUSED (see <see cref="PauseReason"/>): it neither ages nor
    /// judges, which is the designed behaviour behind a timer that sits at "quedan 0,3 s" while the
    /// Nemesis does not see the player.
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

    /// <summary>Records why the window is not ageing this tick, and that it went unfed.</summary>
    private void Pause(EPauseReason reason, float deltaTime)
    {
        PauseReason = reason;
        window.NoteIdle(deltaTime);
    }

    /// <summary>Ends the episode. <see cref="ChaseStalledCount"/> deliberately survives it, and so
    /// does the per-chase count of <see cref="stalls"/>: leaving Chasing for a moment is not the
    /// end of the chase for the habits (see <see cref="ChaseStallCounter"/>).</summary>
    private void Clear()
    {
        window.Clear();
        measurable = false;

        // Zero, so the first frame of the next chase measures straight away instead of waiting
        // out an interval left over from the previous one.
        sampleTimer = 0f;

        IsChaseStagnant = false;
        PauseReason = EPauseReason.None;
    }
}
