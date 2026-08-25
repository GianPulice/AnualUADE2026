using System.Threading;
using Cysharp.Threading.Tasks;
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
/// Written on UniTask rather than coroutines, per the project's async convention, and here that is
/// also the only safe option. A coroutine dies the moment its MonoBehaviour is disabled and Unity
/// does not dispose the iterator, so the cleanup in the finally blocks below would simply never
/// run — and this traversal switches the NavMeshAgent OFF while it rides. A capture, a scene
/// transition or a dormant toggle landing mid-ride would leave the agent disabled permanently and
/// the Nemesis dead for the rest of the run. UniTask runs on the PlayerLoop, so the continuation
/// still arrives and the agent always comes back.
///
/// SETUP: goes on the Nemesis root, next to the NemesisStateManager. It needs no references: it
/// finds the elevator through the link the agent stepped onto.
/// </summary>
[RequireComponent(typeof(NemesisStateManager))]
public class NemesisElevatorUser : MonoBehaviour
{
    // Nothing is serialised on this component. The three speeds live in SO_NemesisMovement next to
    // the ones the NavMeshAgent uses, and the wait timeout in SO_NemesisData with the rest of the
    // behaviour tuning — a designer adjusting how the monster moves should not have to know which
    // of the two systems happens to be driving it at the time.
    //
    // The fallbacks are only reached when an SO is missing, which ValidateReferences already
    // reports as an error. They exist so a broken prefab still crosses its links instead of
    // freezing halfway across one on a speed of 0.
    private const float FallbackLinkTraversalSpeed = 2.5f;
    private const float FallbackBoardingSpeed = 1.5f;
    private const float FallbackTurnSpeed = 180f;
    private const float FallbackWaitTimeout = 20f;

    private NemesisStateManager stateManager;
    private NavMeshAgent agent;
    private bool isTraversing;

    private SO_NemesisMovement Movement => stateManager != null ? stateManager.NemesisMovement : null;
    private SO_NemesisData Data => stateManager != null ? stateManager.NemesisData : null;

    /// <summary>Metres per second crossing a plain link — a jump or a drop.</summary>
    private float LinkTraversalSpeed =>
        Movement != null ? Movement.LinkTraversalSpeed : FallbackLinkTraversalSpeed;

    /// <summary>Metres per second stepping onto and off the platform.</summary>
    private float BoardingSpeed =>
        Movement != null ? Movement.BoardingSpeed : FallbackBoardingSpeed;

    /// <summary>Degrees per second it turns while this component is moving it by hand.</summary>
    private float TraversalTurnSpeed =>
        Movement != null ? Movement.TraversalTurnSpeed : FallbackTurnSpeed;

    /// <summary>Maximum seconds it waits for the platform to free up or finish a trip.</summary>
    private float PlatformWaitTimeout =>
        Data != null ? Data.ElevatorWaitTimeout : FallbackWaitTimeout;

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

        CancellationToken token = this.GetCancellationTokenOnDestroy();

        // IsUsable and not just != null: a misconfigured elevator is crossed like a plain link
        // instead of blowing up on a null reference.
        if (elevator != null && elevator.IsUsable) TraverseElevatorAsync(elevator, token).Forget();
        else                                       TraverseSimpleLinkAsync(data, token).Forget();
    }

    // ── Plain links ─────────────────────────────────────────────────────────

    /// <summary>
    /// Crossing a plain link: interpolate end to end at constant speed and report completion.
    /// This replaces the automatic traversal switched off in Awake.
    /// </summary>
    private async UniTaskVoid TraverseSimpleLinkAsync(OffMeshLinkData data, CancellationToken token)
    {
        isTraversing = true;
        stateManager.PushStuckSuppression();

        try
        {
            Vector3 start = transform.position;
            Vector3 end = data.endPos + Vector3.up * agent.baseOffset;

            await MoveTransformToAsync(start, end, LinkTraversalSpeed, token);

            if (agent.isActiveAndEnabled && agent.isOnOffMeshLink) agent.CompleteOffMeshLink();
        }
        finally
        {
            if (stateManager != null) stateManager.PopStuckSuppression();
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
    ///
    /// That switch-off is exactly why the finally block has to be guaranteed to run, and why this
    /// is a UniTask and not a coroutine — see the class doc.
    /// </summary>
    private async UniTaskVoid TraverseElevatorAsync(NemesisElevatorLink elevator, CancellationToken token)
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
                await CallPlatformAsync(platform, token);

                if (platform.GoingUp != wantsToGoUp) return;   // Timed out: abandon the link.
            }

            // 2. Wait for it to be free (the player may be using it right now).
            await WaitUntilIdleAsync(platform, token);
            if (!platform.IsIdle) return;

            // 3. Board. The agent goes off first: with it alive, moving the Transform by hand does
            //    nothing because the agent snaps it back to its own internal position.
            agent.enabled = false;
            agentDisabled = true;

            await MoveTransformToAsync(transform.position, elevator.RidePosition, BoardingSpeed, token);

            // 3b. Turn to face the landing it will step off at, before the doors close on it.
            //     The ride is purely vertical, so nothing during it has a heading to offer, and
            //     the platform carries passengers by position only — it never touches rotation.
            //     Skipped, the Nemesis rides the whole shaft facing back the way it came in and
            //     steps out backwards.
            await TurnToFaceAsync(exit.position, token);

            // 4. Ride. The platform moves loose passengers in its own FixedUpdate.
            platform.AddPassenger(transform);
            platform.RequestRide();

            await WaitUntilArrivedAsync(platform, token);

            platform.RemovePassenger(transform);
            platform.ReleaseAfterRide();

            // 5. Step off onto the opposite landing, which is on the NavMesh.
            await MoveTransformToAsync(transform.position, exit.position, BoardingSpeed, token);
        }
        finally
        {
            if (platform != null) platform.RemovePassenger(transform);

            if (agentDisabled && agent != null)
            {
                agent.enabled = true;

                // Warp and not transform.position: the agent keeps its own internal position and
                // would drag it straight back to where it boarded.
                if (exit != null && !agent.Warp(exit.position)) agent.Warp(transform.position);

                if (hadPath && agent.isOnNavMesh) agent.SetDestination(savedDestination);
            }

            if (stateManager != null)
            {
                // The Nemesis is now on a different floor than when the cached route was
                // measured, and that cached answer is what NemesisTraversingState reads to decide
                // the trip is over. Left standing it still says "the lift is on the way" — the
                // lift that was just ridden — and the trip does not end until the cache expires
                // on its own.
                stateManager.InvalidateRouteVerdict();
                stateManager.PopStuckSuppression();
            }
            isTraversing = false;
        }
    }

    /// <summary>Requests an empty trip to bring the platform to this floor, and releases it on
    /// arrival so the next trip leaves in the right direction.</summary>
    private async UniTask CallPlatformAsync(MovingPlatform platform, CancellationToken token)
    {
        await WaitUntilIdleAsync(platform, token);
        if (!platform.IsIdle) return;

        platform.RequestRide();

        await WaitUntilArrivedAsync(platform, token);
        platform.ReleaseAfterRide();
    }

    private async UniTask WaitUntilIdleAsync(MovingPlatform platform, CancellationToken token)
    {
        float waited = 0f;
        while (!platform.IsIdle && waited < PlatformWaitTimeout)
        {
            waited += Time.deltaTime;
            await UniTask.Yield(token);
        }
    }

    private async UniTask WaitUntilArrivedAsync(MovingPlatform platform, CancellationToken token)
    {
        float waited = 0f;
        while (!platform.HasArrived && waited < PlatformWaitTimeout)
        {
            waited += Time.deltaTime;
            await UniTask.Yield(token);
        }
    }

    /// <summary>
    /// Interpolates the Transform at constant speed. Used only for the short steps on and off the
    /// platform — the ride itself is driven by the platform, which moves its passengers directly.
    /// </summary>
    private async UniTask MoveTransformToAsync(Vector3 from, Vector3 to, float speed, CancellationToken token)
    {
        float distance = Vector3.Distance(from, to);
        if (distance < 0.01f) return;

        // Turned into the direction of travel as it goes, rather than sliding there rigid. The
        // agent is off for the whole traversal, so this is the only thing writing rotation.
        Quaternion targetRotation = FlatLookRotation(to - from);

        float duration = distance / Mathf.Max(0.1f, speed);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation,
                                                          TraversalTurnSpeed * Time.deltaTime);
            await UniTask.Yield(token);
        }

        transform.position = to;
    }

    /// <summary>
    /// Turns on the spot to face a point, and does not return until it is facing it.
    ///
    /// Used before the ride itself, which is the one leg with no direction of travel to borrow: it
    /// is purely vertical, so <see cref="MoveTransformToAsync"/> has nothing to turn towards and
    /// the platform moves the Nemesis without touching its rotation at all.
    /// </summary>
    private async UniTask TurnToFaceAsync(Vector3 point, CancellationToken token)
    {
        Quaternion target = FlatLookRotation(point - transform.position);

        while (Quaternion.Angle(transform.rotation, target) > 1f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target,
                                                          TraversalTurnSpeed * Time.deltaTime);
            await UniTask.Yield(token);
        }

        transform.rotation = target;
    }

    /// <summary>
    /// Look rotation with the vertical component discarded, so a target above or below does not
    /// tip the Nemesis onto its face.
    ///
    /// Falls back to the current rotation for a direction that is purely vertical — a landing
    /// stacked exactly over the ride point — because Quaternion.LookRotation of a zero vector is
    /// undefined and Unity warns about it.
    /// </summary>
    private Quaternion FlatLookRotation(Vector3 direction)
    {
        direction.y = 0f;

        return direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction)
            : transform.rotation;
    }
}
