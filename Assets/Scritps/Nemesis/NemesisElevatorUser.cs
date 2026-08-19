using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Performs by hand the NavMeshLinks the Nemesis steps onto, treating the ones that are a freight
/// elevator (<see cref="NemesisElevatorLink"/>) specially: it calls the platform over if it is
/// parked on the other side, boards it, rides it and steps off at the opposite landing.
///
/// It turns <c>autoTraverseOffMeshLink</c> off in Awake, so it becomes responsible for ALL links,
/// not just elevators. Plain ones (jumps and drops, including those the NavMeshSurface generates
/// itself with Generate Links) are resolved with a simple interpolation — which also looks better
/// than the instant hop of the automatic mode.
///
/// SETUP: goes on the Nemesis root, next to the NemesisStateManager. It needs no references: it
/// finds the elevator through the link the agent stepped onto.
/// </summary>
[RequireComponent(typeof(NemesisStateManager))]
public class NemesisElevatorUser : MonoBehaviour
{
    [Header("Plain links (jumps / drops)")]
    [Tooltip("Speed at which a plain link is crossed, in metres per second.")]
    [SerializeField, Min(0.1f)] private float linkTraversalSpeed = 2.5f;

    [Header("Freight elevator")]
    [Tooltip("Speed at which it boards and steps off the platform.")]
    [SerializeField, Min(0.1f)] private float boardingSpeed = 1.5f;

    [Tooltip("Maximum seconds it waits for the platform to free up or finish a trip.\n\n" +
             "This is the safety net for 'the player is using the elevator': once it runs out, " +
             "the Nemesis abandons the link and paths whatever other way it can, instead of " +
             "waiting at the landing forever.")]
    [SerializeField, Min(1f)] private float platformWaitTimeout = 20f;

    private NemesisStateManager stateManager;
    private NavMeshAgent agent;
    private bool isTraversing;

    private void Awake()
    {
        stateManager = GetComponent<NemesisStateManager>();
        agent = GetComponent<NavMeshAgent>();
        if (agent == null && stateManager != null) agent = stateManager.NavAgent;

        if (agent == null)
        {
            Debug.LogError($"[{nameof(NemesisElevatorUser)}] '{name}' could not find the " +
                           "NavMeshAgent. Component disabled.", this);
            enabled = false;
            return;
        }

        // From here on this component crosses the links. Without it Unity resolves them on its own
        // and there is no window to step in and drive the elevator: by the time isOnOffMeshLink
        // turns true the automatic traversal has already started.
        agent.autoTraverseOffMeshLink = false;
    }

    private void Update()
    {
        if (isTraversing) return;
        if (stateManager == null || !stateManager.IsActive) return;
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        if (!agent.isOnOffMeshLink) return;

        OffMeshLinkData data = agent.currentOffMeshLinkData;
        if (!data.valid) return;

        // owner is typed as UnityEngine.Object, but NavMeshLink registers itself through
        // NavMesh.SetLinkOwner(instance, this), so in practice it is always a Component. The
        // pattern match covers a link created some other way, which simply is not an elevator.
        NemesisElevatorLink elevator = data.owner is Component owner
            ? owner.GetComponent<NemesisElevatorLink>()
            : null;

        // IsUsable and not just != null: a misconfigured elevator is crossed like a plain link
        // instead of blowing up on a null reference.
        StartCoroutine(elevator != null && elevator.IsUsable
            ? TraverseElevator(elevator, data)
            : TraverseSimpleLink(data));
    }

    // ── Plain links ─────────────────────────────────────────────────────────

    /// <summary>
    /// Crossing a plain link: interpolate end to end at constant speed and report completion.
    /// This replaces the automatic traversal switched off in Awake.
    /// </summary>
    private IEnumerator TraverseSimpleLink(OffMeshLinkData data)
    {
        isTraversing = true;
        stateManager.PushStuckSuppression();

        try
        {
            Vector3 start = transform.position;
            Vector3 end = data.endPos + Vector3.up * agent.baseOffset;

            yield return MoveTransformTo(start, end, linkTraversalSpeed);

            if (agent.isActiveAndEnabled && agent.isOnOffMeshLink) agent.CompleteOffMeshLink();
        }
        finally
        {
            stateManager.PopStuckSuppression();
            isTraversing = false;
        }
    }

    // ── Freight elevator ────────────────────────────────────────────────────

    /// <summary>
    /// Calls the platform over, boards it, rides it and steps off on the other side.
    ///
    /// The agent is switched off for the trip. It is the only thing that works: the NavMesh does
    /// not travel with the platform, so mid-ride the Nemesis is off the mesh, and a live
    /// NavMeshAgent in that situation drags it back down to the lower floor on the next update. On
    /// arrival it is re-enabled with a Warp onto the upper landing, which is baked.
    /// </summary>
    private IEnumerator TraverseElevator(NemesisElevatorLink elevator, OffMeshLinkData data)
    {
        isTraversing = true;
        stateManager.PushStuckSuppression();

        MovingPlatform platform = elevator.Platform;
        Transform exit = elevator.GetExitLanding(transform.position);
        bool wantsToGoUp = elevator.IsAtBottomSide(transform.position);

        // The destination is saved before switching the agent off and restored afterwards:
        // disabling it clears its path, and without this the Nemesis arrives upstairs with no idea
        // where it was going.
        Vector3 savedDestination = agent.destination;
        bool hadPath = agent.hasPath;
        bool agentDisabled = false;

        try
        {
            // 1. Bring the platform to this floor if it is parked on the other one. GoingUp says
            //    which way the next trip would leave, and since this is a two-position shuttle
            //    that is the same as saying which side it is parked on right now.
            if (platform.GoingUp != wantsToGoUp)
            {
                yield return CallPlatform(platform);

                if (platform.GoingUp != wantsToGoUp) yield break;   // Timed out: abandon the link.
            }

            // 2. Wait for it to be free (the player may be using it right now).
            yield return WaitUntilIdle(platform);
            if (!platform.IsIdle) yield break;

            // 3. Board. The agent goes off first: with it alive, moving the Transform by hand does
            //    nothing because the agent snaps it back to its own internal position.
            agent.enabled = false;
            agentDisabled = true;

            yield return MoveTransformTo(transform.position, elevator.RidePosition, boardingSpeed);

            // 4. Ride. The platform moves loose passengers in its own FixedUpdate.
            platform.AddPassenger(transform);
            platform.RequestRide();

            yield return WaitUntilArrived(platform);

            platform.RemovePassenger(transform);
            platform.ReleaseAfterRide();

            // 5. Step off onto the opposite landing, which is on the NavMesh.
            yield return MoveTransformTo(transform.position, exit.position, boardingSpeed);
        }
        finally
        {
            platform.RemovePassenger(transform);

            if (agentDisabled)
            {
                agent.enabled = true;

                // Warp and not transform.position: the agent keeps its own internal position and
                // would drag it straight back to where it boarded.
                if (!agent.Warp(exit.position)) agent.Warp(transform.position);

                if (hadPath && agent.isOnNavMesh) agent.SetDestination(savedDestination);
            }

            stateManager.PopStuckSuppression();
            isTraversing = false;
        }
    }

    /// <summary>Requests an empty trip to bring the platform to this floor, and releases it on
    /// arrival so the next trip leaves in the right direction.</summary>
    private IEnumerator CallPlatform(MovingPlatform platform)
    {
        yield return WaitUntilIdle(platform);
        if (!platform.IsIdle) yield break;

        platform.RequestRide();

        yield return WaitUntilArrived(platform);
        platform.ReleaseAfterRide();
    }

    private IEnumerator WaitUntilIdle(MovingPlatform platform)
    {
        float waited = 0f;
        while (!platform.IsIdle && waited < platformWaitTimeout)
        {
            waited += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator WaitUntilArrived(MovingPlatform platform)
    {
        float waited = 0f;
        while (!platform.HasArrived && waited < platformWaitTimeout)
        {
            waited += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// Interpolates the Transform at constant speed. Used only for the short steps on and off the
    /// platform — the ride itself is driven by the platform, which moves its passengers directly.
    /// </summary>
    private IEnumerator MoveTransformTo(Vector3 from, Vector3 to, float speed)
    {
        float distance = Vector3.Distance(from, to);
        if (distance < 0.01f) yield break;

        float duration = distance / Mathf.Max(0.1f, speed);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        transform.position = to;
    }
}
