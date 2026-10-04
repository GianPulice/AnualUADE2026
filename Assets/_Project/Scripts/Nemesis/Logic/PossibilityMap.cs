using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "How possible is it that the player is HERE", for every node of a <see cref="PossibilityGraph"/>
/// (Plan-Busqueda-Nemesis §3, Damián Isla's occupancy maps). The belief says where the Nemesis sensed
/// the player; this says where they can be now.
///
/// THE RULES, in the order a tick applies them (§3.2):
///   1. EVIDENCE puts the value. A sighting puts all of it on the spot (<see cref="SeedPoint"/>), with
///      the direction the player was seen moving. A noise puts it over the area it was heard in
///      (<see cref="SeedArea"/>): half kept from what the map already had inside that area, half
///      spread evenly over it — hearing the same footsteps twenty times never makes it sharper than
///      one (the belief's rule in Fase 1). Nothing outside the area survives a noise: the player is in
///      it now. Leads never get here: a decoy is never the player (D18).
///   2. IT SPREADS along walkable edges, as fast as the player could run (<see cref="Spread"/>). It
///      never crosses a wall because there is no edge through one. For a few seconds after a sighting
///      it spreads faster the way the player was seen going, and that bias fades on its own.
///   3. WHAT IT LOOKS AT, IT CLEARS (<see cref="ClearSeen"/>): "not here". A closed hiding spot is
///      never cleared by looking at it, only by opening it (<see cref="Open"/>). So when everything in
///      view comes up empty, what is left is past the exit it could not see — or inside a locker.
///   4. THE HUB DRAINS (<see cref="Drain"/>): value reaching its door goes into a sink, "went into the
///      Hub", and never comes back out (C5). Without it the door would collect the value and keep a
///      search camping the one place the game promises is safe.
///   5. RENORMALISE (<see cref="Normalize"/>), sink included: "not where I looked" makes everywhere
///      else more likely.
///
/// WHO READS IT (§3.4, Fase 2b). The search goes where the value still is: <see cref="CollectZones"/>
/// cuts what is left on the floor into a few places worth walking to, and the caller rolls among
/// them by value ÷ (1 + time to get there). Looking at a place clears it (rule 3), so the map is also
/// the search's only memory of where it has already been.
///
/// PURE: no NavMesh, no sensors, no Time — the caller passes the clock.
/// </summary>
public sealed class PossibilityMap
{
    /// <summary>Value below this share of what is left, after a tick, is too unlikely to matter and is
    /// dropped. Keeps renormalisation from blowing numerical dust up into a "most likely place".
    /// Relative, not absolute: when its eyes have just cleared most of the map, what survives is
    /// small in absolute terms and is exactly what it must keep.</summary>
    public const float Dust = 1e-6f;

    /// <summary>The most of a node's value that may leave it in one step. Above this the explicit
    /// step overshoots; a long tick is split into several steps instead.</summary>
    private const float MaxOutflowPerStep = 0.6f;

    /// <summary>How much of a noise's update is the map's own prior inside the heard area; the rest is
    /// the area itself, evenly. See rule 1.</summary>
    private const float NoisePriorWeight = 0.5f;

    /// <summary>
    /// How much more slowly value leaves a hiding spot than a floor point. Someone who climbs into a
    /// locker stays a while; a hiding node that emptied as fast as it filled could never hold what
    /// "everything in view was empty" leaves behind (D38).
    /// </summary>
    private const float HidingSpotOutflowScale = 0.1f;

    /// <summary>Floor of an edge's heading weight, so the way back is slow, not closed.</summary>
    private const float MinHeadingWeight = 0.05f;

    /// <summary>Below this speed a sighting gives no heading: standing still is not a direction.
    /// </summary>
    private const float MinHeadingSpeed = 0.3f;

    /// <summary>A zone gathering less than this share is not a place to walk to, it is the tail of the
    /// spread. Keeps <see cref="CollectZones"/> from handing back the leftovers of the zones it has
    /// already cut as if they were somewhere to look.</summary>
    private const float MinZoneShare = 1e-4f;

    /// <summary>Two zones whose sums differ by less than this fraction gather "the same": see the
    /// tie-break in <see cref="CollectZones"/>.</summary>
    private const float ZoneTieTolerance = 1e-3f;

    private readonly PossibilityGraph graph;
    private float[] value;
    private float[] next;
    private readonly bool[] gateOpen;
    private readonly List<int> scratch = new List<int>();
    private readonly List<float> kernel = new List<float>();

    // CollectZones' working set. Kept here so a pick allocates nothing.
    private readonly float[] zoneRemaining;
    private readonly float[] zoneSum;
    private readonly List<int> zoneTouched = new List<int>();
    private readonly List<int> zoneMembers = new List<int>();

    private float sink;
    private Vector3 heading;
    private float headingSince = float.NegativeInfinity;

    public PossibilityMap(PossibilityGraph graph)
    {
        if (graph == null) throw new System.ArgumentNullException(nameof(graph));
        if (!graph.IsFrozen) graph.Freeze();

        this.graph = graph;
        value = new float[graph.NodeCount];
        next = new float[graph.NodeCount];
        zoneRemaining = new float[graph.NodeCount];
        zoneSum = new float[graph.NodeCount];
        gateOpen = new bool[graph.GateCount];
        for (int i = 0; i < gateOpen.Length; i++) gateOpen[i] = true;
    }

    public PossibilityGraph Graph => graph;

    /// <summary>Whether it believes anything at all. False before the first evidence, after a
    /// capture, and once everything it could believe has been looked at.</summary>
    public bool HasValue { get; private set; }

    public float Value(int node) => value[node];

    /// <summary>The share that went into the Hub (rule 4).</summary>
    public float SinkValue => sink;

    /// <summary>The flat direction the player was last seen moving, or zero.</summary>
    public Vector3 Heading => heading;

    public bool IsGateOpen(int gate) => gate < 0 || gateOpen[gate];

    public void SetGateOpen(int gate, bool open)
    {
        if (gate >= 0 && gate < gateOpen.Length) gateOpen[gate] = open;
    }

    /// <summary>Believes nothing. The capture and the respawn: whatever it believed is now false.
    /// </summary>
    public void Clear()
    {
        System.Array.Clear(value, 0, value.Length);
        sink = 0f;
        heading = Vector3.zero;
        headingSince = float.NegativeInfinity;
        HasValue = false;
    }

    // ── 1. Evidence ──────────────────────────────────────────────────────────

    /// <summary>
    /// A sighting: all of the value on the floor nodes around <paramref name="point"/>, weighted by
    /// closeness, and the direction it saw the player moving. Returns false when there is no floor
    /// node near enough to put it on (off the map: the map is left as it was).
    /// </summary>
    public bool SeedPoint(Vector3 point, float floorBand, Vector3 velocity, float now)
    {
        float spacing = graph.Spacing;
        graph.CollectNear(point, spacing, floorBand, scratch);

        // Seen means out in the open: never inside a hiding spot.
        for (int i = scratch.Count - 1; i >= 0; i--)
            if (graph.Kind(scratch[i]) != PossibilityGraph.ENodeKind.Floor) scratch.RemoveAt(i);

        kernel.Clear();
        float sum = 0f;
        for (int i = 0; i < scratch.Count; i++)
        {
            float d = FlatDistance(graph.Position(scratch[i]), point);
            float w = Mathf.Max(0.05f, 1f - d / spacing);
            kernel.Add(w);
            sum += w;
        }

        if (scratch.Count == 0)
        {
            int nearest = graph.FindNearest(point, spacing * 3f, floorBand, PossibilityGraph.ENodeKind.Floor,
                                            scratch);
            if (nearest < 0) return false;

            scratch.Clear();
            scratch.Add(nearest);
            kernel.Add(1f);
            sum = 1f;
        }

        System.Array.Clear(value, 0, value.Length);
        sink = 0f;
        for (int i = 0; i < scratch.Count; i++) value[scratch[i]] = kernel[i] / sum;
        HasValue = true;

        Vector3 flat = velocity;
        flat.y = 0f;
        heading = flat.magnitude >= MinHeadingSpeed ? flat.normalized : Vector3.zero;
        headingSince = now;
        return true;
    }

    /// <summary>
    /// A noise heard around <paramref name="centre"/> with doubt <paramref name="radius"/>: the value
    /// goes onto the nodes inside that area (hiding spots included — a breath comes out of one). See
    /// rule 1 for the half-and-half. If what the map had inside the area is nothing — the noise is
    /// somewhere it had ruled out, or it believed nothing — the area replaces it outright, the same as
    /// the belief does with a noise that cannot be the same spot. Returns false when no node is near.
    /// </summary>
    public bool SeedArea(Vector3 centre, float radius, float floorBand)
    {
        float spacing = graph.Spacing;
        float r = Mathf.Max(radius, spacing * 0.5f);

        graph.CollectNear(centre, r + spacing, floorBand, scratch);

        // The prior is what the map had INSIDE the area, unweighted: weighting it by the kernel again
        // on every noise would sharpen the edges a little each time — the precision for free rule 1
        // forbids. Unweighted, the same noise heard again leaves the map exactly as it was.
        kernel.Clear();
        float kernelSum = 0f;
        float prior = 0f;
        for (int i = 0; i < scratch.Count; i++)
        {
            int node = scratch[i];
            float d = FlatDistance(graph.Position(node), centre);
            float k = d <= r ? 1f : Mathf.Clamp01(1f - (d - r) / spacing);
            kernel.Add(k);
            kernelSum += k;
            if (k > 0f) prior += value[node];
        }

        if (kernelSum <= 0f)
        {
            int nearest = graph.FindNearest(centre, r + spacing * 3f, floorBand,
                                            PossibilityGraph.ENodeKind.Floor, scratch);
            if (nearest < 0) return false;

            scratch.Clear();
            scratch.Add(nearest);
            kernel.Clear();
            kernel.Add(1f);
            kernelSum = 1f;
            prior = value[nearest];
        }

        bool keepPrior = HasValue && prior > 1e-4f;

        System.Array.Clear(next, 0, next.Length);
        for (int i = 0; i < scratch.Count; i++)
        {
            if (kernel[i] <= 0f) continue;

            int node = scratch[i];
            float even = kernel[i] / kernelSum;
            next[node] = keepPrior
                ? NoisePriorWeight * (value[node] / prior) + (1f - NoisePriorWeight) * even
                : even;
        }

        Swap();
        sink = 0f;
        HasValue = true;
        return true;
    }

    /// <summary>Evidence from inside the Hub: all of it in the sink. The search has nowhere to go.
    /// </summary>
    public void SeedSink()
    {
        System.Array.Clear(value, 0, value.Length);
        sink = 1f;
        HasValue = true;
    }

    // ── 2. Spreading ─────────────────────────────────────────────────────────

    /// <summary>
    /// Lets the value run along the open edges for <paramref name="dt"/> seconds at
    /// <paramref name="speed"/> m/s, faster along the last seen heading while it lasts.
    /// </summary>
    /// <param name="headingBias">How much the heading tilts the spread: 1 is twice as fast ahead and
    /// almost nothing behind, at the moment of the sighting.</param>
    /// <param name="headingDuration">Seconds after the sighting in which the tilt fades to nothing.
    /// </param>
    public void Spread(float dt, float speed, float now, float headingBias, float headingDuration)
    {
        if (!HasValue || dt <= 0f || speed <= 0f) return;

        float fraction = speed * dt / graph.Spacing;
        int steps = Mathf.Max(1, Mathf.CeilToInt(fraction / MaxOutflowPerStep));
        float outflow = fraction / steps;
        float tilt = HeadingTilt(now, headingBias, headingDuration);

        for (int s = 0; s < steps; s++) Step(outflow, tilt);
    }

    /// <summary>How strongly the heading tilts the spread right now: full at the sighting, nothing
    /// after <paramref name="duration"/> seconds.</summary>
    public float HeadingTilt(float now, float bias, float duration)
    {
        if (heading == Vector3.zero || bias <= 0f || duration <= 0f) return 0f;

        float age = now - headingSince;
        return bias * Mathf.Clamp01(1f - age / duration);
    }

    private void Step(float outflow, float tilt)
    {
        System.Array.Clear(next, 0, next.Length);

        for (int i = 0; i < value.Length; i++)
        {
            float v = value[i];
            if (v <= 0f) continue;

            float weights = 0f;
            int begin = graph.EdgesBegin(i);
            int end = graph.EdgesEnd(i);
            for (int e = begin; e < end; e++) weights += EdgeWeight(e, tilt);

            if (weights <= 0f)
            {
                next[i] += v;
                continue;
            }

            float share = graph.Kind(i) == PossibilityGraph.ENodeKind.HidingSpot
                ? outflow * HidingSpotOutflowScale
                : outflow;
            float leaving = v * share;
            next[i] += v - leaving;

            for (int e = begin; e < end; e++)
            {
                float w = EdgeWeight(e, tilt);
                if (w > 0f) next[graph.EdgeTarget(e)] += leaving * w / weights;
            }
        }

        Swap();
    }

    /// <summary>Zero when the edge's gate is closed; otherwise shorter edges and edges along the
    /// heading carry more.</summary>
    private float EdgeWeight(int edge, float tilt)
    {
        if (!IsGateOpen(graph.EdgeGate(edge))) return 0f;

        float w = graph.Spacing / graph.EdgeLength(edge);
        if (tilt > 0f)
            w *= Mathf.Max(MinHeadingWeight, 1f + tilt * Vector3.Dot(graph.EdgeDirection(edge), heading));
        return w;
    }

    // ── 3. Clearing ──────────────────────────────────────────────────────────

    /// <summary>"Not here": a floor node it is looking at. A hiding spot is left alone — looking at a
    /// closed locker says nothing about who is inside. Returns whether there was anything to clear.
    /// </summary>
    public bool ClearSeen(int node)
    {
        if (graph.Kind(node) != PossibilityGraph.ENodeKind.Floor) return false;
        if (value[node] <= 0f) return false;

        value[node] = 0f;
        return true;
    }

    /// <summary>It opened this spot (or stood on this point) and nobody was there.</summary>
    public void Open(int node) => value[node] = 0f;

    // ── 4. The Hub ───────────────────────────────────────────────────────────

    /// <summary>Moves whatever reached a drain node into the sink (rule 4).</summary>
    public void Drain()
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (!graph.IsDrain(i) || value[i] <= 0f) continue;

            sink += value[i];
            value[i] = 0f;
        }
    }

    // ── 5. Renormalising ─────────────────────────────────────────────────────

    /// <summary>Drops the dust and makes everything, sink included, add up to 1. With nothing left it
    /// believes nothing (<see cref="HasValue"/> false).</summary>
    public void Normalize()
    {
        float before = sink;
        for (int i = 0; i < value.Length; i++) before += value[i];

        if (before <= 1e-12f)
        {
            Clear();
            return;
        }

        float dust = Dust * before;
        float total = sink;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] < dust) value[i] = 0f;
            total += value[i];
        }

        if (total <= 1e-12f)
        {
            Clear();
            return;
        }

        float inv = 1f / total;
        for (int i = 0; i < value.Length; i++) value[i] *= inv;
        sink *= inv;
        HasValue = true;
    }

    // ── Reading it ───────────────────────────────────────────────────────────

    /// <summary>The node with the most value, or -1.</summary>
    public int BestNode()
    {
        int best = -1;
        float bestValue = 0f;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] <= bestValue) continue;
            bestValue = value[i];
            best = i;
        }

        return best;
    }

    /// <summary>The value within <paramref name="radius"/> of a point (plan view, floor band).</summary>
    public float SumNear(Vector3 point, float radius, float floorBand)
    {
        graph.CollectNear(point, radius, floorBand, scratch);

        float sum = 0f;
        for (int i = 0; i < scratch.Count; i++) sum += value[scratch[i]];
        return sum;
    }

    /// <summary>The value on nodes within <paramref name="band"/> of height <paramref name="y"/>.
    /// </summary>
    public float SumOnLevel(float y, float band)
    {
        float sum = 0f;
        for (int i = 0; i < value.Length; i++)
            if (Mathf.Abs(graph.Position(i).y - y) <= band) sum += value[i];
        return sum;
    }

    /// <summary>The value on nodes of one kind: all of it inside hiding spots, for instance.</summary>
    public float SumOfKind(PossibilityGraph.ENodeKind kind)
    {
        float sum = 0f;
        for (int i = 0; i < value.Length; i++)
            if (graph.Kind(i) == kind) sum += value[i];
        return sum;
    }

    /// <summary>
    /// How spread out it is, as a number of nodes: 1 when it is sure of one place, N when it is spread
    /// evenly over N (the inverse of the sum of squares, sink counted as one place). Times
    /// Spacing² it is roughly the area it still has to search.
    /// </summary>
    public float EffectiveNodeCount
    {
        get
        {
            float squares = sink * sink;
            for (int i = 0; i < value.Length; i++) squares += value[i] * value[i];
            return squares > 0f ? 1f / squares : 0f;
        }
    }

    // ── Zones: where a search may go (Fase 2b) ───────────────────────────────

    /// <summary>
    /// A place worth walking to (plan §3.4): a floor node and how much of the value sits within the
    /// zone radius of it. A ZONE and not a node on purpose: one 2 m node holds a sliver of what is
    /// really one place to look at, and a search that weighed nodes would walk to the edge of a patch
    /// of value as readily as to its middle.
    /// </summary>
    public readonly struct Zone
    {
        /// <summary>The floor node at its middle: where to stand to look at it.</summary>
        public readonly int Node;

        public readonly Vector3 Position;

        /// <summary>Its share of everything the map believes (the map adds up to 1, the Hub's sink and
        /// the hiding spots included). Zones never share a node, so the shares of one collection add up
        /// to at most what is left on the floor.</summary>
        public readonly float Share;

        public Zone(int node, Vector3 position, float share)
        {
            Node = node;
            Position = position;
            Share = share;
        }
    }

    /// <summary>
    /// Whether a search can walk to this node and look at it: a point on the floor that is not the
    /// Hub's doorway. A hiding spot is not — the value inside one is not somewhere to stand, and
    /// looking does not clear it (when to OPEN a spot is another question, D38). A doorway node is not
    /// either: what reaches it has gone into the Hub (C5), and nothing may ever send a search to that
    /// door on the strength of it.
    /// </summary>
    public bool IsSearchable(int node) =>
        graph.Kind(node) == PossibilityGraph.ENodeKind.Floor && !graph.IsDrain(node);

    /// <summary>The value a search can still walk to and look at within <paramref name="radius"/> of a
    /// point (plan view, floor band): <see cref="SumNear"/> without the hiding spots and the Hub's
    /// doorway. What a zone centred there would gather.</summary>
    public float SearchableSumNear(Vector3 point, float radius, float floorBand)
    {
        graph.CollectNear(point, radius, floorBand, scratch);

        float sum = 0f;
        for (int i = 0; i < scratch.Count; i++)
            if (IsSearchable(scratch[i])) sum += value[scratch[i]];
        return sum;
    }

    /// <summary>
    /// The places a search could walk to, most valuable first: at most <paramref name="max"/> zones of
    /// <paramref name="radius"/> metres, none sharing a node with another.
    ///
    /// HOW THEY ARE CUT. The node with the most value around it is the first zone, and takes every
    /// searchable node within the radius. The node with the most of WHAT IS LEFT around it is the
    /// second, and so on. So a patch of value comes out as one place to stand, in its middle, and a
    /// corridor of it as a few places in a row — instead of every node of the patch competing with its
    /// neighbours, which is what weighing nodes one by one would do.
    ///
    /// WHAT IS NEVER A ZONE:
    ///   - a hiding spot, and the Hub (<see cref="IsSearchable"/>): neither is the middle of a zone,
    ///     and what they hold is not counted in anyone's share. The sink is not a node at all;
    ///   - where it already stands: a node within <paramref name="minTravel"/> of
    ///     <paramref name="standing"/> (plan view, same floor) cannot be the middle of a zone. A
    ///     destination under its own feet is reached without taking a step or turning, so whatever it
    ///     failed to see from here it would fail to see again, for ever. The value on such a node
    ///     still counts — in the zone of a node a step further away, which is where it has to go to
    ///     look at it from.
    ///
    /// It only reads: nothing here changes what the map believes. The caller turns the zones into a
    /// choice — how long each takes to reach, and a ROLL, never the first of the list.
    /// </summary>
    /// <param name="minTravel">0 or less: nowhere is "where it already stands".</param>
    public void CollectZones(float radius, float floorBand, int max, Vector3 standing, float minTravel,
                             List<Zone> results)
    {
        results.Clear();
        if (!HasValue || max <= 0) return;

        radius = Mathf.Max(0f, radius);

        System.Array.Clear(zoneRemaining, 0, zoneRemaining.Length);
        System.Array.Clear(zoneSum, 0, zoneSum.Length);
        zoneTouched.Clear();

        // 1. For every searchable node, how much searchable value sits within the radius of it. Walked
        //    from the nodes that HOLD value outwards: a node with nothing of its own can still be the
        //    middle of what is around it (two patches either side of a spot it has just cleared).
        for (int i = 0; i < value.Length; i++)
        {
            float v = value[i];
            if (v <= 0f || !IsSearchable(i)) continue;

            zoneRemaining[i] = v;

            graph.CollectNear(graph.Position(i), radius, floorBand, scratch);
            for (int s = 0; s < scratch.Count; s++)
            {
                int around = scratch[s];
                if (!IsSearchable(around)) continue;

                if (zoneSum[around] <= 0f) zoneTouched.Add(around);
                zoneSum[around] += v;
            }
        }

        float minTravelSqr = minTravel > 0f ? minTravel * minTravel : 0f;

        // 2. Peel them off, the most valuable first. Bounded by the touched nodes: every pass either
        //    takes a zone or retires the node it looked at.
        int guard = zoneTouched.Count;
        while (results.Count < max && guard-- > 0)
        {
            int best = -1;
            float bestSum = MinZoneShare;
            float bestOwn = -1f;

            for (int t = 0; t < zoneTouched.Count; t++)
            {
                int node = zoneTouched[t];
                float sum = zoneSum[node];
                float tie = bestSum * ZoneTieTolerance;
                if (sum < bestSum - tie) continue;

                // Between two middles that gather the same, the one holding more value itself: a
                // lone node of value is surrounded by neighbours that each "gather" all of it, and
                // the place to stand is the node, not whichever neighbour was listed first. "The
                // same" within rounding — the sums are built by adding and taking away in different
                // orders, and left to exact equality the choice was decided by the last bit.
                float own = zoneRemaining[node];
                if (sum <= bestSum + tie && own <= bestOwn) continue;

                if (IsWithin(node, standing, minTravelSqr, floorBand)) continue;

                best = node;
                bestSum = sum;
                bestOwn = own;
            }

            if (best < 0) break;

            // Everything searchable within the radius that no earlier zone took is this zone's, and
            // stops counting towards anyone else's.
            graph.CollectNear(graph.Position(best), radius, floorBand, zoneMembers);

            float share = 0f;
            for (int m = 0; m < zoneMembers.Count; m++)
            {
                int member = zoneMembers[m];
                float v = zoneRemaining[member];
                if (v <= 0f) continue;

                share += v;
                zoneRemaining[member] = 0f;

                graph.CollectNear(graph.Position(member), radius, floorBand, scratch);
                for (int s = 0; s < scratch.Count; s++) zoneSum[scratch[s]] -= v;
            }

            // What was around it is all taken; whatever is left in the sum is rounding.
            zoneSum[best] = 0f;

            if (share >= MinZoneShare) results.Add(new Zone(best, graph.Position(best), share));
        }
    }

    /// <summary>Whether a node is within a plan-view distance of a point, on its floor.</summary>
    private bool IsWithin(int node, Vector3 point, float sqrRadius, float floorBand)
    {
        if (sqrRadius <= 0f) return false;

        Vector3 offset = graph.Position(node) - point;
        if (Mathf.Abs(offset.y) > floorBand) return false;

        return offset.x * offset.x + offset.z * offset.z < sqrRadius;
    }

    private void Swap()
    {
        float[] t = value;
        value = next;
        next = t;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
