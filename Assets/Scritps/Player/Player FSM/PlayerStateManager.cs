using System.Collections.Generic;
using System.Security.Cryptography;
using Unity.Cinemachine;
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
    [Tooltip("What counts as walkable ground. Ground + Props — NOT Default: this project keeps " +
             "ceilings and decorative shells there, and with Default in the mask the ground probe " +
             "treats them as floor.")]
    [SerializeField] private LayerMask groundLeyerMask;

    [Tooltip("What the player slides along instead of stopping dead against. Ground + Wall + " +
             "Props.\n\n" +
             "Empty means no deflection at all, which is the pre-fix behaviour: you press into a " +
             "crate and stick to it. Tools > Nemesis > Repair Layer Masks fills this in.")]
    [SerializeField] private LayerMask obstacleMask;

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
    // Head (M3): sets IsBlindnessActive true; the overlay itself is driven by
    //            BlindnessOverlayView, which listens to ModuleEvents.OnExploded on its own.

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
    /// <summary>Name of the bare Transform used as the camera-relative input pivot.</summary>
    private const string ORIENTATION_CHILD_NAME = "Placeholder forward direction";

    void Awake()
    {
        ResolveHierarchyReferences();
        if (!ValidateReferences())
        {
            // Disabled rather than left running: every reference below is dereferenced either by
            // InputUpdate/CheckGround each frame or by the states themselves, so carrying on would
            // bury the real cause under a NullReferenceException per frame. A disabled component
            // also never gets Start(), so the FSM cannot enter a state with half its wiring.
            enabled = false;
            return;
        }

        InitializeStates();

        // Registered in Awake and not in OnEnable so that consumers waking up in their own
        // Awake/Start already find the player. The registry is a static class precisely so
        // this cannot depend on another object's initialisation order.
        PlayerRegistry.Register(this);

        // Awake/OnDestroy and not OnEnable/OnDisable: the validation failure above sets
        // enabled = false, and Unity then never calls OnEnable — the subscription would be
        // skipped while OnDisable still ran the '-=', which is the classic asymmetric-handler
        // bug. OnDestroy always runs, so this pair cannot come apart.
        ModuleEvents.OnExploded += HandleModuleExploded;
    }

    /// <summary>
    /// Fills in any reference the prefab left empty by looking it up in the player's own
    /// hierarchy.
    ///
    /// This is what makes the character model swappable: dropping a new rig in and deleting the
    /// old one leaves playerBody, animController and boxColl pointing at a destroyed object, and
    /// they get picked up again from here instead of having to be re-dragged by hand.
    ///
    /// Runs from Awake and not Start on purpose — StateManager.Start() immediately calls
    /// EnterState(), and PlayerIdleState.EnterState() already dereferences AudioEmitingZone, so
    /// resolving in Start would be one step too late.
    /// </summary>
    private void ResolveHierarchyReferences()
    {
        // Explicit '== null' and not '??=': a field pointing at a destroyed object is only null
        // through UnityEngine.Object's overloaded operator, which '??=' does not use — it would
        // happily keep the dead reference, which is the exact case this method exists for.
        // includeInactive is on because the noise emitter is toggled off by the idle/crouch states
        // and would otherwise be invisible to the lookup.
        if (rigBody == null)          rigBody          = GetComponent<Rigidbody>();
        if (capsuleColl == null)      capsuleColl      = GetComponent<CapsuleCollider>();
        if (boxColl == null)          boxColl          = GetComponentInChildren<BoxCollider>(true);
        if (audioEmitingZone == null) audioEmitingZone = GetComponentInChildren<SphereCollider>(true);
        if (animController == null)   animController   = GetComponentInChildren<Animator>(true);

        // The Animator sits on the model root, which is the same object playerBody points at, so
        // the two resolve together and swapping a model only has to get the Animator right.
        if (playerBody == null && animController != null) playerBody = animController.transform;

        // cameraTransform is the Cinemachine rig, not the rendering Camera — that one lives in
        // another scene entirely and would not be found from here.
        if (cameraTransform == null)
        {
            CinemachineCamera vcam = GetComponentInChildren<CinemachineCamera>(true);
            if (vcam != null) cameraTransform = vcam.transform;
        }

        // Nothing identifies the orientation pivot but its name: it is a bare Transform used as
        // scratch space for the camera-relative input basis, with no component to search for.
        if (orientation == null) orientation = FindChildByName(ORIENTATION_CHILD_NAME);
    }

    private Transform FindChildByName(string childName)
    {
        Transform[] candidates = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i].name == childName) return candidates[i];
        }
        return null;
    }

    /// <summary>
    /// Reports everything still unresolved in one message instead of letting each one surface as
    /// its own NullReferenceException later. Returns false if the player cannot run.
    /// </summary>
    private bool ValidateReferences()
    {
        List<string> missing = new List<string>();

        // movement is an asset and not part of the hierarchy, so it can only ever be reported.
        if (movement == null)         missing.Add(nameof(movement));
        if (rigBody == null)          missing.Add(nameof(rigBody));
        if (capsuleColl == null)      missing.Add(nameof(capsuleColl));
        if (boxColl == null)          missing.Add(nameof(boxColl));
        if (audioEmitingZone == null) missing.Add(nameof(audioEmitingZone));
        if (cameraTransform == null)  missing.Add(nameof(cameraTransform));
        if (orientation == null)      missing.Add(nameof(orientation));
        if (playerBody == null)       missing.Add(nameof(playerBody));
        if (animController == null)   missing.Add(nameof(animController));

        // Not fatal, but a mask of Nothing makes CheckGround fail every frame and the player
        // silently never becomes grounded, which reads as "movement is broken" and not as a
        // configuration mistake.
        if (groundLeyerMask.value == 0)
        {
            Debug.LogWarning($"[{nameof(PlayerStateManager)}] '{name}' has an empty " +
                             $"{nameof(groundLeyerMask)}. The player will never be grounded.", this);
        }

        // Same shape of silent failure, one step subtler: with this empty the player still moves
        // perfectly well, it just stops dead against every wall and crate instead of sliding.
        if (obstacleMask.value == 0)
        {
            Debug.LogWarning($"[{nameof(PlayerStateManager)}] '{name}' has an empty " +
                             $"{nameof(obstacleMask)}. The player will not slide along walls and " +
                             "props — it will stick to them. Run Tools > Nemesis > Repair Layer " +
                             "Masks, or set it to Ground + Wall + Props by hand.", this);
        }

        if (missing.Count == 0) return true;

        Debug.LogError($"[{nameof(PlayerStateManager)}] '{name}' could not resolve " +
                       $"{missing.Count} reference(s) from its own hierarchy: " +
                       $"{string.Join(", ", missing)}. The player has been disabled — assign them " +
                       $"in the inspector, or add the missing objects under the player.", this);
        return false;
    }

    private void OnDestroy()
    {
        PlayerRegistry.Unregister(this);

        // Safe even when Awake bailed out at ValidateReferences and never subscribed: '-=' on a
        // handler that was never added is a no-op.
        ModuleEvents.OnExploded -= HandleModuleExploded;
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
    /// <summary>How far above the pivot the ground probe starts. High enough to clear a step the
    /// player is already standing on, low enough to stay inside the capsule.</summary>
    private const float GroundProbeStart = 1f;

    /// <summary>Length of the ground probe. It used to be unbounded, which is how a ray that
    /// missed the floor entirely still came back with a usable-looking normal.</summary>
    private const float GroundProbeLength = 2f;

    /// <summary>
    /// Below this angle the ground counts as flat and gravity stays on.
    ///
    /// It was 1 degree, and that is most of why walking into anything got you stuck: every bevel,
    /// every slightly-off prop face clears one degree, so the probe would read a crate as a slope,
    /// switch gravity OFF, and project the movement up along its face. The player climbed the
    /// prop and stayed there.
    /// </summary>
    private const float FlatGroundAngle = 5f;

    private void CheckGround()
    {
        // Masked, bounded, and its return value actually checked. All three were missing: the old
        // probe was Physics.Raycast(pos + up, down, out hit) with no mask and no distance, so it
        // reported the normal of whatever it happened to hit first — a prop, a decorative shell,
        // geometry on the far side of the level — and THAT normal is what moveDir gets projected
        // onto below. A miss was worse still: hitRay stays default, normal is Vector3.zero, and
        // Vector3.Angle(zero, up) is 0, so a probe that hit nothing read as perfectly flat floor.
        bool hitGround = Physics.Raycast(transform.position + Vector3.up * GroundProbeStart,
                                         Vector3.down, out RaycastHit hitRay, GroundProbeLength,
                                         groundLeyerMask, QueryTriggerInteraction.Ignore);

        if (!hitGround)
        {
            // The probe can miss while the player is still standing on something — on the lip of a
            // step, the ray leaves from just inside the edge. The sphere answers "am I on ground"
            // where the ray answers "what is its normal", and only the second one is unavailable
            // here, so movement falls back to the raw input direction.
            isGrounded = Physics.CheckSphere(transform.position, capsuleColl.radius, groundLeyerMask,
                                             QueryTriggerInteraction.Ignore);
            rigBody.useGravity = true;
            moveDir = inputDir;
            return;
        }

        float groundAngle = Vector3.Angle(hitRay.normal, Vector3.up);

        if (groundAngle >= groundAngleLimit)
        {
            isGrounded = false;
            rigBody.useGravity = true;
            moveDir = inputDir;
            return;
        }

        isGrounded = true;

        // Gravity off is how the player sticks to a ramp instead of bouncing down it — but only on
        // a real ramp. See FlatGroundAngle.
        rigBody.useGravity = groundAngle <= FlatGroundAngle;

        moveDir = Vector3.ProjectOnPlane(inputDir, hitRay.normal);
    }

    /// <summary>
    /// Writes the movement velocity onto the Rigidbody, deflected along anything solid it is about
    /// to run into.
    ///
    /// The states used to assign <c>linearVelocity = MoveDir * CurrentVelocity</c> directly, and
    /// that is the whole of "you get stuck on every prop". The solver resolves the collision and
    /// zeroes the component going into the obstacle; the next frame this assignment puts the full
    /// vector back, pointing into it again. Nothing ever slides — you press forward against a
    /// crate and stop dead, at any angle, which is not how a wall is supposed to feel.
    ///
    /// Only the horizontal part is deflected. The vertical component is the slope-following term
    /// CheckGround produced, and projecting that away would stop the player walking up ramps.
    ///
    /// Takes the whole vector rather than a speed because the two callers do not agree on the
    /// direction: Moving steers by <see cref="MoveDir"/> (input, projected onto the ground normal)
    /// and Crouch by the body's own facing. That difference is theirs to keep; only the deflection
    /// is shared.
    /// </summary>
    public void ApplyMoveVelocity(Vector3 desired)
    {
        Vector3 horizontal = new Vector3(desired.x, 0f, desired.z);

        float horizontalSpeed = horizontal.magnitude;
        if (horizontalSpeed < 0.01f || obstacleMask.value == 0)
        {
            rigBody.linearVelocity = desired;
            return;
        }

        Vector3 direction = horizontal / horizontalSpeed;

        GetCapsuleProbe(out Vector3 bottom, out Vector3 top, out float radius);

        // One physics step of travel plus the skin: far enough to see the wall before touching it,
        // short enough that it does not deflect around something two metres away.
        //
        // Floored, because the speed-derived part collapses at walking pace — crouching at
        // 1.5 m/s gives 3 cm of lookahead, which only ever fires on the frame the capsule is
        // already against the wall, and deflecting one frame late is what a stutter feels like.
        float probeDistance = Mathf.Max(MinObstacleProbe,
                                        horizontalSpeed * Time.fixedDeltaTime + ObstacleSkin);

        if (Physics.CapsuleCast(bottom, top, radius, direction, out RaycastHit hit, probeDistance,
                                obstacleMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 normal = hit.normal;
            normal.y = 0f;

            // A purely horizontal normal is a wall. A purely vertical one is floor or ceiling, and
            // deflecting along it would cancel the movement instead of redirecting it.
            if (normal.sqrMagnitude > 0.0001f)
            {
                Vector3 deflected = Vector3.ProjectOnPlane(horizontal, normal.normalized);
                desired = new Vector3(deflected.x, desired.y, deflected.z);
            }
        }

        rigBody.linearVelocity = desired;
    }

    /// <summary>Margin the obstacle cast is shrunk by, so a capsule already resting against a wall
    /// does not report a zero-distance hit every frame and jitter.</summary>
    private const float ObstacleSkin = 0.05f;

    /// <summary>Shortest the obstacle lookahead is allowed to get, whatever the speed.</summary>
    private const float MinObstacleProbe = 0.15f;

    /// <summary>
    /// The two hemisphere centres and the radius of the capsule in world space, shrunk by
    /// <see cref="ObstacleSkin"/>. Read off the collider rather than from SO_Movement because the
    /// crouch state rewrites the collider at runtime and the SO does not follow.
    /// </summary>
    private void GetCapsuleProbe(out Vector3 bottom, out Vector3 top, out float radius)
    {
        radius = Mathf.Max(0.01f, capsuleColl.radius - ObstacleSkin);

        Vector3 centre = transform.TransformPoint(capsuleColl.center);
        float halfSpine = Mathf.Max(0f, capsuleColl.height * 0.5f - capsuleColl.radius);

        bottom = centre - Vector3.up * halfSpine;
        top = centre + Vector3.up * halfSpine;
    }
    public void SetPlayerPositionAndDirection(Vector3 newPosition, Vector3 newForward)
    {
        nextPosition = new Vector3(newPosition.x, transform.position.y, newPosition.z);
        nextDirection = newForward;
    }
    /// <summary>
    /// Hard-moves the player and cancels any physics momentum.
    ///
    /// Not the same as <see cref="SetPlayerPositionAndDirection"/>, which only queues a target
    /// for the interpolated approach that PlayerBoxInteractingState drives. A checkpoint respawn
    /// has to land instantly and drop the velocity, otherwise the player keeps sliding at the
    /// speed it was running at when the Nemesis grabbed it.
    /// </summary>
    public void TeleportTo(Vector3 position, Quaternion rotation)
    {
        if (rigBody != null)
        {
            rigBody.linearVelocity = Vector3.zero;
            rigBody.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(position, rotation);

        Vector3 forward = rotation * Vector3.forward;
        if (playerBody != null) playerBody.forward = forward;

        // The queued target is overwritten too: a state caught mid-interpolation would otherwise
        // drag the player straight back to where it was captured.
        nextPosition = position;
        nextDirection = forward;

        // The colliders still sit at the old position until the next physics step. Without this
        // the ground check on the respawn frame reads the geometry the player came from.
        Physics.SyncTransforms();
    }

    /// <summary>
    /// The Nemesis grabbed the player. Per spec this is the ONLY thing the Nemesis is allowed to
    /// call directly — it must not reach into save/UI itself. Everything that happens next
    /// (checkpoint respawn, or the hard defeat fallback) reacts to PlayerEvents.OnPlayerCaptured
    /// instead of being invoked from here.
    /// </summary>
    public void OnCaptured()
    {
        if (isDisabled) return;

        isDisabled = true;
        PlayerEvents.PlayerCaptured(this);
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
                // The overlay itself is BlindnessOverlayView's job — it subscribes to
                // ModuleEvents.OnExploded directly and filters by PenaltyType.Head.
                break;
        }
    }
}
