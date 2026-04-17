using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private SO_Movement movement;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform orientation;
    [SerializeField] private Transform playerBody;
    private Vector3 moveDir = Vector3.zero;
    private float currentVelocity = 0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        characterController = GetComponent<CharacterController>();
    }

    // Update is called once per frame
    void Update()
    {
        InputUpdate();
        Movement();
    }
    
    private void InputUpdate() 
    {
        orientation.forward = (transform.position - new Vector3(cameraTransform.position.x, transform.position.y, cameraTransform.position.z)).normalized;
        moveDir = orientation.forward * Input.GetAxis("Vertical") + orientation.right * Input.GetAxis("Horizontal");
        moveDir.Normalize();
    }
    private void Movement() 
    {
        if (moveDir != Vector3.zero)
        {
            playerBody.forward = Vector3.Slerp(playerBody.forward, moveDir, Time.deltaTime * movement.RotationSpeed);
            if (currentVelocity < movement.MoveSpeed) currentVelocity += movement.Acceleration * Time.deltaTime;
            characterController.Move(playerBody.forward * currentVelocity * Time.deltaTime);
        }
        
    }
}
