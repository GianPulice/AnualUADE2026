using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NemesisSearchingState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    /// <summary>Graph nodes already visited during this search. Cleared on entry and whenever a
    /// fresh noise restarts the search somewhere else.</summary>
    private readonly List<int> sweptNodes = new List<int>();

    /// <summary>
    /// How far back the sensed-waypoint trail is allowed to reach when reading which way the
    /// player was heading.
    ///
    /// Not on the SO because it is not really a design value: it has to be long enough to hold
    /// two waypoints' worth of travel and short enough that the trail describes this encounter
    /// rather than the previous one. The tuning that matters — how hard it commits to the answer
    /// — is InterceptForwardDot and InterceptTimeMargin.
    /// </summary>
    private const float TrailMemoryTime = 8f;

    // Reused across calls: the interception runs on entry and on every fresh noise, and there is
    // no reason to allocate three lists each time. Same pattern as NemesisController's own
    // selection buffers.
    private readonly List<int> candidateBuffer = new List<int>();
    private readonly List<int> sampledBuffer = new List<int>();
    private readonly List<float> keyBuffer = new List<float>();
    private readonly List<Vector3> positionBuffer = new List<Vector3>();

    /// <summary>Where the current cut-off is aimed, for the debug HUD and the gizmos. Reading it
    /// is the only way to tell an interception from the fallback sweep at a glance.</summary>
    public Vector3 InterceptPoint { get; private set; }

    /// <summary>Whether the destination came from the cut-off maths or from the fallback sweep.
    /// </summary>
    public bool HasIntercept { get; private set; }

    public NemesisSearchingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        NextState = StateKey;
        sweptNodes.Clear();

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.SearchSpeed);

        // The one expensive decision of this state, taken once. See TryGetInterceptPoint for why
        // it must not be re-taken per frame.
        RetargetSearch();
    }

    public override void ExitState()
    {
        HasIntercept = false;

        // Whatever happens next, the patrol that follows should prowl this area rather than
        // relocate across the level. Set on EVERY exit, the transition to Chasing included: it is
        // consumed by the next BeginPatrolCycle, so a search that succeeded and turned into a
        // chase simply spends it later, after that chase ends — which is still the right zone.
        nemesisStateManager.NemesisController?.RequestNearbyPatrol();
    }

    public override void UpdateState()
    {
        // Agent switched off (freight elevator ride): nothing to ask of it this frame. See
        // NemesisStateManager.IsAgentReady.
        if (!nemesisStateManager.IsAgentReady) return;

        // How long the search lasts, the half-second floor before anything may pull it out, and
        // going back to Chasing on sight are all rungs of NemesisDecision's ladder — rung 0 and
        // rung 6. What is left here is sweeping.
        if (nemesisStateManager.HasAudioTarget)
        {
            // A fresh noise outranks everything: it is newer information than the belief the
            // cut-off was aimed with. Clear the swept set and re-take the decision around the new
            // anchor, rather than carrying on towards a point chosen for where the player used to
            // be.
            sweptNodes.Clear();
            RetargetSearch();
        }

        if (nemesisStateManager.HasArrived)
        {
            nemesisStateManager.NavAgent.destination = GetNextSweepPoint();
        }
    }
    /// <summary>
    /// Takes the search's one expensive decision: cut the player off if it can, sweep if it
    /// cannot.
    ///
    /// Called on entry and on a fresh noise, and NOWHERE ELSE. See
    /// <see cref="TryGetInterceptPoint"/> for the cost.
    /// </summary>
    private void RetargetSearch()
    {
        if (!nemesisStateManager.IsAgentReady) return;

        if (TryGetInterceptPoint(out Vector3 intercept))
        {
            HasIntercept = true;
            InterceptPoint = intercept;
            nemesisStateManager.NavAgent.destination = intercept;
            return;
        }

        HasIntercept = false;

        // No cut-off to be had — no belief, no heading, or nothing ahead it can win the race to.
        // A noise it can still hear is nonetheless better information than a blind sweep, so head
        // for that, nudged along the direction the target was last observed moving.
        FieldOfListening listening = nemesisStateManager.FieldOfListening;
        if (nemesisStateManager.HasAudioTarget && listening != null)
        {
            nemesisStateManager.NavAgent.destination = PredictedFrom(listening.LastKnownPosition);
            return;
        }

        nemesisStateManager.NavAgent.destination = GetNextSweepPoint();
    }

    /// <summary>
    /// The waypoint to cut the player off at: ahead of where they were going, and reachable
    /// before they could get there.
    ///
    /// THE DIFFERENCE FROM SWEEPING
    ///
    /// <see cref="GetNextSweepPoint"/> walks outward from where the Nemesis is STANDING, so it
    /// searches the room it lost you in while you walk out of the building. This starts from
    /// where it last SENSED you, reads which way you were travelling, and asks a different
    /// question entirely: not "where haven't I looked" but "where can I be waiting".
    ///
    /// The two times are what makes it a cut-off rather than a chase. For each candidate it
    /// compares how long IT would take to get there against how long the PLAYER would, and keeps
    /// only the ones it wins — then takes the soonest of those, because arriving early is the
    /// whole point. When the geometry offers a shortcut this produces a flank for free; nobody
    /// had to author "go around".
    ///
    /// IT RUNS ON BELIEF, AND THAT IS DELIBERATE. The heading comes from waypoints the sensors
    /// actually stamped. Break line of sight and double back and the cut-off lands where you were
    /// going, not where you went — the Nemesis commits to a wrong guess, which is exactly the
    /// reward for juking and must not be "fixed".
    ///
    /// COST: two path queries per candidate, over at most WaypointBiasSampleCount candidates —
    /// 16 CalculatePath calls with the shipped value of 8. That is affordable once per entry into
    /// this state and a frame-hitch if it ever leaks into UpdateState. Keep it out of the tick.
    /// </summary>
    private bool TryGetInterceptPoint(out Vector3 point)
    {
        point = Vector3.zero;

        NemesisController controller = nemesisStateManager.NemesisController;
        NemesisRouteGraph graph = controller != null ? controller.RouteGraph : null;
        if (graph == null || !graph.IsBuilt) return false;

        if (!nemesisStateManager.TryGetBelief(out Vector3 anchor)) return false;
        if (!TryGetHeading(graph, anchor, out Vector3 heading)) return false;

        Vector3 origin = nemesisStateManager.transform.position;

        // Same island only, so anything picked is genuinely reachable — the guarantee the whole
        // graph exists to provide.
        if (!graph.TryGetComponentAt(origin, out int component)) return false;
        graph.CollectNodesInComponent(component, candidateBuffer);
        if (candidateBuffer.Count == 0) return false;

        SO_NemesisData data = nemesisStateManager.NemesisData;
        int sampleCount = data != null ? Mathf.Max(2, data.WaypointBiasSampleCount) : 8;

        // Free straight-line prefilter before paying for any path query. Keyed on min(distance to
        // me, distance to the anchor) — the same trim the patrol rolls use, and public static for
        // exactly this reason: keeping only what is near the Nemesis would throw away the
        // candidates near the player, which are the ones worth evaluating.
        positionBuffer.Clear();
        for (int i = 0; i < candidateBuffer.Count; i++)
            positionBuffer.Add(graph.GetNode(candidateBuffer[i]).Position);

        NemesisClusterPatrol.KeepClosest(candidateBuffer, positionBuffer, origin, true, anchor,
                                         sampleCount, sampledBuffer, keyBuffer);
        if (sampledBuffer.Count == 0) return false;

        float forwardDot = data != null ? data.InterceptForwardDot : 0.25f;
        float timeMargin = data != null ? Mathf.Max(1f, data.InterceptTimeMargin) : 1.15f;
        float playerSpeed = data != null ? Mathf.Max(0.5f, data.AssumedPlayerSpeed) : 4.5f;
        float ownSpeed = Mathf.Max(0.5f, nemesisStateManager.NemesisMovement.SearchSpeed);

        int best = -1;
        float bestTime = float.PositiveInfinity;

        for (int i = 0; i < sampledBuffer.Count; i++)
        {
            NemesisRouteGraph.Node node = graph.GetNode(sampledBuffer[i]);
            if (!node.IsValid) continue;

            // Ahead of them, not behind. Flattened: a waypoint one floor up is not "ahead"
            // just because the stairs happen to run that way.
            Vector3 toNode = node.Position - anchor;
            toNode.y = 0f;
            if (toNode.sqrMagnitude < 0.01f) continue;
            if (Vector3.Dot(toNode.normalized, heading) < forwardDot) continue;

            // Both distances over the NavMesh. Infinity when unreachable, which fails the
            // comparison below on its own — no special case needed.
            float ownTime = NemesisNav.PathDistanceOrInfinity(origin, node.Position) / ownSpeed;
            if (ownTime >= bestTime) continue;   // Cannot win: skip before the second query.

            float playerTime = NemesisNav.PathDistanceOrInfinity(anchor, node.Position) / playerSpeed;
            if (ownTime > playerTime * timeMargin) continue;   // Would not get there in time.

            bestTime = ownTime;
            best = sampledBuffer[i];
        }

        if (best < 0) return false;

        point = graph.GetNode(best).Position;
        sweptNodes.Add(best);   // Do not immediately re-pick it as a sweep target on arrival.
        return true;
    }

    /// <summary>
    /// Which way the player was travelling, in order of how much the answer is worth.
    ///
    /// The trail of stamped waypoints comes first because it measures over seconds of real
    /// navigation, where <see cref="FieldOfView.LastKnownVelocity"/> measures over at most half a
    /// second between sightings. For a cut-off several seconds out, a sidestep caught in that
    /// half-second window is noise that sends the Nemesis down the wrong corridor.
    ///
    /// The last resort — "away from me" — is the only assumption in the system, and it is the
    /// conservative one: someone who has just broken line of sight is, more often than not, still
    /// putting distance between you.
    /// </summary>
    private bool TryGetHeading(NemesisRouteGraph graph, Vector3 anchor, out Vector3 heading)
    {
        heading = Vector3.zero;

        if (graph.TryGetSensedTrail(TrailMemoryTime, out Vector3 from, out Vector3 to))
        {
            heading = to - from;
        }
        else
        {
            FieldOfView view = nemesisStateManager.FieldOfView;
            Vector3 velocity = view != null ? view.LastKnownVelocity : Vector3.zero;

            heading = velocity.sqrMagnitude > 0.01f
                ? velocity
                : anchor - nemesisStateManager.transform.position;
        }

        heading.y = 0f;
        if (heading.sqrMagnitude < 0.01f) return false;

        heading.Normalize();
        return true;
    }

    /// <summary>
    /// Where to look next.
    ///
    /// Walks the patrol graph outward from where the Nemesis is standing, nearest unswept node
    /// first, staying on its own island. That replaces scattering random points within five
    /// metres, which had the Nemesis searching the room it was already in — it would circle the
    /// spot it lost you at while you walked out of the building. The waypoints already blanket
    /// the level and already know which of them are connected to which, so the sweep gets its
    /// map for free.
    ///
    /// Falls back to the random scatter when there is no usable graph: an early test scene with
    /// no routes should still get a Nemesis that mills about rather than one that freezes.
    /// </summary>
    private Vector3 GetNextSweepPoint()
    {
        NemesisController controller = nemesisStateManager.NemesisController;
        NemesisRouteGraph graph = controller != null ? controller.RouteGraph : null;

        if (graph == null || !graph.IsBuilt) return GetRandomPointInNavMesh();

        Vector3 origin = nemesisStateManager.transform.position;

        // Which island the Nemesis is standing on, via the node closest to it. Without this the
        // sweep would happily pick a waypoint behind a sealed door and stall against it.
        int anchor = FindNearestNode(graph, origin, respectSwept: false);
        if (anchor < 0) return GetRandomPointInNavMesh();

        int island = graph.ComponentOf(anchor);
        int next = FindNearestNode(graph, origin, respectSwept: true, island: island);

        if (next < 0)
        {
            // Every reachable waypoint has been visited this search. Start the set over so a long
            // search keeps moving instead of standing still on the last one.
            sweptNodes.Clear();
            return GetRandomPointInNavMesh();
        }

        sweptNodes.Add(next);
        return graph.GetNode(next).Position;
    }

    /// <summary>
    /// Index of the graph node closest to a point, or -1.
    /// </summary>
    /// <param name="respectSwept">Skip nodes already visited during this search, and restrict to
    /// <paramref name="island"/>. False ignores both and is used to locate the Nemesis itself on
    /// the graph.</param>
    private int FindNearestNode(NemesisRouteGraph graph, Vector3 origin, bool respectSwept,
                                int island = -1)
    {
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < graph.NodeCount; i++)
        {
            if (respectSwept)
            {
                if (sweptNodes.Contains(i)) continue;
                if (graph.ComponentOf(i) != island) continue;
            }

            NemesisRouteGraph.Node node = graph.GetNode(i);
            if (!node.IsValid) continue;

            // Straight line and not path distance on purpose: this runs over every waypoint in
            // the level each time the Nemesis arrives somewhere, and a CalculatePath per node
            // would be a stall. The island filter above already guarantees whatever it picks is
            // actually reachable, which is the part that matters.
            float distance = Vector3.SqrMagnitude(node.Position - origin);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = i;
        }

        return best;
    }

    /// <summary>
    /// Nudges a remembered position forward along the direction the target was last observed
    /// moving.
    ///
    /// Kept to a fraction of a second deliberately. The velocity comes from what the sensors
    /// actually saw (see <see cref="FieldOfView.LastKnownVelocity"/>), so a longer lead would
    /// extrapolate a stale observation into a confident claim about somewhere nobody has been
    /// seen — and arriving ahead of the player from a guess reads as the game cheating rather
    /// than as the monster being sharp.
    ///
    /// Snapped back onto the NavMesh: extrapolating a running player walks the point straight
    /// through the wall they were about to turn at.
    /// </summary>
    private Vector3 PredictedFrom(Vector3 remembered)
    {
        SO_NemesisData data = nemesisStateManager.NemesisData;
        float leadTime = data != null ? data.SearchLeadTime : 0f;
        if (leadTime <= 0f) return remembered;

        FieldOfView view = nemesisStateManager.FieldOfView;
        if (view == null) return remembered;

        Vector3 velocity = view.LastKnownVelocity;
        if (velocity.sqrMagnitude < 0.01f) return remembered;

        Vector3 predicted = remembered + velocity * leadTime;

        return NavMesh.SamplePosition(predicted, out NavMeshHit hit, 2f, NavMesh.AllAreas)
            ? hit.position
            : remembered;
    }

    /// <summary>
    /// A point on the NavMesh near the current destination, to keep sweeping the area.
    ///
    /// Returns the position snapped by SamplePosition and not the raw random point: the raw
    /// one usually falls off the mesh, and setting it as a destination made the agent walk to
    /// the nearest edge instead. The attempts are capped because the original do/while had no
    /// way out — with the agent outside the NavMesh it span forever and hung Unity.
    /// </summary>
    private Vector3 GetRandomPointInNavMesh()
    {
        const int maxAttempts = 30;
        const float sampleRadius = 1f;

        // Read off the SO rather than a local const. It was hardcoded at 5, which meant the one
        // number deciding how wide the fallback sweep is could not be seen in the inspector, could
        // not be drawn by the gizmos, and could not be tuned without a recompile.
        SO_NemesisData data = nemesisStateManager.NemesisData;
        float range = data != null ? data.SearchSweepRadius : 5f;

        Vector3 origin = nemesisStateManager.NavAgent.destination;

        Vector3 forward = nemesisStateManager.transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;

        for (int i = 0; i < maxAttempts; i++)
        {
            // Horizontal only: onUnitSphere also varied Y and threw points above and below
            // the floor. The forward bias is kept so it sweeps ahead of where it is looking.
            Vector2 circle = Random.insideUnitCircle;
            Vector3 randomDir = new Vector3(circle.x, 0f, circle.y) + forward;
            Vector3 randomPoint = origin + randomDir * range;

            if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        // Nothing valid nearby: stay put rather than heading for an unreachable point.
        return nemesisStateManager.transform.position;
    }
}
