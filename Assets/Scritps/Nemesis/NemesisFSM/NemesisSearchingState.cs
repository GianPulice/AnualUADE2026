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

    /// <summary>Where it is looking right now, whichever way that was chosen. For the HUD and the
    /// gizmos: "what is it searching" has to be answerable from outside or none of the weights
    /// below can be tuned.</summary>
    public Vector3 SearchTarget { get; private set; }

    /// <summary>Standing at a search point, looking around, before choosing the next one.</summary>
    public bool IsPausing => pauseRemaining > 0f;

    /// <summary>Seconds left of the pause at the current point. See SO_NemesisData.SearchPauseTime
    /// for why the search stands still at all.</summary>
    private float pauseRemaining;

    // Scoring buffers for PickSearchTarget, reused for the same reason as the interception's.
    private readonly List<float> weightBuffer = new List<float>();

    /// <summary>
    /// The free-roam sweep, used when the search commits to a room rather than to a cut-off. Owned
    /// by this state and constructed with it, the same arrangement NemesisChasingState has with
    /// NemesisPursuit.
    /// </summary>
    private readonly NemesisFreeRoam freeRoam;

    /// <summary>Whether this search is sweeping a room it watched the player enter, rather than
    /// working the patrol graph. For the debug HUD and the gizmos.</summary>
    public bool IsSweepingRoom => freeRoam.IsCommitted;

    /// <summary>The free-roam sweep, so the gizmos can draw the committed area and what has
    /// already been swept.</summary>
    public NemesisFreeRoam FreeRoam => freeRoam;

    public NemesisSearchingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        freeRoam = new NemesisFreeRoam(stateManager);
    }

    public override void EnterState()
    {
        NextState = StateKey;
        sweptNodes.Clear();
        pauseRemaining = 0f;
        freeRoam.Release();

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.SearchSpeed);

        // The one expensive decision of this state, taken once. See TryGetInterceptPoint for why
        // it must not be re-taken per frame.
        RetargetSearch();
    }

    public override void ExitState()
    {
        HasIntercept = false;
        freeRoam.Release();

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
        if (nemesisStateManager.HasAudioTarget && ShouldNoiseRetarget())
        {
            // A fresh noise outranks everything: it is newer information than the belief the
            // cut-off was aimed with. Clear the swept set and re-take the decision around the new
            // anchor, rather than carrying on towards a point chosen for where the player used to
            // be.
            sweptNodes.Clear();
            RetargetSearch();
        }

        if (!nemesisStateManager.HasArrived) return;

        // ARRIVED: STOP AND LOOK BEFORE MOVING ON.
        //
        // Chaining straight to the next destination is what made the search unreadable from the
        // outside. The Nemesis crossed the room, turned, crossed it again and left, and from
        // inside a locker none of that says whether it is closing in on you or has already
        // written the area off - it just looks like an odd patrol. Standing still for a moment at
        // each point, sweeping its gaze (NemesisLookAround extends to this state for exactly
        // this), turns the search into something the player can read and gamble against.
        //
        // It also stops the search from outrunning its own maths: PickSearchTarget costs path
        // queries, and without a pause it paid them every time the agent brushed a waypoint.
        if (pauseRemaining > 0f)
        {
            pauseRemaining -= Time.deltaTime;

            nemesisStateManager.NavAgent.velocity = Vector3.zero;
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
            return;
        }

        SO_NemesisData data = nemesisStateManager.NemesisData;
        pauseRemaining = data != null ? data.SearchPauseTime : 0f;

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.SearchSpeed);

        SetDestination(PickNextPoint());
    }

    /// <summary>
    /// Where to look next: the committed room if there is one, the patrol graph otherwise.
    ///
    /// The fall-through matters. A room sweep that has run out of unswept ground has genuinely
    /// finished searching that room, and standing in it until SearchTimeOut expires is the failure
    /// this state's whole design is against — so it drops the commitment and hands back to the
    /// graph-wide sweep, which is free to leave. Releasing rather than re-committing also means
    /// the gizmos stop drawing an area nobody is searching any more.
    /// </summary>
    private Vector3 PickNextPoint()
    {
        if (freeRoam.IsCommitted)
        {
            if (freeRoam.TryGetNextPoint(out Vector3 point)) return point;

            freeRoam.Release();
        }

        return PickSearchTarget();
    }

    /// <summary>
    /// Whether a noise should pull the search off what it is currently doing.
    ///
    /// SIGHT OUTRANKS HEARING FOR A WHILE, WHICH IS THE WHOLE POINT. Re-aiming on every noise —
    /// what this state did unconditionally — means that having been WATCHED walking into a room,
    /// throwing something down the corridor gets the Nemesis to leave. That is a free escape from
    /// the one situation the monster should be most dangerous in, and it costs the player nothing
    /// to use, so it becomes the answer to every encounter.
    ///
    /// A noise INSIDE the swept area is always honoured. That one is not competing with the
    /// sighting, it is agreeing with it, and ignoring it would have the Nemesis methodically
    /// working through a room while the player knocks something over in the corner of it.
    ///
    /// The window is measured off TimeInCurrentState rather than a timer of its own: the
    /// commitment is taken on entry, so time-in-state already IS time-since-commitment, and a
    /// second clock would be a second thing to keep in sync.
    /// </summary>
    private bool ShouldNoiseRetarget()
    {
        if (!freeRoam.IsCommitted) return true;

        SO_NemesisData data = nemesisStateManager.NemesisData;
        float commitTime = data != null ? data.SightCommitTime : 0f;

        if (nemesisStateManager.TimeInCurrentState >= commitTime) return true;

        FieldOfListening listening = nemesisStateManager.FieldOfListening;
        if (listening == null) return true;

        return freeRoam.Contains(listening.LastKnownPosition);
    }

    /// <summary>Points the agent somewhere and records it, so the HUD and the gizmos can say what
    /// the search is currently looking at.</summary>
    private void SetDestination(Vector3 point)
    {
        SearchTarget = point;
        nemesisStateManager.NavAgent.destination = point;
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

        // A retarget is new information, so whatever pause was running is over: standing around
        // for another second after hearing a noise somewhere else is the opposite of reacting.
        pauseRemaining = 0f;

        // BEFORE THE INTERCEPTION, because the two answer different questions and the interception
        // answers the wrong one at close range. See TryCommitRoomSweep.
        if (TryCommitRoomSweep()) return;

        freeRoam.Release();

        if (TryGetInterceptPoint(out Vector3 intercept))
        {
            HasIntercept = true;
            InterceptPoint = intercept;
            SetDestination(intercept);
            return;
        }

        HasIntercept = false;

        // No cut-off to be had — no belief, no heading, or nothing ahead it can win the race to.
        // A noise it can still hear is nonetheless better information than a blind sweep, so head
        // for that, nudged along the direction the target was last observed moving.
        FieldOfListening listening = nemesisStateManager.FieldOfListening;
        if (nemesisStateManager.HasAudioTarget && listening != null)
        {
            SetDestination(PredictedFrom(listening.LastKnownPosition));
            return;
        }

        SetDestination(PickSearchTarget());
    }

    /// <summary>
    /// Commits the search to sweeping the place it just watched the player disappear into, if
    /// that is what happened.
    ///
    /// THE PROBLEM THIS SOLVES. Losing sight of someone has two completely different shapes and
    /// this state used to answer both with a cut-off. Lose them across the level and cutting them
    /// off ahead of their heading is exactly right. Lose them because they stepped through a door
    /// five metres away and it is exactly wrong: TryGetHeading reads the trail of waypoints they
    /// walked past on the way — which are the CORRIDOR's — so the interception lands further down
    /// that corridor and the Nemesis jogs straight past the door it saw them go through. Then
    /// PickSearchTarget takes over, rolls over graph nodes, and if nobody placed a waypoint inside
    /// that room the Nemesis will never once look in it.
    ///
    /// THE THREE TESTS, and each of them refuses a different wrong commitment:
    ///
    ///   FROM SIGHT. A noise heard through a wall is not evidence of which room anybody is in, and
    ///   committing to sweep a room on the strength of one would have the Nemesis methodically
    ///   searching the wrong side of that wall while the player walks away. The one exception is a
    ///   noise inside a room it is ALREADY sweeping — see the branch below.
    ///
    ///   FRESH. An old sighting says where they were, not where they went. Sweeping the room
    ///   somebody was seen entering thirty seconds ago is how the Nemesis ends up committed to an
    ///   empty room while the player is two floors up.
    ///
    ///   CLOSE, MEASURED OVER THE NAVMESH. This is the test that actually separates the two
    ///   shapes. Straight-line distance would call a room close when it is on the other side of
    ///   the wall the Nemesis is standing against — the sighting four metres away and a
    ///   forty-metre walk — and there is no sense in which the Nemesis "watched them go in there"
    ///   if getting there means crossing the building.
    ///
    /// RoomCommitRange at 0 disables room sweeps entirely and restores the interception-first
    /// behaviour that shipped before this existed.
    /// </summary>
    private bool TryCommitRoomSweep()
    {
        SO_NemesisData data = nemesisStateManager.NemesisData;
        if (data == null) return false;

        float commitRange = data.RoomCommitRange;
        if (commitRange <= 0f) return false;

        if (!nemesisStateManager.TryGetBelief(out Vector3 belief, out bool fromSight)) return false;

        // A NOISE INSIDE THE ROOM ALREADY BEING SWEPT COUNTS TOO, and this branch is load-bearing
        // rather than a nicety. ShouldNoiseRetarget deliberately lets a noise inside the committed
        // area through — it is confirming the sweep, not contradicting it — and without this the
        // retarget it triggers would find a belief that is no longer from sight, refuse to
        // re-commit, and drop the Nemesis out of the room sweep and into an interception. Knocking
        // something over in the corner of the room it is searching would be a reliable way to make
        // it leave, which is the exact opposite of the intent.
        //
        // Evaluated before Commit below, while Anchor still refers to the sweep being replaced.
        bool insideActiveSweep = freeRoam.IsCommitted && freeRoam.Contains(belief);

        if (!fromSight && !insideActiveSweep) return false;

        if (nemesisStateManager.BeliefAge >= Mathf.Max(0.01f, data.SightCommitTime)) return false;

        Vector3 origin = nemesisStateManager.transform.position;

        // Measured over the NavMesh, not in a straight line. Infinity when there is no route at
        // all, which fails the comparison on its own and needs no special case.
        if (NemesisNav.PathDistanceOrInfinity(origin, belief) > commitRange) return false;

        freeRoam.Commit(belief, data.RoomSweepRadius);

        // The interception is not merely skipped, it is retired for this commitment: leaving
        // HasIntercept true would have the HUD and the gizmos drawing a cut-off point the search
        // is no longer heading for.
        HasIntercept = false;

        if (freeRoam.TryGetNextPoint(out Vector3 point))
        {
            SetDestination(point);
            return true;
        }

        // The area offered nothing reachable. Rather than stand in a room it cannot sweep, drop
        // the commitment and let the caller fall through to the interception as before.
        freeRoam.Release();
        return false;
    }

    /// <summary>
    /// The waypoint to cut the player off at: ahead of where they were going, and reachable
    /// before they could get there.
    ///
    /// THE DIFFERENCE FROM SEARCHING
    ///
    /// <see cref="PickSearchTarget"/> asks "where would I look for someone", and answers it by
    /// weighing where it last sensed you against where it has not been. This asks a different
    /// question: not "where haven't I looked" but "where can I be WAITING". It is the only part
    /// of the search that reasons about the player's travel time rather than its own, and it is
    /// the only one that can put the Nemesis somewhere before you get there.
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
    /// Where to look next: a weighted roll over the patrol waypoints, mixing everything the
    /// Nemesis actually knows.
    ///
    /// WHAT IT REPLACES. This used to be "the nearest waypoint I have not visited yet, measured
    /// from where I am standing". The last known position never entered the maths after the first
    /// destination, so the Nemesis peeled outward from the spot it lost you at in expanding rings
    /// - which is a fine way to search an empty room and a terrible way to find someone who kept
    /// walking. It searched where it WAS, not where you went.
    ///
    /// WHAT IT MIXES NOW, and none of it is knowledge it did not earn:
    ///
    ///   the LAST KNOWN POSITION   the only place it actually observed you
    ///   the PREDICTED position    that same place, carried forward along the heading it saw
    ///   what it has NOT swept     so it spreads out instead of re-walking one corridor
    ///   how long it takes to get  so a promising corner across the level does not win
    ///
    /// A ROLL AND NOT AN ARGMAX, same as everywhere else in this system: always walking to the
    /// single highest-scoring waypoint is indistinguishable from knowing where you are. Weighted
    /// tickets read as a good guess, which is what it is.
    ///
    /// COST: one path query per surviving candidate, capped by WaypointBiasSampleCount. It runs on
    /// arrival at a search point - and now, with SearchPauseTime, no more often than that.
    /// </summary>
    private Vector3 PickSearchTarget()
    {
        NemesisController controller = nemesisStateManager.NemesisController;
        NemesisRouteGraph graph = controller != null ? controller.RouteGraph : null;
        if (graph == null || !graph.IsBuilt) return GetRandomPointInNavMesh();

        Vector3 origin = nemesisStateManager.transform.position;
        if (!graph.TryGetComponentAt(origin, out int component)) return GetRandomPointInNavMesh();

        graph.CollectNodesInComponent(component, candidateBuffer);
        if (candidateBuffer.Count == 0) return GetRandomPointInNavMesh();

        SO_NemesisData data = nemesisStateManager.NemesisData;
        int sampleCount = data != null ? Mathf.Max(2, data.WaypointBiasSampleCount) : 8;

        // The anchor is the belief, and the prediction is that belief carried forward. With no
        // belief at all there is nothing to search around and the scatter is the honest answer.
        bool hasAnchor = nemesisStateManager.TryGetBelief(out Vector3 anchor);
        if (!hasAnchor) return GetRandomPointInNavMesh();

        Vector3 predicted = PredictedFrom(anchor);

        positionBuffer.Clear();
        for (int i = 0; i < candidateBuffer.Count; i++)
            positionBuffer.Add(graph.GetNode(candidateBuffer[i]).Position);

        // Prefiltered on min(near me, near the anchor) - keeping only what is near the Nemesis is
        // exactly the bug this method exists to fix.
        NemesisClusterPatrol.KeepClosest(candidateBuffer, positionBuffer, origin, true, anchor,
                                         sampleCount, sampledBuffer, keyBuffer);
        if (sampledBuffer.Count == 0) return GetRandomPointInNavMesh();

        float lastKnownBias = data != null ? data.SearchLastKnownBias : 3f;
        float predictionBias = data != null ? data.SearchPredictionBias : 2.5f;
        float falloff = data != null ? data.SearchBiasFalloff : 20f;
        float sweptPenalty = data != null ? data.SearchSweptPenalty : 0.15f;
        float speed = Mathf.Max(0.5f, nemesisStateManager.NemesisMovement.SearchSpeed);

        weightBuffer.Clear();

        for (int i = 0; i < sampledBuffer.Count; i++)
        {
            Vector3 candidate = graph.GetNode(sampledBuffer[i]).Position;

            float ownTime = NemesisNav.PathDistanceOrInfinity(origin, candidate) / speed;
            if (float.IsPositiveInfinity(ownTime))
            {
                weightBuffer.Add(0f);
                continue;
            }

            float weight = NemesisClusterPatrol.ProximityWeight(candidate, anchor,
                                                                lastKnownBias, falloff);

            weight *= NemesisClusterPatrol.ProximityWeight(candidate, predicted,
                                                           predictionBias, falloff);

            // Already looked there this time round. Reduced rather than removed: a search that
            // refuses to double back runs out of places to go and falls through to random
            // scatter, and doubling back is a thing people who are looking for you actually do.
            if (sweptNodes.Contains(sampledBuffer[i])) weight *= sweptPenalty;

            // Sooner is better, so a perfect corner on the far side of the floor loses to a decent
            // one nearby. +1 keeps a candidate it is standing on from dividing by zero.
            weight /= 1f + ownTime;

            weightBuffer.Add(weight);
        }

        int index = RouletteSelection.Roulette(weightBuffer);
        if (index < 0) return GetRandomPointInNavMesh();

        int node = sampledBuffer[index];
        sweptNodes.Add(node);

        return graph.GetNode(node).Position;
    }

    /// <summary>
    /// Nudges a remembered position forward along the direction the target was last observed
    /// moving.
    ///
    /// The maths is NemesisPursuit.PredictAhead, shared with the chase. This used to be a second
    /// implementation of it that was missing the dot guard, so a target last seen running TOWARDS
    /// the Nemesis had its predicted position pushed backwards past the Nemesis itself - and the
    /// search then set off away from the only place it had any reason to look.
    ///
    /// What stays local is the LEAD TIME. SearchLeadTime is deliberately a fraction of a second
    /// where the chase can afford more: the velocity comes from what the sensors actually saw (see
    /// <see cref="FieldOfView.LastKnownVelocity"/>), so a long lead on a search extrapolates a
    /// stale observation into a confident claim about somewhere nobody has been seen - and
    /// arriving ahead of the player from a guess reads as the game cheating rather than as the
    /// monster being sharp.
    /// </summary>
    private Vector3 PredictedFrom(Vector3 remembered)
    {
        SO_NemesisData data = nemesisStateManager.NemesisData;
        FieldOfView view = nemesisStateManager.FieldOfView;

        return NemesisPursuit.PredictAhead(nemesisStateManager.transform.position, remembered,
                                           view != null ? view.LastKnownVelocity : Vector3.zero,
                                           data != null ? data.SearchLeadTime : 0f);
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
