using UnityEngine;
using System;
using System.Collections.Generic;

public abstract class StateManager<EState> : MonoBehaviour where EState : Enum
{
    /// <summary>
    /// How many times a single transition may chain into another before it is treated as two
    /// states rejecting each other. See <see cref="TransitionToState(EState, int)"/>.
    /// </summary>
    private const int MaxChainedTransitions = 4;

    protected Dictionary<EState,BaseState<EState>> States = new Dictionary<EState, BaseState<EState>>();
    protected BaseState<EState> CurrentState;
    protected bool IsTransitioningState = false;

    /// <summary><see cref="Time.time"/> at which the machine entered its current state.</summary>
    protected float StateEnteredAt { get; private set; }

    /// <summary>
    /// Seconds the machine has spent in its current state.
    ///
    /// Exposed because a decision layer sitting above the machine is stateless by design: every
    /// dwell floor and every timeout it wants to express — "stay in Searching at least half a
    /// second", "keep chasing for two seconds after losing sight" — has to be measured against
    /// something, and this is it. Without it those live as private counters inside the states,
    /// which is exactly where they cannot be reasoned about from outside.
    /// </summary>
    public float TimeInCurrentState => Time.time - StateEnteredAt;

    /// <summary>
    /// The state the machine was in before the current one — already set when the current state's
    /// EnterState runs, so a state can tell how it was reached. <see cref="HasPreviousState"/> is
    /// false until the first transition.
    ///
    /// For states whose behaviour legitimately depends on where they came from without the decision
    /// layer having to pass it along: Searching entered from Investigating is the short search that
    /// follows an empty investigation of the player's own noise (Nemesis plan D26), not a full one.
    /// </summary>
    public EState PreviousStateKey { get; private set; }

    /// <summary>Whether <see cref="PreviousStateKey"/> means anything yet.</summary>
    public bool HasPreviousState { get; private set; }

    /// <summary>
    /// Stamps the clock <see cref="TimeInCurrentState"/> measures against.
    ///
    /// Public-to-subclasses because entering the first state is not always Start's job: a
    /// puzzle-gated Nemesis stays dormant through Start and enters Patrolling from Activate()
    /// instead, which has to reach the protected States dictionary and so cannot live anywhere
    /// else. Without stamping it there, StateEnteredAt keeps its default of 0 and every dwell
    /// floor and timeout above the machine reads the entire session as time already spent in
    /// that first state — so a commitment that is supposed to last half a second is over before
    /// it starts.
    /// </summary>
    protected void MarkStateEntered() => StateEnteredAt = Time.time;

    public virtual void Start()
    {
        MarkStateEntered();
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

    public void TransitionToState(EState stateKey) => TransitionToState(stateKey, 0);

    /// <param name="depth">How many transitions have already chained off the original one. Only
    /// ever non-zero on the recursive call below.</param>
    private void TransitionToState(EState stateKey, int depth)
    {
        // A value can exist in the enum without ever being registered in InitializeStates()
        // (this happened with Catch, and with EPlayerState.InDanger before it was removed).
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
        PreviousStateKey = CurrentState.StateKey;
        HasPreviousState = true;
        CurrentState = nextState;
        MarkStateEntered();
        CurrentState.EnterState();
        IsTransitioningState = false;

        // A state may REJECT the entry it was just handed: NemesisCatchState does exactly that
        // when the target it was entered for turns out to be gone. Letting the rejected state
        // live out the frame is not free — one frame in Catch is one frame of red vignette and
        // one crossfade into the capture loop, and the player sees both.
        EState immediate = CurrentState.GetNextState();
        if (immediate.Equals(CurrentState.StateKey)) return;

        // Capped rather than trusted. Two states that each reject in favour of the other would
        // otherwise recurse until the stack gives out, and a StackOverflowException reports the
        // symptom nowhere near the pair that caused it — it cannot even be caught. Failing loudly
        // and standing still is a far better outcome than failing silently and taking the process.
        if (depth >= MaxChainedTransitions)
        {
            Debug.LogError($"[{GetType().Name}] '{CurrentState.StateKey}' still wants '{immediate}' " +
                           $"after {MaxChainedTransitions} chained transitions — two states are " +
                           "rejecting each other on entry. Staying put.");
            return;
        }

        TransitionToState(immediate, depth + 1);
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
