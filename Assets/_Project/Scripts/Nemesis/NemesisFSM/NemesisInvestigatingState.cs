using UnityEngine;
using UnityEngine.AI;

public class NemesisInvestigatingState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    // The timeout that used to be cached here is read by NemesisDecision straight off the SO, so
    // retuning InvestigationTimeOut in Play mode now takes effect immediately instead of on the
    // next scene load — these constructors run once, at Awake.

    public NemesisInvestigatingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        NextState = StateKey;

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Walking,
                                    nemesisStateManager.NemesisMovement.InvestigationSpeed);
    }

    public override void ExitState() { }

    public override void UpdateState()
    {
        // Agent switched off (freight elevator ride): nothing to ask of it this frame. See
        // NemesisStateManager.IsAgentReady.
        if (!nemesisStateManager.IsAgentReady) return;

        // Walking to the noise is the whole job now.
        //
        // Both ways out — it arrived and found nothing, or it ran out of patience — are rung 5 of
        // NemesisDecision's ladder, which reads NemesisStateManager.HasArrived and BeliefAge. The
        // "a fresh noise renews its interest" rule that used to be a currentTime reset here comes
        // out for free there: BeliefAge resets on every detection, so a new noise pushes the
        // timeout back without anything having to remember to do it.
        if (nemesisStateManager.HasAudioTarget)
        {
            nemesisStateManager.NavAgent.destination =
                nemesisStateManager.FieldOfListening.LastKnownPosition;
        }
    }
}
