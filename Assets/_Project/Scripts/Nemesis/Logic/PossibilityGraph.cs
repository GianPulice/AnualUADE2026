using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The walkable world as the possibility map sees it (Plan-Busqueda-Nemesis §3.1): points about
/// <see cref="Spacing"/> apart on the Nemesis's NavMesh, the walkable links between neighbours, and
/// one node inside each hiding spot hanging off its approach point.
///
/// Built once by NemesisPossibilityGraphBuilder (which knows about the NavMesh) and then frozen; what
/// changes at runtime is the values on it (<see cref="PossibilityMap"/>) and which GATES are open. A
/// gate is a set of edges that open and close together: the freight elevator (only while it has
/// power: a lift the player cannot ride is no way out of a floor) and each hiding spot (closed once
/// it is burned).
///
/// PURE: positions and lengths in, structure out. No NavMesh and no scene, so EditMode tests can
/// build a corridor or a T by hand.
/// </summary>
public sealed class PossibilityGraph
{
    public enum ENodeKind : byte
    {
        /// <summary>A point on the floor. What it looks at, it clears.</summary>
        Floor,

        /// <summary>The inside of a hiding spot. Never cleared by looking at it, only by opening it.
        /// </summary>
        HidingSpot,
    }

    /// <summary>The gate of an edge that is always open.</summary>
    public const int AlwaysOpen = -1;

    private struct PendingEdge
    {
        public int A;
        public int B;
        public float Length;
        public int Gate;
    }

    private readonly List<Vector3> positions = new List<Vector3>();
    private readonly List<ENodeKind> kinds = new List<ENodeKind>();
    private readonly List<bool> drains = new List<bool>();
    private readonly List<PendingEdge> pending = new List<PendingEdge>();

    // Frozen: compressed rows, one block of outgoing edges per node (each undirected edge twice).
    private int[] edgeStart;
    private int[] edgeTarget;
    private float[] edgeLength;
    private Vector3[] edgeDirection;
    private int[] edgeGate;

    // Columns of the plan view, keyed by cell, for the spatial queries.
    private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();

    public PossibilityGraph(float spacing)
    {
        Spacing = Mathf.Max(0.1f, spacing);
    }

    /// <summary>Nominal distance between neighbouring floor nodes, in metres.</summary>
    public float Spacing { get; }

    public bool IsFrozen { get; private set; }
    public int NodeCount => positions.Count;

    /// <summary>Undirected edges.</summary>
    public int EdgeCount => IsFrozen ? edgeTarget.Length / 2 : pending.Count;

    public int GateCount { get; private set; }

    public Vector3 Position(int node) => positions[node];
    public ENodeKind Kind(int node) => kinds[node];

    /// <summary>Whether value that reaches this node drains out of the map (the Hub's door, C5).
    /// </summary>
    public bool IsDrain(int node) => drains[node];

    // ── Building ─────────────────────────────────────────────────────────────

    public int AddNode(Vector3 position, ENodeKind kind = ENodeKind.Floor)
    {
        EnsureEditable();
        positions.Add(position);
        kinds.Add(kind);
        drains.Add(false);
        return positions.Count - 1;
    }

    public void MarkDrain(int node)
    {
        EnsureEditable();
        drains[node] = true;
    }

    /// <summary>A new gate id for <see cref="AddEdge"/>. Gates start open.</summary>
    public int NewGate()
    {
        EnsureEditable();
        return GateCount++;
    }

    /// <summary>An undirected, walkable link between two nodes.</summary>
    /// <param name="length">How far it is, in metres. A long edge carries less value per tick than a
    /// short one (a lift ride counts as the distance it would take to walk its duration).</param>
    public void AddEdge(int a, int b, float length, int gate = AlwaysOpen)
    {
        EnsureEditable();
        if (a == b) return;
        pending.Add(new PendingEdge { A = a, B = b, Length = Mathf.Max(0.01f, length), Gate = gate });
    }

    /// <summary>Compresses the edges and indexes the nodes. Nothing may be added afterwards.</summary>
    public void Freeze()
    {
        if (IsFrozen) return;

        int n = positions.Count;
        int[] degree = new int[n];
        for (int i = 0; i < pending.Count; i++)
        {
            degree[pending[i].A]++;
            degree[pending[i].B]++;
        }

        edgeStart = new int[n + 1];
        for (int i = 0; i < n; i++) edgeStart[i + 1] = edgeStart[i] + degree[i];

        int total = edgeStart[n];
        edgeTarget = new int[total];
        edgeLength = new float[total];
        edgeDirection = new Vector3[total];
        edgeGate = new int[total];

        int[] fill = new int[n];
        for (int i = 0; i < pending.Count; i++)
        {
            PendingEdge e = pending[i];
            Vector3 flat = positions[e.B] - positions[e.A];
            flat.y = 0f;
            Vector3 dir = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.zero;

            Put(e.A, e.B, e.Length, dir, e.Gate, fill);
            Put(e.B, e.A, e.Length, -dir, e.Gate, fill);
        }

        pending.Clear();

        for (int i = 0; i < n; i++)
        {
            long key = CellKey(positions[i]);
            if (!cells.TryGetValue(key, out List<int> list))
            {
                list = new List<int>();
                cells[key] = list;
            }
            list.Add(i);
        }

        IsFrozen = true;
    }

    private void Put(int from, int to, float length, Vector3 dir, int gate, int[] fill)
    {
        int slot = edgeStart[from] + fill[from]++;
        edgeTarget[slot] = to;
        edgeLength[slot] = length;
        edgeDirection[slot] = dir;
        edgeGate[slot] = gate;
    }

    private void EnsureEditable()
    {
        if (IsFrozen) throw new System.InvalidOperationException("PossibilityGraph is frozen.");
    }

    // ── Edges (frozen only) ──────────────────────────────────────────────────

    public int EdgesBegin(int node) => edgeStart[node];
    public int EdgesEnd(int node) => edgeStart[node + 1];
    public int Degree(int node) => edgeStart[node + 1] - edgeStart[node];
    public int EdgeTarget(int edge) => edgeTarget[edge];
    public float EdgeLength(int edge) => edgeLength[edge];

    /// <summary>Flat unit direction from the edge's node to its target; zero for a vertical one.
    /// </summary>
    public Vector3 EdgeDirection(int edge) => edgeDirection[edge];

    public int EdgeGate(int edge) => edgeGate[edge];

    // ── Spatial queries (frozen only) ────────────────────────────────────────

    /// <summary>
    /// Every node within <paramref name="radius"/> of the point in plan view and within
    /// <paramref name="floorBand"/> of its height. Cleared and refilled.
    /// </summary>
    public void CollectNear(Vector3 point, float radius, float floorBand, List<int> results)
    {
        results.Clear();
        if (!IsFrozen || radius < 0f) return;

        int reach = Mathf.CeilToInt(radius / Spacing);
        int cx = Cell(point.x);
        int cz = Cell(point.z);
        float sqr = radius * radius;

        for (int dx = -reach; dx <= reach; dx++)
        {
            for (int dz = -reach; dz <= reach; dz++)
            {
                if (!cells.TryGetValue(Key(cx + dx, cz + dz), out List<int> list)) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    int node = list[i];
                    Vector3 offset = positions[node] - point;
                    if (Mathf.Abs(offset.y) > floorBand) continue;

                    offset.y = 0f;
                    if (offset.sqrMagnitude <= sqr) results.Add(node);
                }
            }
        }
    }

    /// <summary>The nearest node of the given kind within <paramref name="maxDistance"/> (plan view)
    /// and <paramref name="floorBand"/>, or -1.</summary>
    public int FindNearest(Vector3 point, float maxDistance, float floorBand, ENodeKind kind,
                           List<int> scratch)
    {
        CollectNear(point, maxDistance, floorBand, scratch);

        int best = -1;
        float bestSqr = float.PositiveInfinity;
        for (int i = 0; i < scratch.Count; i++)
        {
            int node = scratch[i];
            if (kinds[node] != kind) continue;

            float sqr = (positions[node] - point).sqrMagnitude;
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            best = node;
        }

        return best;
    }

    private int Cell(float coordinate) => Mathf.FloorToInt(coordinate / Spacing);

    private long CellKey(Vector3 p) => Key(Cell(p.x), Cell(p.z));

    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
}
