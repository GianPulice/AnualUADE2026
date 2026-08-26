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
    private const float FallbackAbandonCooldown = 10f;

    /// <summary>How far from a failed warp target to go looking for baked ground. Generous on
    /// purpose: it only ever runs when the alternative is a Nemesis frozen for the rest of the
    /// run, and half a shaft is not too far to fall back down.</summary>
    private const float AgentRecoveryRadius = 5f;

    private NemesisStateManager stateManager;
    private NavMeshAgent agent;
    private bool isTraversing;

    /// <summary>
    /// The elevator most recently given up on, and until when it stays off the menu.
    ///
    /// Abandoning a link is not the same as leaving it: the agent is still standing on it, so
    /// without this the very next Update sees isOnOffMeshLink and starts the whole wait again —
    /// twenty seconds at a time, forever, at the same landing. Nothing else catches it either,
    /// because a traversal deliberately suppresses the stuck watchdog.
    /// </summary>
    private NemesisElevatorLink abandonedElevator;
    private float abandonedUntil;

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

    /// <summary>Seconds an abandoned elevator stays off the menu.</summary>
    private float AbandonCooldown =>
        Data != null ? Data.ElevatorAbandonCooldown : FallbackAbandonCooldown;

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

        // Recently given up on: step off the link instead of starting the same doomed wait over.
        // Doing it here rather than inside the traversal keeps the cooldown from being paid as
        // another full timeout.
        if (elevator != null && IsOnCooldown(elevator))
        {
            LeaveCurrentLink();
            return;
        }

        // IsUsable and not just != null: a misconfigured elevator is crossed like a plain link
        // instead of blowing up on a null reference.
        if (elevator != null && elevator.IsUsable) TraverseElevatorAsync(elevator, token).Forget();
        else                                       TraverseSimpleLinkAsync(data, token).Forget();
    }

    private bool IsOnCooldown(NemesisElevatorLink elevator) =>
        ReferenceEquals(elevator, abandonedElevator) && Time.time < abandonedUntil;

    /// <summary>
    /// Takes the agent off the link it is standing on WITHOUT crossing it, and makes it re-path.
    ///
    /// <c>CompleteOffMeshLink()</c> is the wrong call here and was the tempting one: it reports the
    /// crossing as done and drops the agent at the far end — teleporting the Nemesis to the other
    /// floor for free, which is the bug the whole elevator system exists to avoid.
    /// <c>ActivateCurrentOffMeshLink(false)</c> deactivates this link for THIS agent, so the
    /// recalculated path routes around it (stairs, another shaft) instead of straight back onto it.
    /// </summary>
    private void LeaveCurrentLink()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        if (!agent.isOnOffMeshLink) return;

        agent.ActivateCurrentOffMeshLink(false);
        agent.ResetPath();

        // The cached verdict was measured believing the lift was on the way. Left standing, the
        // next route query hands back the same answer and the FSM commits to the trip again.
        if (stateManager != null) stateManager.InvalidateRouteVerdict();
    }

    /// <summary>Puts an elevator off the menu for <see cref="AbandonCooldown"/> seconds and steps
    /// the agent off its link.</summary>
    private void AbandonElevator(NemesisElevatorLink elevator)
    {
        abandonedElevator = elevator;
        abandonedUntil = Time.time + AbandonCooldown;

        LeaveCurrentLink();
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
    ///
    /// EVERY STEP THAT CAN FAIL IS CHECKED, and that is not defensiveness for its own sake. With
    /// the agent off, this method IS the Nemesis's physics: a step taken on a false assumption is
    /// not a slightly mispositioned monster, it is a monster gliding through the air across the
    /// shaft. The two ways that used to happen were a ride that never departed (RequestRide refused
    /// because the player had the cabin) and a ride that never arrived (the wait timed out) — both
    /// fell straight through to "step off at the far landing" and flew the Nemesis up the shaft
    /// under its own power, the elevator entirely uninvolved.
    /// </summary>
    private async UniTaskVoid TraverseElevatorAsync(NemesisElevatorLink elevator, CancellationToken token)
    {
        isTraversing = true;
        stateManager.PushStuckSuppression();

        MovingPlatform platform = elevator.Platform;
        Transform boarding = elevator.GetBoardingLanding(transform.position);
        Transform exit = elevator.GetExitLanding(transform.position);

        // The destination is saved before switching the agent off and restored afterwards:
        // disabling it clears its path, and without this the Nemesis arrives upstairs with no idea
        // where it was going.
        Vector3 savedDestination = agent.destination;
        bool hadPath = agent.hasPath;
        bool agentDisabled = false;
        bool completed = false;

        try
        {
            // 1. Reserve it for the whole attempt, from before the first wait. It is what stops a
            //    landing panel from calling the cabin away mid-approach, and what stops the
            //    platform's own auto-return from setting off under a monster still walking onto it.
            //
            //    The result is honoured rather than discarded: a claim that fails means somebody
            //    else is already driving this cabin, and carrying on regardless is how two callers
            //    end up issuing trips over each other.
            if (!platform.TryClaim(this)) return;

            // 2. Get the cabin to this floor, whatever it happens to be doing right now.
            if (!await BringCabinHereAsync(elevator, platform, token)) return;

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

            // Refused means the cabin stopped being idle between the check above and this line —
            // an auto-return setting off, or a panel press landing in the gap. There is no ride to
            // take, so put the body back on the landing it came from rather than let the finally
            // warp it off a cabin it is standing on.
            if (!platform.RequestRide())
            {
                await MoveTransformToAsync(transform.position, boarding.position, BoardingSpeed, token);
                return;
            }

            await WaitUntilTripEndsAsync(platform, token);

            // The trip did not finish inside the timeout. Stepping off HERE means stepping off at
            // whatever height the cabin happens to have reached, which is not a floor — this is the
            // exact fall-through that used to fly the Nemesis up the shaft. Leave it to the
            // finally, which puts the agent back on the landing this started from.
            if (!platform.HasArrived) return;

            platform.RemovePassenger(transform);
            platform.ReleaseAfterRide();

            // 5. Step off onto the opposite landing, which is on the NavMesh.
            await MoveTransformToAsync(transform.position, exit.position, BoardingSpeed, token);

            completed = true;
        }
        finally
        {
            if (platform != null)
            {
                platform.RemovePassenger(transform);
                platform.ReleaseClaim(this);
            }

            if (agentDisabled && agent != null)
            {
                agent.enabled = true;

                // The far landing when the trip worked, the one it boarded at when it did not: an
                // aborted attempt leaves the body on the cabin or halfway onto it, and the floor it
                // started from is the only place known to be both reachable and baked.
                RestoreAgentOnto(completed ? exit : boarding);

                if (hadPath && agent.isOnNavMesh) agent.SetDestination(savedDestination);
            }

            // Gave up — the platform never came, or the player is riding it. The agent is still
            // standing ON the link, and every one of the early returns above used to leave it
            // exactly there: the next Update saw isOnOffMeshLink, started the same twenty-second
            // wait, gave up again, and the Nemesis spent the rest of the run cycling at the
            // landing. Stepping off the link and shelving this shaft is what turns that into "it
            // walks away and takes the stairs".
            //
            // Unconditional rather than guarded on "did it board": LeaveCurrentLink no-ops when
            // the agent is not on a link, so the mid-ride cancellation case — where the warp above
            // has already taken it off — costs nothing and cannot be forgotten.
            if (!completed) AbandonElevator(elevator);

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

    /// <summary>
    /// Leaves the cabin parked at the Nemesis's own landing, or reports that it could not.
    /// </summary>
    /// <remarks>
    /// The order of the two questions is the reverse of what it used to be, and that IS the fix.
    /// The old code asked "is the cabin on my side?" first and waited for the platform to be free
    /// afterwards — so the answer came off a cabin that was still travelling, and by the time the
    /// wait ended the cabin had finished a trip to the OTHER floor with nobody re-checking. Then it
    /// boarded: agent off, Transform lerped to the ride point, which was now five metres up. That
    /// is the flying.
    ///
    /// Waiting first and asking after means the question is only ever put to a stationary cabin,
    /// and it is put to the cabin's actual position rather than to
    /// <see cref="MovingPlatform.GoingUp"/>, which describes the next trip and not the current
    /// parking spot — see <see cref="NemesisElevatorLink.IsCabinAtBottom"/>.
    /// </remarks>
    private async UniTask<bool> BringCabinHereAsync(NemesisElevatorLink elevator,
                                                    MovingPlatform platform,
                                                    CancellationToken token)
    {
        // Whatever it is busy with — the player's trip, an auto-return — has to end before
        // anything can be asked of it.
        await WaitUntilIdleAsync(platform, token);
        if (!platform.IsIdle) return false;

        if (elevator.IsCabinOnSameSideAs(transform.position)) return true;

        // Parked on the other floor: send an empty trip over.
        if (!platform.RequestRide()) return false;

        await WaitUntilTripEndsAsync(platform, token);
        if (platform.IsMoving) return false;

        // No-op when the trip released itself on arrival, which an empty one does. Kept for the
        // case where somebody was aboard after all — releasing is what flips the direction of the
        // next trip, and a platform left in WaitingForExit can never be called again.
        platform.ReleaseAfterRide();

        // Measured again rather than assumed: a trip that ends anywhere but here means a
        // misconfigured shaft, and boarding on the assumption is the flying bug all over again.
        return elevator.IsCabinOnSameSideAs(transform.position);
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

    /// <summary>
    /// Waits out a trip: from the moment it is requested until the platform is standing still
    /// again, however it got there.
    ///
    /// Written against <see cref="MovingPlatform.IsMoving"/> and not <c>HasArrived</c>, which is
    /// what it used to be and is a trap for an EMPTY trip. HasArrived means "parked at the far end,
    /// waiting to be vacated", and a trip with nobody aboard releases itself in the same
    /// FixedUpdate that ends it — so an Update-timed watcher never sees that state at all and sits
    /// out the full timeout. That is where the twenty motionless seconds at a landing came from:
    /// not a deadlock, a watcher looking for a flag that had already been cleared. Calling the
    /// cabin over paid it EVERY time, which is most of what "it gets stuck and stops moving" was.
    /// </summary>
    private async UniTask WaitUntilTripEndsAsync(MovingPlatform platform, CancellationToken token)
    {
        float waited = 0f;
        while (platform.IsMoving && waited < PlatformWaitTimeout)
        {
            waited += Time.deltaTime;
            await UniTask.Yield(token);
        }
    }

    /// <summary>
    /// Puts the agent back on the NavMesh after a traversal, and does not take no for an answer.
    ///
    /// Warp and not <c>transform.position</c>: the agent keeps its own internal position and would
    /// drag the body straight back to where it boarded. But Warp FAILS when the point it is handed
    /// has no NavMesh under it, and the old code treated that failure as the end of the matter — a
    /// traversal that gave up mid-shaft left the agent enabled, off the mesh, and permanently
    /// unable to path. Everything downstream gates on
    /// <see cref="NemesisStateManager.IsAgentReady"/>, which reads <c>isOnNavMesh</c>, so the
    /// Nemesis stopped moving for the rest of the run with nothing logged. That is the difference
    /// between a lift trip that fails and a monster that dies standing up.
    /// </summary>
    private void RestoreAgentOnto(Transform landing)
    {
        if (landing != null && TryWarpNear(landing.position)) return;
        if (TryWarpNear(transform.position)) return;

        Debug.LogError($"[{nameof(NemesisElevatorUser)}] '{name}' came off an elevator traversal " +
                       $"with no NavMesh within {AgentRecoveryRadius}m of the landing or of " +
                       $"{transform.position}. The agent is off the mesh, which means the Nemesis " +
                       "will not move again — check that both landings of this shaft are baked.",
                       this);
    }

    /// <summary>Warps onto a point, or onto the nearest baked spot to it.</summary>
    private bool TryWarpNear(Vector3 target)
    {
        if (agent.Warp(target) && agent.isOnNavMesh) return true;

        return NavMesh.SamplePosition(target, out NavMeshHit hit, AgentRecoveryRadius, NavMesh.AllAreas)
               && agent.Warp(hit.position)
               && agent.isOnNavMesh;
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
