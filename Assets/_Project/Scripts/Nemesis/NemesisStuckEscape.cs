using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Watchdog that warps the Nemesis out when it has stopped making progress — wedged on geometry,
/// or stranded on a NavMesh island it cannot path off.
///
/// Extracted from NemesisStateManager. It is a self-contained loop with its own clock, its own
/// tolerance and its own recovery, and none of it informs any decision the FSM makes: the states
/// neither know nor care that this exists. The only thing they share is the suppression counter
/// below, which is a two-method API.
///
/// SETUP: goes on the Nemesis root. NemesisStateManager finds it, adds it if missing, and ticks
/// it once per frame after the FSM.
/// </summary>
public class NemesisStuckEscape : MonoBehaviour
{
    // Tuning lives in SO_NemesisData, reached through the state manager, so a designer edits one
    // asset instead of hunting for values scattered across the components. Nothing is serialised
    // on this component at all — it has no scene wiring of its own.
    private const float FallbackCheckInterval = 3f;
    private const float FallbackMinDistance = 0.5f;
    private const float FallbackRepathGrace = 1.5f;

    private NemesisStateManager stateManager;

    private Vector3 lastSamplePosition;
    private float sampleTimer;

    /// <summary>
    /// How far up the escalation the current episode has got.
    ///
    /// The watchdog used to have one response to everything — teleport — which is the strongest
    /// move available and the one that reads worst: a monster that vanishes and reappears
    /// elsewhere. Most of what it fired on was not a wedged body but a corrupt path, and a corrupt
    /// path is fixed by asking for it again.
    ///
    /// So: first no-progress window buys a repath, the next one buys the warp. Reset on any
    /// progress, so an episode has to be continuous to escalate — a Nemesis that gets stuck twice
    /// with a clean walk in between gets a repath both times, not a warp the second time.
    /// </summary>
    private EStuckStage stage = EStuckStage.Watching;

    private enum EStuckStage
    {
        /// <summary>Making progress, or not stuck long enough to have done anything about it.</summary>
        Watching,

        /// <summary>The path has been thrown away and asked for again; waiting out
        /// <see cref="RepathGrace"/> to see whether that was all it needed.</summary>
        Repathed,
    }

    // A counter and not a bool: the door and the freight elevator can both request suppression at
    // the same time, and with a bool the first one to release it would re-arm the detection while
    // the other is still busy.
    private int suppressionCount;

    /// <summary>Called by NemesisStateManager during its Awake, so this is wired before any tick.
    /// </summary>
    public void Initialize(NemesisStateManager manager)
    {
        stateManager = manager;
        lastSamplePosition = transform.position;
    }

    /// <summary>Seconds of no progress that count as stuck. Falls back to a sane default when no
    /// SO_NemesisData is assigned, rather than to 0, which would fire the escape every frame.
    /// </summary>
    private float CheckInterval
    {
        get
        {
            SO_NemesisData data = stateManager != null ? stateManager.NemesisData : null;
            return data != null ? Mathf.Max(0.5f, data.StuckCheckInterval) : FallbackCheckInterval;
        }
    }

    /// <summary>Distance that counts as progress within <see cref="CheckInterval"/>.</summary>
    private float MinDistance
    {
        get
        {
            SO_NemesisData data = stateManager != null ? stateManager.NemesisData : null;
            return data != null ? data.StuckMinDistance : FallbackMinDistance;
        }
    }

    /// <summary>Seconds a repath gets to work before the warp. See
    /// <see cref="SO_NemesisData.StuckRepathGrace"/>.</summary>
    private float RepathGrace
    {
        get
        {
            SO_NemesisData data = stateManager != null ? stateManager.NemesisData : null;
            return data != null ? Mathf.Max(0.2f, data.StuckRepathGrace) : FallbackRepathGrace;
        }
    }

    /// <summary>
    /// How many times each rung of the escalation has fired this session, and where the last warp
    /// happened.
    ///
    /// Counted rather than only logged because the number is the actual verdict on the level. A
    /// warp or two over a long session is a watchdog doing its job; a warp every thirty seconds in
    /// the same corner is a NavMesh bake or a badly placed waypoint, and no amount of tuning in
    /// here will fix it. The two counters separate "the path went bad and we fixed it quietly"
    /// from "the body was actually wedged", which are different problems with different owners.
    /// </summary>
    public int RepathCount { get; private set; }

    public int WarpCount { get; private set; }

    /// <summary>Where the last warp fired, for the debug HUD and for QA to point at.</summary>
    public Vector3 LastWarpOrigin { get; private set; }

    /// <summary>
    /// True while something is moving the Nemesis outside the NavMeshAgent: opening a door, or
    /// riding the freight elevator. Detection does not run in that window.
    ///
    /// Without this, an elevator ride longer than <see cref="CheckInterval"/> reads as "made no
    /// progress in 3 seconds while pathing" and the escape warps it out of the lift, mid-ascent.
    /// </summary>
    public bool IsSuppressed => suppressionCount > 0;

    /// <summary>Opens a window with no stuck detection. Every <see cref="Push"/> must have its
    /// <see cref="Pop"/>, even if the traversal is cancelled — use try/finally.</summary>
    public void Push() => suppressionCount++;

    public void Pop()
    {
        suppressionCount = Mathf.Max(0, suppressionCount - 1);

        // Otherwise the first check after the traversal would measure progress from where it stood
        // before boarding, and read it as ground it never actually covered on foot.
        ResetSample();
    }

    /// <summary>
    /// Restarts the measurement from wherever the Nemesis is now.
    ///
    /// Called after every teleport — the spawn pick, the reposition after a capture, this class's
    /// own escape. Without it the next check measures against a position half a level away and
    /// reads a warp as ground covered on foot, which is the opposite of the mistake it is
    /// guarding against but just as wrong.
    /// </summary>
    public void ResetSample()
    {
        ResetProgressSample();

        // A teleport ends the episode as well as the measurement. Without this an external warp —
        // a respawn, the spawn pick — could leave the escalation standing at "already repathed",
        // and the first no-progress window at the new position would skip the cheap fix and warp
        // the Nemesis again.
        stage = EStuckStage.Watching;
    }

    /// <summary>
    /// Restarts only the measurement, deliberately keeping the escalation where it is.
    ///
    /// This is what <see cref="Tick"/> uses for the frames that do not count — and the difference
    /// matters most in the one right after a repath, when the agent reports pathPending and
    /// therefore "not trying to move". Clearing the stage there would forget that a repath had
    /// already been tried, and the watchdog would repath forever and never escalate.
    /// </summary>
    private void ResetProgressSample()
    {
        lastSamplePosition = transform.position;
        sampleTimer = 0f;
    }

    /// <summary>
    /// One frame of the watchdog.
    /// </summary>
    /// <param name="isNavigatingState">Whether the FSM is in a state that is supposed to be
    /// getting somewhere. Passed in rather than read off the FSM, so this class does not need to
    /// know the state enum at all.</param>
    public void Tick(bool isNavigatingState)
    {
        // Only counts while it is actually trying to get somewhere. Waiting out
        // PatrolWaypointWaitTime at a waypoint is not being stuck, and testing the agent's path
        // covers that without having to special-case each state's idle timings.
        if (IsSuppressed || !isNavigatingState || !IsTryingToMove())
        {
            ResetProgressSample();
            return;
        }

        // The window is shorter once a repath is in flight: the Nemesis has ALREADY spent a full
        // interval going nowhere, so this is a second chance rather than a second full wait.
        float interval = stage == EStuckStage.Repathed ? RepathGrace : CheckInterval;

        sampleTimer += Time.deltaTime;
        if (sampleTimer < interval) return;

        sampleTimer = 0f;

        Vector3 position = transform.position;
        float travelled = Vector3.Distance(position, lastSamplePosition);
        lastSamplePosition = position;

        if (travelled >= MinDistance)
        {
            // Moving again. Whatever it was, it is over — the next episode starts from the bottom
            // of the escalation rather than inheriting this one's progress up it.
            stage = EStuckStage.Watching;
            return;
        }

        if (stage == EStuckStage.Watching)
        {
            stage = EStuckStage.Repathed;
            Repath(travelled, interval);
            return;
        }

        Warp(travelled, interval);
        stage = EStuckStage.Watching;
    }

    /// <summary>
    /// First rung: throw the path away and ask for the same destination again.
    ///
    /// Most of what this watchdog fires on is a path that went bad rather than a body that got
    /// wedged — a destination issued while the agent was mid-warp, a path computed against
    /// geometry that has since been carved by a door, a partial path the agent is dutifully
    /// walking to the end of. None of those is a reason to teleport a monster across the level in
    /// front of the player; all of them are fixed by asking the navigation system again.
    ///
    /// The destination is READ BACK and re-issued rather than remembered from somewhere: whatever
    /// the agent is currently aiming at is what the FSM wants it to aim at, and this rung has no
    /// business having an opinion about the target — only about the route to it.
    /// </summary>
    private void Repath(float travelled, float interval)
    {
        NavMeshAgent agent = stateManager.NavAgent;

        // Off the mesh entirely: there is no path to fix, so skip straight to the warp. Repathing
        // here would only spend the grace window logging errors.
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
        {
            Warp(travelled, interval);
            stage = EStuckStage.Watching;
            return;
        }

        RepathCount++;

        Vector3 destination = agent.destination;
        agent.ResetPath();
        agent.SetDestination(destination);

        Debug.Log($"[{nameof(NemesisStuckEscape)}] No progress ({travelled:F2}u in {interval}s). " +
                  $"Repathing to {destination} before giving up on it. " +
                  $"(repaths this session: {RepathCount})", this);
    }

    /// <summary>Last rung: the body really is wedged, so take it somewhere it can walk from.</summary>
    private void Warp(float travelled, float interval)
    {
        WarpCount++;
        LastWarpOrigin = transform.position;

        Debug.LogWarning($"[{nameof(NemesisStuckEscape)}] Still stuck after a repath " +
                         $"({travelled:F2}u in {interval}s) at {LastWarpOrigin}. Warping out. " +
                         $"(warps this session: {WarpCount}) — a warp or two over a long run is " +
                         "this watchdog working; the same corner over and over is a NavMesh bake " +
                         "or a waypoint problem, not a tuning one.", this);

        TeleportToEscapeWaypoint();
    }

    /// <summary>
    /// Whether the Nemesis is currently supposed to be getting somewhere — which is what makes a
    /// lack of progress mean "stuck" rather than "waiting out PatrolWaypointWaitTime".
    ///
    /// The two cases below used to return false, i.e. "not trying to move", which reset the stuck
    /// timer every frame. That left the escape unable to fire in precisely the two situations it
    /// exists for: the Nemesis stood still, animation and all, for the rest of the run.
    /// </summary>
    private bool IsTryingToMove()
    {
        NavMeshAgent agent = stateManager.NavAgent;
        if (agent == null || !agent.isActiveAndEnabled) return false;

        // Off the NavMesh altogether — a Warp that did not land on one (ChooseSpawnPoint and the
        // escape itself both warp blind), or geometry rebuilt out from under it. It cannot path
        // anywhere and will not recover on its own, so this is the most stuck it can possibly be.
        if (!agent.isOnNavMesh) return true;

        if (agent.pathPending) return false;

        // A destination it cannot reach: a waypoint placed off the mesh, or one on an island cut
        // off by a closed door. hasPath stays false while the agent re-requests an impossible
        // path and remainingDistance reads Infinity, so the check below read it as "idle at a
        // waypoint". Guarded on the destination being somewhere else, because an agent that has
        // never been given one reports PathInvalid while standing exactly where it belongs.
        if (agent.pathStatus != NavMeshPathStatus.PathComplete &&
            Vector3.Distance(transform.position, agent.destination) > agent.stoppingDistance)
        {
            return true;
        }

        return agent.hasPath && agent.remainingDistance > agent.stoppingDistance;
    }

    /// <summary>
    /// Nearest waypoint the player cannot see, so the Nemesis is not watched teleporting.
    /// </summary>
    private void TeleportToEscapeWaypoint()
    {
        NemesisController controller = stateManager.NemesisController;
        IReadOnlyList<Transform> allWaypoints = controller != null
            ? controller.AllUnlockedWaypoints
            : null;

        if (allWaypoints == null || allWaypoints.Count == 0)
        {
            Debug.LogWarning($"[{nameof(NemesisStuckEscape)}] Stuck with no waypoints to escape " +
                             "to.", this);
            return;
        }

        Vector3 position = transform.position;
        Transform best = null;
        float bestDistance = float.MaxValue;
        Transform nearestOverall = null;
        float nearestOverallDistance = float.MaxValue;

        foreach (Transform wp in allWaypoints)
        {
            if (wp == null) continue;

            float distance = Vector3.Distance(position, wp.position);

            if (distance < nearestOverallDistance)
            {
                nearestOverallDistance = distance;
                nearestOverall = wp;
            }

            if (!IsHiddenFromPlayer(wp.position)) continue;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = wp;
        }

        // Every waypoint is in view (open room, no cover): warp to the nearest one anyway.
        // Being seen to teleport is bad, staying wedged for the rest of the run is worse.
        if (best == null) best = nearestOverall;
        if (best == null) return;

        stateManager.WarpTo(best.position);
    }

    private bool IsHiddenFromPlayer(Vector3 point)
    {
        Transform player = stateManager.PlayerTransform;
        if (player == null) return true;        // Nobody around to watch it happen.

        FieldOfListening listening = stateManager.FieldOfListening;
        if (listening == null) return false;    // No way to test: assume it is visible.

        return listening.IsOccludedByWall(player.position, point);
    }
}
