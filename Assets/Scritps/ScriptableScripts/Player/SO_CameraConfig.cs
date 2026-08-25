using UnityEngine;

/// <summary>
/// Authoring data for the player's Cinemachine rig. Read by
/// <see cref="PlayerCameraController"/>, which lives on the FreeLook Camera.
/// </summary>
[CreateAssetMenu(fileName = "SO_CameraConfig", menuName = "Scriptable Objects/SO_CameraConfig")]
public class SO_CameraConfig : ScriptableObject
{
    [Header("Lens")]
    [Tooltip("Field of view in degrees. Pushed onto the CinemachineCamera on Start. " +
             "CameraSprintEffect adds its own boost on top of this while sprinting.")]
    [Range(30f, 120f)]
    [SerializeField] private float fov = 72f;

    [Header("Framing")]
    [Tooltip("Over-the-shoulder offset of the aim point, in metres. X moves the character " +
             "sideways in frame, Y up and down. Feeds the RotationComposer's Target Offset.")]
    [SerializeField] private Vector3 shoulderOffset = new Vector3(0.2f, 0.2f, 0f);

    [Tooltip("How far the camera can be tilted up and down, in degrees, measured from the " +
             "horizon. Applied symmetrically as the orbital rig's vertical range.")]
    [Range(0f, 89f)]
    [SerializeField] private float maxVerticalAngle = 80f;

    [Header("Crouch")]
    [Tooltip("How far the camera pivot drops below standing height while crouching, in metres. " +
             "Higher value = camera closer to the floor. 0 keeps the camera at standing height, " +
             "which is what made the player disappear under the containers. " +
             "Tweakable live in Play mode.")]
    [Range(0f, 1.2f)]
    [SerializeField] private float crouchPivotDrop = 0.6f;

    [Tooltip("Seconds the camera takes to settle after crouching or standing up. " +
             "0 = hard cut, 0.3+ = noticeably floaty.")]
    [Range(0f, 0.6f)]
    [SerializeField] private float crouchPivotDamping = 0.15f;

    [Header("Not read by anything (yet)")]
    [Tooltip("NOT IN USE. The live sensitivity comes from PlayerPrefs through " +
             "CameraSensitivityApplier, which the settings menu writes to. Changing this does " +
             "nothing.")]
    [SerializeField] private float cameraSensitivity = 2f;

    [Tooltip("NOT IN USE. Damping is authored directly on the Cinemachine components of the rig " +
             "(OrbitalFollow's Position/Rotation Damping). Changing this does nothing.")]
    [SerializeField] private float cameraSmoothing = 0.2f;

    public float Fov { get => fov; set => fov = value; }
    public Vector3 ShoulderOffset { get => shoulderOffset; set => shoulderOffset = value; }
    public float MaxVerticalAngle { get => maxVerticalAngle; set => maxVerticalAngle = value; }
    public float CrouchPivotDrop { get => crouchPivotDrop; set => crouchPivotDrop = value; }
    public float CrouchPivotDamping { get => crouchPivotDamping; set => crouchPivotDamping = value; }
    public float CameraSensitivity { get => cameraSensitivity; set => cameraSensitivity = value; }
    public float CameraSmoothing { get => cameraSmoothing; set => cameraSmoothing = value; }
}
