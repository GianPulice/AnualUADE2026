using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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

    [Header("Capture")]
    [Tooltip("Seconds the Nemesis stays inert in Catch after the player has respawned, before " +
             "warping to a random waypoint and going back to Patrolling. This is the player's " +
             "window to get away from the checkpoint.")]
    [SerializeField] private float captureGracePeriod = 8f;

    [Tooltip("Waypoints closer than this to the player are not eligible when repositioning " +
             "after a capture, so the Nemesis does not warp on top of the respawned player.")]
    [SerializeField] private float repositionMinPlayerDistance = 15f;

    [Header("Stuck detection")]
    [Tooltip("How long the Nemesis has to make no progress before it counts as stuck.")]
    [SerializeField] private float stuckCheckInterval = 3f;

    [Tooltip("Distance it has to cover within the interval to count as making progress.")]
    [SerializeField] private float stuckMinDistance = 0.5f;

    private bool hasVisualTarget = false;
    private bool hasAudioTarget = false;

    private bool isActive;
    private List<Renderer> hiddenWhileDormant;

    private Transform playerTransform;
    private bool wasBeingChased;
    private bool isCaptureResolved;
    private bool hasReceivedRespawnNotification;

    private Vector3 lastStuckSamplePosition;
    private float stuckSampleTimer;

    private ENemesisState? lastReportedState;

    public float CaptureGracePeriod { get => captureGracePeriod; }
    public bool HasReceivedRespawnNotification { get => hasReceivedRespawnNotification; }

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

    public enum ENemesisState
    {
        Patrolling,
        Investigating,
        Chasing,
        Searching,
        Catch,
    }

    void Awake()
    {
        ResolveHierarchyReferences();

        if (!ValidateReferences())
        {
            // Disabled rather than left running, same as PlayerStateManager. Every reference below
            // is dereferenced either by this Update each frame or by the states themselves, and
            // InitializeStates() is the worst of them: the four state constructors read
            // NemesisData, so a missing asset throws mid-construction and leaves the States
            // dictionary half filled — after which CurrentState = States[Patrolling] throws
            // KeyNotFoundException and the real cause is nowhere in the log.
            //
            // Note this leaves the Nemesis visible but inert rather than dormant: SetDormant(true)
            // lives in Start(), which Unity never calls on a component disabled during Awake. That
            // is deliberate — a broken object should be easy to find in the scene, and the error
            // above points straight at it.
            enabled = false;
            return;
        }

        InitializeStates();
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

        // includeInactive: the sensors are switched off while the Nemesis is dormant, and the
        // Animator sits on the model root, which a prefab may ship disabled.
        if (fieldOfView == null)      fieldOfView      = GetComponentInChildren<FieldOfView>(true);
        if (fieldOfListening == null) fieldOfListening = GetComponentInChildren<FieldOfListening>(true);
        if (animController == null)   animController   = GetComponentInChildren<Animator>(true);
    }

    /// <summary>
    /// Reports everything still unresolved in one message instead of letting each one surface as
    /// its own NullReferenceException later. Returns false if the Nemesis cannot run.
    ///
    /// This is what lets the four states dereference NemesisData/NemesisMovement/AnimController/
    /// NavAgent without guarding — the contract is established here, before InitializeStates()
    /// constructs any of them.
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
        lastStuckSamplePosition = transform.position;

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
            SetDormant(true);
    }
    public override void Update()
    {
        // Dormant: no senses, no navigation, no proximity vignette. Checked before the pause
        // guard because a paused game and a Nemesis that has not spawned yet are different
        // things, and neither should tick the FSM.
        if (!isActive) return;

        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        hasVisualTarget = fieldOfView.HasVisualTarget;
        hasAudioTarget = fieldOfListening.HasAudioTarget;

        // Emitted before the FSM tick and not after it. Proximity is pure distance and owes
        // nothing to the current state, but it used to sit below base.Update() — so anything a
        // state threw (an unassigned Animator, an agent knocked off the NavMesh by a Warp that
        // did not land) took every line after it down too, every frame. The first thing anyone
        // noticed was the proximity vignette silently never lighting up, which pointed at the UI
        // instead of at the state that was actually failing.
        EmitProximity();

        base.Update();

        EmitChaseTransitions();
        EmitStateTransitions();
        CheckStuck();
    }

    private void OnEnable()
    {
        PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
        PlayerRegistry.OnPlayerUnregistered += HandlePlayerUnregistered;
        CheckpointManager.OnRespawned += HandleCheckpointRespawned;
        PuzzleStateManager.OnPuzzleCompleted += HandleActivationPuzzleCompleted;
    }

    private void OnDisable()
    {
        // Unsubscribing goes before the wasBeingChased early-out below, otherwise disabling the
        // Nemesis while it was not chasing would leave the listeners hooked.
        PlayerRegistry.Unsubscribe(HandlePlayerRegistered);
        PlayerRegistry.OnPlayerUnregistered -= HandlePlayerUnregistered;
        CheckpointManager.OnRespawned -= HandleCheckpointRespawned;
        PuzzleStateManager.OnPuzzleCompleted -= HandleActivationPuzzleCompleted;

        // If the Nemesis is switched off while chasing, the red vignette would stay lit
        // on top of the HUD forever.
        if (!wasBeingChased) return;
        wasBeingChased = false;
        NemesisEvents.ChaseEnded();
    }

    /// <summary>
    /// Raises <see cref="NemesisEvents.StateChanged"/> once per FSM transition.
    ///
    /// Detected here by comparing against the last reported key, the same way
    /// <see cref="EmitChaseTransitions"/> does, rather than by editing the five states'
    /// EnterState methods: one place instead of five, and no change to the shared FSM base.
    /// </summary>
    private void EmitStateTransitions()
    {
        if (CurrentState == null) return;

        ENemesisState key = CurrentState.StateKey;
        if (lastReportedState.HasValue && lastReportedState.Value == key) return;

        lastReportedState = key;
        NemesisEvents.StateChanged(key);
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
    /// </summary>
    public void Activate()
    {
        if (isActive) return;
        isActive = true;

        SetDormant(false);

        // Picks the farthest point outside the player's line of sight and warps there. Done
        // before entering Patrolling so the first patrol cycle starts from the spawn point and
        // not from wherever the prefab was sitting.
        nemesisController?.ChooseSpawnPoint();

        // After the warp, or the first stuck sample would be the pre-spawn position and the
        // Nemesis would read as having teleported "without progress" on the next check.
        lastStuckSamplePosition = transform.position;

        CurrentState = States[ENemesisState.Patrolling];
        CurrentState.EnterState();
    }

    /// <summary>
    /// Turns off everything that would make a dormant Nemesis visible or reactive.
    ///
    /// The GameObject itself deliberately stays active: a deactivated one gets no OnEnable, so it
    /// could not listen for its own activation puzzle and would never wake up.
    /// </summary>
    private void SetDormant(bool dormant)
    {
        if (navAgent != null) navAgent.enabled = !dormant;
        if (fieldOfView != null) fieldOfView.enabled = !dormant;
        if (fieldOfListening != null) fieldOfListening.enabled = !dormant;

        if (dormant) HideRenderers();
        else         RestoreRenderers();
    }

    /// <summary>
    /// Switches off only the renderers that were actually on, and remembers exactly those.
    /// Blanket-enabling every renderer on wake-up would light up anything the prefab left
    /// disabled on purpose (spare meshes, effect emitters, debug visuals).
    /// </summary>
    private void HideRenderers()
    {
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        List<Renderer> hidden = new List<Renderer>(all.Length);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || !all[i].enabled) continue;

            all[i].enabled = false;
            hidden.Add(all[i]);
        }

        hiddenWhileDormant = hidden;
    }

    private void RestoreRenderers()
    {
        if (hiddenWhileDormant == null) return;

        for (int i = 0; i < hiddenWhileDormant.Count; i++)
        {
            if (hiddenWhileDormant[i] != null) hiddenWhileDormant[i].enabled = true;
        }

        hiddenWhileDormant = null;
    }

    /// <summary>
    /// The save system finished loading the checkpoint. This is the notification the spec
    /// requires ("el Sistema de Guardado debe notificar al NemesisController") — the Nemesis
    /// never calls into CheckpointManager itself, it only reacts to this.
    ///
    /// Guarded to Catch: CheckpointManager.OnRespawned fires for any respawn, and this is the
    /// only Nemesis listening today, but nothing ties the event to "this specific capture" —
    /// the guard is what makes that safe.
    /// </summary>
    private void HandleCheckpointRespawned(Checkpoint checkpoint)
    {
        if (CurrentState == null || CurrentState.StateKey != ENemesisState.Catch) return;

        isCaptureResolved = true;
        hasReceivedRespawnNotification = true;
    }

    // ── Player feedback ──────────────────────────────────────────────────────
    //
    // The two HUD vignettes (VignetteChaseView / VignetteProximityView) listen to
    // NemesisEvents. They are raised from here, and not from each state, to keep the
    // "I am being chased" logic in a single place.

    /// <summary>
    /// Intensity of the proximity vignette: 0 out of range, 1 right on top of the player.
    ///
    /// Uses the player's real position rather than FieldOfView.LastKnownPosition because
    /// proximity has to rise even if the Nemesis has never seen you — that is exactly the
    /// warning that it is close by without you knowing.
    /// </summary>
    private void EmitProximity()
    {
        if (playerTransform == null)
        {
            NemesisEvents.ProximityChanged(0f);
            return;
        }

        float radius = nemesisData != null ? nemesisData.ProximityRadius : 0f;
        if (radius <= 0f)
        {
            NemesisEvents.ProximityChanged(0f);
            return;
        }

        float distance = Vector3.Distance(transform.position, playerTransform.position);
        NemesisEvents.ProximityChanged(1f - Mathf.Clamp01(distance / radius));
    }

    /// <summary>
    /// Raises ChaseStarted/ChaseEnded when entering and leaving the "it is hunting you" set.
    ///
    /// Catch is part of the set on purpose: if it were cut when leaving Chasing, the red
    /// vignette would switch off on the very frame it grabs you, which is when it needs to
    /// be showing the most.
    /// </summary>
    private void EmitChaseTransitions()
    {
        // Catch only counts while the capture is unresolved. Once the player has respawned at a
        // checkpoint it is free again, and leaving the red vignette lit for the whole grace
        // period would tell it it is still being hunted when it is not.
        bool isBeingChased = CurrentState != null &&
                             (CurrentState.StateKey == ENemesisState.Chasing ||
                              (CurrentState.StateKey == ENemesisState.Catch && !isCaptureResolved));

        if (isBeingChased == wasBeingChased) return;

        wasBeingChased = isBeingChased;

        if (isBeingChased) NemesisEvents.ChaseStarted();
        else               NemesisEvents.ChaseEnded();
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
    /// Warps the Nemesis to a random waypoint after a capture, preferring ones that are not on
    /// top of the respawned player. Without this it would resume patrolling from the spot where
    /// it caught you, which is right where you reappear if the checkpoint is close.
    /// </summary>
    public void RepositionAtRandomWayPoint()
    {
        IReadOnlyList<Transform> allWaypoints = nemesisController != null
            ? nemesisController.AllUnlockedWaypoints
            : null;

        if (allWaypoints == null || allWaypoints.Count == 0)
        {
            Debug.LogWarning("[NemesisStateManager] No waypoints to reposition to after a capture.", this);
            return;
        }

        List<Transform> candidates = new List<Transform>(allWaypoints.Count);
        foreach (Transform wp in allWaypoints)
        {
            if (wp == null) continue;
            if (playerTransform != null &&
                Vector3.Distance(wp.position, playerTransform.position) < repositionMinPlayerDistance)
                continue;

            candidates.Add(wp);
        }

        // Every waypoint sits near the player (small level, or a checkpoint in the middle of the
        // patrol route): fall back to any of them rather than not moving at all, since staying
        // at the capture point is the worse outcome.
        if (candidates.Count == 0) candidates.AddRange(allWaypoints);

        Transform target = candidates[Random.Range(0, candidates.Count)];
        if (target == null) return;

        // Warp and not transform.position: a NavMeshAgent keeps its own internal position and
        // would drag the Nemesis straight back on the next agent update.
        if (navAgent != null && navAgent.isActiveAndEnabled) navAgent.Warp(target.position);
        else transform.position = target.position;
    }

    // ── Stuck detection ─────────────────────────────────────────────────────
    //
    // Hooked into this Update and not into the states because Patrolling, Investigating,
    // Chasing and Searching all need it and this is the one place they share. Catch is
    // excluded on purpose: standing still is the whole point of that state.

    /// <summary>
    /// Warps the Nemesis out when it has stopped making progress — wedged on geometry, or on a
    /// NavMesh island it cannot path off.
    /// </summary>
    private void CheckStuck()
    {
        // Only counts while it is actually trying to get somewhere. Waiting out
        // PatrolWaypointWaitTime at a waypoint is not being stuck, and testing the agent's path
        // covers that without having to special-case each state's idle timings.
        if (!IsNavigatingState() || !IsTryingToMove())
        {
            stuckSampleTimer = 0f;
            lastStuckSamplePosition = transform.position;
            return;
        }

        stuckSampleTimer += Time.deltaTime;
        if (stuckSampleTimer < stuckCheckInterval) return;

        stuckSampleTimer = 0f;

        Vector3 position = transform.position;
        float travelled = Vector3.Distance(position, lastStuckSamplePosition);
        lastStuckSamplePosition = position;

        if (travelled >= stuckMinDistance) return;

        Debug.LogWarning($"[NemesisStateManager] Stuck: moved {travelled:F2}u in " +
                         $"{stuckCheckInterval}s while pathing. Warping out.", this);
        TeleportToStuckEscapeWayPoint();
    }

    private bool IsNavigatingState()
    {
        if (CurrentState == null) return false;

        ENemesisState key = CurrentState.StateKey;
        return key == ENemesisState.Patrolling ||
               key == ENemesisState.Investigating ||
               key == ENemesisState.Chasing ||
               key == ENemesisState.Searching;
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
        if (navAgent == null || !navAgent.isActiveAndEnabled) return false;

        // Off the NavMesh altogether — a Warp that did not land on one (ChooseSpawnPoint and the
        // escape itself both warp blind), or geometry rebuilt out from under it. It cannot path
        // anywhere and will not recover on its own, so this is the most stuck it can possibly be.
        if (!navAgent.isOnNavMesh) return true;

        if (navAgent.pathPending) return false;

        // A destination it cannot reach: a waypoint placed off the mesh, or one on an island cut
        // off by a closed door. hasPath stays false while the agent re-requests an impossible
        // path and remainingDistance reads Infinity, so the check below read it as "idle at a
        // waypoint". Guarded on the destination being somewhere else, because an agent that has
        // never been given one reports PathInvalid while standing exactly where it belongs.
        if (navAgent.pathStatus != NavMeshPathStatus.PathComplete &&
            Vector3.Distance(transform.position, navAgent.destination) > navAgent.stoppingDistance)
        {
            return true;
        }

        return navAgent.hasPath && navAgent.remainingDistance > navAgent.stoppingDistance;
    }

    /// <summary>
    /// Nearest waypoint the player cannot see, so the Nemesis is not watched teleporting.
    /// </summary>
    private void TeleportToStuckEscapeWayPoint()
    {
        IReadOnlyList<Transform> allWaypoints = nemesisController != null
            ? nemesisController.AllUnlockedWaypoints
            : null;

        if (allWaypoints == null || allWaypoints.Count == 0)
        {
            Debug.LogWarning("[NemesisStateManager] Stuck with no waypoints to escape to.", this);
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

        if (navAgent != null && navAgent.isActiveAndEnabled) navAgent.Warp(best.position);
        else transform.position = best.position;

        lastStuckSamplePosition = best.position;
    }

    private bool IsHiddenFromPlayer(Vector3 point)
    {
        if (playerTransform == null) return true;        // Nobody around to watch it happen.
        if (fieldOfListening == null) return false;      // No way to test: assume it is visible.

        return fieldOfListening.IsOccludedByWall(playerTransform.position, point);
    }

    private void InitializeStates()
    {
        States.Add(ENemesisState.Patrolling, new NemesisPatrolState(ENemesisState.Patrolling, this));
        States.Add(ENemesisState.Chasing, new NemesisChasingState(ENemesisState.Chasing,this));
        States.Add(ENemesisState.Searching, new NemesisSearchingState(ENemesisState.Searching,this));
        States.Add(ENemesisState.Investigating, new NemesisInvestigatingState(ENemesisState.Investigating,this));
        States.Add(ENemesisState.Catch, new NemesisCatchState(ENemesisState.Catch, this));
        CurrentState = States[ENemesisState.Patrolling];
    }

}
