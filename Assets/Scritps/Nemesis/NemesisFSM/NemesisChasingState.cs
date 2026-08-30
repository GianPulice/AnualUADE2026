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

    /// <summary>Where to run, and why. See <see cref="NemesisPursuit"/> - the whole of what this
    /// state used to express as one assignment to destination.</summary>
    private readonly NemesisPursuit pursuit;

    /// <summary>Read by NemesisGizmos so the prediction and the chosen detour are visible in the
    /// Scene view. Nothing in the FSM reads it.</summary>
    public NemesisPursuit Pursuit => pursuit;

    public NemesisChasingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        pursuit = new NemesisPursuit(stateManager);
    }

    public override void EnterState()
    {
        NextState = StateKey;

        // A fresh decision, not whatever the last chase ended on - which could be a waypoint on
        // the far side of the level, chosen for a belief that has nothing to do with this one.
        pursuit.Reset();

        // The patrol stopping distance is sized for waypoints and is wider than the capture
        // reach, so leaving it in place here halts the agent outside the only range a grab can
        // fire from. See NemesisStateManager.PursuitStoppingDistance.
        nemesisStateManager.SetStoppingDistance(nemesisStateManager.PursuitStoppingDistance);

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.ChaseSpeed);
    }

    /// <summary>
    /// Hands the agent back its normal stopping distance. Everything else in the FSM — the patrol
    /// waypoint wait, the search sweep advancing, the ladder's arrival test — measures against
    /// that value, so a chase that ended without restoring it would quietly retune all three.
    /// </summary>
    public override void ExitState()
    {
        nemesisStateManager.SetStoppingDistance(nemesisStateManager.DefaultStoppingDistance);
    }

    public override void UpdateState()
    {
        // The agent may be switched off on purpose: NemesisElevatorUser disables it for the whole
        // freight elevator ride. Without this, every frame of that ride writes destination on a
        // dead agent and floods the console with errors.
        if (!nemesisStateManager.IsAgentReady) return;

        // Everything about WHERE to run lives in NemesisPursuit now: this used to be
        // "destination = belief", which is Seek aimed at where the player already was.
        //
        // It still runs on the BELIEF and never on the player's real transform, and that part is
        // load-bearing. Sight and hearing go stale at different rates - break line of sight while
        // still making noise and the visual memory freezes at the doorway you went through while
        // hearing keeps updating - so the pursuit steers by whichever sensor caught you last.
        // Reading FieldOfView directly, which this once did, is what had the Nemesis sprint to a
        // doorway and stand in it while it could plainly hear you leaving.
        if (!pursuit.TryGetDestination(out Vector3 destination)) return;

        // Set every frame: the prediction moves continuously even though the route decision behind
        // it is throttled. Keeps running at the remembered position after both sensors go quiet,
        // for as long as the grace rung keeps the Nemesis in this state - which is what turns
        // breaking line of sight into a few seconds of grace rather than an instant reprieve.
        nemesisStateManager.NavAgent.destination = destination;

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
