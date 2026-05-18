using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NemesisStateManager : StateManager<NemesisStateManager.ENemesisState>
{
    [SerializeField] private Transform selfTransform;
    [SerializeField] private FieldOfView fieldOfView;
    [SerializeField] private SO_NemesisData nemesisData;
    [SerializeField] private Vector3 targetPosition;
    [SerializeField] private NavMeshAgent navAgent;
    [SerializeField] private List<Transform> wayPoints = new List<Transform>();

    private bool hasTarget = false;

    public Transform SelfTransform { get => selfTransform; set => selfTransform = value; }
    public FieldOfView FieldOfView { get => fieldOfView; set => fieldOfView = value; }
    public bool HasTarget { get => hasTarget;}
    public SO_NemesisData NemesisData { get => nemesisData;}
    public Vector3 TargetPosition { get => targetPosition; set => targetPosition = value; }
    public NavMeshAgent NavAgent { get => navAgent; set => navAgent = value; }
    public List<Transform> WayPoints { get => wayPoints; set => wayPoints = value; }

    public enum ENemesisState 
    {
        Patrolling,
        Investigating,
        Chasing,
        Searching,
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

        hasTarget = fieldOfView.HasTarget;
        
        base.Update();
    }
    private void InitializeStates()
    {
        States.Add(ENemesisState.Patrolling, new NemesisPatrolState(ENemesisState.Patrolling, this));
        States.Add(ENemesisState.Chasing, new NemesisChasingState(ENemesisState.Chasing,this));
        States.Add(ENemesisState.Searching, new NemesisSearchingState(ENemesisState.Searching,this));
        States.Add(ENemesisState.Investigating, new NemesisInvestigatingState(ENemesisState.Investigating));
        CurrentState = States[ENemesisState.Patrolling];
    }

}
