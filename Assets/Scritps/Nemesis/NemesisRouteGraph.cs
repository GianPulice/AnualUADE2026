using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Merges every unlocked route into a single navigable set of waypoints, and works out which of
/// them the Nemesis can actually reach from where it is standing.
///
/// This is the "merged routes" logic: the Nemesis is no longer locked inside whichever route the
/// weighted roll handed it, and can borrow a waypoint from another route when that helps — which
/// is exactly how it changes floor. If route 3 has a waypoint upstairs and route 1 does not, the
/// Nemesis patrolling route 1 can still go up, because the graph says that node is reachable.
///
/// Two things are verified while building it, and both are real failures that today only show up
/// mid-playtest:
///   1. That every waypoint lands on the NavMesh. One placed half a metre off the floor is never
///      reachable, and the symptom is a Nemesis standing still or warping for no visible reason.
///   2. Which waypoints are connected to each other. Two zones split by a sealed door are two
///      separate NavMesh islands; sending the Nemesis to the wrong island hangs it.
///
/// Islands are resolved by union against representatives rather than comparing everything with
/// everything: on a well-connected level that is one path query per waypoint, not N squared. The
/// build only re-runs when the set of unlocked routes changes (see <see cref="Fingerprint"/>),
/// i.e. a handful of times per run, when a puzzle opens a zone.
/// </summary>
public sealed class NemesisRouteGraph
{
    /// <summary>A waypoint inside the merged set, without losing which route it came from: the
    /// original route's weight and ordering still apply once it is picked.</summary>
    public readonly struct Node
    {
        public readonly NemesisRoute Route;
        public readonly int IndexInRoute;
        public readonly Transform Waypoint;

        public Node(NemesisRoute route, int indexInRoute, Transform waypoint)
        {
            Route = route;
            IndexInRoute = indexInRoute;
            Waypoint = waypoint;
        }

        public Vector3 Position => Waypoint != null ? Waypoint.position : Vector3.zero;
        public bool IsValid => Route != null && Waypoint != null;
    }

    /// <summary>
    /// A compact group of nearby waypoints — a "cúmulo" — that the Nemesis patrols as one unit
    /// instead of hopping waypoint by waypoint across the level.
    ///
    /// Clusters are SPATIAL, not authored: they are built from where the waypoints actually are,
    /// so one cluster routinely mixes waypoints from several routes that happen to cover the same
    /// corner of the level. That is the point. Picking one waypoint at a time from the whole
    /// merged set is what made the patrol read as teleporting — two consecutive indices of a
    /// route in this level can be thirty metres apart, and the cross-route roll could send it to
    /// the far corner on any arrival. Committing to a cluster makes the same waypoints read as
    /// "it is sweeping that area now", which is what a stalker is supposed to look like.
    ///
    /// Every member is in the same NavMesh island by construction, so anything picked out of a
    /// cluster the Nemesis is standing in is guaranteed reachable.
    /// </summary>
    public readonly struct Cluster
    {
        /// <summary>Index into the graph's flat member list where this cluster's nodes start.</summary>
        public readonly int FirstMember;

        public readonly int MemberCount;

        /// <summary>Average position of the members. What the zone-level rolls measure against:
        /// one path query per cluster instead of one per waypoint.</summary>
        public readonly Vector3 Centroid;

        /// <summary>The NavMesh island every member sits on.</summary>
        public readonly int Component;

        /// <summary>Average of the members' route weights, so the designer's per-zone frequency
        /// still steers which cluster gets patrolled.</summary>
        public readonly float Weight;

        public Cluster(int firstMember, int memberCount, Vector3 centroid, int component, float weight)
        {
            FirstMember = firstMember;
            MemberCount = memberCount;
            Centroid = centroid;
            Component = component;
            Weight = weight;
        }
    }

    private readonly List<Node> nodes = new List<Node>();

    /// <summary>Which NavMesh island each node belongs to. Two nodes sharing a number have a path
    /// between them; different numbers mean no path.</summary>
    private readonly List<int> componentOf = new List<int>();

    /// <summary>One node per island, the first one found. Everything else is tested against
    /// these, which is what keeps this out of N squared.</summary>
    private readonly List<int> representatives = new List<int>();

    private readonly List<Cluster> clusters = new List<Cluster>();

    /// <summary>Every cluster's members, concatenated. A cluster reads its own slice through
    /// <see cref="Cluster.FirstMember"/> and <see cref="Cluster.MemberCount"/> — one list instead
    /// of a list per cluster, so a rebuild does not allocate one array per zone.</summary>
    private readonly List<int> clusterMembers = new List<int>();

    /// <summary>Which cluster each node ended up in, or -1 while the build is still running.
    /// Doubles as the "already assigned" mark the greedy build reads.</summary>
    private readonly List<int> clusterOf = new List<int>();

    /// <summary>
    /// When each node was last near something the Nemesis SENSED, on the <see cref="Time.time"/>
    /// clock. <see cref="float.NegativeInfinity"/> for a node that never has been.
    ///
    /// This is the Nemesis's memory of your route through the level, and it is what a bare
    /// last-known-position cannot give it: a single point says where you were, a trail of stamped
    /// waypoints says which way you were travelling — measured over seconds of real navigation
    /// rather than over the half-second window the velocity estimate is capped to.
    ///
    /// Written only from actual detections (see <see cref="NemesisController.MarkBeliefTrace"/>),
    /// never by polling the player, so it stays belief and not truth: break line of sight and
    /// double back, and the trail keeps pointing the way you were going.
    /// </summary>
    private readonly List<float> lastSensedAt = new List<float>();

    private string fingerprint = string.Empty;
    private int componentCount;

    /// <summary>
    /// Above this many waypoints the densest-seed pass is dropped for plain iteration order.
    ///
    /// Seeding each cluster at the node with the most neighbours gives rounder, more evenly sized
    /// zones, and costs one extra N-squared sweep per cluster — fine for the few dozen waypoints
    /// a hand-built level carries, not fine for a generated one with hundreds. The cheap seeding
    /// produces slightly lumpier clusters and nothing else: no correctness depends on which node
    /// happened to start one.
    /// </summary>
    private const int DensestSeedNodeLimit = 128;

    public int NodeCount => nodes.Count;

    public int ClusterCount => clusters.Count;
    public int ComponentCount => componentCount;
    public bool IsBuilt => nodes.Count > 0;

    public Node GetNode(int index) => nodes[index];

    /// <summary>The node's island, or -1 when the index does not exist.</summary>
    public int ComponentOf(int nodeIndex) =>
        nodeIndex >= 0 && nodeIndex < componentOf.Count ? componentOf[nodeIndex] : -1;

    public bool AreConnected(int nodeA, int nodeB)
    {
        int a = ComponentOf(nodeA);
        return a >= 0 && a == ComponentOf(nodeB);
    }

    // ── Sensed trail ────────────────────────────────────────────────────────

    /// <summary>
    /// Records that the Nemesis sensed the player near here, stamping the closest waypoint within
    /// <paramref name="radius"/>. A detection with no waypoint nearby is simply not recorded —
    /// the trail is a map of the player's route through the PATROL GRAPH, and a corner of the
    /// level with no waypoints is a corner the Nemesis has no way to reason about.
    /// </summary>
    public void MarkSensedAt(Vector3 position, float radius)
    {
        int best = -1;
        float bestSqr = radius * radius;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (!nodes[i].IsValid) continue;

            float distanceSqr = Vector3.SqrMagnitude(nodes[i].Position - position);
            if (distanceSqr > bestSqr) continue;

            bestSqr = distanceSqr;
            best = i;
        }

        if (best >= 0) lastSensedAt[best] = Time.time;
    }

    /// <summary>
    /// The direction the player was last observed travelling, taken from the two most recently
    /// stamped waypoints.
    ///
    /// Preferred over <see cref="FieldOfView.LastKnownVelocity"/> for deciding where to cut
    /// someone off, and the difference is the timescale. That velocity is measured between
    /// sightings less than half a second apart, so it captures a sidestep as faithfully as a
    /// commitment — aim a ten-second interception with it and a player who strafed once at the
    /// moment of the last glimpse sends the Nemesis down the wrong corridor. Two waypoints are
    /// metres apart and seconds apart: they describe where someone is actually GOING.
    /// </summary>
    /// <param name="maxAge">How old the newer of the two stamps may be. Past this the trail is
    /// not evidence of anything current.</param>
    /// <returns>false when fewer than two waypoints were stamped inside the window, or when both
    /// stamps landed on the same waypoint and there is no direction to read.</returns>
    public bool TryGetSensedTrail(float maxAge, out Vector3 from, out Vector3 to)
    {
        from = to = Vector3.zero;

        int newest = -1, previous = -1;
        float newestTime = float.NegativeInfinity, previousTime = float.NegativeInfinity;
        float cutoff = Time.time - maxAge;

        for (int i = 0; i < nodes.Count; i++)
        {
            float stamp = lastSensedAt[i];
            if (stamp < cutoff) continue;

            if (stamp > newestTime)
            {
                previous = newest;         previousTime = newestTime;
                newest = i;                newestTime = stamp;
            }
            else if (stamp > previousTime)
            {
                previous = i;              previousTime = stamp;
            }
        }

        if (newest < 0 || previous < 0) return false;

        from = nodes[previous].Position;
        to = nodes[newest].Position;

        // Two waypoints on top of each other give a zero vector, which LookRotation and
        // normalisation both handle badly. Report "no usable trail" instead.
        return Vector3.SqrMagnitude(to - from) > 0.01f;
    }

    public bool TryGetNodeIndex(Transform waypoint, out int index)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Waypoint == waypoint)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    /// <summary>
    /// Fingerprint of the unlocked set. Compared against the stored one to decide whether a
    /// rebuild is needed: it changes when a route unlocks, when waypoints are added or removed at
    /// runtime, and when the controller's route list changes.
    /// </summary>
    public static string Fingerprint(IReadOnlyList<NemesisRoute> routes)
    {
        if (routes == null) return string.Empty;

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < routes.Count; i++)
        {
            NemesisRoute route = routes[i];
            if (route == null || !route.IsUnlocked) continue;

            // GetEntityId and not the deprecated GetInstanceID. Stringified rather than converted
            // to a number: EntityId exposes no conversion operator to int, and its ToULong is
            // documented as raw data whose "bit arrangement might change" — which is fine to
            // print and wrong to depend on. ToString costs a small allocation per route, and this
            // runs once per patrol cycle, not per frame.
            builder.Append(route.GetEntityId().ToString()).Append(':')
                   .Append(route.Waypoints.Count).Append('|');
        }
        return builder.ToString();
    }

    public bool NeedsRebuild(IReadOnlyList<NemesisRoute> routes) =>
        Fingerprint(routes) != fingerprint;

    /// <summary>
    /// Rebuilds the merged set and its clusters. Cheap to over-call: it does nothing when the
    /// fingerprint matches.
    /// </summary>
    /// <param name="clusterRadius">How far from a cluster's running centre a waypoint may sit and
    /// still join it. This is what "a cúmulo" means in metres.</param>
    /// <param name="maxClusterSize">Ceiling on how many waypoints one cluster may hold, so a dense
    /// zone does not swallow a whole floor.</param>
    /// <param name="force">Rebuild even when the fingerprint matches. For after a NavMesh rebake,
    /// which changes no route but does change what is reachable.</param>
    public void Rebuild(IReadOnlyList<NemesisRoute> routes, float clusterRadius,
                        int maxClusterSize, bool force = false)
    {
        // The cluster settings ride along in the fingerprint: retuning the radius in the SO while
        // in Play mode has to re-cluster, and nothing about the ROUTES changed to say so.
        string next = Fingerprint(routes) + $"#{clusterRadius:0.###}/{maxClusterSize}";
        if (!force && next == fingerprint) return;

        fingerprint = next;
        nodes.Clear();
        componentOf.Clear();
        representatives.Clear();
        clusters.Clear();
        clusterMembers.Clear();
        clusterOf.Clear();

        // Dropped with the rest, and that is correct rather than a loss: the stamps are indexed by
        // node, and the node list is about to be replaced. Carrying them over would attribute the
        // player's trail to whatever waypoints happen to land in those slots.
        lastSensedAt.Clear();

        componentCount = 0;

        if (routes == null) return;

        CollectValidNodes(routes);
        AssignComponents();
        BuildClusters(clusterRadius, maxClusterSize);
    }

    /// <summary>
    /// Flattens the unlocked routes, dropping what is unusable and saying why. A waypoint off the
    /// NavMesh is reported once here, at build time, instead of surfacing mid-run as a Nemesis
    /// that gets stuck and warps away.
    /// </summary>
    private void CollectValidNodes(IReadOnlyList<NemesisRoute> routes)
    {
        for (int r = 0; r < routes.Count; r++)
        {
            NemesisRoute route = routes[r];
            if (route == null || !route.IsUnlocked) continue;

            IReadOnlyList<Transform> waypoints = route.Waypoints;
            for (int w = 0; w < waypoints.Count; w++)
            {
                Transform waypoint = waypoints[w];
                if (waypoint == null) continue;

                if (!NemesisNav.IsOnNavMesh(waypoint.position))
                {
                    Debug.LogWarning($"[NemesisRouteGraph] Waypoint '{waypoint.name}' of route " +
                                     $"'{route.name}' does not land on the NavMesh (not even within " +
                                     $"{NemesisNav.DefaultSampleRadius}u). It is left out of the " +
                                     "graph and the Nemesis will never use it — drop it onto the " +
                                     "floor, or check that its area is baked.", waypoint);
                    continue;
                }

                nodes.Add(new Node(route, w, waypoint));

                // Kept in step with nodes so the two are always indexable by the same integer.
                // Negative infinity and not 0: 0 is a real Time.time value in the first frame.
                lastSensedAt.Add(float.NegativeInfinity);
            }
        }
    }

    /// <summary>
    /// Groups the nodes into NavMesh islands. Each node is tested against the representative of
    /// every island opened so far and joins the first one that accepts it; if none does, it opens
    /// a new island and becomes its representative.
    ///
    /// On a well-connected level that is one path query per node. The worst case (every waypoint
    /// isolated) is N squared, but that case is a broken level and is worth noticing.
    /// </summary>
    private void AssignComponents()
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            componentOf.Add(-1);

            Vector3 position = nodes[i].Position;

            for (int c = 0; c < representatives.Count; c++)
            {
                if (!NemesisNav.IsReachable(position, nodes[representatives[c]].Position)) continue;

                componentOf[i] = c;
                break;
            }

            if (componentOf[i] >= 0) continue;

            componentOf[i] = representatives.Count;
            representatives.Add(i);
        }

        componentCount = representatives.Count;

        if (componentCount > 1)
        {
            Debug.LogWarning($"[NemesisRouteGraph] The unlocked waypoints form {componentCount} " +
                             "NavMesh islands with no path between them. The Nemesis will only be " +
                             "able to move within the island it is standing on — if that is not the " +
                             "intent, stairs, ramps or NavMeshLinks are missing between them.");
        }
    }

    // ── Clustering ──────────────────────────────────────────────────────────

    /// <summary>
    /// Groups the nodes into compact spatial clusters, one island at a time.
    ///
    /// Greedy seed-and-grow rather than k-means: there is no k to pick here — how many zones a
    /// level has is a fact about where the waypoints are, not a number a designer should have to
    /// guess — and k-means would also happily straddle a wall, because it knows nothing about
    /// islands. This grows from a seed, never leaves the seed's island, and stops at the radius.
    ///
    /// Growth is measured from the cluster's RUNNING CENTROID and not from the seed. Measuring
    /// from the seed makes a cluster a circle around whichever node happened to start it, so a
    /// zone shaped like a corridor comes out cut in half; measuring from the centroid lets the
    /// group drift along the corridor as it absorbs it. Single-linkage (measuring from the
    /// nearest member) would do that too, and then keep going: one chain of waypoints two metres
    /// apart would swallow the entire floor.
    /// </summary>
    private void BuildClusters(float clusterRadius, int maxClusterSize)
    {
        for (int i = 0; i < nodes.Count; i++) clusterOf.Add(-1);

        if (nodes.Count == 0) return;

        float radius = Mathf.Max(0.5f, clusterRadius);
        float radiusSqr = radius * radius;
        int maxSize = Mathf.Max(1, maxClusterSize);
        bool useDensestSeed = nodes.Count <= DensestSeedNodeLimit;

        int assigned = 0;
        while (assigned < nodes.Count)
        {
            int seed = useDensestSeed ? FindDensestUnassigned(radiusSqr) : FindFirstUnassigned();
            if (seed < 0) break;   // Unreachable while assigned < count; never spin on a bad state.

            int first = clusterMembers.Count;
            int component = componentOf[seed];
            int clusterIndex = clusters.Count;

            Vector3 sum = nodes[seed].Position;
            float weightSum = RouteWeightOf(seed);

            clusterMembers.Add(seed);
            clusterOf[seed] = clusterIndex;
            assigned++;

            while (clusterMembers.Count - first < maxSize)
            {
                int size = clusterMembers.Count - first;
                int next = FindNearestUnassigned(sum / size, component, radiusSqr);
                if (next < 0) break;

                clusterMembers.Add(next);
                clusterOf[next] = clusterIndex;
                sum += nodes[next].Position;
                weightSum += RouteWeightOf(next);
                assigned++;
            }

            int count = clusterMembers.Count - first;
            clusters.Add(new Cluster(first, count, sum / count, component, weightSum / count));
        }

        WarnIfClusteringIsPointless(radius);
    }

    private float RouteWeightOf(int nodeIndex) =>
        nodes[nodeIndex].Route != null ? Mathf.Max(0f, nodes[nodeIndex].Route.Weight) : 0f;

    private int FindFirstUnassigned()
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (clusterOf[i] < 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// The unassigned node with the most unassigned neighbours of its own island inside the
    /// radius — the middle of the densest remaining knot of waypoints.
    ///
    /// Seeding at the densest node instead of the first one in iteration order is what keeps a
    /// zone from being carved up by whichever of its waypoints the loop reached first: start at
    /// the edge of a room and the far half of it spills into a second, thinner cluster.
    /// </summary>
    private int FindDensestUnassigned(float radiusSqr)
    {
        int best = -1;
        int bestNeighbours = -1;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (clusterOf[i] >= 0) continue;

            int neighbours = 0;
            for (int j = 0; j < nodes.Count; j++)
            {
                if (j == i || clusterOf[j] >= 0) continue;
                if (componentOf[j] != componentOf[i]) continue;
                if (Vector3.SqrMagnitude(nodes[j].Position - nodes[i].Position) <= radiusSqr) neighbours++;
            }

            if (neighbours <= bestNeighbours) continue;

            bestNeighbours = neighbours;
            best = i;
        }

        return best;
    }

    /// <summary>Closest unassigned node of <paramref name="component"/> to a point, or -1 when
    /// nothing unassigned is left inside the radius.</summary>
    private int FindNearestUnassigned(Vector3 centre, int component, float radiusSqr)
    {
        int best = -1;
        float bestSqr = radiusSqr;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (clusterOf[i] >= 0) continue;
            if (componentOf[i] != component) continue;

            float distanceSqr = Vector3.SqrMagnitude(nodes[i].Position - centre);
            if (distanceSqr > bestSqr) continue;

            bestSqr = distanceSqr;
            best = i;
        }

        return best;
    }

    /// <summary>
    /// Says so when the radius is so small that almost every waypoint became its own cluster.
    ///
    /// That case is not an error and nothing breaks — the Nemesis patrols clusters of one, which
    /// is exactly the waypoint-by-waypoint behaviour clusters exist to replace. It fails silently
    /// though: the feature is switched on, the inspector says so, and the monster moves the same
    /// as before. Worth one line at build time.
    /// </summary>
    private void WarnIfClusteringIsPointless(float radius)
    {
        if (clusters.Count == 0 || nodes.Count <= 3) return;

        float averageSize = (float)nodes.Count / clusters.Count;
        if (averageSize >= 1.5f) return;

        Debug.LogWarning($"[NemesisRouteGraph] Cluster radius {radius:0.##}u split {nodes.Count} " +
                         $"waypoints into {clusters.Count} clusters ({averageSize:0.##} waypoints " +
                         "each), so patrolling by cluster is the same as patrolling waypoint by " +
                         "waypoint. Raise Cluster Radius on the SO_NemesisData until a cluster " +
                         "covers a room or a corridor.");
    }

    public Cluster GetCluster(int index) => clusters[index];

    /// <summary>The cluster a node belongs to, or -1 when the index does not exist.</summary>
    public int ClusterOf(int nodeIndex) =>
        nodeIndex >= 0 && nodeIndex < clusterOf.Count ? clusterOf[nodeIndex] : -1;

    /// <summary>
    /// Fills <paramref name="results"/> with the node indices of one cluster. Writes into the
    /// caller's list for the same reason <see cref="CollectNodesInComponent"/> does: this runs
    /// every time the Nemesis moves on to another zone.
    /// </summary>
    public void CollectClusterMembers(int clusterIndex, List<int> results)
    {
        results.Clear();
        if (clusterIndex < 0 || clusterIndex >= clusters.Count) return;

        Cluster cluster = clusters[clusterIndex];
        for (int i = 0; i < cluster.MemberCount; i++)
        {
            results.Add(clusterMembers[cluster.FirstMember + i]);
        }
    }

    /// <summary>Every cluster sitting on the given NavMesh island — the zones the Nemesis can
    /// actually walk to from where it is standing.</summary>
    public void CollectClustersInComponent(int component, List<int> results)
    {
        results.Clear();
        if (component < 0) return;

        for (int i = 0; i < clusters.Count; i++)
        {
            if (clusters[i].Component != component) continue;
            results.Add(i);
        }
    }

    /// <summary>
    /// The NavMesh island an arbitrary point stands on (the Nemesis, typically).
    /// This is what makes the graph usable: ask "where am I" first, and from there everything
    /// picked is guaranteed reachable.
    /// </summary>
    public bool TryGetComponentAt(Vector3 position, out int component)
    {
        for (int c = 0; c < representatives.Count; c++)
        {
            if (!NemesisNav.IsReachable(position, nodes[representatives[c]].Position)) continue;

            component = c;
            return true;
        }

        component = -1;
        return false;
    }

    /// <summary>
    /// Fills <paramref name="results"/> with the nodes of the given island, skipping those of
    /// <paramref name="excludeRoute"/> when asked (for "give me waypoints from OTHER routes").
    /// Writes into the caller's list instead of returning a new one: this runs on every patrol
    /// replan and is not worth allocating for.
    /// </summary>
    public void CollectNodesInComponent(int component, List<int> results,
                                        NemesisRoute excludeRoute = null)
    {
        results.Clear();
        if (component < 0) return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (componentOf[i] != component) continue;
            if (excludeRoute != null && nodes[i].Route == excludeRoute) continue;

            results.Add(i);
        }
    }
}
