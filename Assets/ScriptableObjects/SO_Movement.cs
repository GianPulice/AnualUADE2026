using UnityEngine;

[CreateAssetMenu(fileName = "SO_Movement", menuName = "Scriptable Objects/SO_Movement")]
public class SO_Movement : ScriptableObject
{
    public float moveSpeed;
    public float acceleration;
    public float rotationSpeed;
    public float crouchSpeedMultiplier;
    public float footstepNoiseRadius;
    public float crouchNoiseRadius;
}
