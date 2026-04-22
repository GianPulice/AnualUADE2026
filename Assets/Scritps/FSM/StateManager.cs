using UnityEngine;
using System;
using System.Collections.Generic;

public abstract class StateManager<EState> : MonoBehaviour where EState : Enum
{
    protected Dictionary<EState,BaseState<EState>> States = new Dictionary<EState, BaseState<EState>>();
    protected BaseState<EState> CurrentState;
    protected bool IsTransitioningState = false;

    public virtual void Start()
    {
        CurrentState.EnterState();
    }
    public void Update()
    {
        EState nextStateKey = CurrentState.GetNextState();
        if(!IsTransitioningState && nextStateKey.Equals(CurrentState.Statekey))
        {
            CurrentState.UpdateState();
        }
        else if (!IsTransitioningState)
        {
            TransitionToState(nextStateKey);
        }
    }
    public void TransitionToState(EState stateKey) 
    {
        IsTransitioningState = true;
        CurrentState.ExitState();
        CurrentState = States[stateKey];
        CurrentState.EnterState();
        IsTransitioningState = false;
    }
    private void OnTriggerEnter(Collider other)
    {
        CurrentState.OnTriggerEnter(other);
    }
    private void OnTriggerExit(Collider other) 
    {
        CurrentState.OnTriggerExit(other);
    }
    private void OnTriggerStay(Collider other)
    {
        CurrentState.OnTriggerStay(other);
    }
}
