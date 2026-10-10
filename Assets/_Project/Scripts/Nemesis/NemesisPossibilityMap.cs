using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Where the player can be NOW, as the Nemesis can reason it (Plan-Busqueda-Nemesis §3, Fase 2).
/// The belief knows where it sensed the player; this keeps a <see cref="PossibilityMap"/> over the
/// level and moves it the way a person looking for someone would:
///   - what it sees and hears puts the value (read off <see cref="NemesisBelief"/>, so only evidence
///     that IS the player; never the real position, never a decoy);
///   - the value spreads along the NavMesh at the player's speed, faster the way they were last seen
///     going;
///   - what it is looking at right now, it clears — out to the range its eyes are really working at
///     (FieldOfView.EffectiveViewRange × SearchMapClearRangeScale), inside its cone, with the ray
///     aimed at a crouching player's height so cover still hides someone;
///   - where it is standing, and where it walked in the last few seconds of a hunt, nobody gets
///     through (the TRAIL, 07/10, WIR-062): those floor nodes are blocked, so the value cannot run
///     back past it down the corridor it came along. Evidence of the player near the trail wipes
///     that part of it — a player heard behind the monster is behind the monster;
///   - opening a hiding spot clears that spot, and nothing else does;
///   - the Hub's door drains it (C5).
///
/// WHO DECIDES OFF IT. Since Fase 2b the search does: NemesisSearchingState goes where this still
/// holds value (NemesisSearchPicker rolls among the zones PossibilityMap.CollectZones cuts), drops a
/// place that has lost its value before walking all the way to it, and calls the search over when no
/// place is worth the walk. It has no other memory of where it has looked: looking IS the memory,
/// because looking clears. The patrol after a hunt (2d) and the hiding spots (2e) do not read it yet.
/// NemesisGizmos draws it and F9 has a row for it.
///
/// SETUP: none. NemesisStateManager adds it next to itself, initializes it and ticks it every frame
/// (it throttles itself to SearchMapTickInterval; a reader that cannot wait for the next tick asks
/// for it with CatchUp). The graph is built in Start, once per level load: its node count and build
/// time go to the Console.
/// </summary>
[DisallowMultipleComponent]
public class NemesisPossibilityMap : MonoBehaviour
{
    /// <summary>
    /// Metres of height a node may sit above or below a point and still be on its floor: "same
    /// floor" for seeding the map, for reading it and for the search that walks it.
    ///
    /// Loose enough for a ramp or a few steps inside a room, well under a storey. One definition and
    /// public on purpose: with two, evidence a metre and a half up could count as inside an area whose
    /// nodes the other definition had filtered out (plan §16.2, C2 #3). It lived on NemesisFreeRoam
    /// while the search swept a disc; the map is what "the same place" is measured on now.
    /// </summary>
    public const float FloorBand = 1.5f;

    /// <summary>Longest stretch one tick may spread over: after a pause or a dormant spell it should
    /// not leap across the level in one go.</summary>
    private const float MaxTickDelta = 1f;

    private NemesisStateManager stateManager;
    private NemesisBelief belief;
    private FieldOfView eyes;
    private NemesisHidingAwareness awareness;

    private NemesisPossibilityGraphBuilder.Result built;
    private PossibilityMap map;
    private float builtSpacing = -1f;
    private float lastTickAt = float.NegativeInfinity;
    private int consumedSequence = int.MinValue;
    private NavMeshQueryFilter navFilter;

    private readonly List<int> scratch = new List<int>();
    private readonly Dictionary<HidingSpot, int> nodeOfSpot = new Dictionary<HidingSpot, int>();

    /// <summary>One place the Nemesis stood during a hunt, and when.</summary>
    public readonly struct TrailPoint
    {
        public readonly Vector3 Position;
        public readonly float Time;

        public TrailPoint(Vector3 position, float time)
        {
            Position = position;
            Time = time;
        }
    }

    /// <summary>A new trail point only once it has moved this far from the last one: standing still
    /// it refreshes that one's time instead of piling up copies.</summary>
    private const float TrailStep = 0.5f;

    private readonly List<TrailPoint> trail = new List<TrailPoint>();

    /// <summary>Where it has been in the last SearchMapTrailMemory seconds of a hunt, oldest first.
    /// For the gizmos.</summary>
    public IReadOnlyList<TrailPoint> Trail => trail;

    /// <summary>Floor nodes blocked by the trail after the last tick. For F9 and the gizmos.</summary>
    public int BlockedLastTick => map != null ? map.BlockedCount : 0;

    /// <summary>How far the "not here" reaches right now, in metres: the range its eyes are really
    /// working at (held up when it is looking at the player, stretched when hunting one it lost),
    /// times SearchMapClearRangeScale. Not the base View Range: the map clearing seven metres while
    /// the eyes reached fourteen left value down a corridor it had just looked along (WIR-062).</summary>
    public float ClearRange
    {
        get
        {
            SO_NemesisData data = stateManager != null ? stateManager.NemesisData : null;
            if (data == null) return 0f;
            return eyes != null ? eyes.EffectiveViewRange * data.SearchMapClearRangeScale : data.SearchMapClearRange;
        }
    }

    /// <summary>The map, or null before the graph is built.</summary>
    public PossibilityMap Map => map;

    public bool IsBuilt => map != null;

    /// <summary>What the last build found and how long it took. Null before the first build.</summary>
    public NemesisPossibilityGraphBuilder.Result Build => built;

    /// <summary>Nodes the last tick cleared by looking at them. For F9.</summary>
    public int ClearedLastTick { get; private set; }

    /// <summary>Called once by NemesisStateManager, after its references are resolved.</summary>
    public void Initialize(NemesisStateManager manager)
    {
        stateManager = manager;
        belief = manager != null ? manager.Belief : null;
        eyes = manager != null ? manager.FieldOfView : null;

        if (awareness != null) awareness.SpotOpened -= HandleSpotOpened;
        awareness = manager != null ? manager.HidingAwareness : null;
        if (awareness != null) awareness.SpotOpened += HandleSpotOpened;
    }

    private void OnDestroy()
    {
        if (awareness != null) awareness.SpotOpened -= HandleSpotOpened;
    }

    /// <summary>At level load rather than on the first tick: the build is the one expensive part, and
    /// the first tick is the moment the Nemesis wakes up.</summary>
    private void Start() => EnsureBuilt();

    /// <summary>
    /// One tick of the map, throttled to SearchMapTickInterval: evidence, spread, clear what it sees,
    /// drain the Hub, renormalise (§3.2). Ticked by NemesisStateManager after the belief and the hiding
    /// awareness, so it reads this frame's evidence and this frame's opened spots.
    /// </summary>
    public void Tick()
    {
        if (stateManager == null || !EnsureBuilt()) return;

        SO_NemesisData data = stateManager.NemesisData;
        if (data == null) return;

        float now = Time.time;
        if (now - lastTickAt < data.SearchMapTickInterval) return;

        RunTick(data, now);
    }

    /// <summary>
    /// Brings the map up to date with the belief NOW, for a reader about to decide off it: when
    /// evidence has come in since the last tick, the tick that would have folded it in runs at once
    /// instead of up to SearchMapTickInterval later. Nothing happens when the map is already current.
    ///
    /// WHY NOT SEED ON EVERY FRAME INSTEAD. A reader must never see the map between "evidence put
    /// value here" and "it is looking at here and nobody is there": a noise spreads value over an area
    /// that includes floor the Nemesis is staring at, and a search choosing off that would walk to a
    /// place in plain view. One tick does both, in that order, so the two always arrive together — and
    /// the fan of raycasts behind the clearing stays on the tick's clock. The search asks for this
    /// once per pick, which is once a second or so.
    /// </summary>
    public void CatchUp()
    {
        if (stateManager == null || map == null || belief == null) return;

        // A belief forgotten without its sequence moving (the capture) is news as well: the map it
        // left behind points at the one place the player provably is not.
        bool pending = belief.Sequence != consumedSequence || (!belief.HasBelief && map.HasValue);
        if (!pending) return;

        SO_NemesisData data = stateManager.NemesisData;
        if (data != null) RunTick(data, Time.time);
    }

    /// <summary>The belief sequence this map last folded in (NemesisBelief.Sequence). What a reader
    /// compares to know the map has been redrawn by new evidence — the belief's own sequence runs up
    /// to a tick ahead of it.</summary>
    public int EvidenceSequence => consumedSequence;

    private void RunTick(SO_NemesisData data, float now)
    {
        float dt = Mathf.Min(MaxTickDelta, now - lastTickAt);
        lastTickAt = now;
        ClearedLastTick = 0;

        UpdateGates();

        // The trail first: the evidence decides what part of it still stands, and a noise's area
        // must not reach through the floor the Nemesis is blocking.
        RecordTrail(data, now);
        if (!TakeEvidence(data, now)) BlockTrail(data);
        if (!map.HasValue) return;

        map.Spread(dt, data.SearchMapSpreadSpeed, now, data.SearchMapHeadingBias, data.SearchMapHeadingDuration);
        ClearWhatItSees(data);
        map.Drain();
        map.Normalize();
    }

    // ── Building ─────────────────────────────────────────────────────────────

    /// <summary>Builds the graph if there is none, or if the node spacing was changed (tuning in Play).
    /// </summary>
    private bool EnsureBuilt()
    {
        if (stateManager == null) return false;

        SO_NemesisData data = stateManager.NemesisData;
        if (data == null) return false;

        float spacing = data.SearchMapNodeSpacing;
        if (map != null && Mathf.Approximately(spacing, builtSpacing)) return true;

        NavMeshAgent agent = stateManager.NavAgent;
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = agent != null ? agent.agentTypeID : 0,
            areaMask = agent != null ? agent.areaMask : NemesisNav.AreaMask,
        };
        navFilter = filter;

        LayerMask walls = eyes != null ? eyes.ObstacleMask : (LayerMask)LayerMask.GetMask("Default", "Wall");
        built = NemesisPossibilityGraphBuilder.Build(spacing, filter, data.SearchMapSpreadSpeed, walls);
        builtSpacing = spacing;
        map = new PossibilityMap(built.Graph);
        consumedSequence = int.MinValue;
        trail.Clear();

        nodeOfSpot.Clear();
        foreach (NemesisPossibilityGraphBuilder.SpotNode spot in built.Spots) nodeOfSpot[spot.Spot] = spot.Node;

        PossibilityGraph graph = built.Graph;
        if (graph.NodeCount == 0)
        {
            Debug.LogWarning($"[{nameof(NemesisPossibilityMap)}] No NavMesh for the Nemesis's agent type: " +
                             "the possibility map is empty. Bake the level's NavMesh.", this);
        }
        else
        {
            Debug.Log($"[{nameof(NemesisPossibilityMap)}] {graph.NodeCount} nodos cada {spacing:0.#} m, " +
                      $"{graph.EdgeCount} conexiones, {built.Spots.Count} escondites, {built.Lifts.Count} " +
                      $"montacargas, {built.DrainNodes} en la puerta del Hub — armado en " +
                      $"{built.Milliseconds:0} ms.", this);
        }

        return true;
    }

    // ── Each tick ────────────────────────────────────────────────────────────

    /// <summary>A lift without power and a burned spot are no way to go, and nobody is inside a burned
    /// spot.</summary>
    private void UpdateGates()
    {
        foreach (NemesisPossibilityGraphBuilder.LiftGate lift in built.Lifts)
        {
            bool usable = lift.Link != null && (lift.Power == null || lift.Power.HasPower);
            map.SetGateOpen(lift.Gate, usable);
        }

        foreach (NemesisPossibilityGraphBuilder.SpotNode spot in built.Spots)
        {
            bool usable = spot.Spot != null && spot.Spot.isActiveAndEnabled && !spot.Spot.IsBurned;
            map.SetGateOpen(spot.Gate, usable);
            if (!usable) map.Open(spot.Node);
        }
    }

    /// <summary>
    /// The belief's newest evidence, once: a sighting is a point with a heading, a noise is the area of
    /// its doubt (the perceived position and its radius — Fase 1), and evidence from inside the Hub is
    /// the sink. No belief at all (a capture, a respawn) believes nothing.
    ///
    /// Evidence of the player beats the trail: the part of it within reach of the evidence goes
    /// before anything is seeded, and the rest is blocked so the area does not reach through it.
    /// </summary>
    /// <returns>Whether it blocked the trail itself (new evidence on the map): the caller does it
    /// otherwise.</returns>
    private bool TakeEvidence(SO_NemesisData data, float now)
    {
        if (belief == null) return false;

        if (!belief.HasBelief)
        {
            if (map.HasValue) map.Clear();
            consumedSequence = belief.Sequence;

            // A capture or a respawn: the hunt that laid the trail is over, and the player is
            // somewhere else entirely.
            trail.Clear();
            return false;
        }

        if (belief.Sequence == consumedSequence) return false;
        consumedSequence = belief.Sequence;

        Vector3 at = belief.Position;
        if (NemesisSafeZones.Contains(at))
        {
            map.SeedSink();
            return false;
        }

        // Only inside the evidence's own doubt (a node at least, so a sighting's neighbours can take
        // their share): a footstep a few metres ahead of it must not lift the plug of its own body,
        // or the noise's area reaches back past it — the very leak the trail is there to stop.
        ForgetTrailNear(at, Mathf.Max(belief.EvidenceRadius, map.Graph.Spacing));
        BlockTrail(data);

        int start = StartNodeFor(at);
        if (belief.IsAnchoredBySight) map.SeedPoint(at, FloorBand, belief.ObservedVelocity, now, start);
        else map.SeedArea(at, belief.EvidenceRadius, FloorBand, start);
        return true;
    }

    /// <summary>How far from the evidence a node may be and still be where it stands: a little over a
    /// node spacing, so a point between nodes always has one.</summary>
    private const float StartNodeReach = 1.5f;

    /// <summary>
    /// The node the evidence stands on, checked against the NavMesh: the nearest one that a straight
    /// walk from the evidence reaches without leaving the mesh. The nearest node by distance alone can
    /// be on the far side of a thin wall, and then the whole area would be walked out from the wrong
    /// room. -1 when none qualifies or the evidence is off the mesh: the map takes the nearest.
    /// </summary>
    private int StartNodeFor(Vector3 point)
    {
        PossibilityGraph graph = map.Graph;
        if (!NemesisNav.TrySnapToNavMesh(point, out Vector3 onMesh)) return -1;

        graph.CollectNear(onMesh, graph.Spacing * StartNodeReach, FloorBand, scratch);

        int best = -1;
        float bestSqr = float.PositiveInfinity;
        for (int i = 0; i < scratch.Count; i++)
        {
            int node = scratch[i];
            if (graph.Kind(node) != PossibilityGraph.ENodeKind.Floor) continue;

            float sqr = (graph.Position(node) - onMesh).sqrMagnitude;
            if (sqr >= bestSqr) continue;
            if (NavMesh.Raycast(onMesh, graph.Position(node), out _, navFilter)) continue;

            bestSqr = sqr;
            best = node;
        }

        return best;
    }

    // ── The trail (07/10, WIR-062) ───────────────────────────────────────────

    /// <summary>
    /// Whether where it walks says anything about where the player is not: while it chases them, and
    /// while it searches or investigates what it sensed of them. On patrol, on a lift ride or carrying
    /// someone off, it is not between the player and anywhere.
    /// </summary>
    private bool IsHunting
    {
        get
        {
            switch (stateManager.CurrentStateKey)
            {
                case NemesisStateManager.ENemesisState.Chasing:
                case NemesisStateManager.ENemesisState.Searching:
                case NemesisStateManager.ENemesisState.Investigating:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Drops what is older than SearchMapTrailMemory and, during a hunt, adds where it is
    /// standing now.</summary>
    private void RecordTrail(SO_NemesisData data, float now)
    {
        float memory = data.SearchMapTrailMemory;

        int expired = 0;
        while (expired < trail.Count && now - trail[expired].Time > memory) expired++;
        if (expired > 0) trail.RemoveRange(0, expired);

        if (memory <= 0f || !IsHunting) return;

        Vector3 body = stateManager.transform.position;
        int last = trail.Count - 1;
        if (last >= 0 && (trail[last].Position - body).sqrMagnitude < TrailStep * TrailStep)
        {
            trail[last] = new TrailPoint(trail[last].Position, now);
            return;
        }

        trail.Add(new TrailPoint(body, now));
    }

    /// <summary>The player was sensed around <paramref name="point"/>: the trail there says nothing
    /// any more.</summary>
    private void ForgetTrailNear(Vector3 point, float radius)
    {
        float sqr = radius * radius;
        for (int i = trail.Count - 1; i >= 0; i--)
        {
            Vector3 offset = trail[i].Position - point;
            if (Mathf.Abs(offset.y) > FloorBand) continue;

            offset.y = 0f;
            if (offset.sqrMagnitude <= sqr) trail.RemoveAt(i);
        }
    }

    /// <summary>Blocks the floor within SearchMapTrailRadius of every trail point (PossibilityMap.Block).
    /// Rebuilt every tick: the trail moves, and part of it can be wiped by evidence.</summary>
    private void BlockTrail(SO_NemesisData data)
    {
        map.ClearBlocks();

        float radius = data.SearchMapTrailRadius;
        if (radius <= 0f) return;

        PossibilityGraph graph = map.Graph;
        for (int i = 0; i < trail.Count; i++)
        {
            graph.CollectNear(trail[i].Position, radius, FloorBand, scratch);
            for (int n = 0; n < scratch.Count; n++) map.Block(scratch[n]);
        }
    }

    /// <summary>
    /// "Not here": every floor node with value it can see right now — inside the view cone, within the
    /// clear range, with a clear line from its eye to a crouching player's height over the node — plus
    /// whatever is right under it (the proximity rule's reach). Skipped while it sees the player: then
    /// the sighting is the evidence, and clearing the spot it is looking at would erase it.
    /// </summary>
    private void ClearWhatItSees(SO_NemesisData data)
    {
        if (eyes == null || stateManager.HasVisualTarget) return;

        PossibilityGraph graph = map.Graph;
        Vector3 eye = eyes.ViewTransform.position;
        Vector3 front = eyes.LookDirection;
        Vector3 body = stateManager.transform.position;

        float range = ClearRange;
        float probe = data.SearchMapProbeHeight;
        float proximity = data.ProximityDetectionRange;
        float sameFloor = data.CatchMaxVerticalOffset;

        // A vertical band as tall as the range: it can see down a stairwell or over a balcony.
        graph.CollectNear(body, Mathf.Max(range, proximity), range, scratch);

        for (int i = 0; i < scratch.Count; i++)
        {
            int node = scratch[i];
            if (graph.Kind(node) != PossibilityGraph.ENodeKind.Floor) continue;
            if (map.Value(node) <= 0f) continue;   // Nothing to clear: no ray to pay for.

            Vector3 target = graph.Position(node) + Vector3.up * probe;

            Vector3 under = graph.Position(node) - body;
            bool underIt = Mathf.Abs(under.y) <= sameFloor &&
                           under.x * under.x + under.z * under.z <= proximity * proximity;

            if (!underIt)
            {
                if ((target - eye).sqrMagnitude > range * range) continue;
                if (!LineOfSight.CheckAngle(eye, target, front, data.ViewAngle)) continue;
            }

            if (!eyes.HasLineOfSightTo(target, null)) continue;

            if (map.ClearSeen(node)) ClearedLastTick++;
        }
    }

    private void HandleSpotOpened(HidingSpot spot)
    {
        if (map == null || spot == null) return;
        if (nodeOfSpot.TryGetValue(spot, out int node)) map.Open(node);
    }

    // ── Reading it (the search, F9, gizmos) ──────────────────────────────────
    //
    // The search reads the map itself (Map: CollectZones, SearchableSumNear) through
    // NemesisSearchPicker; what is here are the few questions the HUD and the gizmos ask.

    /// <summary>The share of the value on the Nemesis's own floor.</summary>
    public float ShareOnOwnFloor => map != null && map.HasValue
        ? map.SumOnLevel(stateManager.transform.position.y, FloorBand)
        : 0f;

    /// <summary>The most likely node and its position, when it believes anything.</summary>
    public bool TryGetBest(out int node, out Vector3 position)
    {
        node = map != null && map.HasValue ? map.BestNode() : -1;
        position = node >= 0 ? map.Graph.Position(node) : Vector3.zero;
        return node >= 0;
    }

    /// <summary>The value within <paramref name="radius"/> of a point, on its floor.</summary>
    public float ShareNear(Vector3 point, float radius) =>
        map != null && map.HasValue ? map.SumNear(point, radius, FloorBand) : 0f;

    /// <summary>Roughly how much area it still has to search, in m²: the effective node count times
    /// the area of one node.</summary>
    public float SpreadArea => map != null && map.HasValue
        ? map.EffectiveNodeCount * map.Graph.Spacing * map.Graph.Spacing
        : 0f;
}
