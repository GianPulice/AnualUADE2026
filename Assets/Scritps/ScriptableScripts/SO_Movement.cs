using UnityEngine;

[CreateAssetMenu(fileName = "SO_Movement", menuName = "Scriptable Objects/SO_Movement")]
public class SO_Movement : ScriptableObject
{
    [SerializeField] private float moveSpeed;
    [SerializeField] private float acceleration;
    [SerializeField] private float rotationSpeed;
    [SerializeField] private float crouchSpeedMultiplier;
    [SerializeField] private float runNoiseRadius;
    [SerializeField] private float footstepNoiseRadius;
    [SerializeField] private float crouchNoiseRadius;

    [Header("Capture penalty")]
    [Tooltip("Seconds taken off the running module timers every time the Nemesis catches you. " +
             "This is the cost of a capture now that it respawns you at a checkpoint instead of " +
             "ending the run. 0 disables the penalty.")]
    [SerializeField, Min(0f)] private float captureModuleTimePenalty = 30f;

    public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
    public float Acceleration { get => acceleration; set => acceleration = value; }
    public float RotationSpeed { get => rotationSpeed; set => rotationSpeed = value; }
    public float CrouchSpeedMultiplier { get => crouchSpeedMultiplier; set => crouchSpeedMultiplier = value; }
    public float RunNoiseRadius { get => runNoiseRadius; set => runNoiseRadius = value; }
    public float FootstepNoiseRadius { get => footstepNoiseRadius; set => footstepNoiseRadius = value; }
    public float CrouchNoiseRadius { get => crouchNoiseRadius; set => crouchNoiseRadius = value; }
    public float CaptureModuleTimePenalty { get => captureModuleTimePenalty; set => captureModuleTimePenalty = value; }
}
