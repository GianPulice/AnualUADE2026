using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Tier 3.1: owns the Nemesis's patrol routes/zones and where it spawns in.
///
/// Sits next to <see cref="NemesisStateManager"/> on the Nemesis root and is referenced back from
/// it (<see cref="NemesisStateManager.NemesisController"/>), the same way the FSM states reach
/// FieldOfView/FieldOfListening/NavAgent — through the state manager, never by holding their own
/// direct reference. <see cref="NemesisPatrolState"/> is the only state that talks to this class.
///
/// Responsibilities:
///   - Holds every <see cref="NemesisRoute"/> the Nemesis can patrol, each with a weight (how
///     often it gets picked, i.e. "frequency of appearance per zone") and a locked/unlocked gate.
///   - Merges every unlocked route into a <see cref="NemesisRouteGraph"/> and works out which
///     waypoints are genuinely reachable from where it is standing. The Nemesis is not locked
///     inside whichever route it drew: it can borrow a waypoint from another route and adopt it,
///     which is how it changes floor.
///   - Picks the active route and walks it, including the semi-random reverse/skip variation
///     rolled each time Patrolling is (re)entered, and now also a bias towards the zone where it
///     believes the player is — measured over the NavMesh, not in a straight line.
///   - Unlocks routes on demand (<see cref="UnlockRoute"/>), for whatever progress-tracking
///     system decides a new zone should open up — this class does not know or care what that
///     condition is.
///   - Picks where the Nemesis spawns in (<see cref="ChooseSpawnPoint"/>), the public entry point
///     Tier 3.2 calls from its own pass.
/// </summary>
[RequireComponent(typeof(NemesisStateManager))]
public class NemesisController : MonoBehaviour
{
    [Header("Routes")]
    [Tooltip("Every patrol route/zone the Nemesis can be assigned to. The active one is picked " +
             "by weight among the currently unlocked routes each time Patrolling starts.")]
    [SerializeField] private List<NemesisRoute> routes = new List<NemesisRoute>();

    [Header("Activation")]
    [Tooltip("Puzzle that wakes the Nemesis up. Until it is solved the Nemesis is dormant: " +
             "invisible, no navigation, no senses. On completion it picks a spawn point and " +
             "starts patrolling.\n\n" +
             "Must match the PuzzleId on the puzzle's SO_PuzzleData / SO_ValvePuzzleData / etc. " +
             "Leave empty to have the Nemesis active from the moment you hit Play (the old " +
             "behaviour, where it starts wherever it happens to be placed in the scene).")]
    [SerializeField] private string activatedByPuzzleId;

    [Header("Spawn points")]
    [Tooltip("Candidate points for ChooseSpawnPoint(). The farthest one outside the player's " +
             "line of sight is picked.")]
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

    [Tooltip("Raised by ChooseSpawnPoint() when every configured spawn point is visible to the " +
             "player, right before it warps the Nemesis in anyway. Hook a fade-to-black (or " +
             "similar) transition here.\n\n" +
             "PENDING (Tier 3.1): there is no fade system in the project yet, so this fires and " +
             "the warp still happens the same frame — see the ChooseSpawnPoint doc comment.")]
    [SerializeField] private UnityEvent onAllSpawnPointsVisible;

    [SerializeField] private NemesisStateManager stateManager;

    // ── Active-route traversal state ───────────────────────────────────────
    private NemesisRoute currentRoute;
    private int currentWaypointIndex;
    private int direction = 1;       // +1 walks the route forward, -1 walks it backward.
    private bool pendingSkip;        // Consumed by the first AdvanceToNextWaypoint() of a cycle.
    private float replanTimer;

    /// <summary>Merged set of every unlocked route, with the NavMesh islands already resolved.
    /// Only rebuilt when the unlocked set changes.</summary>
    private readonly NemesisRouteGraph routeGraph = new NemesisRouteGraph();

    // Reused across calls so AllUnlockedWaypoints does not allocate a new list every time the
    // Nemesis gets stuck or needs to reposition after a capture.
    private readonly List<Transform> flatWaypointsBuffer = new List<Transform>();
    private readonly List<Transform> spawnPointsBuffer = new List<Transform>();

    // Waypoint-selection buffers. Reused because this runs on every waypoint arrival and every
    // replan, and it is not worth allocating each time.
    private readonly List<int> candidateBuffer = new List<int>();
    private readonly List<int> sampledBuffer = new List<int>();
    private readonly List<float> weightBuffer = new List<float>();
    private readonly List<float> prefilterKeyBuffer = new List<float>();

    /// <summary>The waypoint the Nemesis should currently be walking towards, or null if no
    /// route is active (nothing unlocked yet, or no routes configured at all).</summary>
    public Transform CurrentWaypoint
    {
        get
        {
            if (currentRoute == null) return null;
            IReadOnlyList<Transform> waypoints = currentRoute.Waypoints;
            if (waypoints.Count == 0) return null;

            return waypoints[Mathf.Clamp(currentWaypointIndex, 0, waypoints.Count - 1)];
        }
    }

    /// <summary>The route being patrolled right now, or null. Debug/gizmos only.</summary>
    public NemesisRoute CurrentRoute => currentRoute;

    /// <summary>The merged route set. Public so an editor tool can inspect it without duplicating
    /// the build.</summary>
    public NemesisRouteGraph RouteGraph => routeGraph;

    /// <summary>Every waypoint across every unlocked route, flattened. Used for the "any
    /// waypoint will do" cases: post-capture repositioning and stuck-escape.</summary>
    public IReadOnlyList<Transform> AllUnlockedWaypoints
    {
        get
        {
            flatWaypointsBuffer.Clear();
            for (int i = 0; i < routes.Count; i++)
            {
                NemesisRoute route = routes[i];
                if (route == null || !route.IsUnlocked) continue;
                flatWaypointsBuffer.AddRange(route.Waypoints);
            }
            return flatWaypointsBuffer;
        }
    }

    /// <summary>
    /// Every configured spawn point, nulls filtered out (an inspector list almost always carries
    /// a couple of empty slots). Used by the post-capture reposition, which wants any of them at
    /// random — <see cref="ChooseSpawnPoint"/> picks the farthest hidden one instead, which is
    /// right for the Nemesis's first arrival and wrong for every capture after it.
    /// </summary>
    public IReadOnlyList<Transform> SpawnPoints
    {
        get
        {
            spawnPointsBuffer.Clear();
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (spawnPoints[i] != null) spawnPointsBuffer.Add(spawnPoints[i]);
            }
            return spawnPointsBuffer;
        }
    }

    public UnityEvent OnAllSpawnPointsVisible => onAllSpawnPointsVisible;

    /// <summary>Puzzle that wakes the Nemesis up, or empty/null when it starts active.
    /// Read by <see cref="NemesisStateManager"/>, which owns the dormant/awake gate.</summary>
    public string ActivatedByPuzzleId => activatedByPuzzleId;

    private SO_NemesisData Data => stateManager != null ? stateManager.NemesisData : null;

    private void Awake()
    {
        if (stateManager == null) stateManager = GetComponent<NemesisStateManager>();
    }

    // ── Patrol routing (called by NemesisPatrolState) ──────────────────────

    /// <summary>
    /// Called once every time Patrolling is (re)entered, and additionally every
    /// RouteReplanInterval seconds while it keeps patrolling (see <see cref="TickPatrol"/>).
    ///
    /// It rebuilds the merged set when needed, picks the active route among the unlocked ones that
    /// have at least one waypoint reachable from here — weighted by their inspector weight and by
    /// how close they are to where it believes the player is — and also picks which waypoint of
    /// that route to start from, instead of always going back to index 0.
    /// </summary>
    public void BeginPatrolCycle()
    {
        replanTimer = 0f;

        routeGraph.Rebuild(routes);

        SO_NemesisData data = Data;
        float reverseChance = data != null ? data.RouteReverseChance : 0f;
        float skipChance = data != null ? data.RouteSkipWaypointChance : 0f;

        direction = Random.value < reverseChance ? -1 : 1;
        pendingSkip = Random.value < skipChance;

        // The NavMesh island the Nemesis is standing on. Everything picked from here on comes out
        // of this island, which is what guarantees it is genuinely reachable.
        if (!routeGraph.TryGetComponentAt(transform.position, out int component))
        {
            // Outside every known island: either off the NavMesh, or in an area with no waypoints.
            // The state manager's stuck-escape is what resolves that; here we only keep whatever
            // route there was, so it is not left without a destination.
            FallBackToWeightedRouteWithoutGraph();
            return;
        }

        routeGraph.CollectNodesInComponent(component, candidateBuffer);
        if (candidateBuffer.Count == 0)
        {
            FallBackToWeightedRouteWithoutGraph();
            return;
        }

        int picked = PickWeightedNode(candidateBuffer, transform.position);
        if (picked < 0)
        {
            FallBackToWeightedRouteWithoutGraph();
            return;
        }

        AdoptNode(routeGraph.GetNode(picked));
    }

    /// <summary>
    /// Counts down to the next replan. Called every frame by <see cref="NemesisPatrolState"/>.
    ///
    /// It exists because BeginPatrolCycle only ran on ENTERING Patrolling: if the Nemesis patrols
    /// for three minutes without being interrupted, the player bias was computed once, at the
    /// start, and the feature feels dead.
    /// </summary>
    public void TickPatrol(float deltaTime)
    {
        SO_NemesisData data = Data;
        float interval = data != null ? data.RouteReplanInterval : 0f;
        if (interval <= 0f) return;

        replanTimer += deltaTime;
        if (replanTimer < interval) return;

        BeginPatrolCycle();
    }

    /// <summary>
    /// Moves the traversal on to the next waypoint, honoring the current direction and consuming
    /// the pending skip (if this cycle rolled one). Called by NemesisPatrolState once it has
    /// waited out PatrolWaypointWaitTime at <see cref="CurrentWaypoint"/>.
    ///
    /// With probability CrossRouteTransferChance, instead of stepping in order it jumps to a
    /// waypoint on another unlocked and reachable route, and adopts that route from there. That
    /// jump is what lets it change floor without waiting for the route roll to hand it the upper
    /// one: the route is still an ordered polyline (the gizmo does not lie), it just stops being
    /// a cage.
    /// </summary>
    public void AdvanceToNextWaypoint()
    {
        if (TryTransferToAnotherRoute()) return;

        if (currentRoute == null) return;

        int count = currentRoute.Waypoints.Count;
        if (count == 0) return;

        int step = direction;
        if (pendingSkip)
        {
            step *= 2;
            pendingSkip = false;
        }

        currentWaypointIndex = Wrap(currentWaypointIndex + step, count);
    }

    private static int Wrap(int index, int count) => ((index % count) + count) % count;

    /// <summary>Hands the traversal over to the chosen node: adopts its route and its index, so
    /// the sequential walk carries on from there.</summary>
    private void AdoptNode(NemesisRouteGraph.Node node)
    {
        if (!node.IsValid) return;

        currentRoute = node.Route;
        currentWaypointIndex = Mathf.Clamp(node.IndexInRoute, 0,
                                           Mathf.Max(0, node.Route.Waypoints.Count - 1));
    }

    // ── Cross-route transfer ────────────────────────────────────────────────

    /// <summary>
    /// Rolls the transfer chance and, if it lands, jumps to a waypoint on another route.
    /// </summary>
    /// <returns>true when a jump happened (and therefore no sequential step is due).</returns>
    private bool TryTransferToAnotherRoute()
    {
        SO_NemesisData data = Data;
        float chance = data != null ? data.CrossRouteTransferChance : 0f;
        if (chance <= 0f || Random.value >= chance) return false;

        routeGraph.Rebuild(routes);
        if (!routeGraph.TryGetComponentAt(transform.position, out int component)) return false;

        // excludeRoute: waypoints from OTHER routes are what is being asked for. Staying on the
        // current one is already what the sequential step does, and rolling for it here would only
        // break the polyline's ordering.
        routeGraph.CollectNodesInComponent(component, candidateBuffer, currentRoute);
        if (candidateBuffer.Count == 0) return false;

        int picked = PickWeightedNode(candidateBuffer, transform.position);
        if (picked < 0) return false;

        AdoptNode(routeGraph.GetNode(picked));
        pendingSkip = false;   // The jump is already this cycle's variation.
        return true;
    }

    // ── Weighted waypoint selection ─────────────────────────────────────────

    /// <summary>
    /// Weighted roll among the candidate nodes. Each one's weight is its route's weight multiplied
    /// by the player bias.
    ///
    /// It stays a roll and not an argmax on purpose: "the zone the player is in gets more tickets"
    /// reads as the Nemesis prowling around you; "it always goes where you are" reads as it seeing
    /// you through walls.
    /// </summary>
    /// <returns>Node index into the graph, or -1 when there was nothing usable.</returns>
    private int PickWeightedNode(List<int> candidates, Vector3 origin)
    {
        if (candidates.Count == 0) return -1;

        bool hasBelief = TryGetPlayerBeliefPosition(out Vector3 belief);

        SO_NemesisData data = Data;
        int sampleCount = data != null ? Mathf.Max(2, data.WaypointBiasSampleCount) : 8;

        PrefilterCandidates(candidates, origin, hasBelief, belief, sampleCount);
        if (sampledBuffer.Count == 0) return -1;

        float biasStrength = data != null ? Mathf.Max(1f, data.RoutePlayerBiasStrength) : 1f;
        float falloff = data != null ? Mathf.Max(1f, data.RoutePlayerBiasFalloff) : 1f;

        // How much the belief is still worth. Without this the bias is memoryless in the worst
        // sense: a sighting from ten minutes ago pulls the patrol exactly as hard as one from two
        // seconds ago, so the Nemesis keeps circling a room the player left long ago and the
        // pursuit never lets go of a stale idea. Decayed, a fresh sighting dominates the roll and
        // an old one fades back into the designer's route weights, which is where a patrol should
        // end up when it genuinely does not know.
        biasStrength = Mathf.Lerp(1f, biasStrength, BeliefFreshness());

        weightBuffer.Clear();
        float total = 0f;

        for (int i = 0; i < sampledBuffer.Count; i++)
        {
            NemesisRouteGraph.Node node = routeGraph.GetNode(sampledBuffer[i]);
            float weight = node.Route != null ? Mathf.Max(0f, node.Route.Weight) : 0f;

            if (weight > 0f && hasBelief && biasStrength > 1f)
            {
                weight *= PlayerBiasFor(node.Position, belief, biasStrength, falloff);
            }

            weightBuffer.Add(weight);
            total += weight;
        }

        // Every candidate route sits at weight 0: the designer switched them off, but it still has
        // to go somewhere. Uniform among the ones that survived the prefilter.
        if (total <= 0f) return sampledBuffer[Random.Range(0, sampledBuffer.Count)];

        float roll = Random.value * total;
        float cumulative = 0f;
        for (int i = 0; i < sampledBuffer.Count; i++)
        {
            cumulative += weightBuffer[i];
            if (roll <= cumulative) return sampledBuffer[i];
        }

        return sampledBuffer[sampledBuffer.Count - 1];
    }

    /// <summary>
    /// How much the current belief about the player is still worth: 1 the instant they are sensed,
    /// falling to 0 once it is <see cref="SO_NemesisData.BeliefMemoryTime"/> seconds old.
    ///
    /// Only applies to the remembered belief. When BiasUsesLastKnownPosition is off the bias reads
    /// the player's live transform, which is never stale by definition — and is worth knowing
    /// about, because in that mode the patrol is quietly steered by where the player actually is
    /// rather than by anything the Nemesis observed.
    /// </summary>
    private float BeliefFreshness()
    {
        SO_NemesisData data = Data;
        if (data == null || !data.BiasUsesLastKnownPosition) return 1f;

        float memory = Mathf.Max(0.01f, data.BeliefMemoryTime);

        float age = float.PositiveInfinity;

        FieldOfView view = stateManager != null ? stateManager.FieldOfView : null;
        if (view != null) age = Mathf.Min(age, view.TimeSinceLastSighting);

        FieldOfListening listening = stateManager != null ? stateManager.FieldOfListening : null;
        if (listening != null) age = Mathf.Min(age, listening.TimeSinceLastNoise);

        if (float.IsPositiveInfinity(age)) return 0f;

        return 1f - Mathf.Clamp01(age / memory);
    }

    /// <summary>
    /// Weight multiplier from closeness to the player, measured over the NavMesh.
    ///
    /// Returns 1 (no bias) when the waypoint is unreachable from where it believes the player is:
    /// that is not "far", it is "not comparable", and penalising it would drop it out of the roll
    /// for a reason that has nothing to do with the zone's design.
    /// </summary>
    private static float PlayerBiasFor(Vector3 waypoint, Vector3 belief, float strength, float falloff)
    {
        if (!NemesisNav.TryGetPathDistance(waypoint, belief, out float distance)) return 1f;

        float t = 1f - Mathf.Clamp01(distance / falloff);
        return Mathf.Lerp(1f, strength, t);
    }

    /// <summary>
    /// Trims the candidates down to the <paramref name="sampleCount"/> most promising ones using
    /// straight-line distance, which is free, so as not to pay two path queries for every waypoint
    /// in the level.
    ///
    /// The key is the minimum of "close to me" and "close to the player": keeping only what is
    /// near the Nemesis would discard precisely the waypoints on the player's floor, which are the
    /// interesting ones. A waypoint one floor up is close in a straight line, so it survives the
    /// prefilter and is then evaluated properly, with real path distance.
    /// </summary>
    private void PrefilterCandidates(List<int> candidates, Vector3 origin,
                                     bool hasBelief, Vector3 belief, int sampleCount)
    {
        sampledBuffer.Clear();

        if (candidates.Count <= sampleCount)
        {
            sampledBuffer.AddRange(candidates);
            return;
        }

        // Sorted insertion into a fixed-size list: simpler and cheaper than sorting the whole list
        // just to keep the first eight.
        List<float> keys = prefilterKeyBuffer;
        keys.Clear();

        for (int i = 0; i < candidates.Count; i++)
        {
            Vector3 position = routeGraph.GetNode(candidates[i]).Position;

            float key = Vector3.SqrMagnitude(position - origin);
            if (hasBelief) key = Mathf.Min(key, Vector3.SqrMagnitude(position - belief));

            int insertAt = sampledBuffer.Count;
            while (insertAt > 0 && keys[insertAt - 1] > key) insertAt--;

            if (insertAt >= sampleCount) continue;

            sampledBuffer.Insert(insertAt, candidates[i]);
            keys.Insert(insertAt, key);

            if (sampledBuffer.Count <= sampleCount) continue;

            sampledBuffer.RemoveAt(sampledBuffer.Count - 1);
            keys.RemoveAt(keys.Count - 1);
        }
    }

    /// <summary>
    /// Where the Nemesis BELIEVES the player is: the last position it saw or heard, not the real
    /// one.
    ///
    /// Biasing against the live real position stops the patrol feeling like a patrol and starts it
    /// feeling remote-controlled. With the flag on and no detection yet, this returns false and
    /// the roll falls back to the inspector weights: it does not know where you are, so it has no
    /// reason to prioritise anything.
    /// </summary>
    private bool TryGetPlayerBeliefPosition(out Vector3 position)
    {
        position = Vector3.zero;

        SO_NemesisData data = Data;
        bool useMemory = data == null || data.BiasUsesLastKnownPosition;

        if (!useMemory)
        {
            Transform player = PlayerRegistry.CurrentTransform;
            if (player == null) return false;

            position = player.position;
            return true;
        }

        FieldOfView view = stateManager != null ? stateManager.FieldOfView : null;
        if (view != null && view.HasLastKnownPosition)
        {
            position = view.LastKnownPosition;
            return true;
        }

        FieldOfListening listening = stateManager != null ? stateManager.FieldOfListening : null;
        if (listening != null && listening.HasLastKnownPosition)
        {
            position = listening.LastKnownPosition;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The old path, without the graph: weighted roll among the unlocked, non-empty routes,
    /// starting at the current index. Used when the Nemesis does not land on any known island —
    /// it should not happen, but leaving it without a route would leave it standing still forever.
    /// </summary>
    private void FallBackToWeightedRouteWithoutGraph()
    {
        NemesisRoute selected = SelectWeightedRoute();

        if (selected != currentRoute)
        {
            currentRoute = selected;
            currentWaypointIndex = 0;
        }
        else if (currentRoute != null && currentRoute.Waypoints.Count > 0)
        {
            currentWaypointIndex = Mathf.Clamp(currentWaypointIndex, 0, currentRoute.Waypoints.Count - 1);
        }
    }

    /// <summary>Weighted-random pick among the routes that are both unlocked and non-empty. Null
    /// if none qualify (e.g. nothing unlocked yet).</summary>
    private NemesisRoute SelectWeightedRoute()
    {
        float totalWeight = 0f;
        for (int i = 0; i < routes.Count; i++)
        {
            NemesisRoute route = routes[i];
            if (route == null || !route.IsUnlocked || route.Waypoints.Count == 0) continue;
            totalWeight += Mathf.Max(0f, route.Weight);
        }

        if (totalWeight <= 0f) return null;

        float roll = Random.value * totalWeight;
        float cumulative = 0f;
        for (int i = 0; i < routes.Count; i++)
        {
            NemesisRoute route = routes[i];
            if (route == null || !route.IsUnlocked || route.Waypoints.Count == 0) continue;

            cumulative += Mathf.Max(0f, route.Weight);
            if (roll <= cumulative) return route;
        }

        // Only reachable through float rounding at the very edge of the range: fall back to the
        // last qualifying route rather than returning nothing when totalWeight was in fact > 0.
        for (int i = routes.Count - 1; i >= 0; i--)
        {
            NemesisRoute route = routes[i];
            if (route != null && route.IsUnlocked && route.Waypoints.Count > 0) return route;
        }
        return null;
    }

    // ── Route unlocking ─────────────────────────────────────────────────────

    /// <summary>
    /// Opens a route up for selection. Meant to be called by whatever tracks progress (a puzzle
    /// completion callback, a checkpoint, a future director) — this class has no opinion on when
    /// that should happen, only on what "unlocked" means for route selection.
    /// </summary>
    public void UnlockRoute(int routeIndex)
    {
        if (routeIndex < 0 || routeIndex >= routes.Count)
        {
            Debug.LogWarning($"[NemesisController] UnlockRoute({routeIndex}) is out of range " +
                             $"(0..{routes.Count - 1}).", this);
            return;
        }

        NemesisRoute route = routes[routeIndex];
        if (route == null) return;

        route.Unlock();
    }

    /// <summary>
    /// Forces the merged set to be rebuilt. Needed when the NavMesh changes without any route
    /// changing: a rebake, or a door that stopped blocking. Without this the graph keeps believing
    /// the islands it worked out last time.
    /// </summary>
    public void InvalidateRouteGraph() => routeGraph.Rebuild(routes, force: true);

    // ── Spawn point selection ───────────────────────────────────────────────

    /// <summary>
    /// Picks a spawn point and warps the Nemesis there. Prefers the point farthest from the
    /// player that is outside the player's line of sight (reusing FieldOfListening's occlusion
    /// raycast — see <see cref="IsHiddenFromPlayer"/> — rather than a dedicated vision check).
    ///
    /// "Farthest" is measured over the NavMesh and not in a straight line: the point on the other
    /// side of the wall is 3 metres away on foot and 30 in a straight line, and picking by
    /// straight line is exactly how the Nemesis ended up spawning on top of the player.
    ///
    /// If every configured point is currently visible, <see cref="onAllSpawnPointsVisible"/>
    /// fires before the warp so a fade-to-black (or similar) can mask the pop-in.
    ///
    /// PENDING (Tier 3.1): the project has no fade system yet, so that event and the warp below
    /// both happen on the same frame — the Nemesis can still be seen popping in for that one
    /// edge case. Once a fade transition exists, split this into "start fade" and "warp once the
    /// fade has covered the screen" (e.g. drive the warp from the fade's own completion
    /// callback) instead of firing them together.
    /// </summary>
    /// <returns>The chosen spawn point, or null if none are configured.</returns>
    public Transform ChooseSpawnPoint()
    {
        Transform chosen = SelectSpawnPoint(out bool allVisible);
        if (chosen == null) return null;

        if (allVisible) onAllSpawnPointsVisible?.Invoke();

        WarpTo(chosen);
        return chosen;
    }

    private Transform SelectSpawnPoint(out bool allVisible)
    {
        allVisible = false;
        if (spawnPoints == null || spawnPoints.Count == 0) return null;

        Transform player = PlayerRegistry.CurrentTransform;

        Transform bestHidden = null;
        float bestHiddenDistance = -1f;
        Transform bestAny = null;
        float bestAnyDistance = -1f;

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            Transform point = spawnPoints[i];
            if (point == null) continue;

            float distance = DistanceToPlayer(point.position, player);
            if (distance > bestAnyDistance)
            {
                bestAnyDistance = distance;
                bestAny = point;
            }

            // No registered player yet: nobody to be seen by, every point counts as hidden.
            if (player == null || IsHiddenFromPlayer(point.position, player.position))
            {
                if (distance > bestHiddenDistance)
                {
                    bestHiddenDistance = distance;
                    bestHidden = point;
                }
            }
        }

        if (bestHidden != null) return bestHidden;

        // Every configured point is in view (or none could be tested): fall back to the
        // farthest one and let the caller know to mask the pop-in.
        allVisible = bestAny != null;
        return bestAny;
    }

    /// <summary>
    /// Path distance to the player. An unreachable point returns the straight-line distance and
    /// not infinity: since the FARTHEST one is what is being looked for, infinity would make it
    /// the automatic favourite — which is precisely the broken point we do not want to pick.
    /// </summary>
    private static float DistanceToPlayer(Vector3 point, Transform player)
    {
        if (player == null) return 0f;

        return NemesisNav.TryGetPathDistance(point, player.position, out float distance)
            ? distance
            : Vector3.Distance(point, player.position);
    }

    private bool IsHiddenFromPlayer(Vector3 point, Vector3 playerPosition)
    {
        FieldOfListening listener = stateManager != null ? stateManager.FieldOfListening : null;
        if (listener == null) return false;   // No way to test: assume it is visible.

        return listener.IsOccludedByWall(playerPosition, point);
    }

    private void WarpTo(Transform point)
    {
        NavMeshAgent agent = stateManager != null ? stateManager.NavAgent : null;

        // Warp and not transform.position: a NavMeshAgent keeps its own internal position and
        // would drag the Nemesis straight back on the next agent update (same reasoning as
        // NemesisStateManager's RepositionAfterCapture/TeleportToStuckEscapeWayPoint).
        if (agent != null && agent.isActiveAndEnabled) agent.Warp(point.position);
        else transform.position = point.position;
    }
}
