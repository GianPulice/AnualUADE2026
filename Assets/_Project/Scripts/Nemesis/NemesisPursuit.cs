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

        predictedPoint = Predict(belief);
        hasPredictedPoint = true;

        TickRoute(belief);

        destination = hasRoutePoint ? routePoint : predictedPoint;
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

        return NavMesh.SamplePosition(leadPoint, out NavMeshHit hit, 2f, NavMesh.AllAreas)
            ? hit.position
            : belief;
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
    /// </summary>
    private void Replan(Vector3 belief)
    {
        hasRoutePoint = false;

        if (stateManager.HasVisualTarget) return;

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

        if (TryPickWaypoint(origin, belief, directWorks, directTime, out Vector3 point))
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
    /// </summary>
    private bool TryPickWaypoint(Vector3 origin, Vector3 belief, bool directWorks, float directTime,
                                 out Vector3 point)
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
        float listenRange = data != null ? data.ListenRange : 10f;
        float speed = ChaseSpeed;

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
