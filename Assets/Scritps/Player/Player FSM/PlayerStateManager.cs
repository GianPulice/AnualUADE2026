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

    // ── Module penalties ────────────────────────────────────────────────────────
    //
    // These factors are multiplied into the movement calculations in Moving/Crouch. They stay at
    // 1 while the corresponding module has not exploded, so the player moves normally. When a
    // module explodes, the ModuleManager fires ModuleEvents.OnExploded and ApplyPenalty routes
    // the effect into the correct factor. Effects are permanent for the rest of the run — there
    // is no method to clear them by design (spec §1.1).
    //
    // Legs (M1): MoveSpeedPenaltyFactor drops to cojeraMultiplier (e.g. 0.6 → 40% slower).
    // Chest (M2): SprintPenaltyFactor drops by sprintReduction (e.g. 0.25 → sprint 25% weaker).
    // Head (M3): sets IsBlindnessActive true; the actual BlindnessLoop overlay is not implemented
    //            yet (no UI/audio) and lives as a TODO in ApplyPenalty.

    public float MoveSpeedPenaltyFactor { get; private set; } = 1f;
    public float SprintPenaltyFactor { get; private set; } = 1f;
    public bool IsBlindnessActive { get; private set; } = false;

    public bool LegsPenaltyActive => MoveSpeedPenaltyFactor < 1f;
    public bool ChestPenaltyActive => SprintPenaltyFactor < 1f;
    public bool HeadPenaltyActive => IsBlindnessActive;

    /// <summary>Base move speed after applying the legs penalty. States multiply by their own
    /// SpeedMultiplier on top (1 walk, 1.5 sprint, crouchSpeedMultiplier crouch).</summary>
    public float EffectiveMoveSpeed => movement != null ? movement.MoveSpeed * MoveSpeedPenaltyFactor : 0f;

    public enum EPlayerState
    {
        Idle,
        Moving,
        Crouch,
        Interacting,
        Hidden,

        // NOT IMPLEMENTED — there is no PlayerInDangerState and nothing transitions here.
        // The spec (§8) mentions it ("the player goes to In Danger and regains control")
        // but never defines it, so the behaviour cannot be written yet. StateManager guards
        // against the transition, so leaving it declared is safe.
        InDanger,

        Disabled,
    }
    void Awake()
    {
        rigBody = GetComponent<Rigidbody>();
        InitializeStates();
        ModuleEvents.OnExploded += HandleModuleExploded;
    }

    private void OnDestroy()
    {
        ModuleEvents.OnExploded -= HandleModuleExploded;
    }

    private void HandleModuleExploded(ModuleRuntime runtime)
    {
        if (runtime == null || runtime.Data == null) return;
        ApplyPenalty(runtime.Data.Penalty, runtime.Data);
    }

    /// <summary>
    /// Applies the given module's penalty to the player. Idempotent per penalty type — calling
    /// twice with the same Legs data leaves the factor unchanged. Public so debug tools (or a
    /// future save-load) can restore penalty state without going through the module lifecycle.
    /// </summary>
    public void ApplyPenalty(PenaltyType type, ModuleData data)
    {
        if (data == null) return;

        switch (type)
        {
            case PenaltyType.Legs:
                MoveSpeedPenaltyFactor = Mathf.Clamp01(data.CojeraMultiplier);
                // TODO(anim): swap the AnimatorController to the 'Limping' clip when we have it.
                break;

            case PenaltyType.Chest:
                SprintPenaltyFactor = Mathf.Clamp01(1f - data.SprintReduction);
                // TODO(camera): start continuous camera shake (Perlin) using data.ShakeAmplitude /
                // data.ShakeFrequency when the CameraController exposes a shake API.
                break;

            case PenaltyType.Head:
                IsBlindnessActive = true;
                // TODO(ui): start the BlindnessLoop overlay (fade-in/hold/fade-out) using
                // data.BlindnessInterval / data.BlindnessDuration / data.BlindnessFadeIn/Out
                // when the UI overlay is in place.
                break;
        }
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
}
