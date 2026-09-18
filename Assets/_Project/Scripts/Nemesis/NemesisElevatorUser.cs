using System.Collections.Generic;
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

    /// <summary>
    /// Stopping distance while walking on or off the cabin.
    ///
    /// The agent's own is the pursuit value — 1.5 m in this project — and a Nemesis that stops
    /// 1.5 m short of the boarding point stops on the landing, not in the lift. The cabin floor is
    /// 4.5 m across; the margin has to be a fraction of that, not a third of it.
    /// </summary>
    private const float BoardingStoppingDistance = 0.3f;

    /// <summary>Seconds allowed for the walk on or off the cabin before the attempt is written
    /// off. Far shorter than the cabin wait: this leg is a couple of metres over open floor, so
    /// anything longer is not slowness, it is a path that never resolved.</summary>
    private const float BoardingWalkTimeout = 12f;

    /// <summary>Seconds to wait for the cabin's floor and a landing's link to come back after a
    /// trip. A frame or two in practice — this is a grace against update order, not a wait.
    /// </summary>
    private const float BoardingOpenTimeout = 1f;

    /// <summary>Seconds allowed for the agent to actually stop standing on the shaft link after
    /// being asked to. Half a second is dozens of frames — long enough that any ordering between
    /// this continuation and the navigation system's own update resolves, short enough that a
    /// genuine refusal is reported rather than waited out.</summary>
    private const float OffLinkTimeout = 0.5f;

    private NemesisStateManager stateManager;
    private NavMeshAgent agent;
    private bool isTraversing;

    /// <summary>
    /// One per crossing, linked to the destroy token. Exists so a capture can end the crossing it
    /// interrupts: with only the destroy token, a Nemesis that grabbed the player while waiting for
    /// the cabin kept its claim on the platform for the rest of that wait (up to twenty seconds),
    /// and the panels read "Forklift in use" across the whole respawn.
    /// </summary>
    private CancellationTokenSource crossingCts;

    /// <summary>The crossing was cut short by a capture or a respawn, not given up on. Keeps the
    /// shaft out of the abandon cooldown: the lift did nothing wrong.</summary>
    private bool crossingInterrupted;

    /// <summary>The body is aboard a cabin that has been asked to move. A capture in this window is
    /// NOT allowed to cancel the crossing — stepping off mid-shaft is a fall, and the grab would be
    /// torn out of its animation — so it is left to the respawn to end it.</summary>
    private bool isRiding;

    /// <summary>
    /// A lift crossing is in flight: waiting for the cabin, boarding, riding, or stepping off.
    ///
    /// Public because the decision ladder needs it. While this is true the Nemesis's body is being
    /// driven by hand and the FSM must say Traversing whatever the route verdict happens to think
    /// this frame - see the "esta cruzando el montacargas" rung. It spans the whole attempt,
    /// including the cabin wait, which is the part where the agent is still enabled and therefore
    /// the part where the rest of the system can least tell that something else is in charge.
    /// </summary>
    public bool IsTraversing => isTraversing;

    /// <summary>
    /// A shaft has just been given up on and is shelved for its cooldown.
    ///
    /// Read by the priority ladder, through <c>NemesisStateManager.HasGivenUpOnElevator</c>, to
    /// release the Traversing commitment: the two time bounds on that rung measure how long the
    /// commitment has been held, and cannot tell that there is no longer a trip to hold it to.
    /// </summary>
    public bool HasGivenUpOnElevator => abandonedElevator != null && Time.time < abandonedUntil;

    /// <summary>
    /// The elevator most recently given up on, and until when it stays off the menu.
    ///
    /// Abandoning a link is not the same as leaving it: the agent is still standing on it, so
    /// without this the very next Update sees isOnOffMeshLink and starts the whole wait again —
    /// twenty seconds at a time, forever, at the same landing. Nothing else catches it either,
    /// because a traversal deliberately suppresses the stuck watchdog.
    ///
    /// While it is set, that shaft's link is SUSPENDED — see <see cref="AbandonElevator"/> for why
    /// stepping off without suspending it produced the walk-in/walk-out loop, and
    /// <see cref="RestoreAbandonedElevator"/> for the one place that puts it back.
    /// </summary>
    private NemesisElevatorLink abandonedElevator;
    private float abandonedUntil;

    /// <summary>
    /// The last boarding-walk failure already reported, so the same one is not printed every
    /// twenty seconds for the rest of the session.
    ///
    /// Keyed on REASON AND LANDING, not once per run, which is what it used to be. "It failed" was
    /// never the useful half of that warning — the failure has six possible causes in three
    /// different inspectors — and the landing belongs in the key for the same reason: the case
    /// worth seeing is a shaft that fails one way from the bottom and another way from the top,
    /// and keying on the reason alone would print the first and swallow the second.
    /// </summary>
    private readonly HashSet<(EBoardingStep step, Transform landing)> warnedBoardingWalk =
        new HashSet<(EBoardingStep, Transform)>();

    /// <summary>
    /// Which step of the walk aboard gave up.
    ///
    /// A field rather than a return value because the walk is spread over three async methods and
    /// UniTask has no <c>out</c>; it is written by whichever step fails and read once, by
    /// <see cref="WarnBoardingWalkFailed"/>, in the same call chain.
    /// </summary>
    private EBoardingStep boardingFailure;

    /// <summary>The ways boarding on foot can fail, each naming what to go and look at.</summary>
    private enum EBoardingStep
    {
        None,

        /// <summary>The shaft reports a cabin NavMesh but the component is gone.</summary>
        NoCabinNavMesh,

        /// <summary>The cabin floor and this landing's link were never live at the same time
        /// inside the grace window — the cabin is not parked here, or its bake is empty.</summary>
        BoardingNeverOpened,

        /// <summary>The agent came off the NavMesh entirely while stepping off the shaft link.
        /// </summary>
        AgentLeftTheNavMesh,

        /// <summary>It would not let go of the shaft link, so it could not be steered anywhere.
        /// </summary>
        StuckOnTheShaftLink,

        /// <summary>NavMeshAgent.SetDestination refused the boarding point outright: nothing
        /// walkable near it for this agent type.</summary>
        DestinationRefused,

        /// <summary>A path was found but it does not reach the cabin — the boarding link is not
        /// joining the two meshes. THE usual one, and it means the link's ends are on the same
        /// island or on none.</summary>
        PathDoesNotReachTheCabin,

        /// <summary>A complete path that never finished being walked. Something is physically in
        /// the way, or the cabin left while it was walking.</summary>
        WalkTimedOut,
    }

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

        // Holding the player. A crossing cancelled by the capture leaves the agent standing on the
        // shaft link, and without this the next frame would start the same crossing over, claim
        // the platform again, and undo the cancellation.
        if (IsCapturing) return;
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        // Before the agent guards, not after: a shaft whose cooldown has expired has to come back
        // into pathfinding whether or not the Nemesis happens to be on the mesh this frame.
        RestoreAbandonedElevator();

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

        // Safe to dispose: this line is only reached with no crossing in flight (isTraversing).
        crossingCts?.Dispose();
        crossingCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        crossingInterrupted = false;
        CancellationToken token = crossingCts.Token;

        // Recently given up on: step off the link instead of starting the same doomed wait over.
        //
        // A safety net now rather than the mechanism. AbandonElevator suspends the shaft link for
        // the whole cooldown, so an agent should not be able to path onto it at all; this catches
        // the frame where it was already standing on it when the suspension landed.
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

    /// <summary>
    /// Freezes the agent where it stands and drops it to the idle gait, or hands it back.
    ///
    /// <c>isStopped</c> and not <c>ResetPath</c>: the path is what the Nemesis goes back to
    /// following if the lift never comes, and throwing it away here would make every abandoned
    /// attempt also a lost destination.
    ///
    /// The gait is set through the state manager rather than on the Animator, because gait and
    /// speed are one decision there - which is the whole reason SetGait exists, and why "standing
    /// still playing a run" is a combination this component cannot accidentally produce once it
    /// goes through it.
    ///
    /// Releasing restores WALKING, not the running gait it used to, and not whatever was there
    /// before. Everything this component does with the body afterwards is a short careful move —
    /// stepping through a doorway onto a cabin, stepping off it — and a sprint animation over a
    /// 1.5 m/s boarding speed is the same mismatch in the other direction. Reading the previous
    /// value back would mean remembering it across an await that can be cancelled halfway; the
    /// state the Nemesis returns to sets its own gait on the way in.
    /// </summary>
    private void HoldStill(bool hold)
    {
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = hold;

            // Zeroed as well as stopped. isStopped halts the steering but leaves whatever velocity
            // the agent had built up, and a body that keeps drifting for a few tenths of a second
            // after being told to wait is exactly how it ends up inside the shaft wall.
            if (hold) agent.velocity = Vector3.zero;
        }

        // Not over a capture: Catch set the grab gait on entry and owns it. A crossing cancelled by
        // that capture runs this from its finally, a frame later, and would put a walk on top of
        // the grab.
        if (stateManager == null || IsCapturing) return;

        if (hold) stateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
        else      stateManager.SetGait(NemesisStateManager.EGait.Walking, BoardingSpeed);
    }

    /// <summary>
    /// Whether to give up on the lift because the player is right here.
    ///
    /// This is the answer to "I got on the elevator with it and it ignored me". A crossing owns the
    /// body for as long as it lasts — up to twenty seconds of that is just waiting for the cabin —
    /// and the priority ladder honours that with an interrupt, so for the whole wait nothing the
    /// senses report can change the Nemesis's mind. Correct while it is riding, absurd while it is
    /// standing at a landing with the player in front of it.
    ///
    /// Seeing them is deliberately not enough on its own. A player one storey up, visible through
    /// the shaft opening, is the exact case the lift exists for, and abandoning the trip there
    /// would mean the Nemesis can never follow anyone upstairs. So the question is "can I get to
    /// them WITHOUT the lift", and it is asked through the same throttled verdict the ladder reads
    /// — <see cref="NemesisDecision.RouteToBeliefCrossesFloors"/> — so the two cannot give
    /// different answers on the same frame and start trading the Nemesis back and forth.
    ///
    /// Only ever consulted BEFORE boarding. Once the body is on the cabin there is nothing to
    /// abandon into: stepping off mid-shaft is a fall, and the grab rung sits above the crossing in
    /// the ladder anyway, so a player who rides up with it can still be caught.
    /// </summary>
    private bool ShouldAbandonForPlayer()
    {
        if (stateManager == null || !stateManager.HasVisualTarget) return false;

        NemesisDecision decision = stateManager.Decision;
        return decision != null && !decision.RouteToBeliefCrossesFloors;
    }

    private bool IsOnCooldown(NemesisElevatorLink elevator) =>
        ReferenceEquals(elevator, abandonedElevator) && Time.time < abandonedUntil;

    /// <summary>
    /// The FSM is holding the player right now.
    ///
    /// It can happen in the middle of a crossing since the capture rung stopped being frozen out
    /// by the ride (see <c>NemesisStateManager.CanCaptureWithoutAgent</c>), and the two things this
    /// component does on its way out — reset the gait, put the agent back on a landing — are both
    /// wrong over a grab in progress.
    /// </summary>
    private bool IsCapturing =>
        stateManager != null &&
        stateManager.CurrentStateKey == NemesisStateManager.ENemesisState.Catch;

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

    /// <summary>
    /// Puts an elevator off the menu for <see cref="AbandonCooldown"/> seconds and steps the agent
    /// off its link.
    ///
    /// THE SHAFT LINK GOES OFF WITH IT, and that is the half that was missing. Stepping off is not
    /// the same as staying off: <see cref="LeaveCurrentLink"/> only refuses THIS crossing, so the
    /// very next path the FSM asked for went straight back through the same link, the agent stood
    /// on it again, this method's cooldown branch in Update stepped it off again — and the two
    /// took turns, every frame, for the whole cooldown. From the outside that is a Nemesis walking
    /// into the lift, out of it, and back in.
    ///
    /// Worse, it was permanent in the other direction. ActivateCurrentOffMeshLink(false) turns the
    /// link off in the navigation system and nothing ever turned it back on — the cooldown expired
    /// but the crossing stayed dead, so the shaft quietly stopped existing for the rest of the run.
    /// Suspending it here as a deliberate, timed decision is what makes
    /// <see cref="RestoreAbandonedElevator"/> the one place that undoes it.
    ///
    /// Suspending it also fixes the decision layer's half of the loop: with the link out of
    /// pathfinding the route to the belief stops crossing floors, so the ladder falls through to
    /// Chasing instead of pinning the Nemesis in Traversing on the strength of a lift it has just
    /// given up on.
    /// </summary>
    private void AbandonElevator(NemesisElevatorLink elevator)
    {
        // One shelf, one shaft. A level with two lifts could otherwise overwrite the reference to
        // the first while it is still suspended, and nothing would ever hand it back.
        if (abandonedElevator != null && !ReferenceEquals(abandonedElevator, elevator))
            abandonedElevator.SetShaftLinkActive(true);

        abandonedElevator = elevator;
        abandonedUntil = Time.time + AbandonCooldown;

        // In this order: deactivating first would leave LeaveCurrentLink with no current link to
        // act on, and the agent standing on a crossing it is no longer allowed to take.
        LeaveCurrentLink();
        elevator.SetShaftLinkActive(false);
    }

    /// <summary>
    /// Puts a shelved shaft back into pathfinding once its cooldown has run out.
    ///
    /// The counterpart to <see cref="AbandonElevator"/>, and the reason the cooldown is a real
    /// cooldown rather than a one-way door. Cleared as soon as it fires, so a shaft is restored
    /// exactly once and <see cref="IsOnCooldown"/> keeps answering false afterwards.
    ///
    /// A destroyed elevator (a level torn down mid-cooldown) drops out of the field the same way,
    /// so the reference cannot keep a dead object alive.
    /// </summary>
    private void RestoreAbandonedElevator()
    {
        if (abandonedElevator == null)
        {
            abandonedElevator = null;   // Clears a Unity-null left by a destroyed elevator.
            return;
        }

        if (Time.time < abandonedUntil) return;

        abandonedElevator.SetShaftLinkActive(true);
        abandonedElevator = null;
    }

    /// <summary>
    /// A Nemesis switched off (dormant, captured, scene teardown) mid-cooldown would leave its
    /// shaft suspended with nothing left running to restore it. The link belongs to the level, not
    /// to this component, so it has to be handed back on the way out.
    /// </summary>
    private void OnEnable()
    {
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
        CheckpointManager.OnRespawned += HandlePlayerRespawned;
    }

    /// <summary>
    /// Ends a crossing that is still only waiting for, walking onto or stepping off the cabin. The
    /// finally in <see cref="TraverseElevatorAsync"/> then releases the claim and the passenger
    /// registration on the next frame, instead of whenever the wait happened to time out.
    /// </summary>
    private void HandlePlayerCaptured(PlayerStateManager player)
    {
        if (!isRiding) CancelCrossing();
    }

    /// <summary>
    /// The player is back at a checkpoint: whatever crossing survived the capture (the one ridden
    /// with the player aboard) has nothing left to do. The respawn is behind the capture fade, so
    /// putting the body back on a landing here is not seen.
    /// </summary>
    private void HandlePlayerRespawned(Checkpoint checkpoint) => CancelCrossing();

    private void CancelCrossing()
    {
        if (!isTraversing || crossingCts == null || crossingCts.IsCancellationRequested) return;

        crossingInterrupted = true;
        crossingCts.Cancel();
    }

    private void OnDisable()
    {
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        CheckpointManager.OnRespawned -= HandlePlayerRespawned;

        if (abandonedElevator == null) return;

        abandonedElevator.SetShaftLinkActive(true);
        abandonedElevator = null;
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

        // Decided ONCE, here, and not re-asked between the two legs. Boarding on foot and stepping
        // off by hand (or the reverse) would leave the body in a place the other half of the
        // traversal does not expect — on the cabin when the exit expects it at the ride point, or
        // at the ride point when the exit expects to walk. One answer for the whole trip.
        bool boardByWalking = elevator.CabinNav != null && elevator.CabinNav.IsReady;

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

            // 1b. STAND STILL AND LOOK LIKE IT.
            //
            // The wait below can last twenty seconds with the agent still ENABLED, and until now
            // nothing told it to stop. Two things went wrong for the whole of that window, both
            // from the same cause.
            //
            // The animation: NemesisTraversingState sets the running gait on entry and never
            // touches it again, so the Nemesis stood at the landing sprinting on the spot.
            //
            // The wall: the agent was standing on the off-mesh link with a destination on the far
            // side of the shaft, and every frame NemesisTraversingState re-issued it. An agent
            // asked to keep going while it sits on a link it is not allowed to auto-traverse
            // grinds along the link direction - which points straight through the shaft wall,
            // because that is what the link is for. It is not that the Nemesis ignores the wall;
            // it is that nobody had told it to stop walking at it.
            //
            // Stopping the agent is the same move NemesisDoorUser already makes while it sweeps a
            // door open, and for the same reason.
            HoldStill(true);

            // 2. Get the cabin to this floor, whatever it happens to be doing right now.
            if (!await BringCabinHereAsync(elevator, platform, token)) return;

            // The hold is released along with the body, whichever way it is about to be moved.
            HoldStill(false);

            // 3. Board — by walking when the cabin carries a NavMesh, by hand when it does not.
            //
            //    THE WALK IS THE FIX. The hand-driven version below interpolates the body in a
            //    straight line from the landing to the ride point, and that line runs through the
            //    ElevatorLandingBarrier and the shaft wall. Nothing was ignoring the wall; a lerp
            //    has no opinion about geometry. With a NavMesh on the cabin the same trip is an
            //    ordinary path: around the wall, in through the doorway, with the animation
            //    following the body because the body is really walking.
            //
            //    Kept as a fallback rather than replaced outright: ElevatorCabinNavMesh reports
            //    IsReady false when its bake produced nothing, and a lift that crosses through the
            //    wall still crosses. Losing the ride entirely would be the worse failure.
            if (boardByWalking && !await WalkAboardAsync(elevator, boarding, token))
            {
                // The cabin has a NavMesh but the walk did not get there — a link that connects
                // to nothing, a path that never resolved. Reported once, then crossed the old way:
                // the wall-crossing boarding is ugly, but a Nemesis that cannot change floors at
                // all is a level the player can walk away from.
                WarnBoardingWalkFailed(elevator, boarding);
                boardByWalking = false;
            }

            // Off only now in the walking case: the agent has to be alive to do the walking, and
            // switching it off any earlier is what made the whole approach hand-driven to begin
            // with. In the hand-driven case it has to go off FIRST — with it alive, moving the
            // Transform does nothing, because the agent snaps the body back to its own internal
            // position.
            agent.enabled = false;
            agentDisabled = true;

            if (!boardByWalking)
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
                if (boardByWalking)
                {
                    // Walked in, so walk back out — same leg as the arrival, aimed at the landing
                    // it came from. Falling back to the finally's warp would teleport it two metres
                    // for a race that resolves cleanly on foot.
                    agent.enabled = true;
                    agentDisabled = false;

                    if (!TryWarpNear(transform.position) ||
                        !await WalkAshoreAsync(elevator, boarding, token))
                    {
                        RestoreAgentOnto(boarding);
                    }
                }
                else
                {
                    await MoveTransformToAsync(transform.position, boarding.position, BoardingSpeed, token);
                }

                return;
            }

            isRiding = true;
            await WaitUntilTripEndsAsync(platform, token);
            isRiding = false;

            // The trip did not finish inside the timeout. Stepping off HERE means stepping off at
            // whatever height the cabin happens to have reached, which is not a floor — this is the
            // exact fall-through that used to fly the Nemesis up the shaft. Leave it to the
            // finally, which puts the agent back on the landing this started from.
            if (!platform.HasArrived) return;

            platform.RemovePassenger(transform);
            platform.ReleaseAfterRide();

            // 5. Step off onto the opposite landing.
            if (boardByWalking)
            {
                // The cabin's own NavMesh and this landing's link come back when the platform
                // stops moving — but they are restored from ElevatorCabinNavMesh's own Update,
                // which has not necessarily run yet on the frame this continuation resumes. Warping
                // before it does would sample the landing floor instead of the cabin under the
                // body, or nothing at all.
                bool ashore = await WaitForBoardingOpenAsync(elevator, exit, token);

                // Back ON THE CABIN rather than at the landing: warping straight across would be
                // the teleport this whole component exists to avoid, only dressed as an arrival.
                // The last two metres of the trip are a walk out through the doorway like any
                // other.
                agent.enabled = true;
                agentDisabled = false;

                if (!ashore ||
                    !TryWarpNear(transform.position) ||
                    !await WalkAshoreAsync(elevator, exit, token))
                {
                    // The ride itself worked; only the last two metres did not. Finishing with the
                    // old warp is worth far more than shelving a shaft that just carried the
                    // Nemesis a whole storey.
                    RestoreAgentOnto(exit);
                }
            }
            else
            {
                await MoveTransformToAsync(transform.position, exit.position, BoardingSpeed, token);
            }

            completed = true;
        }
        finally
        {
            isRiding = false;

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
            }

            // The shaft link goes back into pathfinding whatever happened. Suspending it is how the
            // walk aboard gets the body off it; leaving it suspended would mean this lift stops
            // existing for every future route query, and the Nemesis quietly loses the ability to
            // change floors for the rest of the run. Unconditional, and outside the branch above,
            // because every early return in this method passes through here.
            elevator.SetShaftLinkActive(true);

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
            //
            // Except when a capture or a respawn cut it short: the lift did nothing wrong, and a
            // shelved shaft would keep the Nemesis off it for the whole cooldown after the
            // respawn. The link is left alone too — Update does not restart a crossing while
            // Catch holds the player, and RepositionAfterCapture's warp takes the agent off it.
            if (!completed && !crossingInterrupted) AbandonElevator(elevator);

            // Restored only on a trip that worked, and only after the abandon above — which resets
            // the path on purpose. Disabling an agent clears its destination, so without this a
            // Nemesis that rode up arrives with no idea where it was going and stands at the
            // landing until the FSM happens to issue another one.
            if (completed && hadPath && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                agent.SetDestination(savedDestination);

            // Whatever happened - rode it, gave up, got cancelled - the agent must not be left
            // frozen. An early return between HoldStill(true) and HoldStill(false) would otherwise
            // strand a stopped agent, which reads exactly like the stuck Nemesis this whole
            // component exists to avoid, except the watchdog is suppressed and cannot rescue it.
            HoldStill(false);

            // And handed back at the speed the FSM expects, not this component's. Boarding pace is
            // internal to a crossing: every state the ladder can fall through to from here runs,
            // and a Nemesis that resumed a chase at 1.5 m/s would look like it had lost interest.
            // The animation no longer depends on this being right - TickLocomotionAnimation reads
            // the body - but the agent's speed still does.
            //
            // NOT OVER A CAPTURE. A player who rode up in the cabin can now be grabbed mid-trip
            // (see NemesisStateManager.CanCaptureWithoutAgent), and this line runs after it: the
            // grab animation would be replaced by a run on the spot, with the Nemesis holding
            // somebody. Catch sets its own gait on entry and owns it until it leaves.
            SO_NemesisMovement movement = Movement;
            if (stateManager != null && movement != null && !IsCapturing)
                stateManager.SetGait(NemesisStateManager.EGait.Running, movement.ChaseSpeed);

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

        // Both waits above and below can be cut short by the player turning up on this floor, so
        // the question is re-asked here rather than trusted from before them.
        if (ShouldAbandonForPlayer()) return false;

        if (elevator.IsCabinOnSameSideAs(transform.position)) return true;

        // Parked on the other floor: send an empty trip over.
        if (!platform.RequestRide()) return false;

        await WaitUntilTripEndsAsync(platform, token, abandonIfPlayerReachable: true);
        if (platform.IsMoving) return false;
        if (ShouldAbandonForPlayer()) return false;

        // No-op when the trip released itself on arrival, which an empty one does. Kept for the
        // case where somebody was aboard after all — releasing is what flips the direction of the
        // next trip, and a platform left in WaitingForExit can never be called again.
        platform.ReleaseAfterRide();

        // Measured again rather than assumed: a trip that ends anywhere but here means a
        // misconfigured shaft, and boarding on the assumption is the flying bug all over again.
        return elevator.IsCabinOnSameSideAs(transform.position);
    }

    // ── Walking on and off the cabin ────────────────────────────────────────

    private void WarnBoardingWalkFailed(NemesisElevatorLink elevator, Transform boarding)
    {
        if (!warnedBoardingWalk.Add((boardingFailure, boarding))) return;

        Debug.LogWarning($"[{nameof(NemesisElevatorUser)}] '{name}' could not WALK aboard " +
                         $"'{elevator.name}' from '{boarding.name}', so it boarded the old way — " +
                         $"in a straight line, through the landing barrier.\n" +
                         $"WHERE IT GAVE UP: {Explain(boardingFailure)}\n" +
                         $"{DescribeBoardingGeometry(elevator, boarding)}", this);
    }

    /// <summary>
    /// What the failing step means and what to go and look at, in the terms of the thing that is
    /// actually wrong rather than in the terms of the code that noticed.
    ///
    /// This replaces one warning that named all six causes at once and therefore named none: with
    /// the walk failing for a different reason at each landing, "check that both ends sit on baked
    /// ground" was true, unhelpful, and the same text either way.
    /// </summary>
    private string Explain(EBoardingStep step)
    {
        switch (step)
        {
            case EBoardingStep.NoCabinNavMesh:
                return "the shaft reports a cabin NavMesh but the component is missing.";

            case EBoardingStep.BoardingNeverOpened:
                return $"the cabin floor and this landing's link were never both live within " +
                       $"{BoardingOpenTimeout}s. Either the cabin is not actually parked at this " +
                       "landing (check ElevatorCabinNavMesh's door gizmo: green = joined, grey = " +
                       "not), or the platform still reads as moving.";

            case EBoardingStep.AgentLeftTheNavMesh:
                return "the agent came off the NavMesh while stepping off the shaft link.";

            case EBoardingStep.StuckOnTheShaftLink:
                return $"it would not let go of the shaft link within {OffLinkTimeout}s, and an " +
                       "agent standing on a link it may not auto-traverse cannot be steered " +
                       "anywhere. Check that the NavMeshLink and NemesisElevatorLink are on the " +
                       "STATIC root and not on the cabin.";

            case EBoardingStep.DestinationRefused:
                return "SetDestination refused the boarding point outright — there is no NavMesh " +
                       "of this agent's type anywhere near it. The cabin's bake came out empty.";

            case EBoardingStep.PathDoesNotReachTheCabin:
                return "a path was found but it STOPS SHORT of the cabin, which means the " +
                       "boarding link is not joining the landing's mesh to the cabin's. The two " +
                       "usual causes: both of the link's ends snapped onto the SAME mesh (they " +
                       "are within a few centimetres of each other in height, so the cabin-side " +
                       "end landed on the level's floor instead of the cabin's), or the cabin's " +
                       "island is not where the door point is.";

            case EBoardingStep.WalkTimedOut:
                return $"the path was complete but not walked within {BoardingWalkTimeout}s — " +
                       "something is physically in the way, or the cabin left mid-walk.";

            default:
                return "no reason was recorded, which is itself a bug.";
        }
    }

    /// <summary>
    /// The three numbers that settle a boarding argument, measured rather than assumed: where the
    /// door point is, whether anything walkable is under it, and how far apart the link's two ends
    /// are vertically — which is what decides whether they can snap to the same mesh.
    /// </summary>
    private string DescribeBoardingGeometry(NemesisElevatorLink elevator, Transform boarding)
    {
        ElevatorCabinNavMesh cabin = elevator.CabinNav;
        if (cabin == null) return string.Empty;

        Vector3 door = cabin.BoardingPointFor(boarding);

        string doorGround = NavMesh.SamplePosition(door, out NavMeshHit doorHit, 0.6f, NavMesh.AllAreas)
            ? $"walkable ({doorHit.distance:0.00} m away)"
            : "NOTHING WALKABLE within 0.6 m";

        string landingGround = NavMesh.SamplePosition(boarding.position, out NavMeshHit landingHit,
                                                      0.6f, NavMesh.AllAreas)
            ? $"walkable ({landingHit.distance:0.00} m away)"
            : "NOTHING WALKABLE within 0.6 m";

        return $"landing '{boarding.name}' at {boarding.position} → {landingGround}\n" +
               $"cabin door at {door} → {doorGround}\n" +
               $"vertical gap between the link's two ends: " +
               $"{Mathf.Abs(door.y - boarding.position.y):0.00} m " +
               "(under ~0.3 m and both ends can snap to the same mesh, which makes the link a " +
               "no-op).";
    }

    /// <summary>
    /// Walks the Nemesis from the landing into the cabin, as an ordinary path.
    ///
    /// This is the whole difference between boarding and going through the wall. The old boarding
    /// interpolated the body from the landing to the ride point with the agent switched off — a
    /// straight line across three metres that includes the landing barrier and the shaft wall.
    /// Here the same trip is a NavMeshAgent destination, so it goes around whatever is in the way,
    /// through the doorway, at walking pace, with the animation following because the body is
    /// really moving.
    ///
    /// It needs two things that did not exist before: a NavMesh on the cabin floor, and a link
    /// joining it to this landing — both from <see cref="ElevatorCabinNavMesh"/>, both live only
    /// while the cabin is actually parked here. Either missing and this returns false, which sends
    /// the caller back to the interpolation.
    /// </summary>
    private async UniTask<bool> WalkAboardAsync(NemesisElevatorLink elevator, Transform boarding,
                                                CancellationToken token)
    {
        boardingFailure = EBoardingStep.None;

        ElevatorCabinNavMesh cabin = elevator.CabinNav;
        if (cabin == null)
        {
            boardingFailure = EBoardingStep.NoCabinNavMesh;
            return false;
        }

        // The cabin has only just finished arriving, and its floor and this landing's link are
        // restored from ElevatorCabinNavMesh's own Update — which may not have run yet on the
        // frame this resumes. A one-second grace instead of a same-frame verdict.
        if (!await WaitForBoardingOpenAsync(elevator, boarding, token))
        {
            boardingFailure = EBoardingStep.BoardingNeverOpened;
            return false;
        }

        // THE AGENT HAS TO COME OFF THE SHAFT LINK FIRST, and in this order. An agent standing on
        // a link it may not auto-traverse cannot be steered anywhere: told to keep going, it grinds
        // along the link direction, which points through the shaft wall — that is what the link is
        // for. LeaveCurrentLink runs while the link is still live, because deactivating it first
        // leaves ActivateCurrentOffMeshLink with nothing to act on; suspending it immediately
        // after is what stops the fresh path from stepping straight back onto it.
        //
        // The caller's finally puts the link back, on every exit path.
        LeaveCurrentLink();
        elevator.SetShaftLinkActive(false);

        if (!await WaitUntilOffTheShaftLinkAsync(token)) return false;

        return await WalkAgentToAsync(cabin.BoardingPointFor(boarding), token);
    }

    /// <summary>
    /// Waits until the agent has actually stopped standing on the shaft link.
    ///
    /// It used to be a single <c>UniTask.Yield</c> followed by a same-frame verdict, and that was
    /// a bet rather than a wait: letting go is not instant, the agent's own state updates in a
    /// different part of the player loop from this continuation, and one frame is exactly the kind
    /// of margin that holds until something upstream changes and then silently stops holding.
    /// When it stopped holding, the whole walk aboard was written off and the Nemesis crossed the
    /// wall instead — with a warning that blamed the bake.
    ///
    /// The request is REPEATED each frame rather than made once. The first
    /// <c>ActivateCurrentOffMeshLink(false)</c> lands while the link is still live and can be
    /// overtaken by the path the agent is holding; asking again once the link has actually been
    /// suspended is what makes it stick. It only ever runs in the frames where the agent has not
    /// let go yet, so the repetition costs nothing in the normal case.
    /// </summary>
    private async UniTask<bool> WaitUntilOffTheShaftLinkAsync(CancellationToken token)
    {
        float waited = 0f;

        while (waited < OffLinkTimeout)
        {
            // Yielded first, as the single yield this replaces was: the navigation system has to
            // get a frame to register the step-off that has just been requested.
            waited += Time.deltaTime;
            await UniTask.Yield(token);

            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            {
                boardingFailure = EBoardingStep.AgentLeftTheNavMesh;
                return false;
            }

            if (!agent.isOnOffMeshLink) return true;

            agent.ActivateCurrentOffMeshLink(false);
            agent.ResetPath();
        }

        boardingFailure = EBoardingStep.StuckOnTheShaftLink;
        return false;
    }

    /// <summary>
    /// Waits for the cabin's floor and one landing's link to be live at the same time.
    ///
    /// Both are switched by <see cref="ElevatorCabinNavMesh"/> from its own Update, off the
    /// platform's state, so "the cabin has arrived" and "you can walk aboard it" become true on
    /// different frames — and which of the two components updates first is not something either of
    /// them should be written to depend on. A second is far longer than that gap and far shorter
    /// than anything a player would notice.
    /// </summary>
    private async UniTask<bool> WaitForBoardingOpenAsync(NemesisElevatorLink elevator,
                                                         Transform landing, CancellationToken token)
    {
        ElevatorCabinNavMesh cabin = elevator.CabinNav;
        if (cabin == null) return false;

        float waited = 0f;

        while (waited < BoardingOpenTimeout)
        {
            if (cabin.IsBoardingOpen(landing)) return true;

            waited += Time.deltaTime;
            await UniTask.Yield(token);
        }

        return false;
    }

    /// <summary>
    /// Walks it back out of the cabin onto the landing it arrived at.
    ///
    /// The mirror of boarding, and it exists for the same reason: warping to the landing on arrival
    /// would be a teleport dressed as an arrival, and it is exactly the teleport the whole elevator
    /// system was built to avoid.
    /// </summary>
    private async UniTask<bool> WalkAshoreAsync(NemesisElevatorLink elevator, Transform exit,
                                                CancellationToken token)
    {
        ElevatorCabinNavMesh cabin = elevator.CabinNav;
        if (cabin == null || !cabin.IsBoardingOpen(exit)) return false;

        return await WalkAgentToAsync(exit.position, token);
    }

    /// <summary>
    /// Sends the agent to a point and waits until it is standing on it.
    ///
    /// Three agent settings are borrowed for the duration and handed back in the finally:
    ///
    /// - <c>stoppingDistance</c>, because the agent's own is the pursuit value (1.5 m here) and a
    ///   Nemesis that stops 1.5 m short of the boarding point stops on the landing, not in the lift.
    /// - <c>autoBraking</c>, because this leg wants to arrive precisely rather than flow onwards;
    ///   NemesisLifecycle turns it off globally for patrol movement, which is the opposite case.
    /// - <c>autoTraverseOffMeshLink</c>, which this component switches off in Awake so it can drive
    ///   elevators by hand. The landing-to-cabin link is a step through an open doorway, not a
    ///   shaft, and while a traversal is in flight this component's own Update returns early and so
    ///   would never cross it. Letting Unity take that one is what lets the walk be a walk.
    ///
    /// A PARTIAL path is failure, not arrival. It means the destination could not be reached and
    /// the agent stopped at the nearest point it could — against the barrier, typically — where
    /// remainingDistance duly falls to zero and reads exactly like having got there.
    /// </summary>
    private async UniTask<bool> WalkAgentToAsync(Vector3 target, CancellationToken token)
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;

        float savedStopping = agent.stoppingDistance;
        bool savedAutoBraking = agent.autoBraking;
        bool savedAutoTraverse = agent.autoTraverseOffMeshLink;

        try
        {
            agent.stoppingDistance = BoardingStoppingDistance;
            agent.autoBraking = true;
            agent.autoTraverseOffMeshLink = true;
            agent.isStopped = false;

            stateManager.SetGait(NemesisStateManager.EGait.Walking, BoardingSpeed);

            if (!agent.SetDestination(target))
            {
                boardingFailure = EBoardingStep.DestinationRefused;
                return false;
            }

            float waited = 0f;

            while (waited < BoardingWalkTimeout)
            {
                // Yielded before the first test on purpose: the frame a destination is issued, the
                // path is still pending and remainingDistance reads 0, which is indistinguishable
                // from having arrived.
                waited += Time.deltaTime;
                await UniTask.Yield(token);

                if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
                {
                    boardingFailure = EBoardingStep.AgentLeftTheNavMesh;
                    return false;
                }

                if (agent.pathPending) continue;

                if (agent.pathStatus != NavMeshPathStatus.PathComplete)
                {
                    boardingFailure = EBoardingStep.PathDoesNotReachTheCabin;
                    return false;
                }

                if (agent.remainingDistance <= agent.stoppingDistance) return true;
            }

            boardingFailure = EBoardingStep.WalkTimedOut;
            return false;
        }
        finally
        {
            if (agent != null && agent.isActiveAndEnabled)
            {
                agent.stoppingDistance = savedStopping;
                agent.autoBraking = savedAutoBraking;
                agent.autoTraverseOffMeshLink = savedAutoTraverse;
            }
        }
    }

    private async UniTask WaitUntilIdleAsync(MovingPlatform platform, CancellationToken token)
    {
        float waited = 0f;
        while (!platform.IsIdle && waited < PlatformWaitTimeout)
        {
            // The longest wait in the system, and the one where standing patiently looks most like
            // a broken monster. See ShouldAbandonForPlayer.
            if (ShouldAbandonForPlayer()) return;

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
    /// <param name="abandonIfPlayerReachable">Only for a trip taken BEFORE boarding — the empty one
    /// that calls the cabin over. Once the Nemesis is aboard there is nothing to abandon into:
    /// stepping off mid-shaft is a fall.</param>
    private async UniTask WaitUntilTripEndsAsync(MovingPlatform platform, CancellationToken token,
                                                 bool abandonIfPlayerReachable = false)
    {
        float waited = 0f;
        while (platform.IsMoving && waited < PlatformWaitTimeout)
        {
            if (abandonIfPlayerReachable && ShouldAbandonForPlayer()) return;

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
        // A capture that started mid-ride is holding the player where the body is standing — in
        // the cabin. Stepping the agent onto the landing there would tear the Nemesis two metres
        // out of its own grab animation, so the body's own position is tried first. The cabin's
        // NavMesh is back by now (ElevatorCabinNavMesh restores it the moment the lift parks), so
        // this normally lands on the first try; the landing is still the fallback.
        if (IsCapturing && TryWarpNear(transform.position)) return;

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
