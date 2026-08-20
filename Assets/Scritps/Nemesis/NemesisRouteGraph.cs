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

    private readonly List<Node> nodes = new List<Node>();

    /// <summary>Which NavMesh island each node belongs to. Two nodes sharing a number have a path
    /// between them; different numbers mean no path.</summary>
    private readonly List<int> componentOf = new List<int>();

    /// <summary>One node per island, the first one found. Everything else is tested against
    /// these, which is what keeps this out of N squared.</summary>
    private readonly List<int> representatives = new List<int>();

    private string fingerprint = string.Empty;
    private int componentCount;

    public int NodeCount => nodes.Count;
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

            builder.Append(route.GetInstanceID()).Append(':')
                   .Append(route.Waypoints.Count).Append('|');
        }
        return builder.ToString();
    }

    public bool NeedsRebuild(IReadOnlyList<NemesisRoute> routes) =>
        Fingerprint(routes) != fingerprint;

    /// <summary>
    /// Rebuilds the merged set. Cheap to over-call: it does nothing when the fingerprint matches.
    /// </summary>
    /// <param name="force">Rebuild even when the fingerprint matches. For after a NavMesh rebake,
    /// which changes no route but does change what is reachable.</param>
    public void Rebuild(IReadOnlyList<NemesisRoute> routes, bool force = false)
    {
        string next = Fingerprint(routes);
        if (!force && next == fingerprint) return;

        fingerprint = next;
        nodes.Clear();
        componentOf.Clear();
        representatives.Clear();
        componentCount = 0;

        if (routes == null) return;

        CollectValidNodes(routes);
        AssignComponents();
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
