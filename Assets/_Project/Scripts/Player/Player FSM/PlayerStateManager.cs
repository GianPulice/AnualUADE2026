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
             "crate and stick to it. Tools > Nemesis > Validate Navigation Setup reports it.")]
    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("What stops the player from standing back up out of a crouch. Leave empty to fall " +
             "back to Ground + Wall + Props (groundLeyerMask | obstacleMask); add Default here if " +
             "the level's low ceilings / crawl-space slabs sit on that layer.")]
    [SerializeField] private LayerMask standBlockMask;

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
    // crouch->stand was requested but a low ceiling was in the way; honoured the frame it clears.
    private bool wantsToStand = false;
    private bool isDisabled = false;

    public bool IsInteracting { get => isInteracting; set => isInteracting = value; }
    public bool IsCrouch { get => isCrouch; set => isCrouch = value; }
    public bool IsDisabled { get => isDisabled; set => isDisabled = value; }

    // ── Hiding ──────────────────────────────────────────────────────────────────
    //
    // The player only ever holds the REFERENCE. Everything about getting in and out — the poses,
    // the interior camera, freezing the body, and undoing all of it on a capture, a respawn or a
    // scene unload — belongs to HidingSpot, and what the player does while inside belongs to
    // PlayerHiddenState. This is the seam between the three, and nothing else.

    private HidingSpot currentHidingSpot;
    private bool debugHidden;
    private bool hidingTransition;

    /// <summary>
    /// The spot the player is inside, or null. WHICH spot it is, and not just "am I hidden", is
    /// what the Nemesis needs to be able to walk up to a locker and open it (plan §3.1): with a
    /// bare bool the monster can know you vanished and still have nowhere to look.
    /// </summary>
    public HidingSpot CurrentHidingSpot => currentHidingSpot;

    /// <summary>
    /// True while the Nemesis's vision has to treat the player as gone. Derived, not stored:
    /// a spot that released without clearing a separate flag is the exact bug this replaces.
    /// </summary>
    public bool IsHidden => currentHidingSpot != null || debugHidden;

    /// <summary>
    /// Hidden with no spot at all — the F10 console's Hide toggle, so that the monster's vision
    /// can be exercised in a scene with no hiding spot built in it yet. It is a debug affordance
    /// and nothing in the game should write it.
    /// </summary>
    public bool DebugHidden { get => debugHidden; set => debugHidden = value; }

    /// <summary>
    /// True while the climb-in or climb-out animation is playing: the player cannot move and is
    /// STILL FULLY VISIBLE. That window is what the Nemesis's "I saw you climb in" rule reads —
    /// see <see cref="HidingSpot"/>.
    /// </summary>
    public bool IsHidingTransition => hidingTransition;

    /// <summary>Called by <see cref="HidingSpot"/> only, at both ends of the transition.</summary>
    public void SetHidingTransition(bool active) => hidingTransition = active;

    /// <summary>
    /// Called by <see cref="HidingSpot"/> only: once when the player is in, once with null on
    /// every way back out. Not a property with a setter, because "anyone can assign this" is how
    /// the old loose IsHidden bool ended up with two writers and no owner.
    /// </summary>
    public void SetHidingSpot(HidingSpot spot) => currentHidingSpot = spot;

    /// <summary>
    /// True while the player cannot act: disabled (captured, wake-up, explosion) or lying down /
    /// getting up. The FSM states go to Disabled on this, not on <see cref="IsDisabled"/> alone.
    /// </summary>
    public bool IsImmobilized => isDisabled || IsStandingUp;

    // ── Stand-up animations ─────────────────────────────────────────────────────
    //
    // Two ways the player gets up off the floor: at the start of the level, during the wake-up
    // cinematic's camera pan, and at the checkpoint after the Nemesis caught it. Both go through
    // the same two steps — HoldLyingPose (on the first frame of the clip, behind a black screen)
    // and PlayStandUp (once the screen is revealed) — and keep the player immobilized until the
    // clip has played to its last frame. Pause stays available throughout.
    //
    // The states are added to PlayerController by Tools > Player > Setup Stand-Up Animations. With
    // a controller that does not have them, nothing here locks the player: everything behaves as
    // it did before the stand-ups existed.

    public enum EStandUp
    {
        Init,           // Start of the level, with the wake-up cinematic.
        AfterCapture,   // At the checkpoint, after the Nemesis caught the player.
    }

    private enum EStandUpPhase { None, Lying, Playing }

    [Header("Stand-up animations")]
    [Tooltip("Animator state played when the player gets up at the start of the level, during the " +
             "wake-up cinematic's camera pan.")]
    [SerializeField] private string initStandUpState = "Init Stand Up";

    [Tooltip("Animator state played when the player gets up at the checkpoint after a capture.")]
    [SerializeField] private string captureStandUpState = "Standing Up";

    [Tooltip("Seconds after the Nemesis finishes repositioning before the capture stand-up starts " +
             "on its own, in case the capture fade never reports the reveal (no LevelUI loaded).")]
    [SerializeField, Min(0f)] private float captureStandUpFallback = 2.5f;

    [Tooltip("Scenes where the player gets up at the start of the level (the one New Game loads). " +
             "Anywhere else it starts standing, so test scenes do not wait for the whole clip. The " +
             "capture stand-up plays in every scene.")]
    [SerializeField] private string[] initStandUpScenes = { "WIRED_Zona1_Blockout" };

    [Tooltip("Seconds a stand-up may run past the end of its clip before control is handed back " +
             "anyway. Safety net only: normally control comes back on the clip's last frame.")]
    [SerializeField, Min(1f)] private float standUpTimeout = 3f;

    private EStandUpPhase standUpPhase = EStandUpPhase.None;
    private EStandUp standUpKind;
    private int standUpStateHash;
    private float standUpElapsed;
    private float standUpClipSeconds;
    private float captureFallbackAt = -1f;
    private bool initStandUpHandled;

    // True while this player holds a ModuleManager.PauseTicking from a capture: from the grab,
    // through the black screen and the respawn, until the stand-up ends and control is back.
    private bool captureTimerPaused;

    // Same span as captureTimerPaused, but independent of ModuleManager: from the grab until the
    // stand-up ends and control is back. HUD that must get out of the way of a capture polls it.
    private bool recoveringFromCapture;

    /// <summary>True from the Nemesis grab, through the black screen and the respawn, until the
    /// player is back on their feet with control — the same moment the module timer resumes.</summary>
    public bool IsRecoveringFromCapture => recoveringFromCapture;

    /// <summary>True while the player is lying on the floor or getting up.</summary>
    public bool IsStandingUp => standUpPhase != EStandUpPhase.None;

    /// <summary>True during the level-start stand-up only — the one the wake-up skip can cut.</summary>
    public bool IsWakeUpStandingUp => IsStandingUp && standUpKind == EStandUp.Init;

    /// <summary>True during the checkpoint stand-up after a capture: lying down or getting up.</summary>
    public bool IsCaptureStandingUp => IsStandingUp && standUpKind == EStandUp.AfterCapture;

    /// <summary>
    /// How far through the stand-up the player is: 0 while lying (and on the first frames), then
    /// the clip's normalized time, 1 once it is over. Camera shots sync to it instead of a timer, so
    /// changing the clip or its speed keeps them in step.
    /// </summary>
    public float StandUpProgress { get; private set; }

    /// <summary>
    /// Seconds until the playing stand-up reaches its last frame; 0 while lying, on its first frames
    /// (before the Animator reports the state) and once it is over.
    /// </summary>
    public float StandUpSecondsLeft { get; private set; }

    // ── Moving floors ─────────────────────────────────────────────────────

    /// <summary>
    /// The platform currently carrying the player, or null.
    ///
    /// Set by <see cref="MovingPlatform"/> from its boarding trigger. It exists because
    /// <see cref="CheckGround"/> answers "am I standing on something?" with a layer mask, and a
    /// freight elevator cabin is not on a layer that mask contains: it sits on Interactable so the
    /// crosshair can still pick up the ride button travelling with it, and the NavMesh bake
    /// deliberately leaves it out. Riding it up therefore made the ground probe MISS -- there is
    /// nothing on Ground or Default within two metres below a cabin a storey in the air -- and the
    /// player was never grounded again. PlayerMovingState bounces straight back to Idle while
    /// !IsGrounded, so the character stood in the lift completely unable to move, which is the
    /// reported "press the montacargas button and the player loses his inputs".
    ///
    /// A reference and not a counter, because a platform destroyed mid-ride would leak a count
    /// that nothing ever gives back, while Unity's own null semantics make a destroyed one read as
    /// null here for free.
    /// </summary>
    private MovingPlatform carrier;

    /// <summary>Whether a moving platform is carrying the player right now.</summary>
    public bool IsCarriedByPlatform => carrier != null;

    /// <summary>Called by <see cref="MovingPlatform"/> when the player boards it.</summary>
    public void SetCarrier(MovingPlatform platform) => carrier = platform;

    /// <summary>
    /// Called by <see cref="MovingPlatform"/> when the player steps off it. Ignores a platform
    /// that is not the one carrying us, so two overlapping cabins cannot clear each other's claim.
    /// </summary>
    public void ClearCarrier(MovingPlatform platform)
    {
        if (ReferenceEquals(carrier, platform)) carrier = null;
    }

    // ── Box pushing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Rigidbody of the box the player is holding, or null. Set by <see cref="PushableBox"/> on
    /// grab and cleared on release. PlayerBoxInteractingState needs it because only a forward push
    /// moves the box by contact — pulling it back or sliding it sideways means driving the box
    /// directly, alongside the player.
    /// </summary>
    public Rigidbody PushedBox { get; private set; }

    /// <summary>Called by <see cref="PushableBox"/> when the player grabs it.</summary>
    public void SetPushedBox(Rigidbody box) => PushedBox = box;

    /// <summary>
    /// Called by <see cref="PushableBox"/> on release. Ignores a box that is not the one held, same
    /// reasoning as <see cref="ClearCarrier"/>.
    /// </summary>
    public void ClearPushedBox(Rigidbody box)
    {
        if (ReferenceEquals(PushedBox, box)) PushedBox = null;
    }

    /// <summary>
    /// World-space direction PlayerBoxInteractingState is moving the box this frame, or zero when
    /// it is not. PushableBox gates its push loop sound on it.
    /// </summary>
    public Vector3 PushDirection { get; set; }

    // ── Module penalties ────────────────────────────────────────────────────────
    //
    // These factors are multiplied into the movement calculations in Moving/Crouch. They stay at
    // 1 while the corresponding module has not exploded, so the player moves normally. When a
    // module explodes, ModuleEvents.OnPenaltyApplied fires (after the explosion cinematic) and ApplyPenalty routes
    // the effect into the correct factor. Effects are permanent for the rest of the run — there
    // is no method to clear them by design (spec §1.1).
    //
    // Legs (M1): MoveSpeedPenaltyFactor drops to cojeraMultiplier (e.g. 0.6 → 40% slower).
    // Chest (M2): SprintPenaltyFactor drops by sprintReduction (e.g. 0.25 → sprint 25% weaker).
    // Head (M3): sets IsBlindnessActive true; the overlay itself is driven by
    //            BlindnessOverlayView, which listens to ModuleEvents.OnPenaltyApplied on its own.

    public float MoveSpeedPenaltyFactor { get; private set; } = 1f;
    public float SprintPenaltyFactor { get; private set; } = 1f;
    public bool IsBlindnessActive { get; private set; } = false;

    public bool LegsPenaltyActive => MoveSpeedPenaltyFactor < 1f;
    public bool ChestPenaltyActive => SprintPenaltyFactor < 1f;
    public bool HeadPenaltyActive => IsBlindnessActive;

    // The capsule's authored radius (0.3 on the shipped prefab), cached once so
    // RefreshCapsuleRadius always has a real value to restore to, whatever order crouch and the
    // legs penalty toggle in.
    private float baseCapsuleRadius;

    /// <summary>
    /// Widens the capsule (WIR-025) while crouched or once the legs module has exploded — the two
    /// poses whose arm-for-balance / limp reach swings past the standard radius — and restores it
    /// the moment neither applies any more. Called from PlayerCrouchState's Enter/Exit and from
    /// ApplyPenalty's Legs case; safe to call at any time since it only ever reads current state.
    /// </summary>
    public void RefreshCapsuleRadius()
    {
        if (capsuleColl == null || movement == null) return;

        capsuleColl.radius = (IsCrouch || LegsPenaltyActive) ? movement.WideStanceRadius : baseCapsuleRadius;
    }

    // ── Injured locomotion (M1) ─────────────────────────────────────────────────
    //
    // The legs penalty already slows the player down; these clips are what make it read on
    // screen. Once M1 explodes the normal Idle/Walking/Running clips are replaced in place by their
    // injured versions, for the rest of the run.
    //
    // The swap goes through an AnimatorOverrideController and NOT through a second
    // AnimatorController, because assigning Animator.runtimeAnimatorController rebinds the
    // Animator: every parameter drops back to its default and the state machine restarts from the
    // default state. isCrouch / isPushing / isTrapped are written once on state enter and would be
    // silently lost, and the character would pop back to Idle mid-stride. Overriding the clips of a
    // controller the Animator is ALREADY running changes neither.

    [Tooltip("Hurt idle that replaces the Idle clip once the legs module (M1) explodes. " +
             "Leave empty to keep the healthy animation.")]
    [SerializeField] private AnimationClip injuredIdleClip;

    [Tooltip("Limping walk that replaces the Walking clip once the legs module (M1) explodes. " +
             "Leave empty to keep the healthy animation — the speed penalty still applies.")]
    [SerializeField] private AnimationClip injuredWalkClip;

    [Tooltip("Limping run that replaces the Running clip once the legs module (M1) explodes. " +
             "Leave empty to keep the healthy animation — the speed penalty still applies.")]
    [SerializeField] private AnimationClip injuredRunClip;

    // Names of the ORIGINAL clips inside Idle.fbx / Walking.fbx / Running.fbx, which is what an
    // AnimatorOverrideController keys on — not the names of the Animator states that play them.
    private const string IDLE_CLIP_NAME = "Idle";
    private const string WALK_CLIP_NAME = "Walking";
    private const string RUN_CLIP_NAME = "Running";
    private const string LEGS_HURT_PARAM = "isLegsHurt";

    // Animator state the stand-ups hand over to, and the one skipping jumps to.
    private const string IDLE_STATE_NAME = "Idle";

    /// <summary>
    /// The override controller wrapped around the Animator's own controller in Awake, so the clip
    /// swap later costs no rebind. Null when no injured clip is wired up, in which case the
    /// Animator keeps running the original controller untouched.
    /// </summary>
    private AnimatorOverrideController clipOverrides;

    /// <summary>Base move speed after applying the legs penalty. States multiply by their own
    /// SpeedMultiplier on top (1 walk, 1.5 sprint, crouchSpeedMultiplier crouch).</summary>
    public float EffectiveMoveSpeed => movement != null ? movement.MoveSpeed * MoveSpeedPenaltyFactor : 0f;

    // ── Locomotion cadence ──────────────────────────────────────────────────────

    [Header("Locomotion cadence")]
    [Tooltip("Playback speed range of the walk / run / crouch-walk clips. They play at " +
             "(actual speed / the gait's target speed), clamped to this, so the legs — and the " +
             "footsteps, which are events on the footfall frames — keep time with how fast the " +
             "player really moves. 1 at a steady gait. The floor keeps a player pressing into a " +
             "wall from moonwalking in slow motion; the ceiling is headroom, a player rarely " +
             "outruns its own target.")]
    [SerializeField] private Vector2 locomotionAnimSpeedRange = new Vector2(0.6f, 1.3f);

    [Tooltip("How fast the playback speed follows the velocity (1/s). Higher = snappier.")]
    [SerializeField, Min(0.1f)] private float locomotionAnimSpeedSharpness = 10f;

    // Float, default 1, driving the Speed Multiplier of Walking, Running and Crouched Walking.
    private const string LOCOMOTION_SPEED_PARAM = "locomotionSpeed";
    private static readonly int LocomotionSpeedHash = Animator.StringToHash(LOCOMOTION_SPEED_PARAM);
    private bool hasLocomotionSpeedParam;
    private float locomotionAnimSpeed = 1f;

    public enum EPlayerState
    {
        Idle,
        Moving,
        Crouch,
        Interacting,
        Hidden,
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

        // The box collider is the push hitbox and only belongs on while PlayerBoxInteractingState
        // runs (it enables it on enter, disables it on exit). Left on, it sticks out ~0.2m past
        // the capsule in front of the chest, where the obstacle CapsuleCast in ApplyMoveVelocity
        // cannot see it: that box hits walls and props first, with default friction and square
        // corners, and the player snags on them instead of sliding.
        boxColl.enabled = false;

        // Read before anything (crouch, a legs penalty already restored from a save) has a chance
        // to widen it — see RefreshCapsuleRadius.
        baseCapsuleRadius = capsuleColl.radius;

        SetupClipOverrides();
        hasLocomotionSpeedParam = HasAnimatorParameter(LOCOMOTION_SPEED_PARAM, AnimatorControllerParameterType.Float);

        InitializeStates();

        // Registered in Awake and not in OnEnable so that consumers waking up in their own
        // Awake/Start already find the player. The registry is a static class precisely so
        // this cannot depend on another object's initialisation order.
        PlayerRegistry.Register(this);

        // Awake/OnDestroy and not OnEnable/OnDisable: the validation failure above sets
        // enabled = false, and Unity then never calls OnEnable — the subscription would be
        // skipped while OnDisable still ran the '-=', which is the classic asymmetric-handler
        // bug. OnDestroy always runs, so this pair cannot come apart.
        ModuleEvents.OnPenaltyApplied += HandleModuleExploded;
        CaptureFadeView.OnCaptureRevealed += HandleCaptureRevealed;
        NemesisEvents.OnCaptureResolved += HandleCaptureResolved;
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
        ModuleEvents.OnPenaltyApplied -= HandleModuleExploded;
        CaptureFadeView.OnCaptureRevealed -= HandleCaptureRevealed;
        NemesisEvents.OnCaptureResolved -= HandleCaptureResolved;

        // ModuleManager outlives the level: a player unloaded mid-capture must not leave the
        // module timer frozen for the next one.
        ReleaseCaptureTimerPause();
    }
    public override void Start()
    {
        base.Start();
    }
    public override void Update()
    {
        TickStandUp();

        // Control is back after a capture: the module timer runs again from here.
        if (captureTimerPaused && !IsImmobilized) ReleaseCaptureTimerPause();
        if (recoveringFromCapture && !IsImmobilized) recoveringFromCapture = false;

        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        // During a scene change the level is already running behind the loading screen: keys
        // pressed there must not walk the player off before it is revealed. Same while lying on
        // the floor or getting up, and while climbing into or out of a hiding spot.
        if (ScreenManager.IsInputLocked || IsStandingUp || hidingTransition) inputDir = Vector3.zero;
        else InputUpdate();
        CheckGround();
        base.Update();
        UpdateLocomotionAnimSpeed();
    }

    /// <summary>
    /// Plays Walking / Running / Crouched Walking (and the injured clips standing in for them)
    /// at the rate the player is actually covering ground, relative to the speed that gait is
    /// meant to run at. Written after the state update, so it reads the velocity this frame's
    /// state just applied.
    ///
    /// This is what makes the footsteps follow the player's speed. The steps are AnimationEvents
    /// on the footfall frames, so they come exactly as fast as the legs do — and with the clips
    /// at a fixed rate the legs did not care how fast the player moved: accelerating out of idle,
    /// pressing into a wall, or sprinting with the chest penalty all walked at the clip's one
    /// cadence. Scaling playback by actual / nominal speed fixes the legs and the sound together.
    ///
    /// Nominal is the target speed of the current gait (legs and chest penalties included), so
    /// at a steady walk, run or limp the ratio is 1 and the clips look exactly as authored.
    /// </summary>
    private void UpdateLocomotionAnimSpeed()
    {
        if (!hasLocomotionSpeedParam) return;

        float target = 1f;
        float nominal = EffectiveMoveSpeed * speedMultiplier;
        if (nominal > 0.01f && isGrounded)
        {
            Vector3 v = rigBody.linearVelocity;
            v.y = 0f;
            target = Mathf.Clamp(v.magnitude / nominal, locomotionAnimSpeedRange.x, locomotionAnimSpeedRange.y);
        }

        // Smoothed: the Rigidbody's velocity jitters frame to frame against geometry, and the
        // cadence should not.
        float k = 1f - Mathf.Exp(-locomotionAnimSpeedSharpness * Time.deltaTime);
        locomotionAnimSpeed = Mathf.Lerp(locomotionAnimSpeed, target, k);
        animController.SetFloat(LocomotionSpeedHash, locomotionAnimSpeed);
    }

    private bool HasAnimatorParameter(string paramName, AnimatorControllerParameterType type)
    {
        if (animController == null || animController.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter p in animController.parameters)
            if (p.name == paramName && p.type == type) return true;

        Debug.LogWarning($"[Player] The Animator has no {type} parameter '{paramName}', so the " +
                         "walk/run clips play at a fixed rate whatever the player's speed.", this);
        return false;
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

        // Build the movement direction vector from the inputs.
        //
        // Player/Move, unsmoothed on purpose. The old smoothed legacy axes kept reporting a value
        // for a third of a second after the key was released, and for that whole time
        // PlayerMovingState kept driving the character at FULL speed ("he keeps walking after I
        // let go"). The ramp UP is owned by SO_Movement.Acceleration in PlayerMovingState, where
        // it can be tuned and where it applies to gamepads too.
        Vector2 move = GameInput.MoveValue;
        inputDir = orientation.forward * move.y
                 + orientation.right   * move.x;
        inputDir.Normalize();

        // Crouch mechanic. Standing back up is gated on headroom: pressing crouch under a low
        // ceiling must NOT grow the capsule into it — that wedges the Rigidbody between the floor
        // and the slab and the player freezes in place (the reported bug). The request is
        // remembered instead and honoured automatically as soon as the space above clears.
        // Not under a menu: on a gamepad B is both Crouch and UI/Exit, and closing the inventory
        // must not crouch the player too.
        if (GameInput.CrouchPressed && !PauseManager.IsGameplayInputBlocked)
        {
            if (!isCrouch)
            {
                isCrouch = true;
                wantsToStand = false;
            }
            else if (wantsToStand)
            {
                wantsToStand = false;   // second press: drop the pending stand-up, stay crouched.
            }
            else if (HasHeadroomToStand())
            {
                isCrouch = false;
            }
            else
            {
                wantsToStand = true;
            }
        }

        // Deferred stand-up: the player asked to stand while blocked and has now moved clear.
        if (isCrouch && wantsToStand && HasHeadroomToStand())
        {
            isCrouch = false;
            wantsToStand = false;
        }

        // Debug key, Editor only: in a build Y froze the player.
        //
        // R is gone. It was the stand-in for a hiding spot while the system did not exist, and
        // HidingSpot has replaced it. The F10 console keeps a Hide toggle (DebugHidden) for
        // exercising the Nemesis's vision in a scene with no spot built into it.
#if UNITY_EDITOR
        // Disabled state testing
        if (Input.GetKeyDown(KeyCode.Y))
        {
            if (isDisabled) isDisabled = false;
            else isDisabled = true;
        }
#endif
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
            // Being carried is the third answer, and it outranks both probes: the cabin of a
            // moving platform is solid floor the player is demonstrably standing on, it is just
            // not on a layer either of them is allowed to see. See the carrier field.
            isGrounded = IsCarriedByPlatform
                      || Physics.CheckSphere(transform.position, capsuleColl.radius, groundLeyerMask,
                                             QueryTriggerInteraction.Ignore);
            rigBody.useGravity = true;
            moveDir = inputDir;
            return;
        }

        float groundAngle = Vector3.Angle(hitRay.normal, Vector3.up);

        if (groundAngle >= groundAngleLimit)
        {
            // Aboard a cabin the probe can still find something too steep to stand on -- the shaft
            // wall sliding past, the lip of the landing below. The floor under the player's feet is
            // the cabin either way, so the verdict stays "grounded".
            isGrounded = IsCarriedByPlatform;
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

    /// <summary>Margin the stand-up probe is shrunk by so brushing a wall does not read as a ceiling.</summary>
    private const float StandCheckSkin = 0.05f;

    /// <summary>standBlockMask if the designer set it, otherwise Ground + Wall + Props.</summary>
    private int ResolvedStandBlockMask =>
        standBlockMask.value != 0 ? standBlockMask.value : (groundLeyerMask.value | obstacleMask.value);

    /// <summary>
    /// True when a standing-height capsule would fit where the player is right now.
    ///
    /// Sweeps a sphere the player's width straight up, from the crouched head to where the
    /// standing head would be — so it never touches the floor and never self-hits (the player is
    /// not in <see cref="ResolvedStandBlockMask"/>). Gates the crouch->stand transition; see
    /// InputUpdate. Returns true if the collider or SO is missing rather than trapping the player.
    /// </summary>
    public bool HasHeadroomToStand()
    {
        if (capsuleColl == null || movement == null) return true;

        float currentHeight = capsuleColl.height;
        float targetHeight = movement.StandingHeight;
        if (targetHeight <= currentHeight) return true;

        float radius = Mathf.Max(0.01f, capsuleColl.radius - StandCheckSkin);
        Vector3 origin = transform.position + Vector3.up * (currentHeight - radius);
        float distance = targetHeight - currentHeight;

        return !Physics.SphereCast(origin, radius, Vector3.up, out _, distance,
                                   ResolvedStandBlockMask, QueryTriggerInteraction.Ignore);
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

        // Caught again while still getting up: the new capture owns the player from here.
        EndStandUp();

        // The seconds spent grabbed, on the black screen and getting up are not the player's to
        // lose: the active module's timer stops until control comes back (see Update). The
        // capture penalty itself is still applied at the respawn, by CheckpointManager.
        if (!captureTimerPaused && ModuleManager.Exists)
        {
            ModuleManager.Instance.PauseTicking();
            captureTimerPaused = true;
        }

        isDisabled = true;
        recoveringFromCapture = true;
        PlayerEvents.PlayerCaptured(this);
    }

    private void ReleaseCaptureTimerPause()
    {
        if (!captureTimerPaused) return;
        captureTimerPaused = false;
        if (ModuleManager.Exists) ModuleManager.Instance.ResumeTicking();
    }

    // ── Stand-up ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the player on the first frame of the stand-up clip — lying on the floor — and locks it
    /// there until <see cref="PlayStandUp"/>. Call it while the screen is still black.
    /// </summary>
    /// <returns>False when the controller has no state for this stand-up (setup tool not run):
    /// the player is then left exactly as it was, unlocked.</returns>
    public bool HoldLyingPose(EStandUp kind)
    {
        if (kind == EStandUp.Init && !PlaysInitStandUpHere()) return false;
        if (!TryGetStandUpState(kind, out int hash)) return false;

        standUpKind = kind;
        standUpStateHash = hash;
        standUpPhase = EStandUpPhase.Lying;
        standUpElapsed = 0f;
        StandUpProgress = 0f;
        captureFallbackAt = -1f;

        // Gets up standing, whatever stance the player was caught in.
        isCrouch = false;
        wantsToStand = false;
        inputDir = Vector3.zero;
        currentVelocity = 0f;
        if (rigBody != null)
        {
            rigBody.linearVelocity = Vector3.zero;
            rigBody.angularVelocity = Vector3.zero;
        }

        animController.Play(standUpStateHash, 0, 0f);
        return true;
    }

    /// <summary>
    /// Plays the stand-up from its first frame. Control comes back on its last frame. Lies the
    /// player down first if <see cref="HoldLyingPose"/> was not called; a stand-up of the same kind
    /// already playing is left alone.
    /// </summary>
    public void PlayStandUp(EStandUp kind)
    {
        if (standUpPhase == EStandUpPhase.Playing && standUpKind == kind) return;
        if ((standUpPhase == EStandUpPhase.None || standUpKind != kind) && !HoldLyingPose(kind)) return;

        // The cinematic can start the clip on the very frame the camera locks: without this, the
        // lock pick-up in TickStandUp would run after it and lie the player back down on frame 0.
        if (kind == EStandUp.Init) initStandUpHandled = true;

        standUpPhase = EStandUpPhase.Playing;
        standUpElapsed = 0f;
        standUpClipSeconds = 0f;
        StandUpSecondsLeft = 0f;
        captureFallbackAt = -1f;
        animController.Play(standUpStateHash, 0, 0f);
    }

    /// <summary>
    /// Drops the stand-up and puts the player straight into Idle with control back — the wake-up
    /// cinematic's skip.
    ///
    /// Idle directly, not Play(standUp, 1f): an exit-time transition only fires when the state's
    /// time crosses it, and a state jumped straight onto its end never crosses it — the rig stayed
    /// frozen on the stand-up's last frame, walking included.
    /// </summary>
    public void SkipStandUp()
    {
        if (!IsStandingUp) return;
        EndStandUp();
        PlayIdle(0f);
    }

    private void PlayIdle(float blendSeconds)
    {
        int idle = Animator.StringToHash(IDLE_STATE_NAME);
        if (!animController.HasState(0, idle)) return;

        if (blendSeconds > 0f) animController.CrossFadeInFixedTime(idle, blendSeconds, 0);
        else animController.Play(idle, 0, 0f);
    }

    private void EndStandUp()
    {
        StandUpSecondsLeft = 0f;
        standUpPhase = EStandUpPhase.None;
        StandUpProgress = 1f;
        captureFallbackAt = -1f;
    }

    /// <summary>Whether the player's own scene is one where the level starts with it lying down.</summary>
    private bool PlaysInitStandUpHere()
    {
        if (initStandUpScenes == null) return false;

        string sceneName = gameObject.scene.name;
        foreach (string allowed in initStandUpScenes)
        {
            if (allowed == sceneName) return true;
        }
        return false;
    }

    private bool TryGetStandUpState(EStandUp kind, out int hash)
    {
        string stateName = kind == EStandUp.Init ? initStandUpState : captureStandUpState;
        hash = Animator.StringToHash(stateName);

        if (animController != null && animController.runtimeAnimatorController != null &&
            !string.IsNullOrEmpty(stateName) && animController.HasState(0, hash))
            return true;

        Debug.LogWarning($"[Player] The Animator has no state '{stateName}', so the player does not " +
                         "play that stand-up. Run Tools > Player > Setup Stand-Up Animations.", this);
        return false;
    }

    private void TickStandUp()
    {
        // The wake-up cinematic locks the camera from the black screen on. A flag and not an
        // event, so it is picked up whichever of the two scenes (player / LevelUI) loads first.
        if (!initStandUpHandled && WakeUpCinematicEvents.IsCameraLocked)
        {
            initStandUpHandled = true;
            HoldLyingPose(EStandUp.Init);
        }

        switch (standUpPhase)
        {
            case EStandUpPhase.Lying:
                // Held on frame 0: the state's only way out is its exit-time transition.
                animController.Play(standUpStateHash, 0, 0f);

                // The cinematic ended without ever opening the eyes (no ARC_01a in the bank).
                if (standUpKind == EStandUp.Init && !WakeUpCinematicEvents.IsCameraLocked)
                    PlayStandUp(EStandUp.Init);
                // The capture fade never reported the reveal.
                else if (standUpKind == EStandUp.AfterCapture && captureFallbackAt >= 0f &&
                         Time.unscaledTime >= captureFallbackAt)
                    PlayStandUp(EStandUp.AfterCapture);
                break;

            case EStandUpPhase.Playing:
                // Scaled, like the Animator: a pause holds both.
                standUpElapsed += Time.deltaTime;

                AnimatorStateInfo info = animController.GetCurrentAnimatorStateInfo(0);
                bool inState = info.shortNameHash == standUpStateHash;

                // In seconds at the state's own speed, so a sped-up clip is not waited out at 1x.
                if (inState) standUpClipSeconds = info.length;
                if (inState) StandUpProgress = Mathf.Clamp01(info.normalizedTime);

                // Real seconds to the clip's last frame, at the state's own speed.
                if (inState) StandUpSecondsLeft = info.length * (1f - Mathf.Clamp01(info.normalizedTime));

                // Play() only takes effect on the Animator's next update, so the first frames can
                // still report the previous state. The transition into Idle only starts on the
                // clip's last frame (exit time 1), so the whole clip has played by now.
                bool finished = inState ? info.normalizedTime >= 1f
                                        : standUpElapsed > 0.2f && !animController.IsInTransition(0);

                // The timeout counts from the clip's end, not from its start: a long clip must
                // never be cut short by it.
                bool timedOut = standUpElapsed >= standUpClipSeconds + standUpTimeout;

                if (finished || timedOut)
                {
                    EndStandUp();

                    // Normally the exit-time transition is already blending into Idle. If it did
                    // not fire (a frame hitch jumping past it, the timeout), hand over by hand
                    // rather than leave the rig on the stand-up's last frame.
                    if (inState && !animController.IsInTransition(0)) PlayIdle(0.2f);
                }
                break;
        }
    }

    /// <summary>The capture fade finished clearing at the checkpoint: get up.</summary>
    private void HandleCaptureRevealed()
    {
        if (standUpPhase == EStandUpPhase.Lying && standUpKind == EStandUp.AfterCapture)
            PlayStandUp(EStandUp.AfterCapture);
    }

    /// <summary>Arms the fallback in case <see cref="HandleCaptureRevealed"/> never comes.</summary>
    private void HandleCaptureResolved()
    {
        if (standUpPhase == EStandUpPhase.Lying && standUpKind == EStandUp.AfterCapture)
            captureFallbackAt = Time.unscaledTime + captureStandUpFallback;
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
                ApplyInjuredLocomotion();
                // The limp swings an arm out past the standard capsule (WIR-025) for the rest of
                // the run, same reason crouch does — see RefreshCapsuleRadius.
                RefreshCapsuleRadius();
                break;

            case PenaltyType.Chest:
                SprintPenaltyFactor = Mathf.Clamp01(1f - data.SprintReduction);
                // TODO(camera): start continuous camera shake (Perlin) using data.ShakeAmplitude /
                // data.ShakeFrequency when the CameraController exposes a shake API.
                break;

            case PenaltyType.Head:
                IsBlindnessActive = true;
                // The overlay itself is BlindnessOverlayView's job — it subscribes to
                // ModuleEvents.OnPenaltyApplied directly and filters by PenaltyType.Head.
                break;
        }
    }

    /// <summary>
    /// Switches to the injured animations for this penalty right away, without applying the
    /// gameplay penalty itself. Called by ModuleExplosionSequence on the frame the explosion VFX
    /// goes off on the body, so the idle turns hurt exactly when the hit is seen; the speed factor
    /// still lands later through <see cref="ApplyPenalty"/>. Idempotent, so that later call re-applies
    /// nothing visible.
    /// </summary>
    public void ShowInjuredAnimation(PenaltyType type)
    {
        if (type == PenaltyType.Legs) ApplyInjuredLocomotion();
    }

    /// <summary>
    /// Wraps the Animator's controller in an AnimatorOverrideController that starts out overriding
    /// nothing, so it behaves exactly like the original asset. <see cref="ApplyPenalty"/> can then
    /// drop the injured clips in later without ever touching runtimeAnimatorController — see the
    /// note above <see cref="injuredWalkClip"/> for why that distinction matters.
    ///
    /// Skipped entirely when neither clip is wired up, so a scene that has not been set up yet runs
    /// on the untouched controller instead of a wrapper that could never do anything.
    /// </summary>
    private void SetupClipOverrides()
    {
        if (injuredIdleClip == null && injuredWalkClip == null && injuredRunClip == null) return;
        if (animController == null || animController.runtimeAnimatorController == null) return;

        RuntimeAnimatorController source = animController.runtimeAnimatorController;
        clipOverrides = new AnimatorOverrideController(source) { name = source.name + " (runtime)" };
        animController.runtimeAnimatorController = clipOverrides;
    }

    /// <summary>
    /// Replaces the idle, walk and run clips with their injured versions. Idempotent — re-applying
    /// the same override is a no-op, so a second Legs penalty (or a debug tool replaying one) cannot
    /// stack or restart anything.
    /// </summary>
    private void ApplyInjuredLocomotion()
    {
        if (clipOverrides == null) return;

        if (injuredIdleClip != null) OverrideClip(IDLE_CLIP_NAME, injuredIdleClip);
        if (injuredWalkClip != null) OverrideClip(WALK_CLIP_NAME, injuredWalkClip);
        if (injuredRunClip != null) OverrideClip(RUN_CLIP_NAME, injuredRunClip);

        // Nothing in PlayerController branches on this today — the clip override is what changes
        // the animation. It is set anyway because the parameter already exists for exactly this
        // case, and a future layer or transition that wants to ask "is the player limping?" should
        // read the Animator rather than reach back into this component.
        if (animController != null) animController.SetBool(LEGS_HURT_PARAM, true);
    }

    /// <summary>
    /// Points <paramref name="originalName"/> at <paramref name="replacement"/>, warning instead of
    /// failing quietly when the controller has no clip by that name. The indexer accepts an unknown
    /// key without complaint, so renaming the clip inside Walking.fbx would otherwise just stop the
    /// limp from ever appearing, with nothing in the console to say why.
    /// </summary>
    private void OverrideClip(string originalName, AnimationClip replacement)
    {
        if (clipOverrides[originalName] == null)
        {
            Debug.LogWarning($"[Player] '{clipOverrides.runtimeAnimatorController.name}' has no clip " +
                             $"named '{originalName}', so '{replacement.name}' will never play. Was the " +
                             $"clip inside the source FBX renamed?", this);
            return;
        }

        clipOverrides[originalName] = replacement;
    }
}
