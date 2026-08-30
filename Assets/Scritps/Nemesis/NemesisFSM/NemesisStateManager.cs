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

    [Header("Decision layer")]
    [Tooltip("Tick this once a Unity Behavior graph is wired up and driving the Nemesis. The " +
             "graph then owns the priority order and calls RequestState itself, and the built-in " +
             "C# ladder in NemesisDecision stops running.\n\n" +
             "Left off, that ladder decides — which is what keeps the Nemesis working while the " +
             "graph is being authored. Either way the conditions come from the same predicates " +
             "on NemesisDecision, so the two cannot drift apart on what 'sees the player' means.")]
    [SerializeField] private bool decisionsFromGraph;

    [Header("Capture")]
    [Tooltip("Seconds the Nemesis stays inert in Catch after the player has respawned, before " +
             "warping to a random waypoint and going back to Patrolling. This is the player's " +
             "window to get away from the checkpoint.")]
    [SerializeField] private float captureGracePeriod = 8f;

    [Tooltip("Seconds after leaving Catch during which the Nemesis cannot enter it again.\n\n" +
             "Two jobs: it stops the Nemesis from re-grabbing the player the instant a capture " +
             "resolves, and it breaks the Chasing -> Catch -> Chasing loop for the case where " +
             "Catch bails out because it had no target to capture.")]
    [SerializeField] private float catchCooldown = 2f;

    private bool hasVisualTarget = false;
    private bool hasAudioTarget = false;

    private bool isActive;

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
    /// Ignoring a request for the current state matters: without it, asking for Patrolling every
    /// frame while patrolling would look like a transition to the machine and re-enter the state
    /// continuously.
    /// </summary>
    public void RequestState(ENemesisState key)
    {
        if (CurrentState == null || CurrentState.StateKey.Equals(key)) return;
        CurrentState.NextState = key;
    }

    /// <summary>
    /// The prioritised ladder, read once per frame. Null while a Behavior graph is driving.
    ///
    /// Public so a graph node can call the same predicates rather than reimplementing them.
    /// </summary>
    public NemesisDecision Decision { get; private set; }

    /// <summary>Whether a Unity Behavior graph owns the decision instead of the built-in ladder.
    /// </summary>
    public bool DecisionsFromGraph => decisionsFromGraph;

    /// <summary>The Searching state instance, or null before the machine is built. Reached for by
    /// <see cref="NemesisTelemetry.SearchInterceptPoint"/>, which reports where its cut-off is
    /// aimed — the machine itself never reads it.</summary>
    public NemesisSearchingState SearchingState =>
        States.TryGetValue(ENemesisState.Searching, out BaseState<ENemesisState> state)
            ? state as NemesisSearchingState
            : null;

    private void TickDecision()
    {
        // With a graph driving, it calls RequestState on its own and the ladder must not also
        // vote — two decision layers writing the same channel is the distributed problem all over
        // again, with an extra participant.
        if (decisionsFromGraph || Decision == null) return;

        // NOTHING IS DECIDED WHILE SOMETHING ELSE OWNS THE BODY.
        //
        // NemesisElevatorUser switches the agent off for the whole freight-elevator ride and moves
        // the Transform by hand. Every state used to carry this guard at the top of its own
        // UpdateState, which had the side effect that no transition could happen during a ride —
        // and that side effect was load-bearing. Moving the decision up here without it meant the
        // ladder re-deciding mid-ride: the Nemesis is in the shaft and off the NavMesh, so the
        // route query behind rung 2 fails, "the lift is on the way" goes false, and it drops out
        // of Traversing into a state that cannot act either. The commitment that put it on the
        // lift in the first place evaporates halfway up.
        //
        // Freezing the decision here is also right for the other case that clears this flag — an
        // agent knocked off the NavMesh by a Warp that did not land. Nothing it could decide would
        // be actionable, and NemesisStuckEscape is what resolves that one.
        if (!IsAgentReady) return;

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
    /// Where the Nemesis currently believes the player is: seen first, heard second.
    ///
    /// Deliberately reads <c>HasLastKnownPosition</c> and not <c>HasVisualTarget</c> — a belief
    /// that stopped being refreshed is still a belief, and it is the whole reason a pursuit or a
    /// search is happening at all.
    ///
    /// Lives here rather than in a state because three of them want the same answer (Traversing
    /// to hold its destination, Searching to anchor its cut-off, and the controller's patrol bias
    /// through its own equivalent). Three private copies of the same sight-then-hearing ladder is
    /// how the definition of "belief" quietly drifts apart between them.
    /// </summary>
    public bool TryGetBelief(out Vector3 position)
    {
        position = Vector3.zero;

        if (fieldOfView != null && fieldOfView.HasLastKnownPosition)
        {
            position = fieldOfView.LastKnownPosition;
            return true;
        }

        if (fieldOfListening != null && fieldOfListening.HasLastKnownPosition)
        {
            position = fieldOfListening.LastKnownPosition;
            return true;
        }

        return false;
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
        // Warp and not transform.position: a NavMeshAgent keeps its own internal position and
        // would drag the Nemesis straight back on the next agent update.
        bool moved;

        if (navAgent == null || !navAgent.isActiveAndEnabled)
        {
            transform.position = position;
            moved = true;
        }
        else
        {
            moved = navAgent.Warp(position);
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
        if (!isActive) return;

        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        // Ticked before the FSM, so the frame it expires is already a frame Chasing can catch on.
        if (catchCooldownTimer > 0f) catchCooldownTimer -= Time.deltaTime;

        hasVisualTarget = fieldOfView.HasVisualTarget;
        hasAudioTarget = fieldOfListening.HasAudioTarget;

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
    public void Activate()
    {
        if (isActive) return;
        isActive = true;

        lifecycle.SetDormant(false);
        lifecycle.ApplyMovementTuning();

        // Picks the farthest point outside the player's line of sight and warps there. Done
        // before entering Patrolling so the first patrol cycle starts from the spawn point and
        // not from wherever the prefab was sitting.
        nemesisController?.ChooseSpawnPoint();

        // After the warp, or the first stuck sample would be the pre-spawn position and the
        // Nemesis would read as having teleported "without progress" on the next check.
        stuckEscape.ResetSample();

        CurrentState = States[ENemesisState.Patrolling];
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
