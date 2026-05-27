using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Deshabilita el <see cref="CinemachineInputAxisController"/> mientras haya una modal
/// abierta en el <see cref="UIStateManager"/>. Esto evita que la cámara siga rotando con
/// el mouse aunque <c>Time.timeScale = 0</c> (Cinemachine lee Input.GetAxis sin respetar
/// timeScale).
///
/// Colocar este componente en el mismo GameObject que tiene el InputAxisController
/// (el rig de cámara del player).
/// </summary>
[RequireComponent(typeof(CinemachineInputAxisController))]
public class CameraInputBlocker : MonoBehaviour
{
    private CinemachineInputAxisController _inputController;

    private void Awake()
    {
        _inputController = GetComponent<CinemachineInputAxisController>();
    }

    private void OnEnable()
    {
        UIStateManager.OnModalPushed += HandleModalPushed;
        UIStateManager.OnModalPopped += HandleModalPopped;

        // Aplicar estado actual por si una modal ya está abierta cuando este componente
        // se habilita (por ej. carga de escena con el inventario ya open).
        ApplyState();
    }

    private void OnDisable()
    {
        UIStateManager.OnModalPushed -= HandleModalPushed;
        UIStateManager.OnModalPopped -= HandleModalPopped;
    }

    private void HandleModalPushed(IModalUI _) => ApplyState();
    private void HandleModalPopped(IModalUI _) => ApplyState();

    private void ApplyState()
    {
        if (_inputController == null) return;

        bool modalOpen = UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen;
        _inputController.enabled = !modalOpen;
    }
}
