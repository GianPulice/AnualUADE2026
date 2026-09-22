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

    /// <summary>How many of the nearest waypoints are weighed for the escape. Each one costs up to
    /// two path queries, paid once per warp — rare, but all in one frame, so it is bounded.</summary>
    private const int EscapeCandidateCount = 16;

    /// <summary>
    /// A warp that lands this close to where the body got wedged is not an escape.
    ///
    /// The stairs of Zona1 are the case that proved it (WIR-028): the Nemesis wedged in the funnel
    /// at the top of the flight, and the nearest waypoint was 1.4 m away, on the landing it had
    /// just walked off. It warped there, walked straight back into the funnel, and wedged again.
    /// </summary>
    private const float EscapeMinDistance = 3f;

    /// <summary>Metres added to a candidate the Nemesis cannot walk to from where it stands, so a
    /// waypoint on its own island always wins when there is one — and one on another island is
    /// still taken when it is the only way off the island it is stranded on.</summary>
    private const float OffIslandPenalty = 50f;

    private struct EscapeCandidate
    {
        public Transform Waypoint;
        public float StraightDistance;
    }

    private readonly List<EscapeCandidate> escapeCandidates = new List<EscapeCandidate>();

    /// <summary>
    /// Where to take the body: a waypoint the player cannot see, from which the Nemesis can still
    /// get where it was going, not on top of the trap it is escaping, and as close ON FOOT as the
    /// rest allows.
    ///
    /// It used to be the nearest hidden waypoint in a STRAIGHT LINE, which broke the project's own
    /// rule — distances over the NavMesh, never Vector3.Distance, in a level with floors — in the
    /// three ways that rule exists to prevent (WIR-028, WIR-050): the nearest marker through the
    /// air can be on the other floor; it can sit on an island cut off from the rest, like the floor
    /// of the freight elevator's shaft, where the Nemesis lands, cannot path out, wedges and warps
    /// to the same nearest marker again; and it can be right back where it got stuck (see
    /// <see cref="EscapeMinDistance"/>).
    ///
    /// Each requirement is a tier rather than a filter, so a level with too few waypoints degrades
    /// to the old behaviour instead of leaving the Nemesis wedged: hidden comes first (being seen
    /// to teleport is bad, staying wedged for the rest of the run is worse), then reaching the
    /// goal, then clearing the trap; walking distance only breaks ties within a tier.
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
        bool hasGoal = TryGetGoal(out Vector3 goal);
        bool onMesh = NemesisNav.IsOnNavMesh(position);

        // Nearest first, in a straight line: only a bound on how many are weighed. The ranking
        // itself is over the NavMesh, below.
        escapeCandidates.Clear();
        foreach (Transform wp in allWaypoints)
        {
            if (wp == null) continue;

            escapeCandidates.Add(new EscapeCandidate
            {
                Waypoint = wp,
                StraightDistance = Vector3.Distance(position, wp.position),
            });
        }

        escapeCandidates.Sort((a, b) => a.StraightDistance.CompareTo(b.StraightDistance));

        Transform best = null;
        int bestTier = int.MaxValue;
        float bestCost = float.MaxValue;
        int count = Mathf.Min(escapeCandidates.Count, EscapeCandidateCount);

        for (int i = 0; i < count; i++)
        {
            EscapeCandidate candidate = escapeCandidates[i];
            Vector3 point = candidate.Waypoint.position;

            bool hidden = IsHiddenFromPlayer(point);
            bool reachesGoal = !hasGoal || NemesisNav.IsReachable(point, goal);
            bool clearOfTrap = candidate.StraightDistance >= EscapeMinDistance;

            int tier = (hidden ? 0 : 3) + (reachesGoal ? (clearOfTrap ? 0 : 1) : 2);
            if (tier > bestTier) continue;

            float cost = onMesh && NemesisNav.TryGetPathDistance(position, point, out float walk)
                ? walk
                : candidate.StraightDistance + OffIslandPenalty;

            if (tier == bestTier && cost >= bestCost) continue;

            best = candidate.Waypoint;
            bestTier = tier;
            bestCost = cost;
        }

        if (best == null) return;

        stateManager.WarpTo(best.position);
    }

    /// <summary>
    /// Where the Nemesis was trying to get: the agent's own destination when it has one, else what
    /// it believes about the player.
    ///
    /// Only ever a FILTER on the escape (can the landing still get there), never a pull towards it
    /// — a warp that moved the monster closer to the player would be a shortcut nobody saw it take.
    /// The destination is not read off an agent that is off the NavMesh: Unity logs an error for
    /// that, and such an agent has nowhere meaningful to be going anyway.
    /// </summary>
    private bool TryGetGoal(out Vector3 goal)
    {
        NavMeshAgent agent = stateManager.NavAgent;

        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            goal = agent.destination;
            if ((goal - transform.position).sqrMagnitude > 1f) return true;
        }

        return stateManager.TryGetBelief(out goal);
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
