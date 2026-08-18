using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class PlayerCameraController : MonoBehaviour
{
    [SerializeField] private SO_CameraConfig cameraConfig;
    private CinemachineCamera cinemachineCamera;
    private CinemachineOrbitalFollow cinemachineOrbitalFollow;
    private CinemachineRotationComposer cinemachineRotationComposer;
    private CinemachineInputAxisController cinemachineInputAxisController;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
        cinemachineOrbitalFollow = GetComponent<CinemachineOrbitalFollow>();
        cinemachineRotationComposer = GetComponent<CinemachineRotationComposer>();
        cinemachineInputAxisController = GetComponent<CinemachineInputAxisController>();
        AplyConfig();
    }

    void Update()
    {
        // When a modal UI is open or the game is paused, we do not read camera input.
        if (cinemachineInputAxisController == null) return;
        bool shouldEnable = !PauseManager.IsGameplayInputBlocked;
        if (cinemachineInputAxisController.enabled != shouldEnable)
            cinemachineInputAxisController.enabled = shouldEnable;

        // Gameplay cursor: locked + invisible while there is no modal UI and no pause.
        // When there IS a UI open, the UIStateManager owns the cursor (it releases it),
        // which is why we only force it while gameplay input is active. Doing it every
        // frame also recovers the lock if the OS dropped it (alt-tab, click outside window).
        if (shouldEnable)
        {
            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible) Cursor.visible = false;
        }
    }

    void AplyConfig()
    {
        cinemachineCamera.Lens.FieldOfView = cameraConfig.Fov;
        cinemachineOrbitalFollow.VerticalAxis.Range = new Vector2(-cameraConfig.MaxVerticalAngle, cameraConfig.MaxVerticalAngle);
        Vector3 temp = cameraConfig.ShoulderOffset;
        cinemachineRotationComposer.TargetOffset.Set(temp.x, temp.y, temp.z);
    }
}
