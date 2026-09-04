using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Owns the Nemesis finite state machine and the references its states need.
///
/// It is deliberately a FACADE over four sibling components rather than the implementation of
/// everything they do. It used to be the implementation, all 880 lines of it, carrying twelve
/// separate jobs — telemetry, capture, warping, stuck detection, dormancy, route caching — none of
/// which is "run a state machine". The states still call the one object they hold a reference to;
/// what changed is that the work now lives where its name says it does:
///
///   NemesisPathOracle    route queries, throttled
///   NemesisTelemetry     the HUD vignettes and the audio's state events
///   NemesisStuckEscape   the no-progress watchdog and its warp out
///   NemesisLifecycle     dormancy, agent tuning, and every teleport
///
/// A facade is not a god object: the problem was never that everything could be reached from here,
/// it was that everything was implemented here. All four are added automatically when missing, so
/// no existing Nemesis prefab has to be opened and re-saved.
/// </summary>
public class NemesisStateManager : StateManager<NemesisStateManager.ENemesisState>
{
    [SerializeField] private FieldOfView fieldOfView;
    [SerializeField] private FieldOfListening fieldOfListening;
    [SerializeField] private SO_NemesisData nemesisData;
    [SerializeField] private SO_NemesisMovement nemesisMovement;
    [SerializeField] private NavMeshAgent navAgent;
    [SerializeField] private NemesisController nemesisController;
    [SerializeField] private Animator animController;

    [Header("Sibling components (auto-added when missing)")]
    [SerializeField] private NemesisPathOracle pathOracle;
    [SerializeField] private NemesisTelemetry telemetry;
    [SerializeField] private NemesisStuckEscape stuckEscape;
    [SerializeField] private NemesisLifecycle lifecycle;

    [Tooltip("Barre la mirada de lado a lado mientras el Nemesis espera parado en un waypoint. " +
             "Se agrega solo, igual que los cuatro de arriba.\n\n" +
             "Se tunea desde SO_NemesisData (Scan Half Angle / Scan Speed), no desde ac\u00e1; " +
             "poniendo Scan Half Angle en 0 queda inerte y la mirada vuelve a estar pegada al " +
             "frente del cuerpo.")]
    [SerializeField] private NemesisLookAround lookAround;

    [Tooltip("Opcional. Si el Nemesis lo tiene, la escalera se entera de cuándo está cruzando el " +
             "montacargas y no lo saca de Traversing en el medio. Sin este componente el Nemesis " +
             "simplemente no usa montacargas.")]
    [SerializeField] private NemesisElevatorUser elevatorUser;

    [Header("Decision layer")]
    [Tooltip("La escalera de prioridades: qué estado pide el Nemesis y en qué orden se leen las " +
             "reglas. Es un asset reordenable, así que cambiar el orden no recompila nada.\n\n" +
             "Vacío usa la escalera por defecto que está escrita en " +
             "SO_NemesisPriorities.BuildDefaultLadder() — el Nemesis funciona igual, pero nadie " +
             "puede reordenarla desde el editor. Creá el asset con " +
             "Create > Scriptable Objects > SO_NemesisPriorities.")]
    [SerializeField] private SO_NemesisPriorities nemesisPriorities;

    [Header("Capture")]
    [Tooltip("Seconds the Nemesis stays inert in Catch after the player has respawned, before " +
             "warping to a random waypoint and going back to Patrolling. This is the player's " +
             "window to get away from the checkpoint.\n\n" +
             "The screen stays covered by CaptureFadeView for the whole window (it waits on " +
             "NemesisEvents.OnCaptureResolved, fired right after the warp) — so raising this " +
             "does not risk the player seeing the teleport, only lengthens how long the cover " +
             "stays up.")]
    [SerializeField] private float captureGracePeriod = 4f;

    [Tooltip("Seconds after leaving Catch during which the Nemesis cannot enter it again.\n\n" +
             "Two jobs: it stops the Nemesis from re-grabbing the player the instant a capture " +
             "resolves, and it breaks the Chasing -> Catch -> Chasing loop for the case where " +
             "Catch bails out because it had no target to capture.")]
    [SerializeField] private float catchCooldown = 2f;

    private bool hasVisualTarget = false;
    private bool hasAudioTarget = false;
    private bool isSuspicious = false;

    private bool isActive;

    /// <summary>Activation ran but nowhere was safe to appear, so the Nemesis is dormant and
    /// retrying. See TickDeferredSpawn.</summary>
    private bool awaitingSafeSpawn;
    private float deferredSpawnTimer;

    /// <summary>Seconds between retries. Each attempt costs a NavMesh path query per spawn point,
    /// so it is polled at a human pace rather than every frame — the thing it waits for is the
    /// player walking on or turning round, which takes far longer than this.</summary>
    private const float DeferredSpawnRetryInterval = 0.5f;

    private Transform playerTransform;
    private bool isCaptureResolved;
    private bool hasReceivedRespawnNotification;
    private float catchCooldownTimer;

    public float CaptureGracePeriod { get => captureGracePeriod; }
    public bool HasReceivedRespawnNotification { get => hasReceivedRespawnNotification; }

    /// <summary>False during the catchCooldown window that follows leaving Catch. Read by
    /// <see cref="NemesisChasingState"/>, which is the only way into Catch.</summary>
    public bool CanEnterCatch { get => catchCooldownTimer <= 0f; }

    /// <summary>False while the Nemesis is dormant waiting for its activation puzzle. The FSM
    /// has not entered any state yet in that window.</summary>
    public bool IsActive { get => isActive; }

    // Read-only on purpose. Every one of these had a public setter and not one had an external
    // writer — surface that let any code in the project repoint the Nemesis's agent or animator
    // mid-run, in exchange for nothing.
    public FieldOfView FieldOfView { get => fieldOfView; }
    public FieldOfListening FieldOfListening {get => fieldOfListening; }
    public SO_NemesisData NemesisData { get => nemesisData; }
    public SO_NemesisMovement NemesisMovement { get => nemesisMovement; }
    public NavMeshAgent NavAgent { get => navAgent; }
    public NemesisController NemesisController { get => nemesisController; }
    public Animator AnimController { get => animController; }
    public bool HasVisualTarget { get => hasVisualTarget;}
    public bool HasAudioTarget { get => hasAudioTarget;}

    /// <summary>
    /// The Nemesis has caught something in the corner of its eye without having actually seen it.
    ///
    /// Sampled once per frame alongside the other two rather than read live off the sensor, and
    /// for the same reason they are: the decision layer has to look at ONE snapshot of the world,
    /// taken before it runs. A predicate reading the sensor directly could answer differently to
    /// two rungs of the same ladder pass if a sweep landed in between.
    /// </summary>
    public bool IsSuspicious { get => isSuspicious; }

    /// <summary>
    /// A lift crossing is physically in progress - waiting for the cabin, boarding, riding or
    /// stepping off.
    ///
    /// Read live off the component rather than sampled once per frame like the three sensor flags,
    /// because it is not a sensor reading that can flicker: it is a fact about who is currently
    /// driving the body, and it changes at most twice per trip.
    /// </summary>
    public bool IsUsingElevator => elevatorUser != null && elevatorUser.IsTraversing;

    /// <summary>How full the suspicion meter is, 0 to 1. Read by NemesisDebugHUD - the ladder uses
    /// <see cref="IsSuspicious"/>, which is this against the designer's threshold.</summary>
    public float Awareness => fieldOfView != null ? fieldOfView.Awareness : 0f;

    /// <summary>The player, or null when none is registered. Read by the sibling components, which
    /// all need it and none of which should be subscribing to PlayerRegistry separately.</summary>
    public Transform PlayerTransform => playerTransform;

    /// <summary>
    /// The state the FSM is in, or null before it has started. Nullable so a caller cannot
    /// mistake "not started yet" for Patrolling, which is enum value 0.
    ///
    /// isActive is part of the test and not an extra: InitializeStates assigns
    /// CurrentState = States[Patrolling] during Awake, so without it this returned Patrolling for
    /// a dormant Nemesis and the guarantee above was simply false. The decision layer reads this
    /// to know where it is, so it has to be true.
    /// </summary>
    public ENemesisState? CurrentStateKey =>
        isActive && CurrentState != null ? CurrentState.StateKey : (ENemesisState?)null;

    /// <summary>
    /// Whether the agent can be asked for anything without Unity logging an error.
    ///
    /// isActiveAndEnabled goes first and short-circuits the and: querying isOnNavMesh on a
    /// disabled agent logs on its own. And disabled is a normal state here, not an anomaly —
    /// NemesisElevatorUser switches it off for the whole freight elevator ride, because the
    /// NavMesh does not travel with the platform.
    /// </summary>
    public bool IsAgentReady => navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh;

    /// <summary>
    /// Whether the agent has reached the end of its current path.
    ///
    /// One place, because the expression is subtle and was copied six times across five files. The
    /// subtlety: remainingDistance measures along the ACTUAL path, following stairs and detours,
    /// where Vector3.Distance cuts through the air. A waypoint one floor up reads as "close" the
    /// moment the agent is nearly underneath it, long before it has climbed anything — and a
    /// marker placed at eye height adds a permanent Y gap that can keep arrival from ever
    /// registering at all. That paragraph used to be written out in four separate states, nearly
    /// word for word, which is what a missing method looks like.
    /// </summary>
    public bool HasArrived => IsAgentReady && !navAgent.pathPending &&
                              navAgent.remainingDistance <= navAgent.stoppingDistance;

    /// <summary>The agent's stopping distance as the prefab ships it. Captured in Awake, and used
    /// only when there is no movement asset to read the authored value from.</summary>
    private float defaultStoppingDistance;

    /// <summary>
    /// The stopping distance everything except an active pursuit runs at, and what
    /// <see cref="SetStoppingDistance"/> restores to.
    ///
    /// Read off the movement asset rather than off the agent, because the agent's own value is
    /// only true between Awake and Activate: <see cref="NemesisLifecycle.ApplyMovementTuning"/>
    /// overwrites it from the SO on waking up. Capturing the prefab's number instead would mean a
    /// chase that ends hands the agent back a stopping distance the designer had already
    /// overridden — and that number decides when four separate things count as "arrived".
    /// </summary>
    public float DefaultStoppingDistance =>
        nemesisMovement != null ? nemesisMovement.StoppingDistance : defaultStoppingDistance;

    /// <summary>
    /// How close the agent tries to get while it is actively closing on the player.
    ///
    /// IT IS DERIVED FROM THE CATCH REACH BECAUSE THE TWO ARE ONE SETTING PRETENDING TO BE TWO,
    /// and the prefab proved it: a stopping distance of 1.5 m against a CatchMaxReach of 1 m
    /// means the agent halts half a metre outside the only range at which a capture can fire. The
    /// Nemesis sprints at the player, stops short, plays its idle, and waits — visibly seeing
    /// them, never reaching them, forever. Nothing errors; the two numbers simply do not overlap.
    ///
    /// Kept at a fraction of the reach rather than at zero: an agent asked to arrive exactly on
    /// top of its destination oscillates around it, and the capture only needs to get inside the
    /// range, not to the centre of it.
    ///
    /// Floored so a designer who sets CatchMaxReach very small does not get an agent that jitters
    /// on the spot instead of one that cannot catch.
    /// </summary>
    public float PursuitStoppingDistance
    {
        get
        {
            float reach = nemesisData != null ? nemesisData.CatchMaxReach : 1f;
            return Mathf.Clamp(reach * 0.6f, 0.2f, Mathf.Max(0.2f, DefaultStoppingDistance));
        }
    }

    /// <summary>
    /// Sets how close the agent gets before it counts as arrived.
    ///
    /// A state that changes it owns putting it back — Chasing does, in its ExitState. It is on
    /// the facade rather than reached through NavAgent so that "arrived" keeps one definition:
    /// <see cref="HasArrived"/> reads whatever this last set, and every state's arrival test goes
    /// through that one property.
    /// </summary>
    public void SetStoppingDistance(float distance)
    {
        if (navAgent != null) navAgent.stoppingDistance = Mathf.Max(0f, distance);
    }

    /// <summary>
    /// How the Nemesis is moving. The CONTINUOUS channel: a gait holds until something sets
    /// another one.
    ///
    /// The one-shot channel — play a fall, wait for it to land — is a different thing and does not
    /// exist yet. Keeping them apart from the start is what will let it be added beside this
    /// rather than tangled into it.
    /// </summary>
    public enum EGait
    {
        Idle,
        Walking,
        Running,
        Grabbing,
    }

    private static readonly int WalkingParam = Animator.StringToHash("isWalking");
    private static readonly int RunningParam = Animator.StringToHash("isRunning");
    private static readonly int CatchingParam = Animator.StringToHash("isCatching");

    /// <summary>
    /// Sets how the Nemesis moves and how it looks doing it, together.
    ///
    /// They are one decision and used to be two handles reached through this facade — which is
    /// exactly how Searching ended up playing its run animation at walking speed. Nothing could
    /// catch that, because nothing owned the pair. Setting both here makes the mismatched
    /// combination unrepresentable rather than merely discouraged.
    ///
    /// It also retires seventeen loose Animator string literals in favour of three hashes, and
    /// empties four of the six ExitState bodies: they existed only to switch a bool back off, and
    /// the next state's gait now says what every bool should be.
    /// </summary>
    public void SetGait(EGait gait, float speed)
    {
        if (navAgent != null) navAgent.speed = speed;
        if (animController == null) return;

        animController.SetBool(WalkingParam, gait == EGait.Walking);
        animController.SetBool(RunningParam, gait == EGait.Running);
        animController.SetBool(CatchingParam, gait == EGait.Grabbing);
    }

    public enum ENemesisState
    {
        Patrolling,
        Investigating,
        Chasing,
        Searching,
        Catch,
        Traversing,
    }

    /// <summary>
    /// The two ways the Nemesis is allowed to get around. See <see cref="MovementOf"/> for which
    /// state is which and why the line falls where it does.
    ///
    /// Not serialised anywhere — it is derived from the state, never authored — so unlike
    /// <see cref="ENemesisState"/> and ENemesisPredicate this one carries no append-only hazard.
    /// </summary>
    public enum ENemesisMovement
    {
        /// <summary>Moves between authored waypoints, in the order the designer authored them.
        /// </summary>
        NodeBound,

        /// <summary>Moves anywhere on the NavMesh, using the waypoints as hints rather than as
        /// the only legal destinations.</summary>
        FreeRoam,
    }

    // ── Facade: route verdict (NemesisPathOracle) ───────────────────────────

    /// <summary>The route from here to a point, throttled. See <see cref="NemesisPathOracle"/>.
    /// </summary>
    /// <returns>false when the oracle is missing, or when the query could not run at all (an end
    /// off the NavMesh). A partial path returns true with
    /// <see cref="NemesisNav.NavRoute.IsComplete"/> false.</returns>
    public bool TryGetThrottledRoute(Vector3 target, out NemesisNav.NavRoute route)
    {
        if (pathOracle != null) return pathOracle.TryGetRoute(target, out route);

        route = default;
        return false;
    }

    /// <summary>Whether that route means changing floor by lift. See
    /// <see cref="NemesisPathOracle.IsAcrossFloors"/>.</summary>
    public bool IsRouteAcrossFloors(in NemesisNav.NavRoute route) =>
        pathOracle != null && pathOracle.IsAcrossFloors(route);

    // ── Decision layer ──────────────────────────────────────────────────────

    /// <summary>
    /// The decision layer's way in: ask for a state, and the machine takes it from there.
    ///
    /// The split this exists to enforce is that the tree decides WHICH state and the machine owns
    /// entering it, running it and leaving it. Writing NextState — rather than transitioning
    /// here — is what keeps the tree from needing to know anything about state lifecycles, and it
    /// means a request and a state's own self-rejection travel down the same channel and cannot
    /// contradict each other.
    ///
    /// A STATE'S OWN REQUEST OUTRANKS THE LADDER'S. Finding NextState already pointing somewhere
    /// else means the state itself wrote it, and a state only ever writes it to report something
    /// about its own EXECUTION that the ladder cannot see: NemesisCatchState does it twice, once
    /// on discovering there is nobody to grab and once when its grace window has run out. Both
    /// are set during base.Update() and can only be picked up on the following frame — where this
    /// runs first. Overwriting them there strands the Nemesis in Catch for the rest of the run,
    /// because the ladder's top rung is "a capture in progress is not re-decided" and it would win
    /// every frame, forever.
    ///
    /// THERE IS EXACTLY ONE OTHER WRITER AND THAT IS THE POINT. This guard used to be spelled
    /// "return if the request is for the current state", which does the same job by accident and
    /// stops doing it the moment anything else writes the channel. Something did: the Behavior
    /// graph agent sat on the prefab ticking from its own Update, so a frame where the ladder
    /// wanted to stay put left the GRAPH's stale request standing, and base.Update() transitioned
    /// on it. Chasing and Patrolling then alternated every frame — and a machine that transitions
    /// every frame never runs a single UpdateState, so nothing ever set a destination. That is
    /// the Nemesis that looks straight at you and stands there changing its mind.
    /// </summary>
    public void RequestState(ENemesisState key)
    {
        if (CurrentState == null) return;
        if (!CurrentState.NextState.Equals(CurrentState.StateKey)) return;

        CurrentState.NextState = key;
    }

    /// <summary>
    /// The prioritised ladder, read once per frame. The Nemesis's only voter — see
    /// <see cref="RequestState"/> for what happened the last time there were two.
    /// </summary>
    public NemesisDecision Decision { get; private set; }

    /// <summary>The rung list the ladder walks, or null to use the built-in default. See
    /// <see cref="SO_NemesisPriorities"/>.</summary>
    public SO_NemesisPriorities Priorities => nemesisPriorities;

    /// <summary>The Searching state instance, or null before the machine is built. Reached for by
    /// <see cref="NemesisTelemetry.SearchInterceptPoint"/>, which reports where its cut-off is
    /// aimed — the machine itself never reads it.</summary>
    public NemesisSearchingState SearchingState =>
        States.TryGetValue(ENemesisState.Searching, out BaseState<ENemesisState> state)
            ? state as NemesisSearchingState
            : null;

    /// <summary>The Chasing state instance, or null before the machine is built. Reached for by
    /// NemesisGizmos, which draws where the pursuit is aiming - the machine itself never reads it.
    /// </summary>
    public NemesisChasingState ChasingState =>
        States.TryGetValue(ENemesisState.Chasing, out BaseState<ENemesisState> chasing)
            ? chasing as NemesisChasingState
            : null;

    private void TickDecision()
    {
        // NOTHING IS DECIDED WHILE SOMETHING ELSE OWNS THE BODY.
        //
        // NemesisElevatorUser switches the agent off for the whole freight-elevator ride and moves
        // the Transform by hand. Every state used to carry this guard at the top of its own
        // UpdateState, which had the side effect that no transition could happen during a ride —
        // and that side effect was load-bearing. Moving the decision up here without it meant
        // re-deciding mid-ride: the Nemesis is in the shaft and off the NavMesh, so the route
        // query behind the elevator rung fails, "the lift is on the way" goes false, and it drops
        // out of Traversing into a state that cannot act either. The commitment that put it on the
        // lift evaporates halfway up.
        //
        // Freezing here is also right for the other case that clears this flag — an agent knocked
        // off the NavMesh by a Warp that did not land. Nothing decided would be actionable, and
        // NemesisStuckEscape is what resolves that one.
        if (!IsAgentReady) return;

        if (Decision == null) return;

        RequestState(Decision.Decide());
    }

    /// <summary>
    /// Whether the player is genuinely within arm's reach: close horizontally, on the same floor,
    /// and with no wall in between.
    ///
    /// It answers a different question from the agent's own arrival test, and that is the whole
    /// reason it exists. NavMeshAgent.remainingDistance measures how far the agent still has to go
    /// to reach the end of ITS path, and when that path is partial the end is the closest
    /// reachable point to the player — right against the wall separating them. There
    /// remainingDistance drops to zero with the player two metres away and a wall in between,
    /// which is exactly the grab-through-walls bug.
    ///
    /// Moved out of NemesisChasingState, where it was private: with the decision layer choosing
    /// when to capture, "can it reach them" is a fact about the world rather than something one
    /// state knows about itself.
    /// </summary>
    public bool CanReachPlayerNow
    {
        get
        {
            if (nemesisData == null) return true;   // No data to filter with: do not break the flow.
            if (fieldOfView == null) return false;

            PlayerStateManager target = fieldOfView.GetCurrentTarget();
            if (target == null) return false;

            Vector3 toPlayer = target.transform.position - transform.position;

            // Between floors: the vertical gap rules the capture out before anything else. A
            // player directly above is a metre and a half away in a straight line and half a
            // storey on foot.
            if (Mathf.Abs(toPlayer.y) > nemesisData.CatchMaxVerticalOffset) return false;

            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > nemesisData.CatchMaxReach * nemesisData.CatchMaxReach) return false;

            if (!nemesisData.CatchRequiresLineOfSight) return true;
            if (fieldOfListening == null) return true;   // No way to test it: do not block the capture.

            Vector3 eye = transform.position + Vector3.up * CatchProbeHeight;
            Vector3 targetPoint = target.transform.position + Vector3.up * CatchProbeHeight;
            return !fieldOfListening.IsOccludedByWall(eye, targetPoint);
        }
    }

    /// <summary>Height the capture's line-of-sight ray is fired from. Cast from the pivots, which
    /// sit at floor level, the ray scrapes the ground and always reports occlusion.</summary>
    private const float CatchProbeHeight = 1f;

    // ── Facade: belief ──────────────────────────────────────────────────────

    /// <summary>
    /// Where the Nemesis currently believes the player is: whichever sensor caught them most
    /// recently.
    ///
    /// Deliberately reads <c>HasLastKnownPosition</c> and not <c>HasVisualTarget</c> — a belief
    /// that stopped being refreshed is still a belief, and it is the whole reason a pursuit or a
    /// search is happening at all.
    ///
    /// FRESHEST AND NOT SIGHT-FIRST, WHICH IT USED TO BE. "Seen first, heard second" is only
    /// right while the sighting is at least as recent as the noise, and the case where it is not
    /// is the common one: the player runs behind a wall. Sight stops updating, hearing keeps
    /// updating, and a sight-first belief pins the answer to the spot where they disappeared. The
    /// Nemesis then runs to that spot, arrives, and stands on it — while the noise renewing its
    /// pursuit every frame keeps it from ever giving up and searching. It reads in game as a
    /// monster that heard you, sprinted to the wrong place and froze there.
    ///
    /// Lives here rather than in a state because three of them want the same answer (Traversing
    /// to hold its destination, Searching to anchor its cut-off, and the controller's patrol bias
    /// through its own equivalent). Three private copies of the same comparison is how the
    /// definition of "belief" quietly drifts apart between them.
    /// </summary>
    public bool TryGetBelief(out Vector3 position) => TryGetBelief(out position, out _);

    /// <summary>
    /// <see cref="TryGetBelief(out Vector3)"/>, and WHICH SENSE produced the answer.
    ///
    /// The source matters because the two are not equally good claims about where somebody is. A
    /// sighting is a position; a noise is roughly where a sound came from, and the search's room
    /// commitment is only worth making off the former — committing to sweep a room on the strength
    /// of a footstep heard through a wall would have the Nemesis confidently searching the wrong
    /// side of it.
    ///
    /// The overload exists rather than a separate BeliefFromSight property so the answer and its
    /// provenance can never disagree: a property would resolve the freshest sensor a second time,
    /// on sensor ages that may have moved on between the two calls.
    /// </summary>
    public bool TryGetBelief(out Vector3 position, out bool fromSight)
    {
        position = Vector3.zero;
        fromSight = false;

        bool sawIt = fieldOfView != null && fieldOfView.HasLastKnownPosition;
        bool heardIt = fieldOfListening != null && fieldOfListening.HasLastKnownPosition;

        if (!sawIt && !heardIt) return false;

        // Both ages are infinity when the sensor has never fired, so the comparison picks the one
        // that has without needing a special case. Ties go to sight, which is the more precise of
        // the two: a sighting is a position, a noise is roughly where a sound came from.
        float sightAge = sawIt ? fieldOfView.TimeSinceLastSighting : float.PositiveInfinity;
        float noiseAge = heardIt ? fieldOfListening.TimeSinceLastNoise : float.PositiveInfinity;

        fromSight = sightAge <= noiseAge;

        position = fromSight ? fieldOfView.LastKnownPosition
                             : fieldOfListening.LastKnownPosition;
        return true;
    }

    /// <summary>Seconds since either sensor last caught the player, or infinity if neither ever
    /// has. How much <see cref="TryGetBelief"/>'s answer is still worth.</summary>
    public float BeliefAge
    {
        get
        {
            float age = float.PositiveInfinity;
            if (fieldOfView != null) age = Mathf.Min(age, fieldOfView.TimeSinceLastSighting);
            if (fieldOfListening != null) age = Mathf.Min(age, fieldOfListening.TimeSinceLastNoise);
            return age;
        }
    }

    /// <summary>Drops the cached verdict so the next query recomputes. Called when entering a
    /// state that is about to act on the answer, and after every teleport.</summary>
    public void InvalidateRouteVerdict()
    {
        if (pathOracle != null) pathOracle.Invalidate();
    }

    // ── Facade: stuck detection (NemesisStuckEscape) ────────────────────────

    /// <summary>See <see cref="NemesisStuckEscape.IsSuppressed"/>.</summary>
    public bool IsStuckDetectionSuppressed => stuckEscape != null && stuckEscape.IsSuppressed;

    /// <summary>Opens a window with no stuck detection. Every <see cref="PushStuckSuppression"/>
    /// must have its <see cref="PopStuckSuppression"/>, even if the traversal is cancelled — use
    /// try/finally. Called by NemesisDoorUser and NemesisElevatorUser.</summary>
    public void PushStuckSuppression()
    {
        if (stuckEscape != null) stuckEscape.Push();
    }

    public void PopStuckSuppression()
    {
        if (stuckEscape != null) stuckEscape.Pop();
    }

    // ── Facade: repositioning (NemesisLifecycle) ────────────────────────────

    /// <summary>Warps the Nemesis away after a capture. Called by NemesisCatchState. See
    /// <see cref="NemesisLifecycle.RepositionAfterCapture"/>.</summary>
    public void RepositionAfterCapture()
    {
        if (lifecycle != null) lifecycle.RepositionAfterCapture();
    }

    /// <summary>
    /// Moves the Nemesis, keeping everything that depends on its position in step.
    ///
    /// Public because <see cref="NemesisStuckEscape"/> and <see cref="NemesisLifecycle"/> both
    /// teleport, and neither should be re-deriving the two pieces of bookkeeping below — a warp
    /// that skips either leaves the FSM steering from the floor it just left, or the watchdog
    /// reading the jump as ground covered on foot and never firing again.
    /// </summary>
    /// <returns>false when the agent could not be placed there — the target is off the NavMesh.
    /// </returns>
    public bool WarpTo(Vector3 position)
    {
        // Snapped first, and refused outright when nothing walkable is nearby.
        //
        // Every caller aims at a hand-placed marker, and a marker a few centimetres inside a wall
        // is a warp that lands the agent off the NavMesh. Unity reports that warp as a success;
        // what follows is an agent that cannot path, states whose guards make it stand still, and
        // a stuck watchdog that warps it somewhere else — through the next wall. Refusing here
        // leaves the Nemesis where it was, which the watchdog can retry, instead of stranding it
        // somewhere it can never recover from.
        if (!NemesisNav.TrySnapToNavMesh(position, out Vector3 landing))
        {
            Debug.LogWarning($"[{nameof(NemesisStateManager)}] Refused to warp to {position}: no " +
                             $"walkable NavMesh within {NemesisNav.DefaultSampleRadius}u of it. " +
                             "Check that the marker sits on the mesh and inside an area this " +
                             "Nemesis is allowed to use.", this);
            return false;
        }

        // Warp and not transform.position: a NavMeshAgent keeps its own internal position and
        // would drag the Nemesis straight back on the next agent update.
        bool moved;

        if (navAgent == null || !navAgent.isActiveAndEnabled)
        {
            transform.position = landing;
            moved = true;
        }
        else
        {
            moved = navAgent.Warp(landing);
        }

        if (!moved) return false;

        // Any warp moves further in one frame than either of these was meant to absorb.
        InvalidateRouteVerdict();
        if (stuckEscape != null) stuckEscape.ResetSample();

        return true;
    }

    void Awake()
    {
        ResolveHierarchyReferences();

        if (!ValidateReferences())
        {
            // Disabled rather than left running, same as PlayerStateManager. Every reference below
            // is dereferenced either by this Update each frame or by the states themselves, and
            // InitializeStates() is the worst of them: the state constructors read NemesisData, so
            // a missing asset throws mid-construction and leaves the States dictionary half filled
            // — after which CurrentState = States[Patrolling] throws KeyNotFoundException and the
            // real cause is nowhere in the log.
            //
            // Note this leaves the Nemesis visible but inert rather than dormant: SetDormant(true)
            // lives in Start(), which Unity never calls on a component disabled during Awake. That
            // is deliberate — a broken object should be easy to find in the scene, and the error
            // above points straight at it.
            enabled = false;
            return;
        }

        telemetry.Initialize(this);
        stuckEscape.Initialize(this);
        lifecycle.Initialize(this);

        // After ValidateReferences, because it reads NemesisData through this facade, and before
        // InitializeStates so nothing can tick a half-built machine.
        Decision = new NemesisDecision(this);

        // Captured before anything can change it, so the states have a value to return the agent
        // to. See DefaultStoppingDistance.
        defaultStoppingDistance = navAgent.stoppingDistance;

        InitializeStates();

        // Deliberately the last thing Awake does, and deliberately not in OnEnable.
        //
        // Not in OnEnable because docs/CLAUDE.md is explicit that static events are subscribed in
        // Awake and released in OnDestroy — a static delegate outlives the GameObject's enabled
        // state, so an enabled-scoped subscription is a listener that silently stops listening.
        // Here that was not cosmetic: PuzzleStateManager.OnPuzzleCompleted is this Nemesis's
        // ACTIVATION gate, and the catch-up for an already-solved puzzle only runs in Start(). A
        // Nemesis disabled for so much as a frame around its puzzle would never wake up again,
        // with nothing in the log to say why.
        //
        // Last, and after the early return above, so a Nemesis whose references failed validation
        // stays deaf: it is disabled and cannot service an Activate() call. OnDestroy still
        // unsubscribes unconditionally, which is safe — releasing a delegate that was never added
        // is a no-op.
        SubscribeToEvents();
    }

    /// <summary>
    /// Fills in any reference the prefab left empty by looking it up in the Nemesis's own
    /// hierarchy, so a rig swap or a re-created child does not have to be re-dragged by hand.
    ///
    /// Same shape as PlayerStateManager.ResolveHierarchyReferences, including the explicit
    /// '== null' instead of '??=': a field pointing at a destroyed object is only null through
    /// UnityEngine.Object's overloaded operator, which '??=' does not use — it would keep the
    /// dead reference, which is the exact case this method exists for.
    /// </summary>
    private void ResolveHierarchyReferences()
    {
        // NemesisController is required on this GameObject (see its RequireComponent), so this
        // always succeeds when the inspector reference was simply left unassigned.
        if (nemesisController == null) nemesisController = GetComponent<NemesisController>();
        if (navAgent == null)         navAgent         = GetComponent<NavMeshAgent>();

        // Added rather than reported missing, and deliberately so: none of the four carries scene
        // wiring a designer could get wrong — they read what they need off this object. Requiring
        // every existing Nemesis prefab to be opened and re-saved to gain components with nothing
        // to configure would be friction for its own sake. AddComponent runs each Awake
        // synchronously, so they are constructed before the line after this one.
        pathOracle  = ResolveSibling(pathOracle);
        telemetry   = ResolveSibling(telemetry);
        stuckEscape = ResolveSibling(stuckEscape);
        lifecycle   = ResolveSibling(lifecycle);

        // Added on the same terms as the four above: it carries no scene wiring a designer could
        // get wrong - it finds the state manager and the sensor off this object, and every number
        // it uses lives on SO_NemesisData. Requiring the prefab to be opened and re-saved to gain
        // it would mean the scan silently does nothing on any Nemesis nobody remembered to touch,
        // which is the worst of both worlds: a feature that exists in the code and not in the game.
        lookAround  = ResolveSibling(lookAround);

        // GetComponent and NOT ResolveSibling: unlike the five above, this one is a real feature
        // with scene wiring behind it (links, landings, a platform). A Nemesis in a level with no
        // freight elevator should not silently grow one.
        if (elevatorUser == null) elevatorUser = GetComponent<NemesisElevatorUser>();

        // includeInactive: the sensors are switched off while the Nemesis is dormant, and the
        // Animator sits on the model root, which a prefab may ship disabled.
        if (fieldOfView == null)      fieldOfView      = GetComponentInChildren<FieldOfView>(true);
        if (fieldOfListening == null) fieldOfListening = GetComponentInChildren<FieldOfListening>(true);
        if (animController == null)   animController   = GetComponentInChildren<Animator>(true);
    }

    private T ResolveSibling<T>(T current) where T : Component
    {
        if (current != null) return current;

        T found = GetComponent<T>();
        return found != null ? found : gameObject.AddComponent<T>();
    }

    /// <summary>
    /// Reports everything still unresolved in one message instead of letting each one surface as
    /// its own NullReferenceException later. Returns false if the Nemesis cannot run.
    ///
    /// This is what lets the states dereference NemesisData/NemesisMovement/AnimController/
    /// NavAgent without guarding — the contract is established here, before InitializeStates()
    /// constructs any of them.
    ///
    /// The four sibling components are not checked: ResolveHierarchyReferences adds them when
    /// missing, so they cannot be null by the time this runs.
    /// </summary>
    private bool ValidateReferences()
    {
        List<string> missing = new List<string>();

        // The two SOs are assets and not part of the hierarchy, so they can only ever be reported.
        if (nemesisData == null)       missing.Add(nameof(nemesisData));
        if (nemesisMovement == null)   missing.Add(nameof(nemesisMovement));
        if (navAgent == null)          missing.Add(nameof(navAgent));
        if (fieldOfView == null)       missing.Add(nameof(fieldOfView));
        if (fieldOfListening == null)  missing.Add(nameof(fieldOfListening));
        if (animController == null)    missing.Add(nameof(animController));
        if (nemesisController == null) missing.Add(nameof(nemesisController));

        if (missing.Count == 0) return true;

        Debug.LogError($"[{nameof(NemesisStateManager)}] '{name}' could not resolve " +
                       $"{missing.Count} reference(s) from its own hierarchy: " +
                       $"{string.Join(", ", missing)}. The Nemesis has been disabled — assign " +
                       $"them in the inspector, or add the missing objects under it.", this);
        return false;
    }

    /// <summary>
    /// Deliberately does NOT call base.Start(): that is what runs CurrentState.EnterState() and
    /// starts the FSM, and a puzzle-gated Nemesis must not start until its puzzle is solved.
    /// <see cref="Activate"/> is the only thing that enters the first state.
    /// </summary>
    public override void Start()
    {
        stuckEscape.ResetSample();

        string gate = nemesisController != null ? nemesisController.ActivatedByPuzzleId : null;

        if (string.IsNullOrWhiteSpace(gate))
        {
            // No gate configured: live from Play, as before. It still runs ChooseSpawnPoint(),
            // which is a no-op when no spawn points are assigned — so existing scenes that never
            // filled that list in keep starting the Nemesis exactly where it is placed.
            Activate();
            return;
        }

        // Catch-up, same as Checkpoint and NemesisRoute: the puzzle may already be solved by the
        // time this loads (additive level load, restored snapshot), and OnPuzzleCompleted only
        // fires on the transition — without this the Nemesis would never wake up.
        if (PuzzleStateManager.Exists && PuzzleStateManager.Instance.IsPuzzleCompleted(gate))
            Activate();
        else
            lifecycle.SetDormant(true);
    }

    public override void Update()
    {
        // Dormant: no senses, no navigation, no proximity vignette. Checked before the pause
        // guard because a paused game and a Nemesis that has not spawned yet are different
        // things, and neither should tick the FSM.
        if (!isActive)
        {
            // Woken but with nowhere safe to appear yet. Nothing else in Update may run:
            // the senses, the FSM and the proximity vignette all belong to a Nemesis that
            // is actually in the world, and this one is not in it yet.
            if (awaitingSafeSpawn) TickDeferredSpawn();
            return;
        }

        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        // Ticked before the FSM, so the frame it expires is already a frame Chasing can catch on.
        if (catchCooldownTimer > 0f) catchCooldownTimer -= Time.deltaTime;

        hasVisualTarget = fieldOfView.HasVisualTarget;
        hasAudioTarget = fieldOfListening.HasAudioTarget;
        isSuspicious = fieldOfView.IsSuspicious;

        // Lay down the trail of patrol waypoints the player was sensed near. Done here, off the
        // flags that were just sampled, so there is exactly one place that decides "a detection
        // happened this frame" — and so the trail records only what the sensors actually caught.
        if (hasVisualTarget || hasAudioTarget) nemesisController?.MarkBeliefTrace();

        // Before the FSM tick, not after: proximity owes nothing to the current state, and below
        // base.Update() anything a state threw took it down too, every frame. See
        // NemesisTelemetry.TickProximity.
        telemetry.TickProximity();

        // Decide before executing. The tree looks at the world exactly as the sensors read it a
        // few lines above, and base.Update() acts on that answer in the SAME frame — where a
        // state writing its own NextState during UpdateState could only ever be picked up on the
        // next one. That one-frame lag on every transition disappears with this ordering.
        TickDecision();

        base.Update();

        telemetry.TickStateEvents(isCaptureResolved);
        stuckEscape.Tick(IsNavigatingState());
    }

    private void SubscribeToEvents()
    {
        PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
        PlayerRegistry.OnPlayerUnregistered += HandlePlayerUnregistered;
        CheckpointManager.OnRespawned += HandleCheckpointRespawned;
        PuzzleStateManager.OnPuzzleCompleted += HandleActivationPuzzleCompleted;
    }

    private void OnDestroy()
    {
        PlayerRegistry.Unsubscribe(HandlePlayerRegistered);
        PlayerRegistry.OnPlayerUnregistered -= HandlePlayerUnregistered;
        CheckpointManager.OnRespawned -= HandleCheckpointRespawned;
        PuzzleStateManager.OnPuzzleCompleted -= HandleActivationPuzzleCompleted;
    }

    /// <summary>
    /// Only the HUD cleanup stays enabled-scoped, and it belongs here rather than in OnDestroy:
    /// the red chase vignette is driven by a start/end pair, so a Nemesis switched off mid-chase
    /// leaves it lit over the HUD with nothing left alive to turn it off. Being disabled and being
    /// destroyed both have to close that pair, and OnDisable covers both — Unity raises it on the
    /// way to OnDestroy too.
    /// </summary>
    private void OnDisable()
    {
        if (telemetry != null) telemetry.CloseChase();
    }

    private void HandlePlayerRegistered(PlayerStateManager player) => playerTransform = player.transform;

    private void HandlePlayerUnregistered(PlayerStateManager player) => playerTransform = null;

    // ── Activation ──────────────────────────────────────────────────────────
    //
    // The Nemesis used to be live from the first frame, patrolling out of wherever it happened to
    // be dropped in the scene — which is what made its first appearance feel random. It now stays
    // dormant until its activation puzzle is solved, and only then picks a spawn point.

    private void HandleActivationPuzzleCompleted(string completedId)
    {
        string gate = nemesisController != null ? nemesisController.ActivatedByPuzzleId : null;
        if (string.IsNullOrWhiteSpace(gate) || completedId != gate) return;

        Activate();
    }

    /// <summary>
    /// Wakes the Nemesis up: warps it to a spawn point away from the player and starts the FSM.
    /// Idempotent, so re-completing the activation puzzle never re-spawns it mid-run.
    ///
    /// Entering the first state is the one part that cannot move to NemesisLifecycle: it touches
    /// the protected State dictionary of the shared FSM base, and reaching into that from a
    /// sibling component would give the machine a second owner.
    /// </summary>
    /// <summary>
    /// Retries the spawn-in while the Nemesis waits for somewhere safe to appear.
    ///
    /// Paused with the game on purpose: the player cannot move or turn while paused, so the answer
    /// cannot change, and retrying would only burn path queries behind the menu.
    /// </summary>
    private void TickDeferredSpawn()
    {
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        deferredSpawnTimer -= Time.deltaTime;
        if (deferredSpawnTimer > 0f) return;

        deferredSpawnTimer = DeferredSpawnRetryInterval;

        // Straight back through Activate rather than duplicating its body: the isActive guard at
        // the top makes it safe to call repeatedly, and it either succeeds outright or puts the
        // Nemesis back to sleep with awaitingSafeSpawn still set.
        Activate();
    }

    public void Activate()
    {
        if (isActive) return;

        // Woken before the spawn attempt because the warp goes through the NavMeshAgent, and a
        // dormant Nemesis has its agent disabled.
        lifecycle.SetDormant(false);
        lifecycle.ApplyMovementTuning();

        // Done before entering Patrolling, so the first patrol cycle starts from the spawn point
        // and not from wherever the prefab was sitting.
        //
        // A null means nowhere is safe RIGHT NOW — every spawn point is either too close to the
        // player, inside their view cone, or standing in the open. That is a "not yet", not a
        // "never": it clears itself the moment the player walks on or turns round. So the Nemesis
        // goes back to sleep and tries again, rather than appearing somewhere it should not. See
        // TickDeferredSpawn.
        if (nemesisController != null && nemesisController.ChooseSpawnPoint() == null)
        {
            lifecycle.SetDormant(true);
            awaitingSafeSpawn = true;
            deferredSpawnTimer = 0f;
            return;
        }

        isActive = true;
        awaitingSafeSpawn = false;

        // After the warp, or the first stuck sample would be the pre-spawn position and the
        // Nemesis would read as having teleported "without progress" on the next check.
        stuckEscape.ResetSample();

        CurrentState = States[ENemesisState.Patrolling];

        // Start() deliberately does not call base.Start(), which is what would normally stamp
        // this — so without it here the machine reports having been in Patrolling since the
        // session began, and every dwell floor the decision layer expresses is already expired
        // the first time it is asked.
        MarkStateEntered();
        CurrentState.EnterState();
    }

    /// <summary>
    /// The save system finished loading the checkpoint. This is the notification the spec requires
    /// ("el Sistema de Guardado debe notificar al NemesisController") — the Nemesis never calls
    /// into CheckpointManager itself, it only reacts to this.
    ///
    /// Guarded to Catch: CheckpointManager.OnRespawned fires for any respawn, and this is the only
    /// Nemesis listening today, but nothing ties the event to "this specific capture" — the guard
    /// is what makes that safe.
    /// </summary>
    private void HandleCheckpointRespawned(Checkpoint checkpoint)
    {
        if (CurrentState == null || CurrentState.StateKey != ENemesisState.Catch) return;

        isCaptureResolved = true;
        hasReceivedRespawnNotification = true;
    }

    // ── Capture ─────────────────────────────────────────────────────────────
    //
    // Per spec this class does not resolve captures itself: NemesisCatchState calls
    // player.OnCaptured() and CheckpointManager takes it from there on its own, notifying this
    // class back through HandleCheckpointRespawned above. No direct call into save or UI here.

    /// <summary>Called by NemesisCatchState on entry, so a second capture starts clean.</summary>
    public void BeginCapture()
    {
        isCaptureResolved = false;
        hasReceivedRespawnNotification = false;
    }

    /// <summary>
    /// Opens the window during which Chasing will not hand back to Catch. Called by
    /// NemesisCatchState on exit — including the exit it takes when it found nobody to capture,
    /// which is the loop this exists to break.
    /// </summary>
    public void BeginCatchCooldown() => catchCooldownTimer = catchCooldown;

    /// <summary>
    /// Whether the FSM is in a state that is supposed to be getting somewhere. Handed to
    /// <see cref="NemesisStuckEscape"/> so it never has to know the state enum.
    ///
    /// Catch is excluded on purpose: standing still is the whole point of that state.
    /// </summary>
    private bool IsNavigatingState()
    {
        if (CurrentState == null) return false;

        ENemesisState key = CurrentState.StateKey;
        return key == ENemesisState.Patrolling ||
               key == ENemesisState.Investigating ||
               key == ENemesisState.Chasing ||
               key == ENemesisState.Searching ||
               key == ENemesisState.Traversing;
    }

    /// <summary>
    /// How a state gets around: along the authored waypoint graph, or freely over the NavMesh.
    ///
    /// THE DISTINCTION IS THE DESIGN, and until now it was only implicit — readable by opening
    /// each state and seeing what it assigned to NavAgent.destination, which meant nobody could
    /// see it while playing and it drifted. Searching was on the wrong side of it for a long time
    /// without that being a decision anybody made: it rolled over graph nodes and reached the free
    /// NavMesh only down an error path, so a room with no waypoint in it was a room the Nemesis
    /// could not look inside.
    ///
    /// NODE-BOUND is for movement where the waypoints ARE the route. Patrolling walks the polyline
    /// the designer authored, in the order they authored it, because that order is the level
    /// design speaking. Traversing is node-bound in the same sense — its path is dictated by the
    /// freight elevator's landings, not chosen.
    ///
    /// FREE ROAM is for everything about hunting the player. The waypoints stay useful as hints
    /// about where a person might be worth looking for — NemesisFreeRoam offers them first, and
    /// NemesisPursuit will detour through one that has line of sight — but they stop being the
    /// only places the Nemesis is allowed to stand.
    ///
    /// A STATIC TABLE AND NOT A VIRTUAL PROPERTY. The alternative is re-parenting all six states
    /// onto a Nemesis-specific base class to declare one value each; BaseState is shared with the
    /// player FSM, so it cannot hold this. Six files touched to express six rows is the worse
    /// trade, and <see cref="IsNavigatingState"/> directly above already set the precedent that
    /// classifications of states live here, on the facade.
    /// </summary>
    public static ENemesisMovement MovementOf(ENemesisState state)
    {
        switch (state)
        {
            case ENemesisState.Chasing:
            case ENemesisState.Searching:
            case ENemesisState.Investigating:
                return ENemesisMovement.FreeRoam;

            // Catch navigates nowhere, so its answer is arbitrary. Node-bound is the quieter of
            // the two defaults: nothing reads this for Catch, and if anything ever does, "does not
            // roam" is the true half of the statement.
            default:
                return ENemesisMovement.NodeBound;
        }
    }

    /// <summary>The movement policy of the state the Nemesis is in right now. For the debug HUD
    /// and the gizmos — see <see cref="MovementOf"/> for why this is worth being able to see.
    /// </summary>
    public ENemesisMovement CurrentMovement =>
        CurrentState != null ? MovementOf(CurrentState.StateKey) : ENemesisMovement.NodeBound;

    private void InitializeStates()
    {
        States.Add(ENemesisState.Patrolling, new NemesisPatrolState(ENemesisState.Patrolling, this));
        States.Add(ENemesisState.Chasing, new NemesisChasingState(ENemesisState.Chasing,this));
        States.Add(ENemesisState.Searching, new NemesisSearchingState(ENemesisState.Searching,this));
        States.Add(ENemesisState.Investigating, new NemesisInvestigatingState(ENemesisState.Investigating,this));
        States.Add(ENemesisState.Catch, new NemesisCatchState(ENemesisState.Catch, this));
        States.Add(ENemesisState.Traversing, new NemesisTraversingState(ENemesisState.Traversing, this));
        CurrentState = States[ENemesisState.Patrolling];
    }
}
