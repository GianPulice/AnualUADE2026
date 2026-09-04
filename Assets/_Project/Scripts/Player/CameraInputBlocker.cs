using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Disables the <see cref="CinemachineInputAxisController"/> while there is a modal open in
/// the <see cref="UIStateManager"/>. This prevents the camera from still rotating with the
/// mouse even though <c>Time.timeScale = 0</c> (Cinemachine reads Input.GetAxis without
/// respecting timeScale).
///
/// Place this component on the same GameObject that has the InputAxisController
/// (the player's camera rig).
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

        // Apply the current state in case a modal is already open when this component
        // is enabled (e.g. a scene load with the inventory already open).
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
