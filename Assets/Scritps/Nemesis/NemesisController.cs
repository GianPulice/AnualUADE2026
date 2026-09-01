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
    [PuzzleId]
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
    private bool nearbyPatrolRequested;   // Set by Searching on its way out. See BeginPatrolCycle.

    /// <summary>Merged set of every unlocked route, with the NavMesh islands already resolved.
    /// Only rebuilt when the unlocked set changes.</summary>
    private readonly NemesisRouteGraph routeGraph = new NemesisRouteGraph();

    // Reused across calls so AllUnlockedWaypoints does not allocate a new list every time the
    // Nemesis gets stuck or needs to reposition after a capture.
    private readonly List<Transform> flatWaypointsBuffer = new List<Transform>();
    private readonly List<Transform> spawnPointsBuffer = new List<Transform>();

    // Spawn-point selection buffers. See PickWeightedSpawnPoint.
    private readonly List<Transform> hiddenSpawnCandidatesBuffer = new List<Transform>();
    private readonly List<float> hiddenSpawnDistancesBuffer = new List<float>();
    private readonly List<float> spawnWeightBuffer = new List<float>();

    // Waypoint-selection buffers. Reused because this runs on every waypoint arrival and every
    // replan, and it is not worth allocating each time.
    private readonly List<int> candidateBuffer = new List<int>();
    private readonly List<int> sampledBuffer = new List<int>();
    private readonly List<float> weightBuffer = new List<float>();
    private readonly List<float> prefilterKeyBuffer = new List<float>();
    private readonly List<Vector3> candidatePositionBuffer = new List<Vector3>();

    // Buffers of their own rather than sharing weightBuffer above. The two selections never
    // overlap today (SelectWeightedRoute only runs once PickWeightedNode has already returned),
    // but "these two happen to run in the right order" is not a property anybody would notice
    // breaking, and a shared buffer cleared halfway through a consumer is a silent wrong answer
    // rather than a crash.
    private readonly List<NemesisRoute> routeCandidatesBuffer = new List<NemesisRoute>();
    private readonly List<float> routeWeightBuffer = new List<float>();

    /// <summary>
    /// Zone-level patrol: which cúmulo of waypoints is being swept and in what order. All of the
    /// maths lives in that class, which is a plain object and not a component — this controller
    /// only feeds it the graph and the tuning, and points the agent at whatever node comes back.
    ///
    /// currentRoute and currentWaypointIndex above are kept in step with its tour (see
    /// <see cref="AdoptClusterNode"/>), so CurrentWaypoint, the gizmos and NemesisSetupValidator
    /// all keep reading the same fields they always did, whichever mode is running.
    /// </summary>
    private readonly NemesisClusterPatrol clusterPatrol = new NemesisClusterPatrol();

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

    /// <summary>
    /// Set while the cúmulo's sweep is at one of the auto-generated points around a waypoint,
    /// null while it is at the waypoint itself (and always, outside cluster patrol).
    ///
    /// It is an OFFSET from the route bookkeeping rather than a replacement for it. currentRoute
    /// and currentWaypointIndex keep pointing at the authored waypoint the stop belongs to, so
    /// everything that reads them carries on working; this only changes where the agent is
    /// actually sent. See <see cref="CurrentWaypointPosition"/>.
    /// </summary>
    private Vector3? currentStopPosition;

    /// <summary>
    /// Where the Nemesis should actually walk: the generated sweep point when the tour is at one,
    /// the authored waypoint otherwise.
    ///
    /// THIS AND NOT <see cref="CurrentWaypoint"/>.position IS THE DESTINATION. The Transform is
    /// still the right handle for "which authored waypoint is this", which is what the warnings,
    /// the validator and the gizmos want — but it cannot express a point that has no marker, and
    /// reading .position off it would quietly send the patrol back to the waypoint every time the
    /// sweep tried to visit the space around it.
    ///
    /// Falls back to the Nemesis's own position when there is no route at all, so a caller that
    /// skipped the null check on CurrentWaypoint gets "stay put" instead of the world origin.
    /// </summary>
    public Vector3 CurrentWaypointPosition
    {
        get
        {
            if (currentStopPosition.HasValue) return currentStopPosition.Value;

            Transform waypoint = CurrentWaypoint;
            return waypoint != null ? waypoint.position : transform.position;
        }
    }

    /// <summary>Whether the patrol is currently heading for a generated sweep point rather than an
    /// authored waypoint. For the debug HUD and the gizmos.</summary>
    public bool IsAtGeneratedStop => currentStopPosition.HasValue;

    /// <summary>The route being patrolled right now, or null. Debug/gizmos only.</summary>
    public NemesisRoute CurrentRoute => currentRoute;

    /// <summary>The merged route set. Public so an editor tool can inspect it without duplicating
    /// the build.</summary>
    public NemesisRouteGraph RouteGraph => routeGraph;

    // Read by NemesisDebugHUD. The gizmos below already reach into clusterPatrol directly, being
    // members of this class; the HUD is a separate component and needs a way in that does not
    // hand it the whole object.
    public int CurrentCluster => clusterPatrol.CurrentCluster;
    public int ClusterTourIndex => clusterPatrol.TourIndex;
    public int ClusterTourBudget => clusterPatrol.TourBudget;

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
    /// It rebuilds the merged set when needed and picks where to patrol next, among what is
    /// genuinely reachable from where the Nemesis is standing — weighted by the designer's route
    /// weights and by how close it is to where it believes the player is.
    ///
    /// With cluster patrol on (the default) what gets picked is a ZONE: a cúmulo of nearby
    /// waypoints, swept as a unit. With it off, a single waypoint out of the whole merged set,
    /// which is the older behaviour — see SO_NemesisData.ClusterPatrolEnabled for why that reads
    /// as the monster teleporting around the level.
    ///
    /// Either way this is a fresh decision and deliberately NOT biased towards staying nearby:
    /// arriving here means it just gave up a chase or a search, and pinning it to the zone it
    /// lost you in is the opposite of what should happen next.
    /// </summary>
    public void BeginPatrolCycle()
    {
        replanTimer = 0f;

        RebuildGraph();

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

        // Prowling: a cycle entered straight out of Searching stays around the zone it lost the
        // player in, instead of being free to relocate across the level. Without it the 4-second
        // search reads as the Nemesis forgetting you the instant it expires; with it, giving up
        // the search is the start of it circling the area rather than the end of the encounter.
        //
        // Consumed here so it applies to exactly one cycle: the periodic replan that follows,
        // and every cycle after it, is a fresh decision again.
        bool prowl = ConsumeNearbyPatrolRequest();

        if (UsesClusterPatrol &&
            TryAdoptClusterIn(component, preferNeighbours: prowl, excludeCurrent: false))
        {
            return;
        }

        // Clusters off, or the graph produced none: the single-waypoint pick.
        clusterPatrol.Reset();

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

    /// <summary>Whether the Nemesis patrols by zone. Defaults to on when there is no SO to ask,
    /// matching the asset's own default.</summary>
    private bool UsesClusterPatrol => Data == null || Data.ClusterPatrolEnabled;

    // ── Prowling after a search ─────────────────────────────────────────────

    /// <summary>
    /// Asks the next patrol cycle to stay around here instead of relocating freely. Called by
    /// <see cref="NemesisSearchingState"/> on its way out.
    ///
    /// A one-shot request rather than the state manager exposing a "previous state": the FSM base
    /// is shared with the player, and giving it a notion of state history to serve one Nemesis
    /// behaviour is a change with a much wider blast radius than a bool.
    /// </summary>
    public void RequestNearbyPatrol() => nearbyPatrolRequested = true;

    private bool ConsumeNearbyPatrolRequest()
    {
        bool requested = nearbyPatrolRequested;
        nearbyPatrolRequested = false;
        return requested;
    }

    // ── Sensed trail ────────────────────────────────────────────────────────

    /// <summary>
    /// Stamps the waypoint nearest to whatever the Nemesis is sensing right now, building the
    /// trail <see cref="NemesisSearchingState"/> reads to work out which way the player was
    /// heading. Called once per frame by <see cref="NemesisStateManager"/> while either sensor
    /// has a target.
    ///
    /// It goes through <see cref="TryGetPlayerBeliefPosition"/> — the same sight-then-hearing
    /// resolution the patrol bias already uses — rather than reading the player, so the trail
    /// records only what was actually sensed. That is what makes breaking line of sight and
    /// doubling back work: the trail keeps pointing the way you were going, and the Nemesis
    /// commits to it.
    /// </summary>
    public void MarkBeliefTrace()
    {
        if (!routeGraph.IsBuilt) return;
        if (!TryGetPlayerBeliefPosition(out Vector3 belief)) return;

        SO_NemesisData data = Data;
        routeGraph.MarkSensedAt(belief, data != null ? data.BeliefTraceRadius : 3f);
    }

    /// <summary>Rebuild with the cluster settings currently on the SO. Cheap to over-call: the
    /// graph does nothing when nothing has changed, the cluster knobs included.</summary>
    private void RebuildGraph()
    {
        SO_NemesisData data = Data;
        routeGraph.Rebuild(routes,
                           data != null ? data.ClusterRadius : 12f,
                           data != null ? data.MaxClusterSize : 5,
                           data != null ? data.WaypointSatellites : 0,
                           data != null ? data.WaypointSatelliteRadius : 4f);
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
    /// Moves the traversal on to the next waypoint. Called by NemesisPatrolState once it has
    /// waited out PatrolWaypointWaitTime at <see cref="CurrentWaypoint"/>.
    ///
    /// With cluster patrol on this steps along the current cúmulo's sweep, and moves to a
    /// neighbouring cúmulo once this one's budget is spent — see <see cref="AdvanceWithinCluster"/>.
    ///
    /// With it off it honors the current direction and consumes the pending skip (if this cycle
    /// rolled one), and with probability CrossRouteTransferChance jumps to a waypoint on another
    /// unlocked and reachable route instead, adopting that route from there. That jump is what let
    /// it change floor without waiting for the route roll to hand it the upper one: the route is
    /// still an ordered polyline (the gizmo does not lie), it just stops being a cage.
    /// </summary>
    public void AdvanceToNextWaypoint()
    {
        // Dropped up front rather than in each branch below. AdoptNode clears it for the paths
        // that go through it, but the sequential step at the bottom of this method writes
        // currentWaypointIndex directly and never touches it — which, with cluster patrol switched
        // off mid-run, would leave the patrol walking to a generated point belonging to a sweep
        // that no longer exists. AdvanceWithinCluster re-sets it immediately when it applies.
        currentStopPosition = null;

        if (UsesClusterPatrol && clusterPatrol.HasCluster)
        {
            AdvanceWithinCluster();
            return;
        }

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

        // Cleared on EVERY adoption, so a generated stop can never outlive the sweep that produced
        // it. Every path that changes where the Nemesis is heading comes through here — the
        // sequential step, the cross-route transfer, the weighted roll, the cluster tour — and a
        // stale offset would send the patrol to a point in a zone it has already left.
        currentStopPosition = null;

        currentRoute = node.Route;
        currentWaypointIndex = Mathf.Clamp(node.IndexInRoute, 0,
                                           Mathf.Max(0, node.Route.Waypoints.Count - 1));
    }

    // ── Cluster patrol ──────────────────────────────────────────────────────
    //
    // Thin wrappers. The rolls, the sweep order and the budget all live in NemesisClusterPatrol,
    // which is a plain class with no scene references. All this layer does is translate between
    // "graph node index" and this controller's currentRoute/currentWaypointIndex pair.

    /// <summary>
    /// Steps along the cúmulo being swept, and moves on to another one when this visit is over.
    /// </summary>
    private void AdvanceWithinCluster()
    {
        NemesisClusterPatrol.TourStop stop = clusterPatrol.Advance();
        if (stop.IsValid)
        {
            AdoptClusterNode(stop);
            return;
        }

        // Zone swept. Next one, preferring next door — that preference is the whole difference
        // between a patrol that walks the level and one that jumps around it.
        RebuildGraph();

        if (routeGraph.TryGetComponentAt(transform.position, out int component) &&
            TryAdoptClusterIn(component, preferNeighbours: true, excludeCurrent: true))
        {
            return;
        }

        // Nothing else reachable: sweep this one again rather than stand still.
        stop = clusterPatrol.Resweep(routeGraph, BuildClusterSettings(applyNeighbourBias: false),
                                     transform.position, direction);
        if (stop.IsValid)
        {
            AdoptClusterNode(stop);
            return;
        }

        clusterPatrol.Reset();
        FallBackToWeightedRouteWithoutGraph();
    }

    /// <summary>
    /// Rolls for a cúmulo on the given island and starts sweeping it.
    /// </summary>
    /// <param name="preferNeighbours">Weight the roll towards zones close to where the Nemesis is
    /// standing. On when moving from one cúmulo to the next, off for a fresh patrol cycle — which
    /// should be free to relocate anywhere, since arriving there means it just gave up a chase.
    /// </param>
    /// <param name="excludeCurrent">Drop the zone being swept from the draw.
    ///
    /// It used to be tied to <paramref name="preferNeighbours"/>, on the reasoning that the two
    /// always travelled together: finishing a cúmulo means "somewhere else, preferably next
    /// door". Prowling after a search wants the first half without the second — stay around
    /// here, and the zone it lost you in is the single best place to stay — so the two are
    /// separate parameters now.</param>
    /// <returns>false when the island has no usable cúmulo, so the caller can fall back.</returns>
    private bool TryAdoptClusterIn(int component, bool preferNeighbours, bool excludeCurrent)
    {
        bool skip = pendingSkip;

        NemesisClusterPatrol.TourStop stop =
            clusterPatrol.Begin(routeGraph, BuildClusterSettings(preferNeighbours),
                                transform.position, component, direction, ref skip,
                                excludeCurrent);

        pendingSkip = skip;

        if (!stop.IsValid) return false;

        AdoptClusterNode(stop);
        return true;
    }

    /// <summary>
    /// Points the traversal at a node the cluster patrol handed back.
    ///
    /// It goes through <see cref="AdoptNode"/> rather than keeping a destination of its own, so
    /// currentRoute and currentWaypointIndex stay true in cluster mode too — which is what lets
    /// <see cref="CurrentWaypoint"/>, the gizmos and the setup validator carry on reading the
    /// fields they always read, without any of them having to know which mode is running.
    /// </summary>
    private void AdoptClusterNode(NemesisClusterPatrol.TourStop stop)
    {
        // The route bookkeeping still goes through the node, generated stop or not: that is what
        // keeps currentRoute and currentWaypointIndex true, and with them CurrentWaypoint, the
        // gizmos and the setup validator. AdoptNode clears the offset, so it is set afterwards.
        AdoptNode(routeGraph.GetNode(stop.Node));

        if (stop.IsSatellite) currentStopPosition = stop.Position;
    }

    /// <summary>
    /// Gathers the tuning and the current belief into the struct the cluster patrol rolls with.
    ///
    /// The belief and its freshness are read HERE and not in there on purpose: where the Nemesis
    /// thinks the player is comes off FieldOfView/FieldOfListening, and the point of the auxiliary
    /// class is that it knows nothing about sensors — it is handed a position and a strength.
    /// </summary>
    private NemesisClusterPatrol.Settings BuildClusterSettings(bool applyNeighbourBias)
    {
        bool hasBelief = TryGetPlayerBeliefPosition(out Vector3 belief);
        bool hasZoneAnchor = TryGetZoneAnchor(out Vector3 zoneAnchor);

        return NemesisClusterPatrol.Settings.From(Data, hasBelief, belief, BeliefFreshness(),
                                                  applyNeighbourBias, hasZoneAnchor, zoneAnchor);
    }

    /// <summary>
    /// Where the player ACTUALLY is, for the zone-level patrol bias only.
    ///
    /// THIS IS KNOWLEDGE THE NEMESIS DID NOT EARN, and it is the only place in the system that
    /// reads the player's live transform to decide where to go. Everything else — the pursuit's
    /// velocity, the search's heading, the per-waypoint roll — is measured off what the sensors
    /// actually caught, and that is what makes breaking line of sight real counterplay rather
    /// than a formality. None of that changes: this feeds ONE roll, the choice of cúmulo.
    ///
    /// WHY IT IS HERE. <see cref="TryGetPlayerBeliefPosition"/> returns false until the player has
    /// been sensed at least once, and <see cref="BeliefFreshness"/> then decays what it returns to
    /// nothing over BeliefMemoryTime. Between them, the player bias the designer authored was
    /// exactly zero for the whole opening stretch of a run and again a minute after every lost
    /// contact — which is to say it was off in most of the situations it was written for. The
    /// honest fix for "make it tend towards the player" is not a bigger multiplier on a number
    /// that is being multiplied by zero.
    ///
    /// WHAT KEEPS IT FROM READING AS OMNISCIENCE is that it is coarse in three separate ways: it
    /// only weights ZONES and never individual waypoints, the weight is a roll and not an argmax
    /// (see <see cref="PickWeightedNode"/> for why that distinction carries the whole illusion),
    /// and ZonePlayerBiasFalloff is wide enough that it says "your side of the level" rather than
    /// "your room". Turn ZoneBiasUsesRealPlayer off and the patrol goes back to being steered by
    /// nothing but what it sensed.
    /// </summary>
    private bool TryGetZoneAnchor(out Vector3 position)
    {
        position = Vector3.zero;

        SO_NemesisData data = Data;
        if (data == null || !data.ZoneBiasUsesRealPlayer) return false;

        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null) return false;

        position = player.position;
        return true;
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

        RebuildGraph();
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

        candidatePositionBuffer.Clear();
        for (int i = 0; i < candidates.Count; i++)
            candidatePositionBuffer.Add(routeGraph.GetNode(candidates[i]).Position);

        NemesisClusterPatrol.KeepClosest(candidates, candidatePositionBuffer, origin, hasBelief,
                                         belief, sampleCount, sampledBuffer, prefilterKeyBuffer);
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

        for (int i = 0; i < sampledBuffer.Count; i++)
        {
            NemesisRouteGraph.Node node = routeGraph.GetNode(sampledBuffer[i]);
            float weight = node.Route != null ? Mathf.Max(0f, node.Route.Weight) : 0f;

            if (weight > 0f && hasBelief && biasStrength > 1f)
            {
                weight *= NemesisClusterPatrol.ProximityWeight(node.Position, belief,
                                                              biasStrength, falloff);
            }

            weightBuffer.Add(weight);
        }

        // The roll itself lives in RouletteSelection, which also owns the two edge cases this
        // used to spell out on its own: every candidate at weight 0 (the designer switched those
        // routes off, and it still has to go somewhere — uniform among the survivors) and the
        // float-rounding fallback to the last bucket.
        int index = RouletteSelection.Roulette(weightBuffer);
        return index < 0 ? -1 : sampledBuffer[index];
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
    public float BeliefFreshness()
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
        // This path assigns currentRoute and currentWaypointIndex itself rather than going through
        // AdoptNode, so it has to drop the generated-stop offset on its own. It is reached when
        // the Nemesis is off every known island — exactly when a leftover point from the last
        // sweep would be least reachable.
        currentStopPosition = null;

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
        routeCandidatesBuffer.Clear();
        routeWeightBuffer.Clear();

        float totalWeight = 0f;

        for (int i = 0; i < routes.Count; i++)
        {
            NemesisRoute route = routes[i];
            if (route == null || !route.IsUnlocked || route.Waypoints.Count == 0) continue;

            float weight = Mathf.Max(0f, route.Weight);

            routeCandidatesBuffer.Add(route);
            routeWeightBuffer.Add(weight);
            totalWeight += weight;
        }

        // THE ALL-ZERO CASE IS DELIBERATELY NOT DELEGATED, and this is the one place in the file
        // where that is true.
        //
        // RouletteSelection answers "every candidate weighs nothing" with a uniform pick, which is
        // right for PickWeightedNode: it is choosing among waypoints it has already established are
        // reachable, and the Nemesis has to walk somewhere. Here the question is different — this
        // runs only when the Nemesis did not land on any known NavMesh island — and returning a
        // route the designer explicitly weighted to zero would send it off towards waypoints
        // nobody chose for it. Null instead leaves NemesisPatrolState standing by, which is a state
        // NemesisStuckEscape can see and recover from.
        if (totalWeight <= 0f) return null;

        int index = RouletteSelection.Roulette(routeWeightBuffer);
        return index < 0 ? null : routeCandidatesBuffer[index];
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
    /// Forces the merged set — and its cúmulos — to be rebuilt. Needed when the NavMesh changes
    /// without any route changing: a rebake, or a door that stopped blocking. Without this the
    /// graph keeps believing the islands it worked out last time.
    ///
    /// The cluster being swept is dropped along with it: its index refers to a list that is about
    /// to be rebuilt from scratch, and reusing it would point the sweep at whatever zone happens
    /// to land in that slot. The next AdvanceToNextWaypoint re-picks one.
    /// </summary>
    public void InvalidateRouteGraph()
    {
        SO_NemesisData data = Data;
        routeGraph.Rebuild(routes,
                           data != null ? data.ClusterRadius : 12f,
                           data != null ? data.MaxClusterSize : 5,
                           data != null ? data.WaypointSatellites : 0,
                           data != null ? data.WaypointSatelliteRadius : 4f,
                           force: true);

        clusterPatrol.Reset();
    }

    // ── Spawn point selection ───────────────────────────────────────────────

    /// <summary>
    /// Picks a spawn point and warps the Nemesis there. Weighted towards the point farthest from
    /// the player among the ones outside the player's line of sight (reusing FieldOfListening's
    /// occlusion raycast — see <see cref="IsHiddenFromPlayer"/> — rather than a dedicated vision
    /// check) — see <see cref="PickWeightedSpawnPoint"/> for why this is a roll and not an argmax.
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

        hiddenSpawnCandidatesBuffer.Clear();
        hiddenSpawnDistancesBuffer.Clear();

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
                hiddenSpawnCandidatesBuffer.Add(point);
                hiddenSpawnDistancesBuffer.Add(distance);
            }
        }

        if (hiddenSpawnCandidatesBuffer.Count > 0)
            return PickWeightedSpawnPoint(hiddenSpawnCandidatesBuffer, hiddenSpawnDistancesBuffer);

        // Every configured point is in view (or none could be tested): fall back to the
        // farthest one and let the caller know to mask the pop-in. Argmax and not a roll here —
        // every candidate is equally exposed, so there is no "safer" option to weight towards.
        allVisible = bestAny != null;
        return bestAny;
    }

    /// <summary>
    /// Weighted-random pick among the hidden spawn candidates, weighted by distance SQUARED so the
    /// farthest ones still clearly dominate — giving the player breathing room is the whole point
    /// of preferring distance — but stop being the only thing that can ever happen.
    ///
    /// This used to be a plain argmax ("always the single farthest hidden point"), and that is the
    /// entire explanation for "the Nemesis starts from the same place every run": the player always
    /// begins from the same position, spawn points and geometry are static, so the farthest hidden
    /// point is a pure function of the level and returns the identical Transform every single time
    /// — no roll anywhere in the old function to vary it. The patrol's own route/waypoint rolls
    /// (BeginPatrolCycle, PickWeightedNode) were never the problem: they already use Random.value
    /// correctly. They just kept drawing from the same small neighbourhood of nearby waypoints,
    /// because "nearby" was always measured from the same starting point.
    /// </summary>
    private Transform PickWeightedSpawnPoint(List<Transform> candidates, List<float> distances)
    {
        if (candidates.Count == 1) return candidates[0];

        // Squared here rather than inside the roll, because the squaring IS the design — see the
        // doc comment above — and RouletteSelection has no business knowing that this particular
        // caller weights by distance at all.
        spawnWeightBuffer.Clear();
        for (int i = 0; i < distances.Count; i++)
            spawnWeightBuffer.Add(distances[i] * distances[i]);

        int index = RouletteSelection.Roulette(spawnWeightBuffer);
        return index < 0 ? candidates[candidates.Count - 1] : candidates[index];
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

    /// <summary>
    /// Moves the Nemesis to a spawn point, through the facade rather than by touching the agent.
    ///
    /// It used to call agent.Warp itself, with a comment saying it followed the same reasoning as
    /// the state manager's warps — which is exactly the argument for not writing it twice. The
    /// copy quietly skipped all three things that version does: snapping the marker onto the
    /// NavMesh (a spawn point placed by eye inside a wall strands the agent off the mesh for the
    /// whole session, and every state's guard then makes it stand still), dropping the cached
    /// route verdict, and resetting the stuck watchdog's sample.
    /// </summary>
    private void WarpTo(Transform point)
    {
        if (stateManager != null)
        {
            stateManager.WarpTo(point.position);
            return;
        }

        transform.position = point.position;
    }

    // ── Cluster gizmos ──────────────────────────────────────────────────────

    /// <summary>
    /// Draws the cúmulos and the sweep currently being walked, so "which zone is it in and where
    /// is it going next" is answerable by looking at the Scene view instead of by reading logs.
    ///
    /// Play mode only, and that is not a limitation to work around: the clusters do not exist
    /// outside it. Building them needs the NavMesh islands, which cost one path query per
    /// waypoint — fine once per unlock, ruinous every time the Scene view repaints. The static
    /// half of the picture is already drawn by <see cref="NemesisRoute"/>'s own gizmos.
    ///
    /// Selected-only, unlike NemesisGizmos: this draws over every waypoint in the level, and a
    /// level's worth of spheres and labels on screen at all times stops being readable well before
    /// it stops being correct — the same reasoning NemesisRoute gives for keeping its labels in
    /// OnDrawGizmosSelected.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !routeGraph.IsBuilt) return;

        for (int c = 0; c < routeGraph.ClusterCount; c++)
        {
            DrawCluster(c, isActive: c == clusterPatrol.CurrentCluster);
        }

        DrawTour();
    }

    private void DrawCluster(int clusterIndex, bool isActive)
    {
        // Amber is the active zone, cool blue the dormant ones — the project's own alert/passive
        // pair. No red anywhere: that is reserved for danger, and a patrol route is not danger.
        Color color = isActive ? new Color(1f, 0.784f, 0.314f) : new Color(0.35f, 0.5f, 0.62f);
        Gizmos.color = color;

        NemesisRouteGraph.Cluster cluster = routeGraph.GetCluster(clusterIndex);
        routeGraph.CollectClusterMembers(clusterIndex, gizmoMemberBuffer);

        for (int i = 0; i < gizmoMemberBuffer.Count; i++)
        {
            Vector3 member = routeGraph.GetNode(gizmoMemberBuffer[i]).Position;

            // Centroid to member, so a cluster reads as one group at a glance instead of as
            // loose spheres that happen to share a colour.
            Gizmos.DrawLine(cluster.Centroid, member);
            Gizmos.DrawWireSphere(member, isActive ? 0.5f : 0.3f);
        }

        if (!isActive) return;

        Gizmos.DrawWireCube(cluster.Centroid, Vector3.one * 0.4f);

#if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(cluster.Centroid + Vector3.up * 1.2f,
                                  $"cúmulo #{clusterIndex} — {clusterPatrol.TourIndex + 1}/" +
                                  $"{clusterPatrol.TourBudget} de {clusterPatrol.Stops.Count} " +
                                  $"paradas ({cluster.MemberCount} wp, peso {cluster.Weight:0.##})");
#endif
    }

    /// <summary>
    /// The sweep order of the active cluster, numbered, with the waypoints past this visit's
    /// budget drawn dimmer — they belong to the cúmulo but will not be walked this time round.
    /// </summary>
    private void DrawTour()
    {
        IReadOnlyList<NemesisClusterPatrol.TourStop> stops = clusterPatrol.Stops;
        if (!clusterPatrol.HasCluster || stops.Count == 0) return;

        for (int i = 0; i < stops.Count; i++)
        {
            bool withinBudget = i < clusterPatrol.TourBudget;
            Vector3 position = stops[i].Position;

            Gizmos.color = withinBudget ? new Color(1f, 0.784f, 0.314f)
                                        : new Color(1f, 0.784f, 0.314f, 0.25f);

            if (i > 0)
                Gizmos.DrawLine(stops[i - 1].Position, position);

            // Generated stops drawn as a small sphere, authored waypoints left to the cluster
            // gizmo above, which already rings them. Telling the two apart in the Scene view is
            // the only way to answer "is it sweeping the room or just walking my markers".
            if (stops[i].IsSatellite) Gizmos.DrawWireSphere(position, 0.25f);

#if UNITY_EDITOR
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.Label(position + Vector3.up * 0.7f,
                                      i == clusterPatrol.TourIndex ? $"▶ {i}" : i.ToString());
#endif
        }
    }

    /// <summary>The gizmos read the cluster members through a buffer of their own: the Scene view
    /// repaints while the game is running, and sharing its own would have a gizmo
    /// clear a list the sweep is halfway through consuming.</summary>
    private readonly List<int> gizmoMemberBuffer = new List<int>();
}
