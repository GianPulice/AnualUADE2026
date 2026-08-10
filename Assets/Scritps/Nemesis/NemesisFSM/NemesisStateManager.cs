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

    private Transform playerTransform;
    private bool wasBeingChased;
    private bool isCaptureResolved;
    private bool hasReceivedRespawnNotification;

    private Vector3 lastStuckSamplePosition;
    private float stuckSampleTimer;

    private ENemesisState? lastReportedState;

    public float CaptureGracePeriod { get => captureGracePeriod; }
    public bool HasReceivedRespawnNotification { get => hasReceivedRespawnNotification; }

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
        // Defensive fallback, same spirit as FieldOfView/FieldOfListening self-resolving their
        // SO_NemesisData: NemesisController is required on this GameObject (see its
        // RequireComponent), so GetComponent always succeeds if the inspector reference was
        // left unassigned.
        if (nemesisController == null) nemesisController = GetComponent<NemesisController>();

        InitializeStates();
    }
    public override void Start()
    {
        base.Start();
        lastStuckSamplePosition = transform.position;
    }
    public override void Update()
    {
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        hasVisualTarget = fieldOfView.HasVisualTarget;
        hasAudioTarget = fieldOfListening.HasAudioTarget;

        base.Update();

        EmitProximity();
        EmitChaseTransitions();
        EmitStateTransitions();
        CheckStuck();
    }

    private void OnEnable()
    {
        PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
        PlayerRegistry.OnPlayerUnregistered += HandlePlayerUnregistered;
        CheckpointManager.OnRespawned += HandleCheckpointRespawned;
    }

    private void OnDisable()
    {
        // Unsubscribing goes before the wasBeingChased early-out below, otherwise disabling the
        // Nemesis while it was not chasing would leave the listeners hooked.
        PlayerRegistry.Unsubscribe(HandlePlayerRegistered);
        PlayerRegistry.OnPlayerUnregistered -= HandlePlayerUnregistered;
        CheckpointManager.OnRespawned -= HandleCheckpointRespawned;

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

    private bool IsTryingToMove()
    {
        if (navAgent == null || !navAgent.isActiveAndEnabled || !navAgent.isOnNavMesh) return false;
        if (navAgent.pathPending) return false;

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
