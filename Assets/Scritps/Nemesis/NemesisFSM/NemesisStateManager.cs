using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

public class NemesisStateManager : StateManager<NemesisStateManager.ENemesisState>
{
    [SerializeField] private Transform selfTransform;
    [SerializeField] private FieldOfView fieldOfView;
    [SerializeField] private FieldOfListenig fieldOfListening;
    [SerializeField] private SO_NemesisData nemesisData;
    [SerializeField] private SO_NemesisMovement nemesisMovement;
    [SerializeField] private NavMeshAgent navAgent;
    [SerializeField] private List<Transform> wayPoints = new List<Transform>();
    [SerializeField] private Animator animController;

    [Header("Capture")]
    [Tooltip("Seconds between the Nemesis grabbing you and the defeat screen appearing. " +
             "Gives the capture animation time to play before cutting to the UI.")]
    [SerializeField] private float captureDelay = 1.5f;

    [Header("Player feedback (HUD vignettes)")]
    [Tooltip("Tag of the player GameObject. Looked up at runtime because it lives in another scene.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("How many frames between re-searching for the player while it has not shown up yet.")]
    [SerializeField, Min(1)] private int playerSearchEveryNFrames = 30;

    private bool hasVisualTarget = false;
    private bool hasAudioTarget = false;

    private Transform playerTransform;
    private int playerSearchCounter;
    private bool wasBeingChased;

    public Transform SelfTransform { get => selfTransform; set => selfTransform = value; }
    public FieldOfView FieldOfView { get => fieldOfView; }
    public FieldOfListenig FieldOfListenig {get => fieldOfListening; }
    public SO_NemesisData NemesisData { get => nemesisData; }
    public SO_NemesisMovement NemesisMovement { get => nemesisMovement; set => nemesisMovement = value; }
    public NavMeshAgent NavAgent { get => navAgent; set => navAgent = value; }
    public List<Transform> WayPoints { get => wayPoints; set => wayPoints = value; }
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
        InitializeStates();
    }
    public override void Start()
    {
        base.Start();
    }
    public override void Update()
    {
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        hasVisualTarget = fieldOfView.HasVisualTarget;
        hasAudioTarget = fieldOfListening.HasAudioTarget;

        base.Update();

        EmitProximity();
        EmitChaseTransitions();
    }

    private void OnDisable()
    {
        // If the Nemesis is switched off while chasing, the red vignette would stay lit
        // on top of the HUD forever.
        if (!wasBeingChased) return;
        wasBeingChased = false;
        NemesisEvents.ChaseEnded();
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
            TryAcquirePlayer();
            if (playerTransform == null)
            {
                NemesisEvents.ProximityChanged(0f);
                return;
            }
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
        bool isBeingChased = CurrentState != null &&
                             (CurrentState.StateKey == ENemesisState.Chasing ||
                              CurrentState.StateKey == ENemesisState.Catch);

        if (isBeingChased == wasBeingChased) return;

        wasBeingChased = isBeingChased;

        if (isBeingChased) NemesisEvents.ChaseStarted();
        else               NemesisEvents.ChaseEnded();
    }

    private void TryAcquirePlayer()
    {
        // Staggered: searching by tag every frame while the player does not exist is expensive.
        if (playerSearchCounter++ % playerSearchEveryNFrames != 0) return;

        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        if (go != null) playerTransform = go.transform;
    }
    /// <summary>
    /// Waits <see cref="captureDelay"/> and reports the loss. Called by NemesisCatchState.
    ///
    /// It lives here and not in the state because BaseState is not a MonoBehaviour: a
    /// cancellation token tied to the object's lifecycle is needed so the wait does not
    /// keep running if the scene is unloaded midway.
    ///
    /// The wait is unscaled on purpose: the result screen sets timeScale to 0.
    /// </summary>
    public async UniTaskVoid ReportCaptureLoss()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(captureDelay),
                            DelayType.UnscaledDeltaTime,
                            cancellationToken: this.GetCancellationTokenOnDestroy());

        // ModuleManager is the one tracking session time and module count, so the stats come
        // out the same as in the timer-driven game over.
        if (ModuleManager.Exists) ModuleManager.Instance.ReportLoss();
        else GameResultManager.ReportLoss(0f, 0);
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
