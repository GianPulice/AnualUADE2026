using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Terminal state: the Nemesis reached the player.
///
/// Disables the player, makes both face each other and starts the countdown that ends in
/// the defeat screen. It does not transition to any other state — the run ends here and
/// the scene is reloaded from the UI.
/// </summary>
public class NemesisCatchState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;
    private PlayerStateManager player;
    public NemesisCatchState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    public override void EnterState()
    {
        NextState = StateKey;

        player = nemesisStateManager.FieldOfView.GetCurrentTarget();
        if (player == null)
        {
            Debug.LogWarning("[NemesisCatchState] Entered Catch without a target — there is nobody to capture.");
            return;
        }

        player.OnCaptured();
        FaceEachOther();
        nemesisStateManager.AnimController.SetBool("isCatching", true);

        // Waits for the animation and then opens the defeat screen.
        nemesisStateManager.ReportCaptureLoss().Forget();
    }

    /// <summary>
    /// Makes the Nemesis and the player face each other, yaw only.
    /// Transform.LookAt rotates on all 3 axes, so with a height difference between the two
    /// it tilted them forwards or backwards.
    /// </summary>
    private void FaceEachOther()
    {
        Transform nemesis = nemesisStateManager.transform;
        Transform playerTransform = player.transform;

        Vector3 toPlayer = playerTransform.position - nemesis.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude <= 0.0001f) return;   // One on top of the other: no usable direction.

        nemesis.rotation         = Quaternion.LookRotation(toPlayer);
        playerTransform.rotation = Quaternion.LookRotation(-toPlayer);
    }

    public override void ExitState()
    {
        nemesisStateManager.AnimController.SetBool("isCatching", false);
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

    }
}
