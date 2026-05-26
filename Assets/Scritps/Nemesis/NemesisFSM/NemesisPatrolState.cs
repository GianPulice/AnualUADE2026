using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

public class NemesisPatrolState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;
    private float timeToNextWP = 0f;
    private float currentTime = 0f;
    private int wayPointIndex = 0;

    public NemesisPatrolState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        timeToNextWP = nemesisStateManager.NemesisData.PatrolWaypointWaitTime;
    }

    public override void EnterState()
    {
        //Debug.Log("Nemesis Enter Patrol State");
        NextState = StateKey;
        nemesisStateManager.NavAgent.speed = nemesisStateManager.NemesisMovement.PatrolSpeed;
    }

    public override void ExitState()
    {
        //Debug.Log("Nemesis Exit Patrol State");
        nemesisStateManager.AnimController.SetBool("isWalking", false);
    }

    public override NemesisStateManager.ENemesisState GetNextState()
    {
        if (NextState != StateKey) return NextState;
        else return StateKey;
    }

    public override void OnTriggerEnter(Collider other)
    {
        throw new System.NotImplementedException();
    }

    public override void OnTriggerExit(Collider other)
    {
        throw new System.NotImplementedException();
    }

    public override void OnTriggerStay(Collider other)
    {
        throw new System.NotImplementedException();
    }

    public override void UpdateState()
    {
        if (nemesisStateManager.HasTarget)
        {
            NextState = NemesisStateManager.ENemesisState.Chasing;
        }
        else if (nemesisStateManager.WayPoints.Count > 0) 
        {
            float tempDistance = Vector3.Distance(nemesisStateManager.transform.position, nemesisStateManager.WayPoints[wayPointIndex].position);
            if(tempDistance > nemesisStateManager.NavAgent.stoppingDistance) 
            {
                nemesisStateManager.NavAgent.destination = nemesisStateManager.WayPoints[wayPointIndex].position;
                nemesisStateManager.AnimController.SetBool("isWalking", true);
            }
            else 
            {
                if (currentTime < timeToNextWP) 
                {
                    currentTime += Time.deltaTime;
                    nemesisStateManager.AnimController.SetBool("isWalking", false);
                }
                else
                {
                    currentTime = 0;
                    if (wayPointIndex < nemesisStateManager.WayPoints.Count - 1) wayPointIndex++;
                    else wayPointIndex = 0;
                }
            }
        }
    }
}
