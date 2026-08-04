using UnityEngine;

[CreateAssetMenu(fileName = "SO_Movement", menuName = "Scriptable Objects/SO_Movement")]
public class SO_Movement : ScriptableObject
{
    [SerializeField] private float moveSpeed;
    [SerializeField] private float acceleration;
    [SerializeField] private float rotationSpeed;
    [SerializeField] private float crouchSpeedMultiplier;
    [SerializeField] private float footstepNoiseRadius;
    [SerializeField] private float crouchNoiseRadius;

    public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
    public float Acceleration { get => acceleration; set => acceleration = value; }
    public float RotationSpeed { get => rotationSpeed; set => rotationSpeed = value; }
    public float CrouchSpeedMultiplier { get => crouchSpeedMultiplier; set => crouchSpeedMultiplier = value; }
    public float FootstepNoiseRadius { get => footstepNoiseRadius; set => footstepNoiseRadius = value; }
    public float CrouchNoiseRadius { get => crouchNoiseRadius; set => crouchNoiseRadius = value; }
}
