using UnityEngine;
using System;

public abstract class BaseState<EState> where EState : Enum
{
    /// <summary>
    /// Where the machine should go next, or this state's own key to stay put.
    ///
    /// It is the single channel into a transition, and it is written from two very different
    /// places. A state writes it when its own EXECUTION fails — NemesisCatchState does, on
    /// discovering the target it was entered for is gone. A decision layer above the machine
    /// writes it through NemesisStateManager.RequestState when the WORLD says somewhere else is
    /// more appropriate. Both end up here, and the machine does not need to tell them apart.
    /// </summary>
    public EState NextState;

    public BaseState(EState key)
    {
        StateKey = key;
    }
    public EState StateKey { get; private set; }

    public abstract void EnterState();
    public abstract void ExitState();
    public abstract void UpdateState();

    /// <summary>
    /// The state the machine should be in next. Returning this state's own key means "stay".
    ///
    /// Concrete rather than abstract because all twelve states in the project — six Nemesis, six
    /// player — wrote the identical body: an if/else that returns NextState either way. A hook
    /// every implementer fills in the same way is not a hook, it is a default with extra steps.
    ///
    /// Still virtual: a state that overrides this is declaring it knows something the decision
    /// layer above cannot see, which should be rare and conspicuous rather than routine.
    /// </summary>
    public virtual EState GetNextState() => NextState;

    // Trigger routing. Empty rather than abstract: all twelve states implemented all three as
    // no-ops, so requiring every future state to write three empty methods was a tax with no
    // payer. The forwarding in StateManager stays, so a state that genuinely needs a trigger can
    // just override the one it wants.

    public virtual void OnTriggerEnter(Collider other) { }
    public virtual void OnTriggerStay(Collider other) { }
    public virtual void OnTriggerExit(Collider other) { }
}
