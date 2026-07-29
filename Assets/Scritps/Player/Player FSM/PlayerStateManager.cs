using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.UIElements;

public class PlayerStateManager : StateManager<PlayerStateManager.EPlayerState>
{
    // Components
    [SerializeField] private SO_Movement movement;
    [SerializeField] private Rigidbody rigBody;
    [SerializeField] private CapsuleCollider capsuleColl;
    [SerializeField] private BoxCollider boxColl;
    [SerializeField] private SphereCollider audioEmitingZone;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform orientation;
    [SerializeField] private Transform playerBody;
    [SerializeField] private Animator animController;

    public SO_Movement Movement { get => movement; set => movement = value; }
    public Rigidbody RigBody { get => rigBody; set => rigBody = value; }
    public CapsuleCollider CapsuleColl { get => capsuleColl; set => capsuleColl = value; }
    public BoxCollider BoxColl { get => boxColl; set => boxColl = value; }
    public SphereCollider AudioEmitingZone { get => audioEmitingZone; set => audioEmitingZone = value; }
    public Transform PlayerBody { get => playerBody; set => playerBody = value; }
    public Animator AnimController { get => animController; set => animController = value; }

    // Movement Variables
    [SerializeField] private LayerMask groundLeyerMask;
    [SerializeField] private float groundAngleLimit;
    private Vector3 inputDir = Vector3.zero;
    private Vector3 moveDir = Vector3.zero;
    private bool isGrounded = false;
    private float currentVelocity = 0f;
    private float speedMultiplier = 1f;
    private Vector3 nextPosition;
    private Vector3 nextDirection;

    public Vector3 InputDir { get => inputDir; set => inputDir = value; }
    public Vector3 MoveDir { get => moveDir; set => moveDir = value; }
    public bool IsGrounded { get => isGrounded; set => isGrounded = value; }
    public float CurrentVelocity { get => currentVelocity; set => currentVelocity = value; }
    public float SpeedMultiplier { get => speedMultiplier; set => speedMultiplier = value; }
    public Vector3 NextPosition { get => nextPosition; set => nextPosition = value; }
    public Vector3 NextDirection { get => nextDirection; set => nextDirection = value; }

    // State booleans
    [SerializeField] private bool isInteracting = false;
    private bool isCrouch = false;
    private bool isHidden = false;
    private bool isInDanger = false;
    private bool isDisabled = false;

    public bool IsInteracting { get => isInteracting; set => isInteracting = value; }
    public bool IsCrouch { get => isCrouch; set => isCrouch = value; }
    public bool IsHidden { get => isHidden; set => isHidden = value; }
    public bool IsInDanger { get => isInDanger; set => isInDanger = value; }
    public bool IsDisabled { get => isDisabled; set => isDisabled = value; }

    // Player Module Booleans
    private bool legsModuleDamage = false;
    private bool chestModuleDamage = false;
    private bool headModuleDamage = false;
    private int modulesDamagedCount = 0;

    public bool LegsModuleDamage { get => legsModuleDamage; set => legsModuleDamage = value; }
    public bool ChestModuleDamage { get => chestModuleDamage; set => chestModuleDamage = value; }
    public bool HeadModuleDamage { get => headModuleDamage; set => headModuleDamage = value; }

    public enum EPlayerState
    {
        Idle,
        Moving,
        Crouch,
        Interacting,
        Hidden,
        InDanger,
        Disabled,
    }
    void Awake()
    {
        rigBody = GetComponent<Rigidbody>();
        InitializeStates();
    }
    public override void Start()
    {
        base.Start();
    }
    public override void Update()
    {
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        InputUpdate();
        CheckGround();
        base.Update();
    }
    private void InitializeStates()
    {
        States.Add(EPlayerState.Idle, new PlayerIdleState(EPlayerState.Idle, this));
        States.Add(EPlayerState.Moving, new PlayerMovingState(EPlayerState.Moving, this));
        States.Add(EPlayerState.Crouch, new PlayerCrouchState(EPlayerState.Crouch, this));
        States.Add(EPlayerState.Interacting, new PlayerBoxInteractingState(EPlayerState.Interacting, this));
        States.Add(EPlayerState.Hidden, new PlayerHiddenState(EPlayerState.Hidden, this));
        States.Add(EPlayerState.Disabled, new PlayerDisabledState(EPlayerState.Disabled, this));
        CurrentState = States[EPlayerState.Idle];
    }
    private void InputUpdate()
    {
        // Get the forward vector based on where the camera is looking
        orientation.forward = (transform.position - new Vector3(cameraTransform.position.x, transform.position.y, cameraTransform.position.z)).normalized;

        // Build the movement direction vector from the inputs
        inputDir = orientation.forward * Input.GetAxis("Vertical") + orientation.right * Input.GetAxis("Horizontal");
        inputDir.Normalize();

        // Crouch mechanic
        if (Input.GetButtonDown("Crouch"))
        {
            if (!isCrouch)
            {
                isCrouch = true;
            }
            else
            {
                isCrouch = false;
            }
        }

        // Hidden state testing
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (isHidden) isHidden = false;
            else isHidden = true;
        }

        // InDanger state testing
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (isInDanger) isInDanger = false;
            else isInDanger = true;
        }

        // Disabled state testing
        if (Input.GetKeyDown(KeyCode.Y))
        {
            if (isDisabled) isDisabled = false;
            else isDisabled = true;
        }
    }
    private void CheckGround()
    {
        if (Physics.CheckSphere(transform.position, capsuleColl.radius, groundLeyerMask))
        {
            Physics.Raycast(transform.position + Vector3.up,Vector3.down,out RaycastHit hitRay);
            float groundAngle = Vector3.Angle(hitRay.normal, Vector3.up);
            //Debug.Log(groundAngle);
            if (groundAngle < groundAngleLimit)
            {
                isGrounded = true;
                if(groundAngle > 1) rigBody.useGravity = false;
                else rigBody.useGravity = true;
                moveDir = Vector3.ProjectOnPlane(inputDir, hitRay.normal);
            }
            else
            {
                isGrounded = false;
                rigBody.useGravity = true;
            }
        }
        else
        {
            isGrounded = false;
            rigBody.useGravity = true;
        }
    }
    public void SetPlayerPositionAndDirection(Vector3 newPosition, Vector3 newForward)
    {
        nextPosition = new Vector3(newPosition.x, transform.position.y, newPosition.z);
        nextDirection = newForward;
    }
    public void OnCaptured()
    {
        if(!isDisabled) isDisabled = true;
    }
    public void OnLegsModuleExplosion()
    {
        legsModuleDamage = true;
        modulesDamagedCount++;
    }
    public void OnChestModuleExplosion()
    {
        chestModuleDamage = true;
        modulesDamagedCount++;
    }
    public void OnHeadModuleExplosion()
    {
        headModuleDamage = true;
        modulesDamagedCount++;
    }
}
