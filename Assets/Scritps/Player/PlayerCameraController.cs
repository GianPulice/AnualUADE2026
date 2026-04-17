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

    void AplyConfig()
    {
        cinemachineCamera.Lens.FieldOfView = cameraConfig.Fov;
        cinemachineOrbitalFollow.VerticalAxis.Range = new Vector2(-cameraConfig.MaxVerticalAngle, cameraConfig.MaxVerticalAngle);
        Vector3 temp = cameraConfig.ShoulderOffset;
        cinemachineRotationComposer.TargetOffset.Set(temp.x, temp.y, temp.z);
    }
}
