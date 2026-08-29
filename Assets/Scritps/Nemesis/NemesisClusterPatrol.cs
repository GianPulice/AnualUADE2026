using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Patrols by ZONE: picks a cúmulo of nearby waypoints out of a <see cref="NemesisRouteGraph"/>,
/// lays out the order it will sweep them in, and hands them back one at a time.
///
/// WHY THIS EXISTS AT ALL
///
/// The patrol used to pick ONE waypoint at a time out of the whole merged set, and that is what
/// made the Nemesis read as teleporting rather than prowling. Two consecutive indices of a route
/// in this level can be thirty metres apart, and the cross-route roll could hand it the far corner
/// of the floor on any arrival — so a player watching from a doorway sees the monster cross the
/// room, leave, and reappear somewhere unrelated. Committing to a cúmulo turns the same waypoints
/// into "it is sweeping that area now", and then moving to a cúmulo NEXT DOOR turns the patrol
/// into a walk through the level instead of a series of jumps.
///
/// WHY IT IS NOT A MonoBehaviour
///
/// Same reasoning as <see cref="NemesisRouteGraph"/>, which it sits next to: it holds no scene
/// references, needs no Update, and nothing about it belongs on a GameObject. Keeping it a plain
/// class means it can be constructed, driven and read from anywhere — a test, an editor tool, a
/// second controller — without a scene, and it keeps <see cref="NemesisController"/> from growing
/// another two hundred lines of maths it does not own. The controller holds one of these, feeds it
/// the graph and the tuning, and does nothing with the result but point the agent at it.
///
/// It never touches the NavMeshAgent, the FSM or the Transform. Everything it needs arrives as
/// arguments; everything it decides leaves as a graph node index.
/// </summary>
public sealed class NemesisClusterPatrol
{
    /// <summary>
    /// Everything the rolls need, gathered in one place so the calls do not carry nine loose
    /// floats. Built per call from <see cref="SO_NemesisData"/> plus whatever the Nemesis
    /// currently believes about the player — this class deliberately knows nothing about sensors.
    /// </summary>
    public readonly struct Settings
    {
        /// <summary>Whether <see cref="Belief"/> means anything. False when the Nemesis has
        /// neither seen nor heard the player, and then the rolls use the route weights alone.</summary>
        public readonly bool HasBelief;

        /// <summary>Where the Nemesis BELIEVES the player is — the last position it sensed, not
        /// the real one.</summary>
        public readonly Vector3 Belief;

        /// <summary>How many times more likely a zone becomes when the belief is on top of it.
        /// Already decayed by how stale the belief is, so this class does not need a clock.</summary>
        public readonly float PlayerBiasStrength;

        public readonly float PlayerBiasFalloff;

        /// <summary>How many times more likely a zone becomes for being next door. 1 disables it,
        /// which is what a fresh patrol cycle passes.</summary>
        public readonly float NeighbourBiasStrength;

        public readonly float NeighbourBiasFalloff;

        /// <summary>How many candidate zones are evaluated with real path distance. The rest are
        /// dropped by a free straight-line prefilter.</summary>
        public readonly int SampleCount;

        public readonly int MinWaypoints;
        public readonly int MaxWaypoints;

        public Settings(bool hasBelief, Vector3 belief,
                        float playerBiasStrength, float playerBiasFalloff,
                        float neighbourBiasStrength, float neighbourBiasFalloff,
                        int sampleCount, int minWaypoints, int maxWaypoints)
        {
            HasBelief = hasBelief;
            Belief = belief;
            PlayerBiasStrength = Mathf.Max(1f, playerBiasStrength);
            PlayerBiasFalloff = Mathf.Max(1f, playerBiasFalloff);
            NeighbourBiasStrength = Mathf.Max(1f, neighbourBiasStrength);
            NeighbourBiasFalloff = Mathf.Max(1f, neighbourBiasFalloff);
            SampleCount = Mathf.Max(2, sampleCount);
            MinWaypoints = Mathf.Max(1, minWaypoints);
            MaxWaypoints = Mathf.Max(1, maxWaypoints);
        }

        /// <summary>
        /// Reads the tuning off the asset and folds the belief in.
        /// </summary>
        /// <param name="beliefFreshness">1 the instant the player is sensed, 0 once the belief is
        /// BeliefMemoryTime old. It scales the player bias down to nothing, which is what stops
        /// the patrol orbiting the room it lost the player in for the rest of the run.</param>
        /// <param name="applyNeighbourBias">False for a fresh patrol cycle, which should be free
        /// to relocate anywhere; true when moving from one cúmulo to the next.</param>
        public static Settings From(SO_NemesisData data, bool hasBelief, Vector3 belief,
                                    float beliefFreshness, bool applyNeighbourBias)
        {
            // The fallbacks are only reached when the asset is missing, which the Nemesis already
            // reports elsewhere. They exist so a broken prefab still patrols instead of standing
            // still on a strength of 0.
            float playerStrength = data != null ? data.RoutePlayerBiasStrength : 1f;
            float neighbourStrength = applyNeighbourBias && data != null ? data.ClusterNeighbourBias : 1f;

            return new Settings(
                hasBelief, belief,
                Mathf.Lerp(1f, Mathf.Max(1f, playerStrength), Mathf.Clamp01(beliefFreshness)),
                data != null ? data.RoutePlayerBiasFalloff : 1f,
                neighbourStrength,
                data != null ? data.ClusterNeighbourFalloff : 25f,
                data != null ? data.WaypointBiasSampleCount : 8,
                data != null ? data.ClusterMinWaypoints : 3,
                data != null ? data.ClusterMaxWaypoints : 6);
        }
    }

    /// <summary>Cluster being swept, or -1 when there is none.</summary>
    private int currentCluster = -1;

    /// <summary>Graph node indices, in the order this visit sweeps them.</summary>
    private readonly List<int> tour = new List<int>();

    private int tourIndex;

    /// <summary>How many of the tour's waypoints this visit will actually walk before moving on.
    /// Rolled per visit between MinWaypoints and MaxWaypoints.</summary>
    private int tourBudget;

    // Reused across calls: this runs every time the Nemesis finishes a zone, and none of it is
    // worth allocating for. Same reasoning as NemesisController's own selection buffers.
    private readonly List<int> candidateBuffer = new List<int>();
    private readonly List<int> sampledBuffer = new List<int>();
    private readonly List<int> memberBuffer = new List<int>();
    private readonly List<float> weightBuffer = new List<float>();
    private readonly List<float> keyBuffer = new List<float>();
    private readonly List<Vector3> positionBuffer = new List<Vector3>();

    public bool HasCluster => currentCluster >= 0;

    /// <summary>The cluster being swept, or -1. For gizmos and debugging.</summary>
    public int CurrentCluster => currentCluster;

    /// <summary>The sweep order, as graph node indices. For gizmos and debugging.</summary>
    public IReadOnlyList<int> Tour => tour;

    /// <summary>How far into <see cref="Tour"/> the sweep is.</summary>
    public int TourIndex => tourIndex;

    /// <summary>How many of <see cref="Tour"/>'s entries this visit will walk. Entries past it
    /// belong to the cúmulo but are being left for another visit.</summary>
    public int TourBudget => tourBudget;

    /// <summary>
    /// Forgets the current cluster. Call it whenever the graph is rebuilt: cluster and node
    /// indices both refer to lists that are about to be replaced, and reusing them would point
    /// the sweep at whatever happens to land in those slots.
    /// </summary>
    public void Reset()
    {
        currentCluster = -1;
        tour.Clear();
        tourIndex = 0;
        tourBudget = 0;
    }

    /// <summary>
    /// Rolls for a cúmulo on the given island and starts sweeping it.
    /// </summary>
    /// <param name="origin">Where the Nemesis is standing. Both the "next door" bias and the
    /// sweep's entry point are measured from it.</param>
    /// <param name="component">NavMesh island the Nemesis is on. Nothing outside it is a
    /// candidate, which is what guarantees whatever comes back is genuinely reachable.</param>
    /// <param name="direction">+1 enters the zone at its near side, -1 at its far side. This is
    /// what RouteReverseChance means for a cúmulo — see <see cref="BuildTour"/>.</param>
    /// <param name="pendingSkip">This cycle's skip roll. Consumed (set to false) when it is spent
    /// on dropping a waypoint from the sweep.</param>
    /// <param name="excludeCurrent">Drop the zone being swept from the draw. True when this visit
    /// is OVER and the question is "where next"; false for a re-plan, which is a fresh decision
    /// and may perfectly well land on the zone it is already in.
    ///
    /// It has to be a parameter and not "always exclude": BeginPatrolCycle runs again every
    /// RouteReplanInterval seconds without the Nemesis having left Patrolling, and excluding the
    /// current zone there would force a relocation every interval no matter what the waypoint
    /// budget said — which quietly turns MinWaypoints/MaxWaypoints into decoration.</param>
    /// <returns>Graph node index to walk to, or -1 when the island has no usable cúmulo.</returns>
    public int Begin(NemesisRouteGraph graph, in Settings settings, Vector3 origin, int component,
                     int direction, ref bool pendingSkip, bool excludeCurrent)
    {
        if (graph == null) return -1;

        graph.CollectClustersInComponent(component, candidateBuffer);

        // Re-rolling the zone just finished is what Resweep is for, and only once nothing else
        // is left. Guarded on Count > 1 so a one-cluster island still returns something.
        if (excludeCurrent && currentCluster >= 0 && candidateBuffer.Count > 1)
            candidateBuffer.Remove(currentCluster);

        if (candidateBuffer.Count == 0) return -1;

        int picked = PickCluster(graph, candidateBuffer, origin, settings);
        if (picked < 0) return -1;

        return Adopt(graph, picked, settings, origin, direction, ref pendingSkip);
    }

    /// <summary>
    /// Lays out a fresh sweep of the cluster already being patrolled.
    ///
    /// For the case where the island holds only one cúmulo: without it the Nemesis would finish
    /// its budget and have nowhere to go. The tour is rebuilt from where it is standing NOW, so it
    /// comes out in a different order and it re-walks the zone rather than freezing on the last
    /// waypoint of the old sweep.
    /// </summary>
    /// <returns>Graph node index to walk to, or -1 when there is no cluster to re-sweep.</returns>
    public int Resweep(NemesisRouteGraph graph, in Settings settings, Vector3 origin, int direction)
    {
        if (graph == null || currentCluster < 0) return -1;

        bool noSkip = false;
        return Adopt(graph, currentCluster, settings, origin, direction, ref noSkip);
    }

    /// <summary>
    /// Steps the sweep on to the next waypoint.
    /// </summary>
    /// <returns>The next graph node index, or -1 when this visit is over — the caller should then
    /// call <see cref="Begin"/> for a neighbouring cúmulo.</returns>
    public int Advance()
    {
        if (currentCluster < 0) return -1;

        tourIndex++;
        if (tourIndex >= tour.Count || tourIndex >= tourBudget) return -1;

        return tour[tourIndex];
    }

    /// <summary>Takes over a cluster and lays out the order this visit will sweep it in.</summary>
    /// <returns>The first node of the sweep, or -1 when the cluster turned out to be empty.</returns>
    private int Adopt(NemesisRouteGraph graph, int clusterIndex, in Settings settings,
                      Vector3 origin, int direction, ref bool pendingSkip)
    {
        graph.CollectClusterMembers(clusterIndex, memberBuffer);
        if (memberBuffer.Count == 0) return -1;

        currentCluster = clusterIndex;
        tourIndex = 0;

        BuildTour(graph, origin, direction);
        if (tour.Count == 0)
        {
            currentCluster = -1;
            return -1;
        }

        // The skip roll, translated to a zone: drop one of its waypoints from this visit. A cúmulo
        // has no authored order to "skip ahead" in, so skipping a step would mean nothing; leaving
        // a hole in the sweep is the same idea — the same room walked slightly differently. Never
        // the first one, which is the entry point the whole tour was built around.
        if (pendingSkip && tour.Count > 2)
        {
            tour.RemoveAt(Random.Range(1, tour.Count));
            pendingSkip = false;
        }

        tourBudget = Mathf.Min(tour.Count,
                               Random.Range(settings.MinWaypoints, settings.MaxWaypoints + 1));

        return tour[0];
    }

    /// <summary>
    /// Orders the cúmulo's waypoints into a sweep: a nearest-neighbour chain from an entry point.
    ///
    /// Nearest-neighbour and not the routes' own indices, because a cluster is SPATIAL — its
    /// members come from whichever routes cover that corner of the level, in whatever order those
    /// routes happened to be authored in. Following the authored order inside a zone has the
    /// Nemesis crossing its own path over and over inside one room. The chain is not optimal (it is
    /// the classic greedy travelling-salesman opening) and does not need to be: for the handful of
    /// waypoints a cluster holds it is the difference between a sweep and a scribble.
    ///
    /// The entry point is the member nearest the Nemesis, or the FARTHEST one when this cycle
    /// rolled a reverse. That is what RouteReverseChance means here: a cluster has no direction to
    /// invert, but entering from its far side and working back changes the shape of the sweep just
    /// as much as walking a polyline backwards does.
    ///
    /// Consumes <see cref="memberBuffer"/> as it goes — the caller has just refilled it.
    /// </summary>
    private void BuildTour(NemesisRouteGraph graph, Vector3 origin, int direction)
    {
        tour.Clear();

        int start = TakeExtremeMember(graph, origin, farthest: direction < 0);
        if (start < 0) return;

        tour.Add(start);

        while (memberBuffer.Count > 0)
        {
            Vector3 from = graph.GetNode(tour[tour.Count - 1]).Position;

            int next = TakeExtremeMember(graph, from, farthest: false);
            if (next < 0) break;

            tour.Add(next);
        }
    }

    /// <summary>
    /// Removes and returns the member nearest to — or farthest from — a point, or -1 when none
    /// are left.
    ///
    /// Straight-line distance: every member of a cluster is a few metres from every other and on
    /// the same island already, so a path query per pair would buy nothing but a stall.
    /// </summary>
    private int TakeExtremeMember(NemesisRouteGraph graph, Vector3 from, bool farthest)
    {
        int best = -1;
        float bestSqr = farthest ? -1f : float.PositiveInfinity;

        for (int i = 0; i < memberBuffer.Count; i++)
        {
            float distanceSqr = Vector3.SqrMagnitude(graph.GetNode(memberBuffer[i]).Position - from);

            if (farthest ? distanceSqr <= bestSqr : distanceSqr >= bestSqr) continue;

            bestSqr = distanceSqr;
            best = i;
        }

        if (best < 0) return -1;

        int node = memberBuffer[best];
        memberBuffer.RemoveAt(best);
        return node;
    }

    /// <summary>
    /// Weighted roll among candidate cúmulos: route weight, times the player bias, times the
    /// "next door" bonus when one applies.
    ///
    /// It stays a roll and not an argmax for the same reason the per-waypoint pick does: "the zone
    /// you are in gets more tickets" reads as the Nemesis prowling around you, "it always goes
    /// where you are" reads as it seeing you through walls.
    ///
    /// Measured on centroids, so this is ONE pair of path queries per candidate ZONE where the
    /// per-waypoint pick paid them per WAYPOINT. Clustering makes the bias cheaper, not dearer.
    /// </summary>
    /// <returns>Cluster index, or -1.</returns>
    private int PickCluster(NemesisRouteGraph graph, List<int> candidates, Vector3 origin,
                            in Settings settings)
    {
        positionBuffer.Clear();
        for (int i = 0; i < candidates.Count; i++)
            positionBuffer.Add(graph.GetCluster(candidates[i]).Centroid);

        KeepClosest(candidates, positionBuffer, origin, settings.HasBelief, settings.Belief,
                    settings.SampleCount, sampledBuffer, keyBuffer);

        if (sampledBuffer.Count == 0) return -1;

        weightBuffer.Clear();
        float total = 0f;

        for (int i = 0; i < sampledBuffer.Count; i++)
        {
            NemesisRouteGraph.Cluster cluster = graph.GetCluster(sampledBuffer[i]);
            float weight = Mathf.Max(0f, cluster.Weight);

            if (weight > 0f && settings.HasBelief && settings.PlayerBiasStrength > 1f)
            {
                weight *= ProximityWeight(cluster.Centroid, settings.Belief,
                                          settings.PlayerBiasStrength, settings.PlayerBiasFalloff);
            }

            if (weight > 0f && settings.NeighbourBiasStrength > 1f)
            {
                weight *= ProximityWeight(cluster.Centroid, origin,
                                          settings.NeighbourBiasStrength, settings.NeighbourBiasFalloff);
            }

            weightBuffer.Add(weight);
            total += weight;
        }

        // Every candidate zone sits at weight 0 — the designer switched those routes off — but it
        // still has to go somewhere. Uniform among the survivors.
        if (total <= 0f) return sampledBuffer[Random.Range(0, sampledBuffer.Count)];

        float roll = Random.value * total;
        float cumulative = 0f;
        for (int i = 0; i < sampledBuffer.Count; i++)
        {
            cumulative += weightBuffer[i];
            if (roll <= cumulative) return sampledBuffer[i];
        }

        // Only reachable through float rounding at the very edge of the range.
        return sampledBuffer[sampledBuffer.Count - 1];
    }

    // ── Shared selection maths ──────────────────────────────────────────────
    //
    // Public and static because NemesisController's per-waypoint pick runs exactly the same two
    // steps on nodes that this runs on clusters. They live here rather than on the controller so
    // the auxiliary class is readable on its own, and so the two rolls cannot quietly drift apart
    // — the one thing that would make "clusters on" and "clusters off" behave differently for a
    // reason nobody chose.

    /// <summary>
    /// Weight multiplier from closeness to an anchor, measured over the NavMesh.
    ///
    /// Returns 1 (no bias) when the point is unreachable from the anchor: that is not "far", it is
    /// "not comparable", and penalising it would drop it out of the roll for a reason that has
    /// nothing to do with the zone's design.
    /// </summary>
    public static float ProximityWeight(Vector3 point, Vector3 anchor, float strength, float falloff)
    {
        if (!NemesisNav.TryGetPathDistance(point, anchor, out float distance)) return 1f;

        float t = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, falloff));
        return Mathf.Lerp(1f, strength, t);
    }

    /// <summary>
    /// Trims candidates down to the <paramref name="sampleCount"/> most promising ones using
    /// straight-line distance, which is free, so as not to pay two path queries for every
    /// candidate in the level.
    ///
    /// The key is the minimum of "close to me" and "close to the player": keeping only what is
    /// near the Nemesis would discard precisely the candidates on the player's floor, which are
    /// the interesting ones. One floor up is close in a straight line, so it survives the
    /// prefilter and is then evaluated properly, with real path distance.
    ///
    /// Positions arrive as a parallel list rather than being read off the graph, so the same
    /// filter serves both rolls: node positions for the per-waypoint pick, cluster centroids for
    /// the per-zone one. <paramref name="results"/> and <paramref name="keyScratch"/> are the
    /// caller's reused buffers — nothing here allocates.
    /// </summary>
    public static void KeepClosest(List<int> candidates, List<Vector3> positions, Vector3 origin,
                                   bool hasBelief, Vector3 belief, int sampleCount,
                                   List<int> results, List<float> keyScratch)
    {
        results.Clear();

        if (candidates.Count <= sampleCount)
        {
            results.AddRange(candidates);
            return;
        }

        // Sorted insertion into a fixed-size list: simpler and cheaper than sorting the whole list
        // just to keep the first eight.
        keyScratch.Clear();

        for (int i = 0; i < candidates.Count; i++)
        {
            Vector3 position = positions[i];

            float key = Vector3.SqrMagnitude(position - origin);
            if (hasBelief) key = Mathf.Min(key, Vector3.SqrMagnitude(position - belief));

            int insertAt = results.Count;
            while (insertAt > 0 && keyScratch[insertAt - 1] > key) insertAt--;

            if (insertAt >= sampleCount) continue;

            results.Insert(insertAt, candidates[i]);
            keyScratch.Insert(insertAt, key);

            if (results.Count <= sampleCount) continue;

            results.RemoveAt(results.Count - 1);
            keyScratch.RemoveAt(keyScratch.Count - 1);
        }
    }
}
