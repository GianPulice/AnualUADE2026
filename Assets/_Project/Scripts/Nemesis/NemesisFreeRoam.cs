using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Sweeps an AREA on the NavMesh itself, instead of walking from patrol waypoint to patrol
/// waypoint.
///
/// WHAT IT REPLACES
///
/// NemesisSearchingState.PickSearchTarget (removed 27/09) could only ever return a graph node's
/// position. The free NavMesh scatter beside it was reachable only when the graph was missing,
/// empty, or held no belief — an error path, not a movement mode. The consequence was the one the
/// game designer reported: a room with no waypoint inside it was a room the Nemesis could not
/// search, no matter how plainly it had just watched you walk into it. It stood in the corridor
/// outside, picked the nearest corridor waypoint, and left.
///
/// This is the other half of the movement dichotomy — see NemesisStateManager.MovementOf. Patrol
/// is NODE-BOUND: the waypoints are the route, and the designer's authored order is the point of
/// them. Search and pursuit are FREE ROAM: the waypoints are hints about where a person might be
/// worth looking for, and nothing more. The same waypoints, demoted from a cage to a set of
/// suggestions.
///
/// THE TWO GUARANTEES THE GRAPH USED TO PROVIDE, AND HOW THEY ARE RECOVERED
///
/// Dropping the graph drops two things that were free, and both have to be paid for explicitly or
/// this is a downgrade dressed as a feature:
///
///   REACHABILITY. Picking nodes out of one NavMesh island via TryGetComponentAt guaranteed the
///   destination was genuinely walkable-to. NavMesh.SamplePosition offers no such promise — it
///   returns the nearest surface, cheerfully including one across a chasm or on another floor —
///   and handing the agent an unreachable destination is precisely how it ends up pressed against
///   a wall with remainingDistance at zero, which NemesisPursuit's class comment describes as the
///   failure players actually notice. So every candidate is path-tested. It is not optional.
///
///   CONFINEMENT. A room is not a circle. Sampling a disc around the last sighting spills through
///   every doorway and half of the corridor outside it, and a "room sweep" that wanders back out
///   the way it came is not one. The wall test below is what makes the disc room-shaped.
///
/// WHY IT IS NOT A MonoBehaviour
///
/// Same shape as <see cref="NemesisPursuit"/>, which it sits beside: a plain object constructed
/// with the state manager and owned by the state that uses it. It needs no Update of its own — the
/// state ticks it — and nothing about it belongs on a GameObject.
///
/// SINCE 27/09 IT IS THE WHOLE SEARCH (plan §18, Fase 2B part 2). It used to be a special case —
/// "it watched you walk into that room" — beside a search that otherwise rolled over waypoints and
/// cut the player off at them. Now every search is a sweep of an area around the belief, so:
///   - NavMesh points come FIRST and waypoints are two extra candidates, not the head of the list.
///     With eight waypoints inside the disc the old order never sampled the NavMesh at all, which
///     was one of the ways the Nemesis ended up at "a node nearby" instead of where you went.
///   - The area can MOVE and GROW without forgetting what it swept (<see cref="Recenter"/>,
///     <see cref="Follow"/>, <see cref="Widen"/>): a footstep inside the room it is searching
///     used to recommit the sweep and wipe the memory.
///   - A point counts as swept when the Nemesis gets THERE (<see cref="MarkSwept"/>), not when it
///     is picked: a destination abandoned half way was never looked at.
///   - The anchor itself — the point the evidence came from — is visited first
///     (<see cref="IsAnchorPending"/>), and the disc around it after.
///   - It can tell "I have looked everywhere here" (<see cref="IsFullySwept"/>) from "there is
///     nowhere reachable here" (<see cref="HasCoverage"/> false).
/// </summary>
public sealed class NemesisFreeRoam
{
    private readonly NemesisStateManager stateManager;

    /// <summary>
    /// How far a candidate has to be from an already-swept point to count as somewhere new.
    ///
    /// Not on the SO because it is not a design value: it is the resolution of the sweep, and it
    /// only has to be coarse enough that two candidates a stride apart are not treated as two
    /// different places to look. The tuning that matters is RoomSweepRadius.
    /// </summary>
    private const float SweptRadius = 2.5f;

    /// <summary>
    /// Height above the floor at which the wall test is cast.
    ///
    /// Raised for the same reason NemesisPursuit.CanSeeFrom raises its own probe: sampled NavMesh
    /// points and the belief both sit at floor level, and a ray between two points on the floor
    /// scrapes along it and reports occlusion practically everywhere. Cast at ankle height this
    /// method rejects every candidate and the sweep degenerates to standing still.
    /// </summary>
    private const float ProbeHeight = 1f;

    /// <summary>
    /// How many sampling attempts to make per candidate slot before giving up on it.
    ///
    /// Capped rather than a do/while for the reason NemesisSearchingState.GetRandomPointInNavMesh
    /// documents: with the agent somewhere the sampler cannot satisfy, an uncapped loop span
    /// forever and hung Unity.
    /// </summary>
    private const int AttemptsPerSlot = 4;

    /// <summary>
    /// How far a candidate has to be from the Nemesis to be worth walking to at all. Comfortably
    /// above the agent's stopping distance, so a chosen point is one it has to travel to rather
    /// than one it has already arrived at.
    /// </summary>
    private const float MinTravel = 2f;

    /// <summary>
    /// Metres of height a candidate may sit above or below the centre and still be on its floor.
    ///
    /// Sampling a flat disc and snapping it with SamplePosition cheerfully returns the storey above
    /// or below where the two overlap (plan §16.2, C2 #3). Loose enough for a ramp or a few steps
    /// inside a room, well under a storey.
    ///
    /// Public because the search asks "is this the same place" with the same band: two definitions
    /// of "same floor" let evidence 1.5–2.5 m up count as inside a disc whose candidates it filtered
    /// out.
    /// </summary>
    public const float FloorBand = 1.5f;

    /// <summary>
    /// How many waypoints inside the area join the sampled points as candidates.
    ///
    /// A waypoint the designer put in a room is still a considered opinion about where someone would
    /// be, so it stays in the draw — as one or two tickets among the NavMesh points, never as the
    /// whole list.
    /// </summary>
    private const int WaypointCandidates = 2;

    /// <summary>How much wider the area gets each time it has been fully swept. The sweep's own
    /// resolution: one step is one more ring of new places to look.</summary>
    public const float WidenStep = SweptRadius;

    // Reused across calls. This runs once per arrival at a sweep point, which is often enough that
    // allocating five lists each time is worth avoiding, and rare enough that the path queries
    // below are affordable. Same pattern as NemesisPursuit's own buffers.
    private readonly List<Vector3> candidateBuffer = new List<Vector3>();
    private readonly List<float> weightBuffer = new List<float>();
    private readonly List<int> nodeBuffer = new List<int>();

    /// <summary>
    /// Where the sweep has already looked, as POSITIONS rather than graph node indices.
    ///
    /// NemesisSearchingState tracks this as a list of node indices, which it can do because every
    /// destination it produces is a node. Here most of them are not, so the memory has to be
    /// spatial: a candidate counts as swept if it is within <see cref="SweptRadius"/> of somewhere
    /// already visited.
    /// </summary>
    private readonly List<Vector3> sweptPoints = new List<Vector3>();

    private Vector3 anchor;
    private float radius;
    private bool committed;
    private bool exhausted;
    private bool fullySwept;
    private bool anchorPending;

    /// <summary>Whether a sweep is currently committed to an area.</summary>
    public bool IsCommitted => committed;

    /// <summary>
    /// The sweep still owes a visit to the anchor itself — the point the evidence came from — and
    /// <see cref="TryGetNextPoint"/> will offer it before anything else in the disc.
    ///
    /// WHY THE ANCHOR FIRST (playtest 27/09): the pick is a roll over the disc, weighted only a
    /// little towards the centre and a lot towards "sooner". The Nemesis ended up at the near edge of
    /// the area, metres short of where it heard or last saw the player, and read as not caring where
    /// they went — worst over long distances, where the disc is widest and the near edge furthest
    /// from the point. Walking to the point and then sweeping around it is what a person looking for
    /// someone does.
    ///
    /// Not for a noise from inside a hiding spot (D22): that point is the locker door.
    /// </summary>
    public bool IsAnchorPending => committed && anchorPending;

    /// <summary>The centre of the committed area — where the Nemesis believes you went. Drawn by
    /// NemesisGizmos: an invisible decision is an untunable one.</summary>
    public Vector3 Anchor => anchor;

    /// <summary>Radius of the committed area.</summary>
    public float Radius => radius;

    /// <summary>Points already swept this commitment, for the gizmos and the debug HUD.</summary>
    public IReadOnlyList<Vector3> SweptPoints => sweptPoints;

    /// <summary>
    /// False once the area has stopped yielding anywhere new to look, so the caller can widen the
    /// search or hand it back to the waypoint sweep rather than standing still.
    ///
    /// It is set by a failed <see cref="TryGetNextPoint"/> rather than computed, because "is there
    /// anywhere left" and "find me somewhere" are the same set of path queries and there is no
    /// reason to pay for them twice.
    /// </summary>
    public bool HasCoverage => committed && !exhausted;

    /// <summary>
    /// The last pick found reachable places to look, but every one of them was already swept: the
    /// area has been covered. Distinct from <see cref="HasCoverage"/> going false, which means there
    /// was nowhere reachable at all — covering an area and failing to get into it are not the same
    /// thing, and only the first one is "I looked everywhere here".
    ///
    /// Like HasCoverage, it is the answer of the last <see cref="TryGetNextPoint"/>, not a fresh
    /// query: the candidates are sampled, so this is an estimate that gets better with every pick.
    /// </summary>
    public bool IsFullySwept => committed && fullySwept;

    public NemesisFreeRoam(NemesisStateManager manager)
    {
        stateManager = manager;
    }

    private SO_NemesisData Data => stateManager.NemesisData;

    /// <summary>
    /// Commits the sweep to an area. Clears whatever the previous commitment had swept — a new
    /// anchor is new information, and carrying the old visited set into it would have the Nemesis
    /// skipping parts of a room it has never been in.
    /// </summary>
    public void Commit(Vector3 sweepAnchor, float sweepRadius) => Commit(sweepAnchor, sweepRadius, null, false);

    /// <summary>
    /// Commits the sweep to an area and to the ROOM the player was seen going into (see
    /// <see cref="NemesisRooms"/>). While that room still offers anywhere to look, it is the only
    /// place the sweep looks; the rest of the disc only comes into play once it runs dry.
    ///
    /// Why the room and not just the disc: the disc is centred on where the player was LAST SEEN,
    /// and when you lose someone going through a door, that is the doorway — from which the
    /// corridor outside is exactly as visible as the room. The wall test cannot tell them apart,
    /// and the corridor points competed on equal terms with the room the Nemesis watched you walk
    /// into. Null room: the disc alone, as before.
    ///
    /// <paramref name="visitAnchor"/>: go to the anchor itself before sweeping (see
    /// <see cref="IsAnchorPending"/>).
    /// </summary>
    public void Commit(Vector3 sweepAnchor, float sweepRadius, string enteredRoom, bool visitAnchor)
    {
        sweptPoints.Clear();
        Recenter(sweepAnchor, sweepRadius, enteredRoom, visitAnchor);
    }

    /// <summary>
    /// Moves the sweep to a new area WITHOUT forgetting what it has swept: the evidence came from
    /// somewhere else (plan §17.4, question 4 answered "no"). The swept points are positions, so
    /// they stay true wherever the area goes — re-walking a corner it looked at ten seconds ago just
    /// because the player was heard again is the "restarting on every noise" this replaces.
    /// </summary>
    public void Recenter(Vector3 sweepAnchor, float sweepRadius, string enteredRoom, bool visitAnchor)
    {
        anchor = sweepAnchor;
        radius = Mathf.Max(1f, sweepRadius);
        room = string.IsNullOrEmpty(enteredRoom) ? null : enteredRoom;
        committed = true;
        exhausted = false;
        fullySwept = false;
        anchorPending = visitAnchor;
    }

    /// <summary>
    /// Follows fresh evidence inside the area it is already sweeping (question 4 answered "yes"):
    /// the centre slides to it, nothing it has swept is forgotten and nothing restarts.
    ///
    /// The radius only ever GROWS here, to at least <paramref name="radiusAtLeast"/>: evidence less
    /// precise than the one the disc was sized for — a breath from inside a locker after a footstep
    /// in the open (D22) — has to widen it, or the search keeps pacing the small disc in front of the
    /// door. More precise evidence does not shrink it: it is the same place, and a disc that tightens
    /// on every footstep would drop what it was about to look at.
    ///
    /// The room is replaced: the player may have walked out of the one it saw them go into, and a
    /// room that no longer matches the evidence would keep pulling half the candidates back into it.
    ///
    /// The new point is owed a visit unless the sweep has already stood there: heard somewhere it has
    /// not looked yet, it goes to look. <paramref name="mayVisitAnchor"/> false (a noise from inside a
    /// hiding spot, D22) cancels any visit instead: the new point is the locker door.
    /// </summary>
    public void Follow(Vector3 sweepAnchor, float radiusAtLeast, string enteredRoom, bool mayVisitAnchor)
    {
        if (!committed) return;

        anchor = sweepAnchor;
        room = string.IsNullOrEmpty(enteredRoom) ? null : enteredRoom;
        anchorPending = mayVisitAnchor && !WasSwept(sweepAnchor);

        if (radiusAtLeast > radius)
        {
            radius = radiusAtLeast;
            exhausted = false;
            fullySwept = false;
        }
    }

    /// <summary>
    /// Opens a covered area one <see cref="WidenStep"/> wider, up to <paramref name="maxRadius"/>.
    /// Returns false when it is already at the maximum — then the area really has been searched.
    /// </summary>
    public bool Widen(float maxRadius)
    {
        if (!committed || radius >= maxRadius) return false;

        radius = SearchSweepRules.Widen(radius, WidenStep, maxRadius);
        exhausted = false;
        fullySwept = false;
        return true;
    }

    /// <summary>
    /// Records a place the Nemesis actually stood and looked around at. Called on ARRIVAL: marking
    /// on the pick counted a point it then walked away from as searched.
    /// </summary>
    public void MarkSwept(Vector3 point)
    {
        if (!committed) return;

        // Standing on the anchor is the visit it owed, whichever pick brought it there.
        if (anchorPending && Vector3.SqrMagnitude(point - anchor) < SweptRadius * SweptRadius)
            anchorPending = false;

        if (WasSwept(point)) return;
        sweptPoints.Add(point);
    }

    /// <summary>Drops the commitment. Called on leaving the state.</summary>
    public void Release()
    {
        committed = false;
        exhausted = false;
        fullySwept = false;
        anchorPending = false;
        room = null;
        sweptPoints.Clear();
    }

    /// <summary>
    /// The anchor, when a visit to it is still owed and it can be walked to. Settles the debt without
    /// a walk when the Nemesis is already standing there (marked swept, as an arrival would) or when
    /// it cannot get there — then the sweep around it is all there is to do.
    /// </summary>
    private bool TryTakeAnchor(Vector3 origin, out Vector3 point)
    {
        point = anchor;
        if (!anchorPending) return false;

        if (Vector3.SqrMagnitude(anchor - origin) < MinTravel * MinTravel)
        {
            MarkSwept(anchor);
            return false;
        }

        if (NemesisNav.IsReachable(origin, anchor)) return true;

        anchorPending = false;
        return false;
    }

    private string room;

    /// <summary>The room the sweep is prioritising, or null when it is sweeping the disc alone.
    /// For the debug HUD and the gizmos.</summary>
    public string Room => room;

    /// <summary>
    /// The next place to look inside the committed area.
    ///
    /// WHAT IT MIXES:
    ///
    ///   SAMPLED NAVMESH POINTS come first. They are what lets the sweep go wherever the player
    ///   could be — a room nobody ever put a waypoint in included — which is the entire reason this
    ///   class exists.
    ///
    ///   A COUPLE OF WAYPOINTS INSIDE THE AREA join them. A waypoint the designer placed in this
    ///   room is a considered opinion about where someone would hide in it, so it stays in the
    ///   draw. It used to be offered FIRST, filling the list before any sampling: with enough
    ///   waypoints in the disc the sweep was a waypoint tour (plan §18.1).
    ///
    /// Both go through the same filters and the same weighting afterwards: a waypoint gets no
    /// special treatment, it competes on the same terms.
    ///
    /// It does NOT mark the pick as swept: the caller does that on arrival (<see cref="MarkSwept"/>).
    ///
    /// A ROLL AND NOT AN ARGMAX, like every other selection in this system: always walking to the
    /// single best-scoring point is indistinguishable from knowing where you are.
    ///
    /// COST: one path query per surviving candidate, capped by WaypointBiasSampleCount. That is
    /// affordable once per arrival — which, with SearchPauseTime, is no more than once a second or
    /// so — and a frame hitch if it ever leaks into a per-frame tick. Keep it out of the tick.
    /// </summary>
    /// <returns>false when the area has nothing left to offer; <see cref="HasCoverage"/> then
    /// goes false too.</returns>
    public bool TryGetNextPoint(out Vector3 point)
    {
        point = Vector3.zero;

        if (!committed) return false;

        Vector3 origin = stateManager.transform.position;

        // The point the evidence came from, before any of the disc around it (IsAnchorPending).
        if (TryTakeAnchor(origin, out point))
        {
            exhausted = false;
            fullySwept = false;
            return true;
        }

        SO_NemesisData data = Data;
        int sampleCount = data != null ? Mathf.Max(2, data.WaypointBiasSampleCount) : 8;

        CollectCandidates(sampleCount);

        fullySwept = false;

        if (candidateBuffer.Count == 0)
        {
            exhausted = true;
            return false;
        }

        float speed = Mathf.Max(0.5f, stateManager.NemesisMovement != null
            ? stateManager.NemesisMovement.SearchSpeed
            : 2.7f);

        float sweptPenalty = data != null ? data.SearchSweptPenalty : 0.15f;

        weightBuffer.Clear();
        int kept = 0;
        int unswept = 0;

        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            Vector3 candidate = candidateBuffer[i];

            // Somewhere it is already standing is not somewhere to go. Without this the weighting
            // actively favours it — the arrival-time term peaks at zero distance — so the first
            // pick after committing to a room the Nemesis is already inside is its own feet, the
            // agent reports HasArrived immediately, and the sweep spins on the spot burning path
            // queries. The waypoint sweep never had this problem because a node it was standing on
            // was already in its swept set.
            if (Vector3.SqrMagnitude(candidate - origin) < MinTravel * MinTravel)
            {
                weightBuffer.Add(0f);
                continue;
            }

            // The reachability guarantee the graph used to give for free. Infinity here means the
            // sampler found a surface the agent cannot actually walk to — another island, the far
            // side of a drop — and weighting it at zero is what stops the agent setting off
            // towards it and stalling against the geometry in between.
            float ownTime = NemesisNav.PathDistanceOrInfinity(origin, candidate) / speed;
            if (float.IsPositiveInfinity(ownTime))
            {
                weightBuffer.Add(0f);
                continue;
            }

            // Closer to the middle of the room is a better place to look than its edge, and the
            // shared helper measures over the NavMesh so "close" means close to walk to.
            float weight = NemesisClusterPatrol.ProximityWeight(candidate, anchor, 2f, radius);

            // Already looked there. Reduced rather than removed: a search that refuses to double
            // back runs out of places to go, and doubling back is a thing people looking for you
            // actually do. Counted, so the caller can tell a covered area from a fresh one.
            if (WasSwept(candidate)) weight *= sweptPenalty;
            else unswept++;

            // Sooner is better. +1 so a candidate it is standing on does not divide by zero.
            weight /= 1f + ownTime;

            weightBuffer.Add(weight);
            kept++;
        }

        if (kept == 0)
        {
            exhausted = true;
            return false;
        }

        // Somewhere reachable to go, and all of it already looked at: the area is covered. The pick
        // still happens (doubling back), but the caller now knows it could widen instead.
        fullySwept = unswept == 0;

        PreferEnteredRoom();

        int index = RouletteSelection.Roulette(weightBuffer);
        if (index < 0 || weightBuffer[index] <= 0f)
        {
            exhausted = true;
            return false;
        }

        point = candidateBuffer[index];
        return true;
    }

    /// <summary>
    /// Strict priority for the room the player was seen entering: if any live candidate in it is
    /// still UNSWEPT, every candidate outside it is zeroed for this pick. A roll between the two
    /// would still send the Nemesis out into the corridor a fair share of the time, which is the
    /// behaviour the rule exists to stop.
    ///
    /// Only unswept ones hold the priority. Counting swept ones too kept the Nemesis pacing a room it
    /// had finished for as long as any point in it was reachable (plan §18.1); once the room is
    /// covered, this does nothing and the disc takes over.
    /// </summary>
    private void PreferEnteredRoom()
    {
        if (room == null) return;

        bool anyInRoom = false;
        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            if (weightBuffer[i] <= 0f || WasSwept(candidateBuffer[i])) continue;
            if (NemesisRooms.TryGetRoom(candidateBuffer[i], out string r) && r == room) { anyInRoom = true; break; }
        }
        if (!anyInRoom) return;

        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            if (weightBuffer[i] <= 0f) continue;
            if (!NemesisRooms.TryGetRoom(candidateBuffer[i], out string r) || r != room) weightBuffer[i] = 0f;
        }
    }

    // -- Candidates ----------------------------------------------------------

    /// <summary>
    /// Fills <see cref="candidateBuffer"/> with places inside the area worth considering: sampled
    /// NavMesh points first, then up to <see cref="WaypointCandidates"/> waypoints that happen to be
    /// in it.
    /// </summary>
    private void CollectCandidates(int sampleCount)
    {
        candidateBuffer.Clear();

        AddSampledPoints(sampleCount);
        AddWaypointsInArea(candidateBuffer.Count + WaypointCandidates);
    }

    /// <summary>
    /// A few of the waypoints the designer put inside this area, on top of the sampled points.
    ///
    /// Restricted to the Nemesis's own NavMesh island, which is the graph's one real guarantee and
    /// worth keeping even here — and to the floor of the area: a waypoint four metres away through a
    /// floor slab passes the radius test and is not in the room.
    /// </summary>
    private void AddWaypointsInArea(int upTo)
    {
        NemesisController controller = stateManager.NemesisController;
        NemesisRouteGraph graph = controller != null ? controller.RouteGraph : null;
        if (graph == null || !graph.IsBuilt) return;

        if (!graph.TryGetComponentAt(stateManager.transform.position, out int component)) return;

        graph.CollectNodesInComponent(component, nodeBuffer);

        for (int i = 0; i < nodeBuffer.Count && candidateBuffer.Count < upTo; i++)
        {
            Vector3 position = graph.GetNode(nodeBuffer[i]).Position;

            if (!SearchSweepRules.IsInside(anchor, radius, position, FloorBand)) continue;
            if (IsOutsideSweep(position)) continue;
            if (IsDuplicate(position)) continue;

            candidateBuffer.Add(position);
        }
    }

    /// <summary>
    /// Points on the NavMesh inside the area, sampled at random.
    ///
    /// HORIZONTAL ONLY, for the reason NemesisSearchingState.GetRandomPointInNavMesh documents:
    /// Random.onUnitSphere also varies Y and throws candidates above and below the floor, which
    /// SamplePosition then snaps to whatever surface happens to be nearest — including the storey
    /// below.
    ///
    /// Sampled inside the disc rather than on its rim so the middle of the room is covered too. A
    /// sweep that only ever visits the perimeter of where it saw you go is a strange thing to
    /// watch from a hiding place in the middle of it.
    /// </summary>
    private void AddSampledPoints(int sampleCount)
    {
        // With an entered room, the first half of the free slots only accepts points IN it: the
        // disc is centred on the doorway, so an unbiased scatter lands half of its points in the
        // corridor and the room it is meant to search gets two or three candidates.
        int firstFree = candidateBuffer.Count;
        int roomSlots = room != null ? (sampleCount - firstFree + 1) / 2 : 0;

        for (int slot = firstFree; slot < sampleCount; slot++)
        {
            bool inRoomOnly = slot - firstFree < roomSlots;
            int attempts = inRoomOnly ? AttemptsPerSlot * 3 : AttemptsPerSlot;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle * radius;
                Vector3 raw = anchor + new Vector3(circle.x, 0f, circle.y);

                if (!NavMesh.SamplePosition(raw, out NavMeshHit hit, SweptRadius, NemesisNav.AreaMask))
                    continue;

                // The snap can land on the storey above or below where the two overlap.
                if (Mathf.Abs(hit.position.y - anchor.y) > FloorBand) continue;

                if (inRoomOnly && (!NemesisRooms.TryGetRoom(hit.position, out string r) || r != room)) continue;
                if (IsOutsideSweep(hit.position)) continue;
                if (IsDuplicate(hit.position)) continue;

                candidateBuffer.Add(hit.position);
                break;
            }
        }
    }

    /// <summary>Whether a candidate is close enough to one already collected that evaluating both
    /// would only spend a path query to offer the same place twice.</summary>
    private bool IsDuplicate(Vector3 candidate)
    {
        const float MinSeparation = 1.5f;

        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            if (Vector3.SqrMagnitude(candidateBuffer[i] - candidate) < MinSeparation * MinSeparation)
                return true;
        }

        return false;
    }

    // -- The room test -------------------------------------------------------

    /// <summary>
    /// Whether a point is on the far side of a wall from the anchor — which is this system's
    /// stand-in for "in a different room".
    ///
    /// THIS IS WHAT MAKES THE DISC ROOM-SHAPED. There are no authored room volumes anywhere in the
    /// project, so "the room the player ran into" has to be derived, and the cheapest honest
    /// derivation is: everything within the radius that the sighting can see across. A doorway
    /// stays open (you can see through it), the corridor behind the wall does not. It is an
    /// approximation and it will treat an L-shaped room as two, which is a failure mode worth
    /// having — the Nemesis sweeping the half of the room it watched you enter is still the right
    /// behaviour.
    ///
    /// Borrows FieldOfListening's obstacle mask rather than adding a seventh "what is solid" mask
    /// to the project, joining the capture check, the spawn-point visibility test, the stuck
    /// escape and NemesisPursuit.CanSeeFrom. Changing that mask changes all of them.
    /// </summary>
    /// <summary>
    /// Whether a point is outside what this sweep is searching. A point on the floor of the entered
    /// room is inside by definition — the floor says so, which is better evidence than a line of
    /// sight from the doorway, and it lets the sweep reach the corner of an L-shaped room that the
    /// doorway cannot see into. Anything else still has to pass the wall test.
    /// </summary>
    private bool IsOutsideSweep(Vector3 point)
    {
        if (room != null && NemesisRooms.TryGetRoom(point, out string r) && r == room) return false;
        return IsBehindWall(point);
    }

    private bool IsBehindWall(Vector3 point)
    {
        FieldOfListening listening = stateManager.FieldOfListening;

        // No way to test it. Vetoing every candidate would leave the sweep with nowhere to go at
        // all, which is strictly worse than an unclipped disc.
        if (listening == null) return false;

        return listening.IsOccludedByWall(anchor + Vector3.up * ProbeHeight,
                                          point + Vector3.up * ProbeHeight);
    }

    private bool WasSwept(Vector3 candidate)
    {
        for (int i = 0; i < sweptPoints.Count; i++)
        {
            if (Vector3.SqrMagnitude(sweptPoints[i] - candidate) < SweptRadius * SweptRadius)
                return true;
        }

        return false;
    }
}
