using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Running at where the Nemesis last saw the player.
///
/// It used to decide all three of its own outcomes — hand over to the lift, grab, or give up — and
/// the comments explaining why each test had to come before the next were most of the file. All
/// three were questions about the WORLD, so they are rungs of <see cref="NemesisDecision"/>'s
/// ladder now, in the same order and for the same reasons:
///
///   rung 1  close enough to grab            → Catch
///   rung 2  the route there crosses a lift  → Traversing
///   rung 4  lost sight, grace still running → stay here
///
/// What is left is the running.
/// </summary>
public class NemesisChasingState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    public NemesisChasingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        NextState = StateKey;

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.ChaseSpeed);
    }

    public override void ExitState() { }

    public override void UpdateState()
    {
        // The agent may be switched off on purpose: NemesisElevatorUser disables it for the whole
        // freight elevator ride. Without this, every frame of that ride writes destination on a
        // dead agent and floods the console with errors.
        if (!nemesisStateManager.IsAgentReady) return;

        FieldOfView view = nemesisStateManager.FieldOfView;
        if (view == null || !view.HasLastKnownPosition) return;

        // Runs at the remembered position, not at the player — and keeps running at it after sight
        // is lost, for as long as rung 4 keeps the Nemesis in this state. That is what turns
        // breaking line of sight into a few seconds of grace rather than an instant reprieve.
        nemesisStateManager.NavAgent.destination = view.LastKnownPosition;

        // Standing over the player with the capture cooldown still closed, or against the wall at
        // the end of a partial path: either way there is nowhere left to run, and continuing to
        // play a run animation on the spot is the mismatch SetGait exists to prevent. The ladder
        // decides what happens next; this only stops pretending to sprint.
        if (nemesisStateManager.HasArrived)
        {
            nemesisStateManager.NavAgent.velocity = Vector3.zero;
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
        }
        else
        {
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                        nemesisStateManager.NemesisMovement.ChaseSpeed);
        }
    }
}
