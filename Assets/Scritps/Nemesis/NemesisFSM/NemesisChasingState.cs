using Unity.Mathematics;
using UnityEngine;

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

    public override void UpdateState()
    {
        if (nemesisStateManager.HasTarget) 
        {
            nemesisStateManager.NavAgent.destination = nemesisStateManager.FieldOfView.LastKnownPosition; 
            float tempDistance = Vector3.Distance(nemesisStateManager.transform.position, nemesisStateManager.NavAgent.destination);
            if (tempDistance < nemesisStateManager.NavAgent.stoppingDistance)
            {
                nemesisStateManager.NavAgent.ResetPath();
                nemesisStateManager.NavAgent.velocity = Vector3.zero;
                Debug.Log("Ya te Caché");
                nemesisStateManager.AnimController.SetBool("isRunning", false);
            }
            else nemesisStateManager.AnimController.SetBool("isRunning", true);
        }
        else
        {
            if (currentTime < timeToExit)
            {
                float tempDistance = Vector3.Distance(nemesisStateManager.transform.position, nemesisStateManager.NavAgent.destination);
                if (tempDistance < nemesisStateManager.NavAgent.stoppingDistance)
                {
                    nemesisStateManager.AnimController.SetBool("isRunning", false);
                }
                currentTime += Time.deltaTime;
            }
            else 
            {
                NextState = NemesisStateManager.ENemesisState.Searching;
            }
        }
    }
}
