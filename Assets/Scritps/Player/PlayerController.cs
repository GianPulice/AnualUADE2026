using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private SO_Movement movement;
    [SerializeField] private CharacterController charController;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform orientation;
    [SerializeField] private Transform playerBody;
    private Vector3 moveDir = Vector3.zero;
    private bool canMove = true;
    private bool isCrouch = false;
    private float currentVelocity = 0f;
    private float speedMultiplier = 1f;

    public float CurrentVelocity { get => currentVelocity; set => currentVelocity = value; }


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        charController = GetComponent<CharacterController>();
    }

    // Update is called once per frame
    void Update()
    {
        InputUpdate();
        Movement();
    }
    
    private void InputUpdate() 
    {
        // Conseguir forward en función de a donde mira la cámara
        orientation.forward = (transform.position - new Vector3(cameraTransform.position.x, transform.position.y, cameraTransform.position.z)).normalized;
        
        // Conseguir vector dirección de movimiento segun los inputs
        moveDir = orientation.forward * Input.GetAxis("Vertical") + orientation.right * Input.GetAxis("Horizontal");
        moveDir.Normalize();
        
        //Mecánica Agacharse
        if (Input.GetButtonDown("Crouch")) 
        {
            if (!isCrouch) 
            {
                isCrouch = true;
                speedMultiplier = movement.CrouchSpeedMultiplier;
                charController.height = 0.9f;
                charController.center = new Vector3(0, 0.45f, 0);
            }
            else 
            {
                isCrouch = false;
                speedMultiplier = 1;
                charController.height = 1.8f;
                charController.center = new Vector3(0, 0.9f, 0);
            }
        }

        //Mecánica Correr
        if (Input.GetButtonDown("Sprint") && !isCrouch) speedMultiplier = 1.5f;
        else if (Input.GetButtonUp("Sprint") && !isCrouch) speedMultiplier = 1f;
    }
    private void Movement() 
    {
        if (canMove)
        {
            if (moveDir != Vector3.zero)
            {
                playerBody.forward = Vector3.Slerp(playerBody.forward, moveDir, Time.deltaTime * movement.RotationSpeed);
                if (currentVelocity < movement.MoveSpeed * speedMultiplier)
                {
                    currentVelocity += movement.Acceleration * Time.deltaTime;
                }
                else currentVelocity = movement.MoveSpeed * speedMultiplier;
                charController.Move(playerBody.forward * currentVelocity * Time.deltaTime);
                //Debug.Log(currentVelocity);
            }
            else currentVelocity = 0;
        }
    }
}
