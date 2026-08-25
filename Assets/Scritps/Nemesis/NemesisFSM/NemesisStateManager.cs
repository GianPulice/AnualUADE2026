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
    [SerializeField] private Transform selfTransform;
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

    public Transform SelfTransform { get => selfTransform; set => selfTransform = value; }
    public FieldOfView FieldOfView { get => fieldOfView; }
    public FieldOfListening FieldOfListening {get => fieldOfListening; }
    public SO_NemesisData NemesisData { get => nemesisData; }
    public SO_NemesisMovement NemesisMovement { get => nemesisMovement; set => nemesisMovement = value; }
    public NavMeshAgent NavAgent { get => navAgent; set => navAgent = value; }
    public NemesisController NemesisController { get => nemesisController; set => nemesisController = value; }
    public Animator AnimController { get => animController; set => animController = value; }
    public bool HasVisualTarget { get => hasVisualTarget;}
    public bool HasAudioTarget { get => hasAudioTarget;}

    /// <summary>The player, or null when none is registered. Read by the sibling components, which
    /// all need it and none of which should be subscribing to PlayerRegistry separately.</summary>
    public Transform PlayerTransform => playerTransform;

    /// <summary>The state the FSM is in, or null before it has started. Nullable so a caller
    /// cannot mistake "not started yet" for Patrolling, which is enum value 0.</summary>
    public ENemesisState? CurrentStateKey => CurrentState != null ? CurrentState.StateKey : (ENemesisState?)null;

    /// <summary>
    /// Whether the agent can be asked for anything without Unity logging an error.
    ///
    /// isActiveAndEnabled goes first and short-circuits the and: querying isOnNavMesh on a
    /// disabled agent logs on its own. And disabled is a normal state here, not an anomaly —
    /// NemesisElevatorUser switches it off for the whole freight elevator ride, because the
    /// NavMesh does not travel with the platform.
    /// </summary>
    public bool IsAgentReady => navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh;

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
        if (selfTransform == null) selfTransform = transform;

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

        // Before the FSM tick, not after: proximity owes nothing to the current state, and below
        // base.Update() anything a state threw took it down too, every frame. See
        // NemesisTelemetry.TickProximity.
        telemetry.TickProximity();

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
