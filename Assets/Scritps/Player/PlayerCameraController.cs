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
        // Cuando hay UI modal abierta o el juego esta en pausa, no leemos input de camara.
        if (cinemachineInputAxisController == null) return;
        bool shouldEnable = !PauseManager.IsGameplayInputBlocked;
        if (cinemachineInputAxisController.enabled != shouldEnable)
            cinemachineInputAxisController.enabled = shouldEnable;

        // Cursor de gameplay: bloqueado + invisible mientras no haya UI modal ni pausa.
        // Cuando SÍ hay UI abierta, el UIStateManager es el dueño del cursor (lo libera),
        // por eso solo lo forzamos cuando el input de gameplay está activo. Hacerlo cada
        // frame además recupera el lock si el SO lo soltó (alt-tab, click fuera de ventana).
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
