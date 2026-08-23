using System.Collections.Generic;
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

    /// <summary>How close a path corner has to pass to a landing to count as "this path went
    /// through that elevator". The corner Unity emits for a link IS the link endpoint, so this
    /// only has to absorb the SamplePosition snap between the landing marker and the baked
    /// surface underneath it.</summary>
    private const float LandingMatchRadius = 1.5f;

    // Reused across calls: NavMeshPath allocates native memory in its constructor and this runs
    // several times per replan.
    private static NavMeshPath scratchPath;

    private static NavMeshPath ScratchPath => scratchPath ??= new NavMeshPath();

    /// <summary>
    /// Everything one path query already worked out, instead of throwing all but one number away.
    ///
    /// Why it exists: answering "is the player upstairs, and would getting there mean the lift?"
    /// used to need a separate set of height comparisons and raycasts. It does not.
    /// <see cref="NavMesh.CalculatePath"/> already walked the floors, so the answer is sitting in
    /// the path — whether it completed, how far it really is, how much of a detour that is
    /// against the straight line, and which freight elevator it went through. Reading them off
    /// the one query is both cheaper and more honest than inferring them from geometry.
    /// </summary>
    public readonly struct NavRoute
    {
        /// <summary>The path reaches the target. False means partial: it stops at the closest
        /// reachable point, which is usually right against whatever separates the two.</summary>
        public readonly bool IsComplete;

        /// <summary>Length of the path actually walked, following stairs and detours.</summary>
        public readonly float PathDistance;

        /// <summary>Straight-line distance between the two snapped endpoints.</summary>
        public readonly float StraightDistance;

        /// <summary>Signed height difference, target minus origin. Positive means the target is
        /// above.</summary>
        public readonly float VerticalDelta;

        /// <summary>The freight elevator this path goes through, or null. A reference and not a
        /// bool because whoever decides to take the lift also needs to know WHICH one to walk
        /// to.</summary>
        public readonly NemesisElevatorLink CrossedElevator;

        public NavRoute(bool isComplete, float pathDistance, float straightDistance,
                        float verticalDelta, NemesisElevatorLink crossedElevator)
        {
            IsComplete = isComplete;
            PathDistance = pathDistance;
            StraightDistance = straightDistance;
            VerticalDelta = verticalDelta;
            CrossedElevator = crossedElevator;
        }

        public bool CrossesLink => CrossedElevator != null;

        /// <summary>How much longer walking is than flying. Near 1 is a straight corridor;
        /// anything past ~2.5 is a detour big enough to mean another floor or the far side of the
        /// level.</summary>
        public float DetourFactor => StraightDistance > 0.01f ? PathDistance / StraightDistance : 1f;
    }

    /// <summary>
    /// One path query, every answer it can give.
    /// </summary>
    /// <returns>
    /// false only when the query could not run at all: an end that does not land on the NavMesh,
    /// or a CalculatePath that refused outright. A partial path still returns true, with
    /// <see cref="NavRoute.IsComplete"/> false — that is the whole difference from
    /// <see cref="TryGetPathDistance"/>, which collapses "unreachable" and "unaskable" into the
    /// same "no" and so cannot tell the FSM which of the two it is looking at.
    /// </returns>
    public static bool TryGetRoute(Vector3 from, Vector3 to, out NavRoute route,
                                   float sampleRadius = DefaultSampleRadius,
                                   int areaMask = NavMesh.AllAreas)
    {
        route = default;

        if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, sampleRadius, areaMask)) return false;
        if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, sampleRadius, areaMask)) return false;

        NavMeshPath path = ScratchPath;
        if (!NavMesh.CalculatePath(fromHit.position, toHit.position, areaMask, path)) return false;

        route = new NavRoute(
            isComplete: path.status == NavMeshPathStatus.PathComplete,
            pathDistance: GetPathLength(path),
            straightDistance: Vector3.Distance(fromHit.position, toHit.position),
            verticalDelta: toHit.position.y - fromHit.position.y,
            crossedElevator: FindCrossedElevator(path));

        return true;
    }

    /// <summary>
    /// Which freight elevator this path goes through, if any.
    ///
    /// NavMeshPath carries no per-corner metadata — there is no "this corner is a link" flag to
    /// read. But the corners Unity emits for a link traversal ARE the link's two endpoints, and
    /// those endpoints are the landings, which the elevators register themselves in
    /// <see cref="NemesisElevatorLink.Active"/>. So the test is whether the path touches down near
    /// BOTH landings of the same elevator. Both and not either: a path that merely walks past the
    /// bottom landing on its way elsewhere is not a path that uses the lift.
    ///
    /// Cost is corners x active elevators, and a level has one or two elevators.
    /// </summary>
    private static NemesisElevatorLink FindCrossedElevator(NavMeshPath path)
    {
        IReadOnlyList<NemesisElevatorLink> elevators = NemesisElevatorLink.Active;
        if (elevators.Count == 0) return null;

        Vector3[] corners = path.corners;
        if (corners.Length < 2) return null;

        const float matchSqr = LandingMatchRadius * LandingMatchRadius;

        for (int e = 0; e < elevators.Count; e++)
        {
            NemesisElevatorLink elevator = elevators[e];
            if (elevator == null) continue;

            Vector3 bottom = elevator.BottomLanding.position;
            Vector3 top = elevator.TopLanding.position;

            bool touchesBottom = false;
            bool touchesTop = false;

            for (int c = 0; c < corners.Length; c++)
            {
                if (!touchesBottom && Vector3.SqrMagnitude(corners[c] - bottom) <= matchSqr)
                    touchesBottom = true;

                if (!touchesTop && Vector3.SqrMagnitude(corners[c] - top) <= matchSqr)
                    touchesTop = true;

                if (touchesBottom && touchesTop) return elevator;
            }
        }

        return null;
    }

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

        if (!TryGetRoute(from, to, out NavRoute route, sampleRadius, areaMask)) return false;
        if (!route.IsComplete) return false;

        distance = route.PathDistance;
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
