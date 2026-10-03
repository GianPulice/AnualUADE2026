using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds the <see cref="PossibilityGraph"/> the possibility map runs on, out of the level as it is
/// baked (Plan-Busqueda-Nemesis §3.1). One job: NavMesh and scene in, graph out.
///
///   - NODES: the Nemesis's NavMesh triangles rasterised onto a plan-view grid every Spacing metres,
///     one node per floor in each column (a column over a catwalk has two), each snapped onto the
///     agent's own mesh. Isolated points (a table top that baked as its own island) are dropped.
///   - EDGES: between neighbouring columns (the eight around), when a NavMesh raycast between the two
///     nodes is clear. That is what keeps the value from ever crossing a wall: there is no edge
///     through one. Stairs connect like any slope; drops do not exist here, because the player does not
///     take them (D9).
///   - HIDING SPOTS: one node inside each, hanging off the floor node nearest its approach point,
///     behind a gate of its own (closed once the spot is burned).
///   - THE FREIGHT ELEVATOR: one edge between its two landings, as long as walking for the duration of
///     the ride, behind a gate that follows its ElevatorPower — the player cannot ride a dead lift, so
///     an unpowered one is no way off the floor (WIR-063).
///   - THE HUB'S DOOR: floor nodes beside a safe zone with nothing between them and its inside but a
///     door are drains (C5) — tested with the eyes' own obstacle mask, skipping DoorInteractable
///     colliders, because levels do not agree on which layer a wall is on. Nodes along the Hub's outer
///     walls are not drains: nobody gets in through a wall.
///
/// Doors need nothing: the NavMesh is baked through them, so a closed door still has its edge and only
/// blocks the LOOK (the map's clearing), exactly as it does for the player.
///
/// Measured: the build logs its node count and time. It runs once per level load (and again if
/// SearchMapNodeSpacing is changed in Play), not per tick.
/// </summary>
public static class NemesisPossibilityGraphBuilder
{
    public readonly struct SpotNode
    {
        public readonly HidingSpot Spot;
        public readonly int Node;
        public readonly int Gate;

        public SpotNode(HidingSpot spot, int node, int gate)
        {
            Spot = spot;
            Node = node;
            Gate = gate;
        }
    }

    public readonly struct LiftGate
    {
        public readonly NemesisElevatorLink Link;

        /// <summary>Null when the shaft has no ElevatorPower: always powered.</summary>
        public readonly ElevatorPower Power;

        public readonly int Gate;

        public LiftGate(NemesisElevatorLink link, ElevatorPower power, int gate)
        {
            Link = link;
            Power = power;
            Gate = gate;
        }
    }

    public sealed class Result
    {
        public PossibilityGraph Graph;
        public readonly List<SpotNode> Spots = new List<SpotNode>();
        public readonly List<LiftGate> Lifts = new List<LiftGate>();
        public int DrainNodes;
        public float Milliseconds;
    }

    /// <summary>Two surface heights in one column closer than this are the same floor.</summary>
    private const float ColumnMergeHeight = 0.6f;

    /// <summary>How far a raster point may be from the agent's own mesh and still become a node.
    /// </summary>
    private const float SurfaceSnap = 0.6f;

    /// <summary>Height two neighbouring nodes may differ by and still be linked: a stair at 2 m of run
    /// climbs about this much. Well under a storey.</summary>
    private const float MaxStep = 1.6f;

    /// <summary>Same-floor band for the landings and approach points.</summary>
    private const float FloorBand = 1.5f;

    /// <summary>How far an approach point may be from its floor node.</summary>
    private const float SpotReach = 2.5f;

    /// <summary>How far an elevator landing may be from its floor node.</summary>
    private const float LiftReach = 3f;

    /// <summary>Height of the "is this the Hub's doorway" ray: a body, above skirting and props.</summary>
    private const float DoorProbeHeight = 1f;

    /// <summary>How deep into the safe zone the doorway ray aims.</summary>
    private const float DoorProbeInset = 0.75f;

    /// <summary>How far above the bottom of a safe zone a node may stand and still be at its door:
    /// under a storey (SO_NemesisData.FloorHeightThreshold's 2.5 m).</summary>
    private const float DoorwayFloorReach = 2.5f;

    private static readonly (int dx, int dz)[] Forward = { (1, 0), (0, 1), (1, 1), (1, -1) };

    /// <param name="filter">The Nemesis agent's type and areas: the graph is its NavMesh.</param>
    /// <param name="assumedSpeed">Turns a lift ride's seconds into the metres its edge is long.</param>
    /// <param name="wallMask">What counts as a wall for the Hub's doorway test: the eyes' obstacle
    /// mask.</param>
    public static Result Build(float spacing, NavMeshQueryFilter filter, float assumedSpeed, LayerMask wallMask)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = new Result();
        spacing = Mathf.Max(0.5f, spacing);

        // 1. Raster: one height per floor in every grid column the mesh covers.
        var heights = new Dictionary<(int, int), List<float>>();
        Raster(NavMesh.CalculateTriangulation(), spacing, filter.areaMask, heights);

        // 2. Snap onto the agent's own mesh.
        var points = new List<Vector3>();
        var cellOf = new List<(int x, int z)>();
        var byCell = new Dictionary<(int, int), List<int>>();
        foreach (KeyValuePair<(int, int), List<float>> column in heights)
        {
            (int x, int z) = column.Key;
            foreach (float y in column.Value)
            {
                var raster = new Vector3(x * spacing, y, z * spacing);
                if (!NavMesh.SamplePosition(raster, out NavMeshHit hit, SurfaceSnap, filter)) continue;

                Vector3 flat = hit.position - raster;
                flat.y = 0f;
                if (flat.sqrMagnitude > spacing * spacing * 0.25f) continue;
                if (HasNodeAtHeight(byCell, column.Key, points, hit.position.y)) continue;

                AddToCell(byCell, column.Key, points.Count);
                points.Add(hit.position);
                cellOf.Add(column.Key);
            }
        }

        // 3. Walkable links between neighbouring columns.
        var edges = new List<(int a, int b, float length)>();
        var degree = new int[points.Count];
        for (int a = 0; a < points.Count; a++)
        {
            (int x, int z) = cellOf[a];
            foreach ((int dx, int dz) in Forward)
            {
                if (!byCell.TryGetValue((x + dx, z + dz), out List<int> neighbours)) continue;

                foreach (int b in neighbours)
                {
                    if (Mathf.Abs(points[a].y - points[b].y) > MaxStep) continue;
                    if (NavMesh.Raycast(points[a], points[b], out _, filter)) continue;

                    edges.Add((a, b, Vector3.Distance(points[a], points[b])));
                    degree[a]++;
                    degree[b]++;
                }
            }
        }

        // 4. Into the graph, without the isolated points.
        var graph = new PossibilityGraph(spacing);
        var remap = new int[points.Count];
        var keptByCell = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < points.Count; i++)
        {
            remap[i] = -1;
            if (degree[i] == 0) continue;

            remap[i] = graph.AddNode(points[i]);
            AddToCell(keptByCell, cellOf[i], remap[i]);
        }
        foreach ((int a, int b, float length) in edges) graph.AddEdge(remap[a], remap[b], length);

        result.DrainNodes = MarkHubDoor(graph, spacing, wallMask);
        AddHidingSpots(graph, keptByCell, spacing, result);
        AddLifts(graph, keptByCell, spacing, assumedSpeed, result);

        graph.Freeze();
        result.Graph = graph;
        result.Milliseconds = (float)watch.Elapsed.TotalMilliseconds;
        return result;
    }

    // ── Raster ───────────────────────────────────────────────────────────────

    private static void Raster(NavMeshTriangulation tri, float spacing, int areaMask,
                               Dictionary<(int, int), List<float>> heights)
    {
        Vector3[] v = tri.vertices;
        int[] index = tri.indices;
        int[] areas = tri.areas;

        for (int t = 0; t + 2 < index.Length; t += 3)
        {
            int area = areas != null && t / 3 < areas.Length ? areas[t / 3] : 0;
            if ((areaMask & (1 << area)) == 0) continue;

            Vector3 a = v[index[t]];
            Vector3 b = v[index[t + 1]];
            Vector3 c = v[index[t + 2]];

            int x0 = Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / spacing);
            int x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / spacing);
            int z0 = Mathf.CeilToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / spacing);
            int z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / spacing);

            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    if (!HeightInTriangle(a, b, c, x * spacing, z * spacing, out float y)) continue;
                    AddHeight(heights, (x, z), y);
                }
            }
        }
    }

    /// <summary>The triangle's height over a plan-view point, when the point falls inside it.</summary>
    private static bool HeightInTriangle(Vector3 a, Vector3 b, Vector3 c, float px, float pz, out float y)
    {
        y = 0f;
        float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
        if (Mathf.Abs(d) < 1e-8f) return false;   // Seen edge-on from above: a wall, not a floor.

        float w1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / d;
        float w2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / d;
        float w3 = 1f - w1 - w2;

        const float Edge = -1e-4f;
        if (w1 < Edge || w2 < Edge || w3 < Edge) return false;

        y = w1 * a.y + w2 * b.y + w3 * c.y;
        return true;
    }

    private static void AddHeight(Dictionary<(int, int), List<float>> heights, (int, int) cell, float y)
    {
        if (!heights.TryGetValue(cell, out List<float> list))
        {
            list = new List<float>(1);
            heights[cell] = list;
        }

        for (int i = 0; i < list.Count; i++)
            if (Mathf.Abs(list[i] - y) < ColumnMergeHeight) return;

        list.Add(y);
    }

    private static bool HasNodeAtHeight(Dictionary<(int, int), List<int>> byCell, (int, int) cell,
                                        List<Vector3> points, float y)
    {
        if (!byCell.TryGetValue(cell, out List<int> list)) return false;

        for (int i = 0; i < list.Count; i++)
            if (Mathf.Abs(points[list[i]].y - y) < ColumnMergeHeight) return true;

        return false;
    }

    private static void AddToCell(Dictionary<(int, int), List<int>> byCell, (int, int) cell, int node)
    {
        if (!byCell.TryGetValue(cell, out List<int> list))
        {
            list = new List<int>(1);
            byCell[cell] = list;
        }
        list.Add(node);
    }

    /// <summary>The kept floor node nearest a point, within reach (plan view) and the floor band, or
    /// -1. Before the graph is frozen, so it walks the builder's own columns.</summary>
    private static int NearestKept(PossibilityGraph graph, Dictionary<(int, int), List<int>> keptByCell,
                                   float spacing, Vector3 point, float reach)
    {
        int cx = Mathf.RoundToInt(point.x / spacing);
        int cz = Mathf.RoundToInt(point.z / spacing);
        int r = Mathf.CeilToInt(reach / spacing);

        int best = -1;
        float bestSqr = reach * reach;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                if (!keptByCell.TryGetValue((cx + dx, cz + dz), out List<int> list)) continue;

                foreach (int node in list)
                {
                    Vector3 offset = graph.Position(node) - point;
                    if (Mathf.Abs(offset.y) > FloorBand) continue;

                    offset.y = 0f;
                    float sqr = offset.sqrMagnitude;
                    if (sqr > bestSqr) continue;

                    bestSqr = sqr;
                    best = node;
                }
            }
        }

        return best;
    }

    // ── The Hub's door (C5) ──────────────────────────────────────────────────

    private static int MarkHubDoor(PossibilityGraph graph, float spacing, LayerMask wallMask)
    {
        var volumes = new List<NavMeshModifierVolume>(NemesisSafeZones.Volumes);
        if (volumes.Count == 0) return 0;

        float reach = spacing + 0.5f;
        int marked = 0;

        for (int node = 0; node < graph.NodeCount; node++)
        {
            Vector3 p = graph.Position(node);
            if (NemesisSafeZones.DistanceOnSameLevel(p) > reach) continue;
            if (!OpensInto(p, volumes, wallMask)) continue;

            graph.MarkDrain(node);
            marked++;
        }

        return marked;
    }

    private static readonly RaycastHit[] DoorwayHits = new RaycastHit[16];

    /// <summary>Whether a straight walk from <paramref name="p"/> into a safe zone beside it meets
    /// nothing but a door: the node is at the doorway. An empty mask: every node beside it counts.
    /// </summary>
    private static bool OpensInto(Vector3 p, List<NavMeshModifierVolume> volumes, LayerMask wallMask)
    {
        foreach (NavMeshModifierVolume volume in volumes)
        {
            // A doorway is at the Hub's own floor. A Hub volume is usually taller than its room, and
            // the floor above it (a catwalk alongside, Zona1's y 4.2) is "the same level" for the
            // volume's span — with a ray at that height clearing the Hub's walls from above, the whole
            // storey above drained (36 nodes in Zona1 before this).
            NemesisSafeZones.GetVerticalSpan(volume, out float minY, out _);
            if (p.y < minY - 1f || p.y > minY + DoorwayFloorReach) continue;

            Transform t = volume.transform;
            Vector3 half = volume.size * 0.5f;
            Vector3 local = t.InverseTransformPoint(p) - volume.center;
            local.x = Mathf.Clamp(local.x, -Mathf.Max(0f, half.x - DoorProbeInset), Mathf.Max(0f, half.x - DoorProbeInset));
            local.z = Mathf.Clamp(local.z, -Mathf.Max(0f, half.z - DoorProbeInset), Mathf.Max(0f, half.z - DoorProbeInset));

            Vector3 inside = t.TransformPoint(volume.center + local);
            inside.y = p.y;

            if (wallMask.value == 0) return true;

            Vector3 from = p + Vector3.up * DoorProbeHeight;
            Vector3 to = inside + Vector3.up * DoorProbeHeight;
            Vector3 dir = to - from;
            float distance = dir.magnitude;
            if (distance < 0.01f) return true;

            if (!IsWallBetween(from, dir / distance, distance, wallMask)) return true;
        }

        return false;
    }

    /// <summary>Anything on the mask in the way that is not a door. A closed door is still a way in:
    /// the player opens it.</summary>
    private static bool IsWallBetween(Vector3 from, Vector3 direction, float distance, LayerMask mask)
    {
        int count = Physics.RaycastNonAlloc(from, direction, DoorwayHits, distance, mask,
                                            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (DoorwayHits[i].collider.GetComponentInParent<DoorInteractable>() == null) return true;

        return false;
    }

    // ── Hiding spots and the lift ────────────────────────────────────────────

    private static void AddHidingSpots(PossibilityGraph graph, Dictionary<(int, int), List<int>> keptByCell,
                                       float spacing, Result result)
    {
        IReadOnlyList<HidingSpot> spots = HidingSpot.Active;
        for (int i = 0; i < spots.Count; i++)
        {
            HidingSpot spot = spots[i];
            if (spot == null || spot.IsBurned) continue;

            Vector3 approach = spot.ApproachPoint.position;
            int floor = NearestKept(graph, keptByCell, spacing, approach, SpotReach);
            if (floor < 0) continue;

            Vector3 interior = spot.InteriorPose.position;
            int node = graph.AddNode(interior, PossibilityGraph.ENodeKind.HidingSpot);
            int gate = graph.NewGate();
            float length = Vector3.Distance(graph.Position(floor), approach) + Vector3.Distance(approach, interior);
            graph.AddEdge(floor, node, length, gate);

            result.Spots.Add(new SpotNode(spot, node, gate));
        }
    }

    private static void AddLifts(PossibilityGraph graph, Dictionary<(int, int), List<int>> keptByCell,
                                 float spacing, float assumedSpeed, Result result)
    {
        IReadOnlyList<NemesisElevatorLink> lifts = NemesisElevatorLink.Active;
        for (int i = 0; i < lifts.Count; i++)
        {
            NemesisElevatorLink link = lifts[i];
            if (link == null || link.BottomLanding == null || link.TopLanding == null) continue;

            int bottom = NearestKept(graph, keptByCell, spacing, link.BottomLanding.position, LiftReach);
            int top = NearestKept(graph, keptByCell, spacing, link.TopLanding.position, LiftReach);
            if (bottom < 0 || top < 0 || bottom == top) continue;

            float ride = link.Platform != null ? link.Platform.RideSeconds : 0f;
            float length = Mathf.Max(Vector3.Distance(graph.Position(bottom), graph.Position(top)),
                                     ride * Mathf.Max(0.1f, assumedSpeed));

            int gate = graph.NewGate();
            graph.AddEdge(bottom, top, length, gate);
            result.Lifts.Add(new LiftGate(link, link.GetComponentInParent<ElevatorPower>(), gate));
        }
    }
}
