using UnityEngine;

public class NemesisStateManager : StateManager<NemesisStateManager.ENemesisState>
{
    [SerializeField] private Transform selfTransform;
    [SerializeField] private FieldOfView fieldOfView;

    private bool hasTarget = false;

    public Transform SelfTransform { get => selfTransform; set => selfTransform = value; }
    public FieldOfView FieldOfView { get => fieldOfView; set => fieldOfView = value; }
    public bool HasTarget { get => hasTarget;}

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
        States.Add(ENemesisState.Searching, new NemesisSearchingState(ENemesisState.Searching));
        States.Add(ENemesisState.Investigating, new NemesisInvestigatingState(ENemesisState.Investigating));
        CurrentState = States[ENemesisState.Patrolling];
    }

}
