using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// A locker, a work table or a cargo container the player can climb into (Hiding System spec v1.0).
///
/// It owns getting in and out: the poses, the interior camera, freezing the body, and clearing all
/// of that again on EVERY way out — the deliberate one, a capture, a checkpoint respawn, the scene
/// unloading under it. What the player does while inside (breathing, holding it) belongs to
/// <see cref="PlayerHiddenState"/>, and what the Nemesis knows about spots is phase 2's
/// NemesisHidingAwareness. Three components, three jobs.
///
/// GETTING IN IS NOT INSTANT ON PURPOSE. The climb-in runs for
/// <see cref="SO_HidingData.EnterDuration"/> with the player immobilized and still completely
/// visible, and only then does hiding start. That window is the entire basis of the Nemesis's
/// "I saw you climb in" rule (plan §3.4): with an instantaneous entry there is nothing for the rule
/// to catch, and a player could vanish mid-chase in front of the monster's face.
///
/// SETUP — see plan §14.4:
///   Spot root          ← this component + a collider on the Interactable layer for the crosshair.
///                         The solid BoxCollider of the prop itself STAYS on Default: it still has
///                         to block vision, hearing and the camera.
///   |-- InteriorPose    ← where the player ends up; its forward is what they end up facing.
///   |-- ApproachPoint   ← on the NavMesh, within catchMaxReach (1 m) of InteriorPose. Where the
///   |                     Nemesis stands to check or open it (phase 2).
///   |-- ExitPose        ← optional; where the player is put back down. Falls back to ApproachPoint.
///   \-- Interior Camera ← a CinemachineCamera with the spec's look clamps (locker ±15/±10,
///                         under-table ±45/−5..+15, container ±10/±10).
/// Tools > Player > Validate Hiding Spots reports what is missing or misplaced.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Hiding/Hiding Spot")]
public class HidingSpot : BaseRangeInteractable
{
    /// <summary>
    /// Every enabled spot in the loaded scenes. Phase 2's NemesisHidingAwareness has to ask
    /// "which spots fall inside my search sweep?" without a FindObjectsByType per search, and the
    /// editor validator needs it to catch duplicate ids. Same pattern, and the same one-player
    /// assumption, as <c>NemesisElevatorLink.Active</c>.
    /// </summary>
    private static readonly List<HidingSpot> active = new List<HidingSpot>();
    public static IReadOnlyList<HidingSpot> Active => active;

    /// <summary>
    /// The spot the player is inside right now, or null. Mirrors
    /// <c>PlayerStateManager.CurrentHidingSpot</c>; kept here so a spot can refuse to be used while
    /// another one is occupied without having to reach for the player first.
    /// </summary>
    public static HidingSpot Occupied { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
        Occupied = null;
    }

    [Header("Identity")]
    [Tooltip("Stable id for this spot, unique across the project. The habit tracker counts uses " +
             "per id (plan §5.3), so renaming one mid-project loses its history and two spots " +
             "sharing an id merge theirs. Tools > Player > Validate Hiding Spots checks it.")]
    [SerializeField] private string spotId;

    [Tooltip("Which of the three spec types this is. It decides how much the monster can still " +
             "see (phase 2) and how muffled the breathing is.")]
    [SerializeField] private EHidingSpotType type = EHidingSpotType.Locker;

    [Tooltip("Shared authoring asset: breathing, exhale, per-type multipliers, and how long " +
             "getting in and out takes. One asset for every spot in the game.")]
    [SerializeField] private SO_HidingData data;

    [Header("Poses")]
    [Tooltip("Where the player ends up, facing this transform's forward. Put it where the body " +
             "really fits — phase 2 measures the Nemesis's grab reach from here.")]
    [SerializeField] private Transform interiorPose;

    [Tooltip("Where the Nemesis stands to check or open this spot. Must be ON the NavMesh and " +
             "within its catch reach (1 m) of the interior pose, or it can walk all the way up to " +
             "the locker and still not be able to open it.")]
    [SerializeField] private Transform approachPoint;

    [Tooltip("Optional. Where the player is put back down on the way out. Left empty, the approach " +
             "point is used — it is already required to be on the NavMesh right next to the spot.")]
    [SerializeField] private Transform exitPose;

    [Header("Camera")]
    [Tooltip("The interior virtual camera, with this type's look clamps authored on it. It is " +
             "switched off on Awake, enabled above the player rig on entry and switched off again " +
             "on exit; the Cinemachine brain blends both ways — do NOT hand-lerp the main camera, " +
             "PlayerCameraController and SO_CameraConfig own the normal rig.")]
    [SerializeField] private CinemachineCamera interiorCamera;

    [Tooltip("Priority given to the interior camera while the player is inside. It has to beat the " +
             "player rig and stay well under the module-explosion defeat camera (1000), which is " +
             "allowed to cut over everything.")]
    [SerializeField] private int interiorCameraPriority = 100;

    [Header("Prompt")]
    [SerializeField] private string hidePrompt = "Hide";
    [SerializeField] private string leavePrompt = "Get out";

    [Header("Colliders")]
    [Tooltip("This prop's own colliders, filled in automatically. Phase 2 makes the Nemesis's " +
             "extreme-proximity test and its grab IGNORE these while the spot is occupied: today a " +
             "solid locker on the Default layer blocks both raycasts, which makes hiding total " +
             "immunity (plan §3.3).")]
    [SerializeField] private Collider[] ownColliders;

    // ── Runtime ─────────────────────────────────────────────────────────────

    private PlayerStateManager player;

    // One source per climb (in or out), linked to this spot's lifetime. Cancelling it is how every
    // way out that is not the player pressing E cuts a climb short.
    private CancellationTokenSource climbCts;
    private bool transitioning;

    private bool burned;

    // What the body and the camera were before we took them over, so the restore does not have to
    // assume anything.
    private bool cachedKinematic;
    private int cachedCameraPriority;

    public string SpotId => spotId;
    public EHidingSpotType Type => type;
    public SO_HidingData Data => data;
    public Transform ApproachPoint => approachPoint != null ? approachPoint : transform;
    public Transform InteriorPose => interiorPose != null ? interiorPose : transform;

    /// <summary>True while the player is inside this spot.</summary>
    public bool IsOccupied => ReferenceEquals(Occupied, this);

    /// <summary>True while the climb-in or the climb-out is playing: the player is visible,
    /// immobilized, and cannot interact with anything.</summary>
    public bool IsTransitioning => transitioning;

    /// <summary>True once the Nemesis has torn this spot apart (plan §3.6). Permanent.</summary>
    public bool IsBurned => burned;

    protected override void Awake()
    {
        base.Awake();

        if (ownColliders == null || ownColliders.Length == 0) CacheOwnColliders();

        // Off until the player is inside. A live CinemachineCamera is a candidate for the brain
        // whatever its priority, and on a tie the most recently enabled one wins — so a spot left
        // enabled in the prefab could take the view off the player rig the moment the level loads.
        // The component, not the GameObject: that stays safe even if a designer puts the camera
        // on the spot root itself.
        if (interiorCamera != null) interiorCamera.enabled = false;

        if (data == null)
        {
            Debug.LogError($"[{nameof(HidingSpot)}] '{name}' has no {nameof(SO_HidingData)}, so it " +
                           "has no breathing, no exhale and no timings. Assign the shared asset. " +
                           "The spot has been disabled.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);

        // Subscribed only while the spot exists, and always released in OnDisable. These are the
        // ways out that are NOT the player pressing E, and every one of them has to leave the body
        // unfrozen and the camera back on the rig — case 11 of the plan's test list.
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
        CheckpointManager.OnRespawned += HandleRespawned;
    }

    private void OnDisable()
    {
        active.Remove(this);

        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        CheckpointManager.OnRespawned -= HandleRespawned;

        // The scene is unloading (or a designer switched the spot off) with the player inside it,
        // or half way in or out. Nothing else is going to give the body and the input back.
        if (IsOccupied || IsTransitioning) ReleaseImmediate();
    }

    private void Update()
    {
        // A cinematic began with the player inside or half way in. In normal play it cannot — the
        // escape sequence starts at a socket, and the wake-up before the player can move — but
        // its test entry points can, and a player left kinematic in a locker with the interior
        // camera live would fight the cinematic for both. The cinematic wins; the spot lets go.
        if ((IsOccupied || IsTransitioning) && CinematicState.IsPlaying) ReleaseImmediate();
    }

    // ── Interactable ────────────────────────────────────────────────────────

    public override string GetInteractText() => IsOccupied ? leavePrompt : hidePrompt;

    public override bool IsRepeatable() => true;

    /// <summary>A burned spot is done for good, which is what stops the crosshair lighting it up.</summary>
    public override bool IsFinished() => burned;

    protected override bool CanInteractInCloseRange()
    {
        // Disabled in Awake for a missing SO_HidingData — but a disabled component still answers
        // the crosshair, and every timing below comes from that asset.
        if (!isActiveAndEnabled || data == null) return false;

        if (burned) return false;
        if (IsTransitioning) return false;

        // A scripted cinematic owns the player and the camera (the escape sequence; the wake-up
        // already immobilizes the player below). Never start a climb-in under it.
        if (CinematicState.IsPlaying) return false;

        PlayerStateManager p = ResolvePlayer();
        if (p == null) return false;

        // Getting out is always allowed while inside — including out of a spot something else has
        // since made unusable, so the player can never be sealed in.
        if (IsOccupied) return true;

        // Climbing into or out of ANOTHER spot. Without this, a second E on a neighbouring locker
        // during the first one's climb-in starts a second entry racing the first.
        if (p.IsHidingTransition) return false;

        // Someone is already in a spot, and it is not this one.
        if (Occupied != null) return false;

        // Captured, waking up, getting up off the floor: none of those are states a player climbs
        // into a locker from. The same guard the interaction system already applies for IsStandingUp.
        if (p.IsImmobilized) return false;

        // Hands full with the box (PlayerBoxInteractingState). A state added after the hiding spec
        // was written, which is why the spec does not mention it.
        if (p.IsInteracting) return false;

        // Hidden with no spot at all: the F10 console's debug toggle. Leave it alone rather than
        // letting a designer's test drive the player into a half state.
        if (p.IsHidden) return false;

        return true;
    }

    protected override void OnInteract()
    {
        // Safe to dispose: CanInteract refuses while a climb is in flight, so none is using it.
        climbCts?.Dispose();
        climbCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        CancellationToken token = climbCts.Token;

        if (IsOccupied) LeaveAsync(token).Forget();
        else            EnterAsync(token).Forget();
    }

    // ── Getting in and out ──────────────────────────────────────────────────
    //
    // UniTask and not coroutines, per the project's async convention — and here it is also the
    // correct choice rather than the conventional one: a coroutine is stopped dead when its
    // GameObject is switched off, so the finally that hands the input back would never run and a
    // player caught mid-climb by a scene change would stand frozen. Same reasoning as
    // NemesisDoorUser.WaitForDoorAsync.
    //
    // Scaled time on purpose: this is a gameplay wait and it freezes with the pause menu, the same
    // way the animation it stands in for does. cancelImmediately so that the finally runs inside
    // Cancel() itself, not a frame later — a late finally could hand back the input of a climb
    // that has already been replaced by another one.

    private async UniTaskVoid EnterAsync(CancellationToken token)
    {
        PlayerStateManager p = ResolvePlayer();
        if (p == null) return;

        // Visible and immobilized for the length of the climb-in. This is the window the Nemesis's
        // "I saw you climb in" rule reads — see the class summary.
        transitioning = true;
        p.SetHidingTransition(true);
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(data.EnterDuration),
                                cancellationToken: token, cancelImmediately: true);
        }
        finally
        {
            // Whatever ended the wait — the timer, or a cancel from a capture, a respawn, a
            // cinematic or the spot going away — the climb is over and the input comes back.
            transitioning = false;
            if (p != null) p.SetHidingTransition(false);
        }

        // A cancel throws out of the await above and never gets here. What can still have changed
        // while the animation played: a cinematic that immobilized the player, a scene change, or
        // another spot that claimed them first.
        if (p == null || !p.isActiveAndEnabled || p.IsImmobilized || Occupied != null) return;

        FreezeBodyAt(p, InteriorPose);
        RaiseInteriorCamera();
        ApplyInteriorMix();

        Occupied = this;
        p.SetHidingSpot(this);

        // The crosshair cannot be relied on from inside the box it is standing in, so E is pinned
        // to this spot for as long as the player is in it. The same mechanism PushableBox uses.
        if (InteractionManager.Exists) InteractionManager.Instance.SetForcedInteractable(this);

        HidingEvents.Entered(this);
    }

    private async UniTaskVoid LeaveAsync(CancellationToken token)
    {
        PlayerStateManager p = ResolvePlayer();

        ReleaseInternal(p, exitPose != null ? exitPose : ApproachPoint);
        if (p == null) return;

        // Visible and immobilized for the climb-out, the same way as on the way in.
        transitioning = true;
        p.SetHidingTransition(true);
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(data.ExitDuration),
                                cancellationToken: token, cancelImmediately: true);
        }
        finally
        {
            transitioning = false;
            if (p != null) p.SetHidingTransition(false);
        }
    }

    /// <summary>
    /// Hands the body back with no animation and no waiting. Every way out that is not the player
    /// pressing E lands here: a capture, a respawn, a cinematic, the scene unloading.
    /// </summary>
    private void ReleaseImmediate(Transform landing = null)
    {
        // A climb cut short: cancelling runs its finally right here (cancelImmediately), which is
        // what unlocks the input of a player caught half way into a locker.
        if (climbCts != null && !climbCts.IsCancellationRequested) climbCts.Cancel();

        // No landing by default: a respawn has already moved the player, a cinematic owns them, and
        // a climb cut short never put them inside. The one exception is a capture from INSIDE —
        // see HandlePlayerCaptured.
        ReleaseInternal(ResolvePlayer(), landing);
    }

    private void OnDestroy()
    {
        climbCts?.Dispose();
        climbCts = null;
    }

    private void ReleaseInternal(PlayerStateManager p, Transform landing)
    {
        if (!IsOccupied) return;

        Occupied = null;

        if (InteractionManager.Exists) InteractionManager.Instance.ClearForcedInteractable(this);
        DropInteriorCamera();
        RestoreOutsideMix();

        if (p != null)
        {
            // Cleared BEFORE anything else touches the player: PlayerHiddenState reads IsHidden to
            // know it is done, and the capture stand-up (EStandUp.AfterCapture) always puts the
            // player back on their feet — it must never start with the player still marked hidden.
            p.SetHidingSpot(null);
            p.SetHidingTransition(false);
            UnfreezeBody(p, landing);
        }

        HidingEvents.Exited(this);
    }

    private void HandlePlayerCaptured(PlayerStateManager captured)
    {
        // Caught INSIDE — the Nemesis pulled them out (NemesisCatchState's pull-out phase): they are
        // put back down outside the prop, at the exit pose. Left where the grab found them, the body
        // goes dynamic again inside the prop's own solid collider and PhysX shoots it out, possibly
        // through the wall behind. Before phase 2 nothing could capture a hidden player, so nothing
        // ever hit this.
        if (IsOccupied)
        {
            ReleaseImmediate(exitPose != null ? exitPose : ApproachPoint);
            return;
        }

        // Mid-climb counts too: grabbed half way into the locker, the entry must not complete and
        // hide a player who is already in the monster's hands.
        if (IsTransitioning) ReleaseImmediate();
    }

    private void HandleRespawned(Checkpoint checkpoint)
    {
        // Spec §6: a respawn never puts the player back inside a spot. Belt and braces — the
        // capture that caused the respawn has already released it.
        if (IsOccupied || IsTransitioning) ReleaseImmediate();
    }

    // ── Body ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the player at the interior pose and takes the body out of the simulation.
    ///
    /// KINEMATIC, not "disable the collider". The prop's own collider is solid and on Default, so a
    /// dynamic Rigidbody standing inside it is depenetrated straight back out — and the collider
    /// has to STAY enabled, because phase 2 reaches the player THROUGH exactly those colliders for
    /// the proximity test and the grab (plan §3.3). Kinematic freezes the pose without touching
    /// either.
    /// </summary>
    private void FreezeBodyAt(PlayerStateManager p, Transform pose)
    {
        // Teleport FIRST, while the body is still dynamic: TeleportTo zeroes the velocity, and
        // writing velocity to a kinematic body is unsupported (PhysX warns). Both happen in the
        // same frame, so no simulation step sees the body inside the prop while still dynamic.
        p.TeleportTo(pose.position, pose.rotation);

        if (p.RigBody != null)
        {
            cachedKinematic = p.RigBody.isKinematic;
            p.RigBody.isKinematic = true;
        }
    }

    private void UnfreezeBody(PlayerStateManager p, Transform landing)
    {
        // Dynamic again BEFORE the teleport, for the same reason as above.
        if (p.RigBody != null) p.RigBody.isKinematic = cachedKinematic;

        if (landing != null)
        {
            p.TeleportTo(landing.position, landing.rotation);
        }
        else if (p.RigBody != null && !p.RigBody.isKinematic)
        {
            // No landing (a capture): nothing zeroed the momentum, and a body that comes back
            // dynamic must not inherit whatever it was doing when it climbed in.
            p.RigBody.linearVelocity = Vector3.zero;
            p.RigBody.angularVelocity = Vector3.zero;
        }
    }

    // ── Camera ──────────────────────────────────────────────────────────────

    private void RaiseInteriorCamera()
    {
        if (interiorCamera == null) return;
        cachedCameraPriority = interiorCamera.Priority;
        interiorCamera.Priority = interiorCameraPriority;
        interiorCamera.enabled = true;
    }

    private void DropInteriorCamera()
    {
        if (interiorCamera == null) return;
        // Disabled and not just demoted — see Awake. The brain blends back to the rig either way.
        interiorCamera.enabled = false;
        interiorCamera.Priority = cachedCameraPriority;
    }

    // ── Mix ─────────────────────────────────────────────────────────────────
    //
    // Next to the camera on purpose: the ear goes inside with the eye, and comes back out on
    // exactly the same paths — a capture or a scene unload that restored the camera and left the
    // world lowpassed would be heard for the rest of the run.
    //
    // Only the lowpass cutoffs live in these snapshots. The bus VOLUMES are exposed parameters the
    // settings sliders own through AudioManager, and a snapshot cannot move an exposed parameter
    // once SetFloat has written it — so nothing here fights the player's volume settings.

    private bool mixApplied;

    private void ApplyInteriorMix()
    {
        AudioMixerSnapshot inside = data.SnapshotFor(type);
        if (inside == null) return;

        inside.TransitionTo(data.SnapshotTransitionSeconds);
        mixApplied = true;
    }

    private void RestoreOutsideMix()
    {
        if (!mixApplied) return;
        mixApplied = false;

        if (data.OutsideSnapshot != null) data.OutsideSnapshot.TransitionTo(data.SnapshotTransitionSeconds);
    }

    // ── Burning (phase 6) ───────────────────────────────────────────────────

    /// <summary>
    /// The Nemesis has torn this spot apart for good (plan §3.6, D2). Nothing calls it yet — what
    /// decides WHEN is phase 6, and the broken model is art. The EFFECT lands here now so that
    /// phase 6 is a caller and not a redesign.
    ///
    /// The spot is not switched off by invisible bookkeeping: whoever calls this is expected to
    /// have played the animation that explains it, or to have left the door lying on the floor.
    /// </summary>
    public void Burn()
    {
        if (burned) return;
        burned = true;

        if (IsOccupied) ReleaseImmediate();

        InteractionEvents.RequestPromptRefresh();
        HidingEvents.SpotBurned(this);
    }

    // ── Queries ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether <paramref name="c"/> is one of this prop's own colliders. Phase 2's fix for the
    /// total-immunity bug (plan §3.3) walks the raycast hits and skips the ones the OCCUPIED spot
    /// owns, instead of moving the prop to another layer — it still has to block vision, hearing
    /// and the camera for everyone else.
    /// </summary>
    public bool OwnsCollider(Collider c)
    {
        if (c == null || ownColliders == null) return false;
        for (int i = 0; i < ownColliders.Length; i++)
            if (ReferenceEquals(ownColliders[i], c)) return true;
        return false;
    }

    // One buffer for every spot: line tests run one at a time on the main thread, and one ray
    // through one prop plus whatever stands behind it returns a handful of hits, not thirty-two.
    private static readonly RaycastHit[] lineHits = new RaycastHit[32];

    /// <summary>
    /// Whether anything on <paramref name="mask"/> OTHER than this spot's own colliders stands
    /// between the two points. The Nemesis's side of the total-immunity fix (plan §3.3): its
    /// proximity test, its grab and what it can still make out through the slats ask this instead
    /// of a plain raycast while the player is inside, so the shell stops being a wall between the
    /// monster and the person in it — and stays one for everyone else, and for every other
    /// question. Triggers never block, same as every other line test in the project.
    /// </summary>
    public bool IsLineBlockedIgnoringSelf(Vector3 from, Vector3 to, LayerMask mask)
    {
        Vector3 toPoint = to - from;
        float distance = toPoint.magnitude;
        if (distance <= 0.0001f) return false;

        int count = Physics.RaycastNonAlloc(from, toPoint / distance, lineHits, distance, mask,
                                            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!OwnsCollider(lineHits[i].collider)) return true;

        return false;
    }

    private PlayerStateManager ResolvePlayer()
    {
        if (player == null) player = PlayerRegistry.Current;
        return player;
    }

    private void CacheOwnColliders() => ownColliders = GetComponentsInChildren<Collider>(true);

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Kept in step automatically: a designer who adds the door mesh's collider later must not
        // have to remember to press a button, or phase 2's immunity fix silently misses it.
        //
        // The Spot Id is deliberately NOT auto-filled. Anything generated here runs on the prefab
        // asset too, and every instance would inherit the same id — the exact duplicate the
        // validator exists to catch. It is authored per instance, like a [PuzzleId].
        CacheOwnColliders();
    }

    private void OnDrawGizmosSelected()
    {
        if (interiorPose != null)
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(interiorPose.position, 0.3f);
            Gizmos.DrawRay(interiorPose.position, interiorPose.forward * 0.8f);
        }

        if (approachPoint != null)
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(approachPoint.position, 0.3f);
            // The gap the Nemesis's 1 m grab reach has to cover.
            if (interiorPose != null) Gizmos.DrawLine(approachPoint.position, interiorPose.position);
        }

        if (exitPose != null)
        {
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(exitPose.position, 0.25f);
        }
    }
#endif
}
