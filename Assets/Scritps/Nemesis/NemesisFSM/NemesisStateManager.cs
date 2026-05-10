using UnityEngine;

public class NemesisStateManager : StateManager<NemesisStateManager.ENemesisState>
{
    public enum ENemesisState 
    {
        Patrolling,
        Investigating,
        Chasing,
        Searching,
    }

    private void Awake()
    {
        InitializeStates();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public override void Start()
    {
        base.Start();
    }

    // Update is called once per frame
    public override void Update()
    {
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        base.Update();
    }
    private void InitializeStates()
    {
        States.Add(ENemesisState.Patrolling, new NemesisPatrolState(ENemesisState.Patrolling));
        States.Add(ENemesisState.Searching, new NemesisSearchingState(ENemesisState.Searching));
        States.Add(ENemesisState.Investigating, new NemesisInvestigatingState(ENemesisState.Investigating));
        States.Add(ENemesisState.Chasing, new NemesisChasingState(ENemesisState.Chasing));
        CurrentState = States[ENemesisState.Patrolling];
    }
}
