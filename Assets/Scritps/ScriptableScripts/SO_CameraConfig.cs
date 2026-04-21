using UnityEngine;

[CreateAssetMenu(fileName = "SO_CameraConfig", menuName = "Scriptable Objects/SO_CameraConfig")]
public class SO_CameraConfig : ScriptableObject
{

    [SerializeField] private float cameraSensitivity;
    [SerializeField] private float fov;
    [SerializeField] private float cameraSmoothing;
    [SerializeField] private Vector3 shoulderOffset;
    [SerializeField] private float maxVerticalAngle;

    public float CameraSensitivity { get => cameraSensitivity; set => cameraSensitivity = value; }
    public float Fov { get => fov; set => fov = value; }
    public float CameraSmoothing { get => cameraSmoothing; set => cameraSmoothing = value; }
    public Vector3 ShoulderOffset { get => shoulderOffset; set => shoulderOffset = value; }
    public float MaxVerticalAngle { get => maxVerticalAngle; set => maxVerticalAngle = value; }
}
