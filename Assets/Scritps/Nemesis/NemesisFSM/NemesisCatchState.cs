using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Estado terminal: el Nemesis alcanzo al jugador.
///
/// Deshabilita al jugador, encara a los dos y arranca la cuenta regresiva que termina
/// en la pantalla de derrota. No transiciona a ningun otro estado — la partida termina
/// aca y la escena se recarga desde la UI.
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
            Debug.LogWarning("[NemesisCatchState] Se entro a Catch sin target — no hay a quien capturar.");
            return;
        }

        player.OnCaptured();
        FaceEachOther();
        nemesisStateManager.AnimController.SetBool("isCatching", true);

        // Espera la animacion y despues abre la pantalla de derrota.
        nemesisStateManager.ReportCaptureLoss().Forget();
    }

    /// <summary>
    /// Encara al Nemesis y al jugador entre si, solo en yaw.
    /// Transform.LookAt rota en los 3 ejes, asi que con desnivel entre los dos los
    /// inclinaba hacia adelante o atras.
    /// </summary>
    private void FaceEachOther()
    {
        Transform nemesis = nemesisStateManager.transform;
        Transform playerTransform = player.transform;

        Vector3 toPlayer = playerTransform.position - nemesis.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude <= 0.0001f) return;   // Uno encima del otro: no hay direccion util.

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
