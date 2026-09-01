using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Sweeps an AREA on the NavMesh itself, instead of walking from patrol waypoint to patrol
/// waypoint.
///
/// WHAT IT REPLACES
///
/// NemesisSearchingState.PickSearchTarget can only ever return a graph node's position. The free
/// NavMesh scatter beside it is reachable only when the graph is missing, empty, or holds no
/// belief — that is, it is an error path, not a movement mode. The consequence is the one the game
/// designer reported: a room with no waypoint inside it is a room the Nemesis cannot search, no
/// matter how plainly it just watched you walk into it. It stands in the corridor outside, picks
/// the nearest corridor waypoint, and leaves.
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

    /// <summary>Whether a sweep is currently committed to an area.</summary>
    public bool IsCommitted => committed;

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
    public void Commit(Vector3 sweepAnchor, float sweepRadius)
    {
        anchor = sweepAnchor;
        radius = Mathf.Max(1f, sweepRadius);
        committed = true;
        exhausted = false;
        sweptPoints.Clear();
    }

    /// <summary>Drops the commitment. Called on leaving the state, and when the area runs dry.
    /// </summary>
    public void Release()
    {
        committed = false;
        exhausted = false;
        sweptPoints.Clear();
    }

    /// <summary>Whether a position falls inside the committed area, walls included. Used by the
    /// search to decide whether a noise is confirming the sweep or contradicting it.</summary>
    public bool Contains(Vector3 point)
    {
        if (!committed) return false;
        if (Vector3.SqrMagnitude(point - anchor) > radius * radius) return false;

        return !IsBehindWall(point);
    }

    /// <summary>
    /// The next place to look inside the committed area.
    ///
    /// WHAT IT MIXES, and the order matters because the first source is the one that makes this
    /// "supported by the waypoints but not restricted by them" rather than "ignores the waypoints":
    ///
    ///   WAYPOINTS INSIDE THE AREA come first. A waypoint the designer placed in this room is a
    ///   considered opinion about where someone would hide in it, and throwing that away in the
    ///   name of moving freely would be discarding the level design to prove a point.
    ///
    ///   SAMPLED NAVMESH POINTS fill the rest. They are what lets the sweep enter a room nobody
    ///   ever put a waypoint in, which is the entire reason this class exists.
    ///
    /// Both go through the same two filters and the same weighting afterwards, so a waypoint gets
    /// no special treatment beyond being offered first — it competes on the same terms.
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

        SO_NemesisData data = Data;
        int sampleCount = data != null ? Mathf.Max(2, data.WaypointBiasSampleCount) : 8;

        CollectCandidates(sampleCount);

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

            // Already looked there. Reduced rather than removed, for the reason
            // NemesisSearchingState gives about its own swept set: a search that refuses to double
            // back runs out of places to go, and doubling back is a thing people looking for you
            // actually do.
            if (WasSwept(candidate)) weight *= sweptPenalty;

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

        int index = RouletteSelection.Roulette(weightBuffer);
        if (index < 0 || weightBuffer[index] <= 0f)
        {
            exhausted = true;
            return false;
        }

        point = candidateBuffer[index];
        sweptPoints.Add(point);

        return true;
    }

    // -- Candidates ----------------------------------------------------------

    /// <summary>
    /// Fills <see cref="candidateBuffer"/> with places inside the area worth considering: the
    /// waypoints that happen to be in it first, then sampled NavMesh points to make up the number.
    /// </summary>
    private void CollectCandidates(int sampleCount)
    {
        candidateBuffer.Clear();

        AddWaypointsInArea(sampleCount);
        AddSampledPoints(sampleCount);
    }

    /// <summary>
    /// The waypoints the designer put inside this area.
    ///
    /// Restricted to the Nemesis's own NavMesh island, which is the graph's one real guarantee and
    /// worth keeping even here — a waypoint four metres away through a floor slab passes the
    /// radius test and is not in the room.
    /// </summary>
    private void AddWaypointsInArea(int sampleCount)
    {
        NemesisController controller = stateManager.NemesisController;
        NemesisRouteGraph graph = controller != null ? controller.RouteGraph : null;
        if (graph == null || !graph.IsBuilt) return;

        if (!graph.TryGetComponentAt(stateManager.transform.position, out int component)) return;

        graph.CollectNodesInComponent(component, nodeBuffer);

        float sqrRadius = radius * radius;

        for (int i = 0; i < nodeBuffer.Count && candidateBuffer.Count < sampleCount; i++)
        {
            Vector3 position = graph.GetNode(nodeBuffer[i]).Position;

            if (Vector3.SqrMagnitude(position - anchor) > sqrRadius) continue;
            if (IsBehindWall(position)) continue;

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
        for (int slot = candidateBuffer.Count; slot < sampleCount; slot++)
        {
            for (int attempt = 0; attempt < AttemptsPerSlot; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle * radius;
                Vector3 raw = anchor + new Vector3(circle.x, 0f, circle.y);

                if (!NavMesh.SamplePosition(raw, out NavMeshHit hit, SweptRadius, NemesisNav.AreaMask))
                    continue;

                if (IsBehindWall(hit.position)) continue;
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
