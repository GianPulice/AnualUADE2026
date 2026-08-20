using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Distances and reachability measured over the NavMesh rather than in a straight line.
///
/// It exists because <c>Vector3.Distance</c> lies in a level with floors: the Nemesis standing on
/// floor 0 right below the player is 4 metres away as the crow flies and half a storey away on
/// foot. That error is behind three separate bugs — the threat vignette lighting up between
/// floors, the "nearest" spawn/waypoint pick that is in fact on the other side of a wall, and the
/// patrol bias aiming at an unreachable zone — so the measurement lives in one place and
/// everything shares it.
///
/// Cost: every query is two <see cref="NavMesh.SamplePosition"/> calls plus one
/// <see cref="NavMesh.CalculatePath"/>. That is cheap for a few calls per second and expensive
/// per frame — throttling is the caller's job (see NemesisStateManager.EmitProximity and
/// NemesisController.BeginPatrolCycle, which both do).
/// </summary>
public static class NemesisNav
{
    /// <summary>Search radius used when snapping a loose point onto the NavMesh. A hand-placed
    /// waypoint usually ends up a few centimetres above the floor, and the player may be
    /// mid-jump.</summary>
    public const float DefaultSampleRadius = 2f;

    // Reused across calls: NavMeshPath allocates native memory in its constructor and this runs
    // several times per replan.
    private static NavMeshPath scratchPath;

    private static NavMeshPath ScratchPath => scratchPath ??= new NavMeshPath();

    /// <summary>
    /// Length of the actual path between two points, following stairs and detours.
    /// </summary>
    /// <returns>
    /// false when either end does not land on the NavMesh, or when no complete path exists
    /// between them — that is, "unreachable". A partial path counts as unreachable on purpose:
    /// getting close to the wall that separates you from the player is not getting there.
    /// </returns>
    public static bool TryGetPathDistance(Vector3 from, Vector3 to, out float distance,
                                          float sampleRadius = DefaultSampleRadius,
                                          int areaMask = NavMesh.AllAreas)
    {
        distance = float.PositiveInfinity;

        if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, sampleRadius, areaMask)) return false;
        if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, sampleRadius, areaMask)) return false;

        NavMeshPath path = ScratchPath;
        if (!NavMesh.CalculatePath(fromHit.position, toHit.position, areaMask, path)) return false;
        if (path.status != NavMeshPathStatus.PathComplete) return false;

        distance = GetPathLength(path);
        return true;
    }

    /// <summary>Same as <see cref="TryGetPathDistance"/> without the length, for when only
    /// reachability matters. Same cost: there is no cheaper shortcut in Unity's API.</summary>
    public static bool IsReachable(Vector3 from, Vector3 to,
                                   float sampleRadius = DefaultSampleRadius,
                                   int areaMask = NavMesh.AllAreas)
        => TryGetPathDistance(from, to, out _, sampleRadius, areaMask);

    /// <summary>Path distance, or <see cref="float.PositiveInfinity"/> when there is no path.
    /// Sugar for the weighting maths, where infinity already means "zero weight".</summary>
    public static float PathDistanceOrInfinity(Vector3 from, Vector3 to,
                                               float sampleRadius = DefaultSampleRadius)
        => TryGetPathDistance(from, to, out float distance, sampleRadius)
            ? distance
            : float.PositiveInfinity;

    /// <summary>Sum of the legs between corners. It is what <c>NavMeshAgent.remainingDistance</c>
    /// returns for its own path, computed here for an arbitrary one.</summary>
    public static float GetPathLength(NavMeshPath path)
    {
        if (path == null || path.corners.Length < 2) return 0f;

        float total = 0f;
        Vector3[] corners = path.corners;
        for (int i = 1; i < corners.Length; i++)
        {
            total += Vector3.Distance(corners[i - 1], corners[i]);
        }
        return total;
    }

    /// <summary>
    /// Whether the point lands on the NavMesh. The check that makes a misplaced waypoint show up
    /// while the scene is being built instead of as a Nemesis frozen mid-run.
    /// </summary>
    public static bool IsOnNavMesh(Vector3 point, float sampleRadius = DefaultSampleRadius,
                                   int areaMask = NavMesh.AllAreas)
        => NavMesh.SamplePosition(point, out _, sampleRadius, areaMask);
}
