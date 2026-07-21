using UnityEngine;

public class NemesisStateManager : StateManager<NemesisStateManager.ENemesisState>
{
    public enum ENemesisState 
    {
        Patrolling,
        Investigating,
        Chasing,
        Searching,
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public override void Start()
    {
        base.Start();
    }

    // Update is called once per frame
    public override void Update()
    {
        base.Update();
    }
}
