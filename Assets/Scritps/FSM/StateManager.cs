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
    public virtual void Update()
    {
        EState nextStateKey = CurrentState.GetNextState();
        if(!IsTransitioningState && nextStateKey.Equals(CurrentState.StateKey))
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
        // A value can exist in the enum without ever being registered in InitializeStates()
        // (this already happened with Catch, and EPlayerState.InDanger is still in that spot).
        // Indexing States[] directly would throw KeyNotFoundException every frame; better to
        // report it once and stay put than to take the whole FSM down.
        if (!States.TryGetValue(stateKey, out BaseState<EState> nextState))
        {
            Debug.LogError($"[{GetType().Name}] State '{stateKey}' is declared in the enum but was " +
                           $"never registered in InitializeStates(). Staying in '{CurrentState.StateKey}'.");
            return;
        }

        IsTransitioningState = true;
        CurrentState.ExitState();
        CurrentState = nextState;
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
