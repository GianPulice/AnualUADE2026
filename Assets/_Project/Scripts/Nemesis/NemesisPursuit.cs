using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Where the Nemesis should actually run while it is chasing.
///
/// WHAT IT REPLACES
///
/// NemesisChasingState used to be one line: destination = belief. That is Seek, aimed at where the
/// player WAS, and it has two failure modes that between them cover most of what a chase is.
///
/// The first is arithmetic. Running at the point someone has already left means arriving there
/// after they have left the next one too, so against a player moving in a straight line the
/// Nemesis holds station instead of closing. Every metre it gains it immediately spends going to a
/// stale coordinate.
///
/// The second is worse and is the one players notice: when the belief is somewhere the agent
/// cannot path to, NavMeshAgent walks to the nearest point it CAN reach - which is the wall in
/// between - and stops there. remainingDistance drops to zero, the state thinks it arrived, and
/// the monster stands with its face against a partition while the player watches from the other
/// side.
///
/// This class answers both: predict where the target is going, and when the direct route is not
/// good enough, route through a patrol waypoint chosen for being somewhere it could actually SEE
/// them from.
///
/// WHY IT IS NOT A MonoBehaviour
///
/// Same shape as <see cref="NemesisDecision"/>: a plain object constructed with the state manager,
/// owned by the state that uses it. It needs no Update of its own - the state ticks it - and
/// nothing about it belongs on a GameObject.
/// </summary>
public sealed class NemesisPursuit
{
    private readonly NemesisStateManager stateManager;

    // Reused across replans, same reasoning as NemesisController's own selection buffers: this
    // runs several times a second during a chase and none of it is worth allocating for.
    private readonly List<int> candidateBuffer = new List<int>();
    private readonly List<int> sampledBuffer = new List<int>();
    private readonly List<float> keyBuffer = new List<float>();
    private readonly List<float> weightBuffer = new List<float>();
    private readonly List<Vector3> positionBuffer = new List<Vector3>();

    private float replanTimer;
    private bool hasRoutePoint;
    private Vector3 routePoint;
    private Vector3 lastReplanBelief;
    private bool hasReplanned;

    private Vector3 predictedPoint;
    private bool hasPredictedPoint;

    /// <summary>
    /// How far back the sensed trail counts as "the way they came" when a stalled chase looks for
    /// the other side.
    ///
    /// The same number and the same reasoning as NemesisSearchingState's own TrailMemoryTime,
    /// which reads the trail for a heading: long enough to hold a lap's worth of stamped
    /// waypoints, short enough to describe this encounter rather than the last one. Not on the SO
    /// for the reason given there — it is not a design value; what the designer tunes is how hard
    /// the trail pushes (ChaseTrailPenalty) and how wide it is (ChaseTrailPenaltyRadius). Public so
    /// NemesisGizmos draws the trail this class actually reads, rather than a guess at it.
    /// </summary>
    public const float TrailMemoryTime = 8f;

    /// <summary>Flat metres to the target under which the chase stops leading it. See Predict.
    /// Not on the SO for the same reason as TrailMemoryTime: it is a property of the steering (a
    /// lead longer than the gap is always wrong), not a difficulty knob.</summary>
    private const float CloseRangeNoLead = 3f;

    /// <summary>How old a sighting may be and still be "where it lost them". The same 10 s the
    /// ladder gives the walk back there (SO_NemesisPriorities, "va a donde lo vio por última vez").
    /// </summary>
    public const float RecentSightingSeconds = 10f;

    /// <summary>How many detour candidates the last replan marked down for sitting on the sensed
    /// trail. For the debug HUD: a stalled chase with nothing penalised is a stall the counterplay
    /// had nothing to work with — the lap passes no waypoints, so there is no "way they came" to
    /// steer away from — and that is a level problem (waypoints around the obstacle), not a tuning
    /// one.</summary>
    public int PenalizedLastReplan { get; private set; }

    /// <summary>Where the pursuit currently thinks the player is heading. Drawn by NemesisGizmos -
    /// see there for why an invisible decision is an untunable one.</summary>
    public Vector3 PredictedPoint => predictedPoint;

    public bool HasPredictedPoint => hasPredictedPoint;

    /// <summary>The waypoint being routed through, when the pursuit decided a detour beats going
    /// straight at the belief. False means it is running direct.</summary>
    public bool HasRoutePoint => hasRoutePoint;

    public Vector3 RoutePoint => routePoint;

    public NemesisPursuit(NemesisStateManager manager)
    {
        stateManager = manager;
    }

    private SO_NemesisData Data => stateManager.NemesisData;

    /// <summary>
    /// Called when the chase starts. Clears the route so the first frame takes a fresh decision
    /// rather than inheriting whatever the previous chase ended on - which could be a waypoint on
    /// the far side of the level.
    /// </summary>
    public void Reset()
    {
        replanTimer = 0f;
        hasRoutePoint = false;
        hasPredictedPoint = false;
        hasReplanned = false;
        PenalizedLastReplan = 0;
    }

    /// <summary>
    /// The point to steer at this frame. Called every tick by NemesisChasingState.
    ///
    /// The PREDICTION is recomputed every frame - it is a couple of vector operations and it has
    /// to track the target continuously. The ROUTE decision is throttled, because it costs a
    /// NavMesh path query per candidate.
    /// </summary>
    /// <returns>false when there is nothing to chase, and the caller should leave the agent alone.
    /// </returns>
    public bool TryGetDestination(out Vector3 destination)
    {
        destination = Vector3.zero;

        if (!stateManager.TryGetBelief(out Vector3 belief)) return false;

        // LOST SIGHT: GO BACK TO WHERE IT LAST SAW THEM, NOTHING CLEVERER. Leading the target and
        // routing through a flank only pay while it still sees them; with the sighting gone, the
        // lead point is a guess that runs past the corner the player turned — or straight to the
        // locker they were running for. Walking to the last known spot is what a player can read
        // and play against, and it is what the search then starts from. Only when that spot can be
        // reached: a partial path is the wall-hugging failure above, and the ladder's
        // IsBeliefUnreachable rung owns that case.
        //
        // The SEEN spot, not the freshest belief: the belief follows whichever sense fired last,
        // and a player who breaks line of sight and keeps running is heard all the way to the
        // locker they dive into. Chasing that is not going back to where it lost them — it is
        // being led to the hiding spot by the footsteps.
        if (!stateManager.HasVisualTarget && TryGetRecentSighting(out Vector3 lastSeen) &&
            stateManager.TryGetThrottledRoute(lastSeen, out NemesisNav.NavRoute toLastSeen) &&
            toLastSeen.IsComplete)
        {
            predictedPoint = lastSeen;
            hasPredictedPoint = true;
            hasRoutePoint = false;
            destination = lastSeen;
            return true;
        }

        predictedPoint = Predict(belief);
        hasPredictedPoint = true;

        TickRoute(belief);

        destination = hasRoutePoint ? routePoint : predictedPoint;
        return true;
    }

    /// <summary>
    /// Where the eyes last had the player, if that was recent enough to still be this chase. An
    /// old sighting from another encounter is not where it lost them, so past
    /// <see cref="RecentSightingSeconds"/> it does not count.
    /// </summary>
    public bool TryGetRecentSighting(out Vector3 position)
    {
        FieldOfView eyes = stateManager.FieldOfView;
        position = Vector3.zero;

        if (eyes == null || !eyes.HasLastKnownPosition ||
            eyes.TimeSinceLastSighting >= RecentSightingSeconds)
            return false;

        position = eyes.LastKnownPosition;
        return true;
    }

    // -- Prediction ----------------------------------------------------------

    /// <summary>
    /// Where the target is going, from where it was last sensed and how fast it appeared to be
    /// moving.
    ///
    /// THE DOT GUARD IS HALF THE VALUE OF THIS METHOD. Extrapolating blindly is fine while the
    /// player runs away and actively harmful the moment they run TOWARDS the Nemesis: the lead
    /// point then lands behind the monster, and it turns around and sprints away from the person
    /// it is chasing. Comparing the direction-to-the-lead-point against the direction-to-the-target
    /// catches exactly that case - a negative dot means the two disagree about which way to go -
    /// and falls back to aiming at the target itself.
    ///
    /// The velocity is OBSERVED (FieldOfView.LastKnownVelocity, measured between sightings) and
    /// never read off the player's own movement code. That is the difference between predicting
    /// and cheating, and it is what keeps changing direction the instant you break line of sight a
    /// real counterplay rather than a formality.
    ///
    /// The result is snapped back onto the NavMesh: extrapolating a running player walks the point
    /// straight through the wall they were about to turn at, and handing the agent a destination
    /// inside geometry is how it ends up pressed against it.
    /// </summary>
    private Vector3 Predict(Vector3 belief)
    {
        SO_NemesisData data = Data;
        FieldOfView view = stateManager.FieldOfView;

        // Up close there is nothing to cut off: the lead is longer than the gap, so every sidestep
        // swings the target past the player — behind a table, to the far side of it. Close in, it
        // goes at them and lets the path find the way round.
        Vector3 toBelief = belief - stateManager.transform.position;
        toBelief.y = 0f;
        if (toBelief.sqrMagnitude < CloseRangeNoLead * CloseRangeNoLead) return belief;

        return PredictAhead(stateManager.transform.position, belief,
                            view != null ? view.LastKnownVelocity : Vector3.zero,
                            data != null ? data.ChaseTimePrediction : 0f);
    }

    /// <summary>
    /// The prediction itself, static and free of any Nemesis, so the SEARCH can use the same one.
    ///
    /// NemesisSearchingState grew its own copy of this before the pursuit existed, with a shorter
    /// lead and without the dot guard - which meant a search nudged its target backwards through
    /// the Nemesis whenever the player had last been observed running towards it. Two versions of
    /// "where are they going" is exactly the drift the shared helpers in this refactor exist to
    /// stop; the only thing the two callers should differ on is the lead time, which is a number
    /// on the SO and not a second algorithm.
    /// </summary>
    /// <param name="leadTime">Seconds to extrapolate. 0 disables the prediction entirely and hands
    /// the belief straight back.</param>
    public static Vector3 PredictAhead(Vector3 self, Vector3 belief, Vector3 velocity,
                                       float leadTime)
    {
        if (leadTime <= 0f) return belief;
        if (velocity.sqrMagnitude < 0.01f) return belief;

        Vector3 leadPoint = belief + velocity * leadTime;

        Vector3 toLead = leadPoint - self;
        Vector3 toBelief = belief - self;
        if (toLead.sqrMagnitude < 0.0001f || toBelief.sqrMagnitude < 0.0001f) return belief;

        // Coming at me rather than running away: the lead point is on the wrong side, and chasing
        // it would turn the Nemesis around and send it away from the person it is chasing.
        if (Vector3.Dot(toLead.normalized, toBelief.normalized) < 0f) return belief;

        return KeepOnTargetSide(belief, leadPoint);
    }

    /// <summary>
    /// The lead point, walked from the target along the NavMesh and stopped at the first edge.
    ///
    /// A plain SamplePosition of the lead point is what left the Nemesis mirroring a player across
    /// a table. Strafing behind it puts the lead point inside the table's hole in the NavMesh, and
    /// the nearest surface to a point inside a hole is just as likely the Nemesis's own side. It
    /// ran to that side, "arrived", and stood there, never going round. NavMesh.Raycast walks the
    /// surface from where the player IS, so the answer can never be across a gap from them: past
    /// an edge it stops at the edge, on their side.
    /// </summary>
    private static Vector3 KeepOnTargetSide(Vector3 belief, Vector3 leadPoint)
    {
        const float SnapRadius = 1f;
        int mask = NemesisNav.AreaMask;

        if (!NavMesh.SamplePosition(belief, out NavMeshHit from, SnapRadius, mask)) return belief;

        if (NavMesh.Raycast(from.position, leadPoint, out NavMeshHit edge, mask)) return edge.position;

        // Clear run along the surface: the lead point is on the player's side, only lifted back
        // onto the floor.
        return NavMesh.SamplePosition(leadPoint, out NavMeshHit onFloor, SnapRadius, mask)
            ? onFloor.position
            : from.position;
    }

    // -- Route choice --------------------------------------------------------

    /// <summary>
    /// Decides whether to run straight at the predicted point or to route through a waypoint, on a
    /// throttle.
    ///
    /// THROTTLED, AND NOT AS AN OPTIMISATION. Choosing a waypoint costs a NavMesh path query per
    /// candidate; NemesisSearchingState's own interception carries an explicit warning that
    /// letting that leak into a per-frame tick is a frame hitch. The belief-moved test is what
    /// keeps the throttle from also making it feel slow: catching sight of the player again
    /// somewhere new re-decides immediately instead of waiting out the interval.
    /// </summary>
    private void TickRoute(Vector3 belief)
    {
        SO_NemesisData data = Data;

        float interval = data != null ? data.ChaseRouteReplanInterval : 0.75f;
        float moveThreshold = data != null ? data.ChaseBeliefMoveThreshold : 3f;

        replanTimer -= Time.deltaTime;

        bool beliefMoved = hasReplanned &&
                           (belief - lastReplanBelief).sqrMagnitude > moveThreshold * moveThreshold;

        if (hasReplanned && replanTimer > 0f && !beliefMoved) return;

        replanTimer = Mathf.Max(0.1f, interval);
        lastReplanBelief = belief;
        hasReplanned = true;

        Replan(belief);
    }

    /// <summary>
    /// Picks this replan's destination.
    ///
    /// SEEING THEM ENDS THE ARGUMENT. With the player in view there is nothing a waypoint can add:
    /// the shortest way to someone you can see is at them, and detouring "cleverly" while looking
    /// straight at the player is the single most obviously broken thing an enemy can do.
    ///
    /// ...UNLESS GOING AT THEM HAS STOPPED WORKING. That argument assumes running at someone you
    /// can see closes the gap, and a loop round a table is precisely the case where it does not:
    /// the player is in view on every lap, the speed difference means the tail chase can never
    /// end, and NemesisChaseProgress has measured a whole window of it. Keeping the early-out there
    /// would leave the counterplay below as dead code in the one situation it exists for — a low
    /// table never breaks line of sight at all. Every candidate still has to SEE the predicted
    /// point, so this cannot send the Nemesis off to stand somewhere it has lost the player.
    /// </summary>
    private void Replan(Vector3 belief)
    {
        hasRoutePoint = false;
        PenalizedLastReplan = 0;

        bool stagnant = stateManager.IsChaseStagnant;

        if (stateManager.HasVisualTarget && !stagnant) return;

        Vector3 origin = stateManager.transform.position;

        // How good going direct is. A COMPLETE route means the agent can genuinely get there and
        // the bar for a detour is high; an incomplete one means the destination is the wall in
        // between, and then almost anything reachable is an improvement.
        //
        // DELIBERATELY NOT THROUGH NemesisPathOracle, which every other route question in the
        // system goes through. The oracle holds exactly ONE cached answer and does not key it on
        // the target it was asked about: querying it here would hand this a verdict computed for
        // the decision layer's belief, and - worse - reset its timer with a verdict computed for
        // the predicted point, which the elevator rung would then read as its own. Two callers
        // sharing an unkeyed cache also halves the effective interval, which is the frame-to-frame
        // flip NemesisTraversingState was built to stop.
        //
        // Paying for a query of its own is affordable precisely because this is already throttled:
        // one CalculatePath per replan, next to the per-candidate ones a few lines below.
        bool directWorks = NemesisNav.TryGetRoute(origin, predictedPoint,
                                                  out NemesisNav.NavRoute route) &&
                           route.IsComplete;

        float directTime = DirectTime(directWorks, route);

        if (TryPickWaypoint(origin, belief, directWorks, directTime, stagnant, out Vector3 point))
        {
            hasRoutePoint = true;
            routePoint = point;
        }
    }

    private float DirectTime(bool directWorks, in NemesisNav.NavRoute route)
    {
        if (directWorks) return route.PathDistance / ChaseSpeed;

        // No complete route: the direct option is not "slow", it is "does not arrive". Infinity
        // makes every reachable candidate below beat it on its own, with no special case.
        return float.PositiveInfinity;
    }

    private float ChaseSpeed
    {
        get
        {
            SO_NemesisMovement movement = stateManager.NemesisMovement;
            return movement != null ? Mathf.Max(0.5f, movement.ChaseSpeed) : 4f;
        }
    }

    /// <summary>
    /// Scores patrol waypoints as places to run to instead of the belief, and rolls one.
    ///
    /// THE FOUR THINGS IT WEIGHS, AND WHY EACH IS THERE
    ///
    /// LINE OF SIGHT to the predicted point is the factor that produces the behaviour worth
    /// having. A waypoint you could SEE the player from is worth far more than one that merely
    /// sits near them, and preferring it is what makes the Nemesis swing round to open the angle
    /// on a corner instead of following you into it. Nobody authored "go around"; it falls out of
    /// scoring the geometry.
    ///
    /// HEARING RANGE is what keeps a pursuit alive after it goes wrong. Ending up somewhere within
    /// earshot of where it believes you are means the next footstep re-acquires you; ending up
    /// outside it means the chase quietly becomes a search.
    ///
    /// THE LAST KNOWN POSITION anchors the whole thing to something observed, and is decayed by
    /// BeliefFreshness so a memory going stale stops steering. Without the decay a sighting from
    /// thirty seconds ago pulls exactly as hard as one from now, which is how a pursuit ends up
    /// committed to a room the player left long ago.
    ///
    /// ITS OWN ARRIVAL TIME is the brake on all three. A perfect vantage point it reaches in nine
    /// seconds is not a pursuit, it is sightseeing.
    ///
    /// A ROLL AND NOT AN ARGMAX, for the same reason NemesisController gives: always taking the
    /// single best-scoring position reads as the monster knowing exactly where you are, because
    /// functionally it does. Weighted tickets read as it having a good idea.
    ///
    /// AND WHEN THE CHASE HAS STALLED, THE WAY THEY CAME IS MARKED DOWN. This is the whole of the
    /// answer to looping an obstacle, and it is deliberately not a behaviour: no "flank" state, no
    /// point computed on the far side of the table. The waypoints the player was sensed running
    /// past — NemesisController's sensed trail — lose most of their tickets, the detour budget
    /// widens so the far side is affordable at all, and the roll does the rest: what is left is
    /// the other way round. It works on the waypoint graph rather than on NavMesh area costs
    /// because nothing here rebakes at runtime, and it is still a roll, so it is a tendency the
    /// player can read and beat, not a certainty.
    ///
    /// Never faster: the speed gap is the design, and a monster that accelerates when you outwit it
    /// reads as the game cheating, not as the monster being clever.
    /// </summary>
    private bool TryPickWaypoint(Vector3 origin, Vector3 belief, bool directWorks, float directTime,
                                 bool stagnant, out Vector3 point)
    {
        point = Vector3.zero;

        NemesisController controller = stateManager.NemesisController;
        NemesisRouteGraph graph = controller != null ? controller.RouteGraph : null;
        if (graph == null || !graph.IsBuilt) return false;

        // Same island only, which is what guarantees anything picked is genuinely reachable - the
        // guarantee the whole graph exists to provide.
        if (!graph.TryGetComponentAt(origin, out int component)) return false;

        graph.CollectNodesInComponent(component, candidateBuffer);
        if (candidateBuffer.Count == 0) return false;

        SO_NemesisData data = Data;
        int sampleCount = data != null ? Mathf.Max(2, data.WaypointBiasSampleCount) : 8;

        // Free straight-line prefilter before paying for any path query, keyed on min(near me,
        // near the belief) - keeping only what is near the Nemesis would throw away precisely the
        // candidates around the player, which are the ones worth evaluating.
        positionBuffer.Clear();
        for (int i = 0; i < candidateBuffer.Count; i++)
            positionBuffer.Add(graph.GetNode(candidateBuffer[i]).Position);

        NemesisClusterPatrol.KeepClosest(candidateBuffer, positionBuffer, origin, true, belief,
                                         sampleCount, sampledBuffer, keyBuffer);
        if (sampledBuffer.Count == 0) return false;

        float tolerance = data != null ? Mathf.Max(1f, data.ChaseDetourTolerance) : 1.25f;

        // Raised, never lowered: a stagnant tolerance typed below the normal one would make the
        // counterplay SHRINK the choice, which is the opposite of what a stall asks for.
        if (stagnant)
        {
            tolerance = Mathf.Max(tolerance,
                                  data != null ? data.ChaseStagnantDetourTolerance : 2.5f);
        }

        float listenRange = data != null ? data.ListenRange : 10f;
        float speed = ChaseSpeed;

        // Read once per replan rather than per candidate. Only consulted while stagnant.
        float trailPenalty = data != null ? Mathf.Clamp01(data.ChaseTrailPenalty) : 0.2f;
        float trailRadius = data != null ? Mathf.Max(0f, data.ChaseTrailPenaltyRadius) : 3f;
        float floorBand = data != null ? data.FloorHeightThreshold : 2.5f;

        float freshness = controller != null ? controller.BeliefFreshness() : 1f;

        // The budget a detour has to fit inside. With no complete direct route this is infinity,
        // so the tolerance stops filtering and any reachable candidate qualifies.
        float budget = directWorks ? directTime * tolerance : float.PositiveInfinity;

        weightBuffer.Clear();
        int kept = 0;

        for (int i = 0; i < sampledBuffer.Count; i++)
        {
            Vector3 candidate = graph.GetNode(sampledBuffer[i]).Position;

            float ownTime = NemesisNav.PathDistanceOrInfinity(origin, candidate) / speed;

            // Unreachable, or a longer walk than the detour is worth.
            if (float.IsPositiveInfinity(ownTime) || ownTime > budget)
            {
                weightBuffer.Add(0f);
                continue;
            }

            bool sees = CanSeeFrom(candidate, predictedPoint);

            // The whole point of the detour. Without a clear view of where it thinks you are, a
            // waypoint is just a place - and going to a place instead of after the player is
            // strictly worse than going direct.
            if (!sees)
            {
                weightBuffer.Add(0f);
                continue;
            }

            float weight = 1f;

            // Within earshot of the belief: it can re-acquire from there.
            if (LineOfSight.CheckRange(candidate, belief, listenRange)) weight *= 2f;

            // Close to what it actually observed, faded as that observation goes stale.
            weight *= NemesisClusterPatrol.ProximityWeight(candidate, belief,
                                                           Mathf.Lerp(1f, 3f, freshness),
                                                           Mathf.Max(1f, listenRange));

            // Sooner is better. +1 so a candidate it is standing on does not divide by zero.
            weight /= 1f + ownTime;

            // The way they came round, while the direct chase is getting nowhere. A multiplier
            // and not a veto (unless the asset sets it to 0): if the trail side is the only place
            // with a view of the player it still wins the roll, because a detour there beats
            // tail-chasing them round the same lap again.
            if (stagnant && graph.IsNearSensedTrail(candidate, trailRadius, floorBand,
                                                    TrailMemoryTime))
            {
                weight *= trailPenalty;
                PenalizedLastReplan++;
            }

            weightBuffer.Add(weight);
            kept++;
        }

        if (kept == 0) return false;

        int index = RouletteSelection.Roulette(weightBuffer);
        if (index < 0 || weightBuffer[index] <= 0f) return false;

        point = graph.GetNode(sampledBuffer[index]).Position;
        return true;
    }

    /// <summary>
    /// Whether a waypoint has a clear view of a point, tested at chest height.
    ///
    /// Raised off the ground for the same reason the capture's own line-of-sight probe is: both
    /// the waypoint markers and the belief sit at floor level, and a ray cast between two points
    /// on the floor scrapes along it and reports occlusion practically everywhere.
    ///
    /// Borrows FieldOfListening's obstacle mask rather than adding a seventh "what is solid" mask
    /// to the project - the same one NemesisStateManager's capture check and NemesisController's
    /// spawn-visibility check already share.
    /// </summary>
    private bool CanSeeFrom(Vector3 from, Vector3 to)
    {
        FieldOfListening listening = stateManager.FieldOfListening;
        if (listening == null) return true;   // No way to test it: do not veto every candidate.

        const float ProbeHeight = 1f;

        return !listening.IsOccludedByWall(from + Vector3.up * ProbeHeight,
                                           to + Vector3.up * ProbeHeight);
    }
}
