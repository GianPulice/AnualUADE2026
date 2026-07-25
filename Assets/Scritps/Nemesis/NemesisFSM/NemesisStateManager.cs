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

    [Header("Captura")]
    [Tooltip("Segundos entre que el Nemesis te agarra y aparece la pantalla de derrota. " +
             "Le da tiempo a la animacion de captura antes de cortar a la UI.")]
    [SerializeField] private float captureDelay = 1.5f;

    [Header("Feedback al jugador (vignettes del HUD)")]
    [Tooltip("Tag del GameObject del player. Se busca en runtime porque vive en otra escena.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Cada cuántos frames re-buscar al player si todavía no apareció.")]
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
        // Si el Nemesis se apaga persiguiendo, la vignette roja se quedaria prendida
        // encima del HUD para siempre.
        if (!wasBeingChased) return;
        wasBeingChased = false;
        NemesisEvents.ChaseEnded();
    }

    // ── Feedback al jugador ──────────────────────────────────────────────────
    //
    // Las dos vignettes del HUD (VignetteChaseView / VignetteProximityView) escuchan
    // NemesisEvents. Se emite desde aca, y no desde cada estado, para tener la logica
    // de "me estan persiguiendo" en un solo lugar.

    /// <summary>
    /// Intensidad de la vignette de proximidad: 0 fuera de rango, 1 encima del jugador.
    ///
    /// Usa la posicion real del player y no FieldOfView.LastKnownPosition porque la
    /// proximidad tiene que subir aunque el Nemesis no te haya visto nunca — es
    /// justamente el aviso de que lo tenes cerca sin saberlo.
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
    /// Dispara ChaseStarted/ChaseEnded al entrar y salir del conjunto "te esta cazando".
    ///
    /// Catch entra en el conjunto a proposito: si se cortara al salir de Chasing, la
    /// vignette roja se apagaria justo en el frame en que te agarra, que es cuando mas
    /// tiene que estar.
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
        // Escalonado: buscar por tag todos los frames mientras el player no exista es caro.
        if (playerSearchCounter++ % playerSearchEveryNFrames != 0) return;

        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        if (go != null) playerTransform = go.transform;
    }
    /// <summary>
    /// Espera <see cref="captureDelay"/> y reporta la derrota. Lo llama NemesisCatchState.
    ///
    /// Vive aca y no en el estado porque los BaseState no son MonoBehaviour: hace falta
    /// un token de cancelacion atado al ciclo de vida del objeto para que la espera no
    /// siga corriendo si se descarga la escena en el medio.
    ///
    /// La espera es unscaled a proposito: la pantalla de resultado pone timeScale en 0.
    /// </summary>
    public async UniTaskVoid ReportCaptureLoss()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(captureDelay),
                            DelayType.UnscaledDeltaTime,
                            cancellationToken: this.GetCancellationTokenOnDestroy());

        // El InventoryManagerUI es el que lleva el tiempo de sesion y el conteo de modulos,
        // asi que las stats salgan iguales que en el game over por timers.
        if (InventoryManagerUI.Exists) InventoryManagerUI.Instance.ReportLoss();
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
