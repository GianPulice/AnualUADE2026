using UnityEngine;
using UnityEngine.AI;

public class NemesisSearchingState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    private float timeOut = 0;
    private float currentTime = 0;

    public NemesisSearchingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        timeOut = nemesisStateManager.NemesisData.SearchTimeOut;
    }

    public override void EnterState()
    {
        Debug.Log("Nemesis Enter Searching State");
        NextState = StateKey;
        currentTime = 0;
        nemesisStateManager.NavAgent.speed = nemesisStateManager.NemesisMovement.SearchSpeed;
        nemesisStateManager.AnimController.SetBool("isRunning", true);
    }

    public override void ExitState()
    {
        Debug.Log("Nemesis Exit Searching State");
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

    public override void UpdateState()
    {
        if (nemesisStateManager.HasVisualTarget)
        {
            NextState = NemesisStateManager.ENemesisState.Chasing;
        }
        else
        {
            if (currentTime < timeOut)
            {
                currentTime += Time.deltaTime;
                if (nemesisStateManager.HasAudioTarget)
                {
                    nemesisStateManager.NavAgent.destination = nemesisStateManager.FieldOfListenig.LastKnownPosition;
                }
                float tempDistance = Vector3.Distance(nemesisStateManager.transform.position, nemesisStateManager.NavAgent.destination);
                if (tempDistance < nemesisStateManager.NavAgent.stoppingDistance)
                {
                   nemesisStateManager.NavAgent.destination = GetRandomPointInNavMesh();
                }
            }
            else
            {
                NextState = NemesisStateManager.ENemesisState.Patrolling;
            }
        }
    }
    /// <summary>
    /// A point on the NavMesh near the current destination, to keep sweeping the area.
    ///
    /// Returns the position snapped by SamplePosition and not the raw random point: the raw
    /// one usually falls off the mesh, and setting it as a destination made the agent walk to
    /// the nearest edge instead. The attempts are capped because the original do/while had no
    /// way out — with the agent outside the NavMesh it span forever and hung Unity.
    /// </summary>
    private Vector3 GetRandomPointInNavMesh()
    {
        const int maxAttempts = 30;
        const float range = 5f;
        const float sampleRadius = 1f;

        Vector3 origin = nemesisStateManager.NavAgent.destination;

        Vector3 forward = nemesisStateManager.transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;

        for (int i = 0; i < maxAttempts; i++)
        {
            // Horizontal only: onUnitSphere also varied Y and threw points above and below
            // the floor. The forward bias is kept so it sweeps ahead of where it is looking.
            Vector2 circle = Random.insideUnitCircle;
            Vector3 randomDir = new Vector3(circle.x, 0f, circle.y) + forward;
            Vector3 randomPoint = origin + randomDir * range;

            if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        // Nothing valid nearby: stay put rather than heading for an unreachable point.
        return nemesisStateManager.transform.position;
    }
}
