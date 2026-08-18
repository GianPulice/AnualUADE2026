using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Tier 3.1: owns the Nemesis's patrol routes/zones and where it spawns in.
///
/// Sits next to <see cref="NemesisStateManager"/> on the Nemesis root and is referenced back from
/// it (<see cref="NemesisStateManager.NemesisController"/>), the same way the FSM states reach
/// FieldOfView/FieldOfListening/NavAgent — through the state manager, never by holding their own
/// direct reference. <see cref="NemesisPatrolState"/> is the only state that talks to this class.
///
/// Responsibilities:
///   - Holds every <see cref="NemesisRoute"/> the Nemesis can patrol, each with a weight (how
///     often it gets picked, i.e. "frequency of appearance per zone") and a locked/unlocked gate.
///   - Picks the active route and walks it, including the semi-random reverse/skip variation
///     rolled each time Patrolling is (re)entered.
///   - Unlocks routes on demand (<see cref="UnlockRoute"/>), for whatever progress-tracking
///     system decides a new zone should open up — this class does not know or care what that
///     condition is.
///   - Picks where the Nemesis spawns in (<see cref="ChooseSpawnPoint"/>), the public entry point
///     Tier 3.2 calls from its own pass.
/// </summary>
[RequireComponent(typeof(NemesisStateManager))]
public class NemesisController : MonoBehaviour
{
    [Header("Routes")]
    [Tooltip("Every patrol route/zone the Nemesis can be assigned to. The active one is picked " +
             "by weight among the currently unlocked routes each time Patrolling starts.")]
    [SerializeField] private List<NemesisRoute> routes = new List<NemesisRoute>();

    [Header("Activation")]
    [Tooltip("Puzzle that wakes the Nemesis up. Until it is solved the Nemesis is dormant: " +
             "invisible, no navigation, no senses. On completion it picks a spawn point and " +
             "starts patrolling.\n\n" +
             "Must match the PuzzleId on the puzzle's SO_PuzzleData / SO_ValvePuzzleData / etc. " +
             "Leave empty to have the Nemesis active from the moment you hit Play (the old " +
             "behaviour, where it starts wherever it happens to be placed in the scene).")]
    [SerializeField] private string activatedByPuzzleId;

    [Header("Spawn points")]
    [Tooltip("Candidate points for ChooseSpawnPoint(). The farthest one outside the player's " +
             "line of sight is picked.")]
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

    [Tooltip("Raised by ChooseSpawnPoint() when every configured spawn point is visible to the " +
             "player, right before it warps the Nemesis in anyway. Hook a fade-to-black (or " +
             "similar) transition here.\n\n" +
             "PENDING (Tier 3.1): there is no fade system in the project yet, so this fires and " +
             "the warp still happens the same frame — see the ChooseSpawnPoint doc comment.")]
    [SerializeField] private UnityEvent onAllSpawnPointsVisible;

    [SerializeField] private NemesisStateManager stateManager;

    // ── Active-route traversal state ───────────────────────────────────────
    private NemesisRoute currentRoute;
    private int currentWaypointIndex;
    private int direction = 1;       // +1 walks the route forward, -1 walks it backward.
    private bool pendingSkip;        // Consumed by the first AdvanceToNextWaypoint() of a cycle.

    // Reused across calls so AllUnlockedWaypoints does not allocate a new list every time the
    // Nemesis gets stuck or needs to reposition after a capture.
    private readonly List<Transform> flatWaypointsBuffer = new List<Transform>();
    private readonly List<Transform> spawnPointsBuffer = new List<Transform>();

    /// <summary>The waypoint the Nemesis should currently be walking towards, or null if no
    /// route is active (nothing unlocked yet, or no routes configured at all).</summary>
    public Transform CurrentWaypoint
    {
        get
        {
            if (currentRoute == null) return null;
            IReadOnlyList<Transform> waypoints = currentRoute.Waypoints;
            if (waypoints.Count == 0) return null;

            return waypoints[Mathf.Clamp(currentWaypointIndex, 0, waypoints.Count - 1)];
        }
    }

    /// <summary>Every waypoint across every unlocked route, flattened. Used for the "any
    /// waypoint will do" cases: post-capture repositioning and stuck-escape.</summary>
    public IReadOnlyList<Transform> AllUnlockedWaypoints
    {
        get
        {
            flatWaypointsBuffer.Clear();
            for (int i = 0; i < routes.Count; i++)
            {
                NemesisRoute route = routes[i];
                if (route == null || !route.IsUnlocked) continue;
                flatWaypointsBuffer.AddRange(route.Waypoints);
            }
            return flatWaypointsBuffer;
        }
    }

    /// <summary>
    /// Every configured spawn point, nulls filtered out (an inspector list almost always carries
    /// a couple of empty slots). Used by the post-capture reposition, which wants any of them at
    /// random — <see cref="ChooseSpawnPoint"/> picks the farthest hidden one instead, which is
    /// right for the Nemesis's first arrival and wrong for every capture after it.
    /// </summary>
    public IReadOnlyList<Transform> SpawnPoints
    {
        get
        {
            spawnPointsBuffer.Clear();
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (spawnPoints[i] != null) spawnPointsBuffer.Add(spawnPoints[i]);
            }
            return spawnPointsBuffer;
        }
    }

    public UnityEvent OnAllSpawnPointsVisible => onAllSpawnPointsVisible;

    /// <summary>Puzzle that wakes the Nemesis up, or empty/null when it starts active.
    /// Read by <see cref="NemesisStateManager"/>, which owns the dormant/awake gate.</summary>
    public string ActivatedByPuzzleId => activatedByPuzzleId;

    private void Awake()
    {
        if (stateManager == null) stateManager = GetComponent<NemesisStateManager>();
    }

    // ── Patrol routing (called by NemesisPatrolState) ──────────────────────

    /// <summary>
    /// Called once every time Patrolling is (re)entered. Picks the active route by weight among
    /// the unlocked ones — switching route resets the walk index to 0, staying on the same route
    /// just clamps it in case the route's waypoint count changed at runtime — and rolls the
    /// semi-random reverse/skip variation for this cycle.
    /// </summary>
    public void BeginPatrolCycle()
    {
        NemesisRoute selected = SelectWeightedRoute();

        if (selected != currentRoute)
        {
            currentRoute = selected;
            currentWaypointIndex = 0;
        }
        else if (currentRoute != null && currentRoute.Waypoints.Count > 0)
        {
            currentWaypointIndex = Mathf.Clamp(currentWaypointIndex, 0, currentRoute.Waypoints.Count - 1);
        }

        SO_NemesisData data = stateManager != null ? stateManager.NemesisData : null;
        float reverseChance = data != null ? data.RouteReverseChance : 0f;
        float skipChance = data != null ? data.RouteSkipWaypointChance : 0f;

        direction = Random.value < reverseChance ? -1 : 1;
        pendingSkip = Random.value < skipChance;
    }

    /// <summary>
    /// Moves the traversal on to the next waypoint on the active route, honoring the current
    /// direction and consuming the pending skip (if this cycle rolled one). Called by
    /// NemesisPatrolState once it has waited out PatrolWaypointWaitTime at <see cref="CurrentWaypoint"/>.
    /// </summary>
    public void AdvanceToNextWaypoint()
    {
        if (currentRoute == null) return;

        int count = currentRoute.Waypoints.Count;
        if (count == 0) return;

        int step = direction;
        if (pendingSkip)
        {
            step *= 2;
            pendingSkip = false;
        }

        currentWaypointIndex = Wrap(currentWaypointIndex + step, count);
    }

    private static int Wrap(int index, int count) => ((index % count) + count) % count;

    /// <summary>Weighted-random pick among the routes that are both unlocked and non-empty. Null
    /// if none qualify (e.g. nothing unlocked yet).</summary>
    private NemesisRoute SelectWeightedRoute()
    {
        float totalWeight = 0f;
        for (int i = 0; i < routes.Count; i++)
        {
            NemesisRoute route = routes[i];
            if (route == null || !route.IsUnlocked || route.Waypoints.Count == 0) continue;
            totalWeight += Mathf.Max(0f, route.Weight);
        }

        if (totalWeight <= 0f) return null;

        float roll = Random.value * totalWeight;
        float cumulative = 0f;
        for (int i = 0; i < routes.Count; i++)
        {
            NemesisRoute route = routes[i];
            if (route == null || !route.IsUnlocked || route.Waypoints.Count == 0) continue;

            cumulative += Mathf.Max(0f, route.Weight);
            if (roll <= cumulative) return route;
        }

        // Only reachable through float rounding at the very edge of the range: fall back to the
        // last qualifying route rather than returning nothing when totalWeight was in fact > 0.
        for (int i = routes.Count - 1; i >= 0; i--)
        {
            NemesisRoute route = routes[i];
            if (route != null && route.IsUnlocked && route.Waypoints.Count > 0) return route;
        }
        return null;
    }

    // ── Route unlocking ─────────────────────────────────────────────────────

    /// <summary>
    /// Opens a route up for selection. Meant to be called by whatever tracks progress (a puzzle
    /// completion callback, a checkpoint, a future director) — this class has no opinion on when
    /// that should happen, only on what "unlocked" means for route selection.
    /// </summary>
    public void UnlockRoute(int routeIndex)
    {
        if (routeIndex < 0 || routeIndex >= routes.Count)
        {
            Debug.LogWarning($"[NemesisController] UnlockRoute({routeIndex}) is out of range " +
                             $"(0..{routes.Count - 1}).", this);
            return;
        }

        NemesisRoute route = routes[routeIndex];
        if (route == null) return;

        route.Unlock();
    }

    // ── Spawn point selection ───────────────────────────────────────────────

    /// <summary>
    /// Picks a spawn point and warps the Nemesis there. Prefers the point farthest from the
    /// player that is outside the player's line of sight (reusing FieldOfListening's occlusion
    /// raycast — see <see cref="IsHiddenFromPlayer"/> — rather than a dedicated vision check).
    ///
    /// If every configured point is currently visible, <see cref="onAllSpawnPointsVisible"/>
    /// fires before the warp so a fade-to-black (or similar) can mask the pop-in.
    ///
    /// PENDING (Tier 3.1): the project has no fade system yet, so that event and the warp below
    /// both happen on the same frame — the Nemesis can still be seen popping in for that one
    /// edge case. Once a fade transition exists, split this into "start fade" and "warp once the
    /// fade has covered the screen" (e.g. drive the warp from the fade's own completion
    /// callback) instead of firing them together.
    /// </summary>
    /// <returns>The chosen spawn point, or null if none are configured.</returns>
    public Transform ChooseSpawnPoint()
    {
        Transform chosen = SelectSpawnPoint(out bool allVisible);
        if (chosen == null) return null;

        if (allVisible) onAllSpawnPointsVisible?.Invoke();

        WarpTo(chosen);
        return chosen;
    }

    private Transform SelectSpawnPoint(out bool allVisible)
    {
        allVisible = false;
        if (spawnPoints == null || spawnPoints.Count == 0) return null;

        Transform player = PlayerRegistry.CurrentTransform;

        Transform bestHidden = null;
        float bestHiddenDistance = -1f;
        Transform bestAny = null;
        float bestAnyDistance = -1f;

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            Transform point = spawnPoints[i];
            if (point == null) continue;

            float distance = player != null ? Vector3.Distance(point.position, player.position) : 0f;
            if (distance > bestAnyDistance)
            {
                bestAnyDistance = distance;
                bestAny = point;
            }

            // No registered player yet: nobody to be seen by, every point counts as hidden.
            if (player == null || IsHiddenFromPlayer(point.position, player.position))
            {
                if (distance > bestHiddenDistance)
                {
                    bestHiddenDistance = distance;
                    bestHidden = point;
                }
            }
        }

        if (bestHidden != null) return bestHidden;

        // Every configured point is in view (or none could be tested): fall back to the
        // farthest one and let the caller know to mask the pop-in.
        allVisible = bestAny != null;
        return bestAny;
    }

    private bool IsHiddenFromPlayer(Vector3 point, Vector3 playerPosition)
    {
        FieldOfListening listener = stateManager != null ? stateManager.FieldOfListening : null;
        if (listener == null) return false;   // No way to test: assume it is visible.

        return listener.IsOccludedByWall(playerPosition, point);
    }

    private void WarpTo(Transform point)
    {
        NavMeshAgent agent = stateManager != null ? stateManager.NavAgent : null;

        // Warp and not transform.position: a NavMeshAgent keeps its own internal position and
        // would drag the Nemesis straight back on the next agent update (same reasoning as
        // NemesisStateManager's RepositionAfterCapture/TeleportToStuckEscapeWayPoint).
        if (agent != null && agent.isActiveAndEnabled) agent.Warp(point.position);
        else transform.position = point.position;
    }
}
