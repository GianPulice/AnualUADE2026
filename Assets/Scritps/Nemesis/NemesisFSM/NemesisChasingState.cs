using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

public class NemesisChasingState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    private float currentTime = 0f;
    private float timeToExit = 0f;

    public NemesisChasingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        timeToExit = nemesisStateManager.NemesisData.VisionLossGracePeriod;
    }

    public override void EnterState()
    {
        //Debug.Log("Nemesis Enter Chasing State");
        NextState = StateKey;
        currentTime = 0;
        nemesisStateManager.AnimController.SetBool("isRunning", true);
        nemesisStateManager.NavAgent.speed = nemesisStateManager.NemesisMovement.ChaseSpeed;
    }

    public override void ExitState()
    {
        //Debug.Log("Nemesis Exit Chasing State");
        nemesisStateManager.AnimController.SetBool("isRunning", false);
    }

    public override NemesisStateManager.ENemesisState GetNextState()
    {
        if (NextState != StateKey) return NextState;
        else return StateKey;
    }

    public override void OnTriggerEnter(Collider other)
    {
        
    }

    public override void OnTriggerExit(Collider other)
    {
        
    }

    public override void OnTriggerStay(Collider other)
    {
        
    }

    /// <summary>Height the capture's line-of-sight ray is fired from. Cast from the pivots, which
    /// sit at floor level, the ray scrapes the ground and always reports occlusion.</summary>
    private const float CatchProbeHeight = 1f;

    public override void UpdateState()
    {
        // The agent may be switched off on purpose: NemesisElevatorUser disables it for the whole
        // freight elevator ride. Without this, every frame of that ride writes destination on a
        // dead agent and floods the console with errors.
        if (!nemesisStateManager.IsAgentReady) return;

        NavMeshAgent agent = nemesisStateManager.NavAgent;

        if (nemesisStateManager.HasVisualTarget)
        {
            agent.destination = nemesisStateManager.FieldOfView.LastKnownPosition;

            // remainingDistance (actual path length) instead of a straight-line check — the
            // capture trigger cannot afford to fire through a floor slab just because the
            // player is a couple of meters straight up/down, nor stay permanently unreachable
            // because the last known position sits slightly above the walkable surface.
            bool hasArrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;
            if (hasArrived)
            {
                // Gated on the cooldown NemesisCatchState opened when it last left Catch. While
                // it is closed the Nemesis just stands over the player instead of transitioning:
                // the alternative was bouncing straight back into Catch on the next frame, which
                // is what turned the "no target to capture" fallback into a warning-per-frame
                // loop. Nothing else is needed to resume — hasArrived stays true, so the frame
                // the window closes this fires on its own.
                if (nemesisStateManager.CanEnterCatch && CanActuallyReachPlayer())
                {
                    agent.ResetPath();
                    agent.velocity = Vector3.zero;
                    nemesisStateManager.AnimController.SetBool("isRunning", false);
                    NextState = NemesisStateManager.ENemesisState.Catch;
                    return;
                }

                // It reached the end of a PARTIAL path: the player is on the other side of a wall,
                // or on a NavMesh island it cannot get to. remainingDistance measures up to the
                // closest reachable point, not up to the player, so it drops to zero against the
                // wall — and that is what used to fire the capture through it. Losing the player
                // and starting to search is the honest reading.
                bool pathIsComplete = agent.pathStatus == NavMeshPathStatus.PathComplete;
                if (!pathIsComplete)
                {
                    NextState = NemesisStateManager.ENemesisState.Searching;
                    return;
                }

                // Complete path but still out of reach (stoppingDistance is larger than
                // catchMaxReach, or the player moved since the last sensor sweep): it stands over
                // them and waits, and hooks on its own the moment they come closer.
                nemesisStateManager.AnimController.SetBool("isRunning", false);
            }
            else nemesisStateManager.AnimController.SetBool("isRunning", true);
        }
        else
        {
            if (currentTime < timeToExit)
            {
                bool hasArrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;
                if (hasArrived)
                {
                    NextState = NemesisStateManager.ENemesisState.Searching;
                }
                currentTime += Time.deltaTime;
            }
            else
            {
                NextState = NemesisStateManager.ENemesisState.Searching;
            }
        }
    }

    /// <summary>
    /// Whether the player is genuinely within arm's reach: close horizontally, on the same floor,
    /// and with no wall in between.
    ///
    /// It is a separate check from the agent's because the agent answers a different question.
    /// remainingDistance measures how far the NavMeshAgent still has to go to reach the end of ITS
    /// path, and when that path is partial the end is the closest reachable point to the player —
    /// right against the wall separating them. There remainingDistance drops to zero with the
    /// player two metres away and a wall in between, which is exactly the grab-through-walls bug.
    /// </summary>
    private bool CanActuallyReachPlayer()
    {
        SO_NemesisData data = nemesisStateManager.NemesisData;
        if (data == null) return true;   // No data to filter with: do not break the flow.

        PlayerStateManager target = nemesisStateManager.FieldOfView.GetCurrentTarget();
        if (target == null) return false;

        Transform self = nemesisStateManager.transform;
        Vector3 toPlayer = target.transform.position - self.position;

        // Between floors: the vertical gap rules the capture out before anything else. A player
        // directly above is a metre and a half away in a straight line and half a storey on foot.
        if (Mathf.Abs(toPlayer.y) > data.CatchMaxVerticalOffset) return false;

        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > data.CatchMaxReach * data.CatchMaxReach) return false;

        if (!data.CatchRequiresLineOfSight) return true;

        FieldOfListening listening = nemesisStateManager.FieldOfListening;
        if (listening == null) return true;   // No way to test it: do not block the capture.

        Vector3 eye = self.position + Vector3.up * CatchProbeHeight;
        Vector3 targetPoint = target.transform.position + Vector3.up * CatchProbeHeight;
        return !listening.IsOccludedByWall(eye, targetPoint);
    }
}
