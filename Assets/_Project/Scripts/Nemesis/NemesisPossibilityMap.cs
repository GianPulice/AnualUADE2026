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
///   - what it is looking at right now, it clears — out to SO_NemesisData.SearchMapClearRange, inside
///     its cone, with the ray aimed at a crouching player's height so cover still hides someone;
///   - opening a hiding spot clears that spot, and nothing else does;
///   - the Hub's door drains it (C5).
///
/// FASE 2a: IT ONLY WATCHES. Nothing reads it to decide anything yet; NemesisGizmos draws it and F9
/// has a row for it. The search starts reading it in 2b.
///
/// SETUP: none. NemesisStateManager adds it next to itself, initializes it and ticks it every frame
/// (it throttles itself to SearchMapTickInterval). The graph is built in Start, once per level load:
/// its node count and build time go to the Console.
/// </summary>
[DisallowMultipleComponent]
public class NemesisPossibilityMap : MonoBehaviour
{
    /// <summary>"Same floor" for seeding, the band the search uses.</summary>
    private const float FloorBand = NemesisFreeRoam.FloorBand;

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

    private readonly List<int> scratch = new List<int>();
    private readonly Dictionary<HidingSpot, int> nodeOfSpot = new Dictionary<HidingSpot, int>();

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

        float dt = Mathf.Min(MaxTickDelta, now - lastTickAt);
        lastTickAt = now;
        ClearedLastTick = 0;

        UpdateGates();
        TakeEvidence(now);
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

        LayerMask walls = eyes != null ? eyes.ObstacleMask : (LayerMask)LayerMask.GetMask("Default", "Wall");
        built = NemesisPossibilityGraphBuilder.Build(spacing, filter, data.SearchMapSpreadSpeed, walls);
        builtSpacing = spacing;
        map = new PossibilityMap(built.Graph);
        consumedSequence = int.MinValue;

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
    /// </summary>
    private void TakeEvidence(float now)
    {
        if (belief == null) return;

        if (!belief.HasBelief)
        {
            if (map.HasValue) map.Clear();
            consumedSequence = belief.Sequence;
            return;
        }

        if (belief.Sequence == consumedSequence) return;
        consumedSequence = belief.Sequence;

        Vector3 at = belief.Position;
        if (NemesisSafeZones.Contains(at))
        {
            map.SeedSink();
            return;
        }

        if (belief.IsAnchoredBySight) map.SeedPoint(at, FloorBand, belief.ObservedVelocity, now);
        else map.SeedArea(at, belief.EvidenceRadius, FloorBand);
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

        float range = data.SearchMapClearRange;
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

    // ── Reading it (F9, gizmos) ──────────────────────────────────────────────

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
