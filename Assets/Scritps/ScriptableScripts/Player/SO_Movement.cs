using UnityEngine;

/// <summary>
/// Authoring data for how the player moves, how loud it is, and how big its collider is in each
/// stance. Read by PlayerStateManager and the movement states.
/// </summary>
[CreateAssetMenu(fileName = "SO_Movement", menuName = "Scriptable Objects/SO_Movement")]
public class SO_Movement : ScriptableObject
{
    [Header("Speed")]
    [Tooltip("Walking speed in metres per second, before any stance multiplier or module " +
             "penalty is applied.")]
    [SerializeField, Min(0f)] private float moveSpeed = 3.5f;

    [Tooltip("How fast the player reaches its target speed, in m/s per second. Higher = snappier " +
             "starts, lower = more weight.")]
    [SerializeField, Min(0f)] private float acceleration = 8f;

    [Tooltip("How fast the body turns to face the input direction. Higher = turns on the spot.")]
    [SerializeField, Min(0f)] private float rotationSpeed = 10f;

    [Tooltip("Speed while sprinting, as a multiple of Move Speed. 1.5 = 50% faster than " + "walking. The chest module penalty (M2) eats into this at runtime. Below ~1.05 the " +
             "camera sprint pull-back stops triggering, which is why the slider stops there.")]
    [SerializeField, Range(1.05f, 3f)] private float sprintSpeedMultiplier = 1.5f;

    [Header("Crouch")]
    [Tooltip("Speed while crouching, as a fraction of Move Speed. 0.45 = 45% of walking speed.")]
    [SerializeField, Range(0.1f, 1f)] private float crouchSpeedMultiplier = 0.45f;

    [Tooltip("Height of the player's capsule while standing, in metres. The collider centre is " +
             "always half of this, so the capsule stays sitting on the floor.")]
    [SerializeField, Range(1f, 2.5f)] private float standingHeight = 1.8f;

    [Tooltip("Height of the player's capsule while crouching, in metres. This is what has to fit " +
             "under the containers. Keep it below Standing Height. " +
             "How far the camera follows the player down is a separate value: " +
             "Crouch Pivot Drop, on SO_PlayerCamera.")]
    [SerializeField, Range(0.4f, 2f)] private float crouchHeight = 0.9f;

    [Header("Noise radius")]
    [Tooltip("Radius in metres of the noise the player makes while sprinting. This is what the " +
             "Nemesis' FieldOfListening picks up.")]
    [SerializeField, Min(0f)] private float runNoiseRadius = 6f;

    [Tooltip("Radius in metres of the noise the player makes while walking.")]
    [SerializeField, Min(0f)] private float footstepNoiseRadius = 2f;

    [Tooltip("Radius in metres of the noise the player makes while crouch-walking. Smallest of " +
             "the three, which is the point of crouching.")]
    [SerializeField, Min(0f)] private float crouchNoiseRadius = 1f;

    [Header("Capture penalty")]
    [Tooltip("Seconds taken off the running module timers every time the Nemesis catches you. " +
             "This is the cost of a capture now that it respawns you at a checkpoint instead of " +
             "ending the run. 0 disables the penalty.")]
    [SerializeField, Min(0f)] private float captureModuleTimePenalty = 30f;

    public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
    public float Acceleration { get => acceleration; set => acceleration = value; }
    public float RotationSpeed { get => rotationSpeed; set => rotationSpeed = value; }
    public float SprintSpeedMultiplier { get => sprintSpeedMultiplier; set => sprintSpeedMultiplier = value; }
    public float CrouchSpeedMultiplier { get => crouchSpeedMultiplier; set => crouchSpeedMultiplier = value; }
    public float StandingHeight { get => standingHeight; set => standingHeight = value; }
    public float CrouchHeight { get => crouchHeight; set => crouchHeight = value; }
    public float RunNoiseRadius { get => runNoiseRadius; set => runNoiseRadius = value; }
    public float FootstepNoiseRadius { get => footstepNoiseRadius; set => footstepNoiseRadius = value; }
    public float CrouchNoiseRadius { get => crouchNoiseRadius; set => crouchNoiseRadius = value; }
    public float CaptureModuleTimePenalty { get => captureModuleTimePenalty; set => captureModuleTimePenalty = value; }
}
