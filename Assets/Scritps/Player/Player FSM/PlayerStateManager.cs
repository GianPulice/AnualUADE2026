using UnityEditor.Animations;
using UnityEngine;

public class PlayerStateManager : StateManager<PlayerStateManager.EPlayerState>
{
    [SerializeField] private SO_Movement movement;
    [SerializeField] private CharacterController charController;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform orientation;
    [SerializeField] private Transform playerBody;
    [SerializeField] private Animator animatorController;
    private Vector3 moveDir = Vector3.zero;
    private Vector3 charGravity = Vector3.zero;
    private bool isCrouch = false;
    private float currentVelocity = 0f;
    private float speedMultiplier = 1f;

    private bool isInteracting = false;
    private bool isHidden = false;
    private bool isInDanger = false;
    private bool isDisabled = false;

    public SO_Movement Movement { get => movement; set => movement = value; }
    public CharacterController CharController { get => charController; set => charController = value; }
    public Transform PlayerBody { get => playerBody; set => playerBody = value; }
    public Vector3 MoveDir { get => moveDir; set => moveDir = value; }
    public Vector3 CharGravity { get => charGravity; set => charGravity = value; }
    public float CurrentVelocity { get => currentVelocity; set => currentVelocity = value; }
    public float SpeedMultiplier { get => speedMultiplier; set => speedMultiplier = value; }
    public bool IsInteracting { get => isInteracting; set => isInteracting = value; }
    public bool IsHidden { get => isHidden; set => isHidden = value; }
    public bool IsInDanger { get => isInDanger; set => isInDanger = value; }
    public bool IsDisabled { get => isDisabled; set => isDisabled = value; }
    public Animator AnimatorController { get => animatorController; set => animatorController = value; }

    public enum EPlayerState 
    {
        Idle,
        Moving,
        Interacting,
        Hidden,
        InDanger,
        Disabled,
    }
    void Awake()
    {
        charController = GetComponent<CharacterController>();
        InitializeStates();
    }
    public override void Start()
    {
        base.Start();
    }
    public override void Update() 
    {
        InputUpdate();
        GravityController();
        base.Update();
    }
    private void InitializeStates() 
    {
        States.Add(EPlayerState.Idle,new PlayerIdleState(EPlayerState.Idle, this));
        States.Add(EPlayerState.Moving, new PlayerMovingState(EPlayerState.Moving, this));
        States.Add(EPlayerState.Interacting, new PlayerInteractingState(EPlayerState.Interacting, this));
        States.Add(EPlayerState.Hidden, new PlayerHiddenState(EPlayerState.Hidden, this));
        States.Add(EPlayerState.Disabled, new PlayerDisabledState(EPlayerState.Disabled, this));
        CurrentState = States[EPlayerState.Idle];
    }
    private void GravityController() 
    {
        if (charController.isGrounded) 
        {
            charGravity.y = -0.5f;
        }
        else 
        {
            charGravity.y = -9.8f;
        }
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
                animatorController.SetBool("isCrouch", true);
                speedMultiplier = movement.CrouchSpeedMultiplier;
                charController.height = 0.9f;
                charController.center = new Vector3(0, 0.45f, 0);
            }
            else
            {
                isCrouch = false;
                animatorController.SetBool("isCrouch", false);
                speedMultiplier = 1;
                charController.height = 1.8f;
                charController.center = new Vector3(0, 0.9f, 0);
            }
        }

        //Mecánica Correr
        if (Input.GetButtonDown("Sprint") && !isCrouch) speedMultiplier = 1.5f;
        else if (Input.GetButtonUp("Sprint") && !isCrouch) speedMultiplier = 1f;

        //Testeo de estado Interacting
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (isInteracting) isInteracting = false;
            else isInteracting = true;
        }

        //Testeo de estado Hidden
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (isHidden) isHidden = false;
            else isHidden = true;
        }

        //Testeo de estado InDanger
        if (Input.GetKeyDown(KeyCode. T))
        {
            if (isInDanger) isInDanger = false;
            else isInDanger = true;
        }

        //Testeo de estado Disabled
        if (Input.GetKeyDown(KeyCode.Y))
        {
            if (isDisabled) isDisabled = false;
            else isDisabled = true;
        }
    }
}
