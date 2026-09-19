using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class PlayerCameraController : MonoBehaviour
{
    [Tooltip("Everything tuneable about this rig lives in here: FOV, shoulder offset, look " +
             "limits and how far the camera drops when the player crouches.")]
    [SerializeField] private SO_CameraConfig cameraConfig;

    private CinemachineCamera cinemachineCamera;
    private CinemachineOrbitalFollow cinemachineOrbitalFollow;
    private CinemachineRotationComposer cinemachineRotationComposer;
    private CinemachineInputAxisController cinemachineInputAxisController;
    private WakeUpCameraPan wakeUpPan;

    // The transform the rig orbits around and aims at ("Placeholder forward direction"). It sits
    // at standing head height and never moved, which is the whole bug this dip fixes: crouching
    // only shrank the capsule, so the camera kept framing a point well above the crouched
    // character. Under a container that point is inside the geometry, the Deoccluder cannot find
    // a clear shot to it, and the player disappears behind the crate instead of being followed.
    private Transform pivot;
    private float standingPivotHeight;
    private float pivotVelocity;

    private PlayerStateManager player;

    /// <summary>The config this rig is authored from. CameraSprintEffect reads its FOVs here.</summary>
    public SO_CameraConfig Config => cameraConfig;

    // The registry rather than GetComponentInParent: the rig is parented under the player today,
    // but the same lookup keeps working if it is ever pulled out of the character hierarchy.
    private void OnEnable()  => PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
    private void OnDisable() => PlayerRegistry.Unsubscribe(HandlePlayerRegistered);

    private void HandlePlayerRegistered(PlayerStateManager registered) => player = registered;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
        cinemachineOrbitalFollow = GetComponent<CinemachineOrbitalFollow>();
        cinemachineRotationComposer = GetComponent<CinemachineRotationComposer>();
        cinemachineInputAxisController = GetComponent<CinemachineInputAxisController>();
        wakeUpPan = GetComponent<WakeUpCameraPan>();
        AplyConfig();
        CachePivot();
    }

    void Update()
    {
        // Ahead of the input guard below on purpose: the pivot has to keep tracking the stance
        // even on a rig without an input controller.
        UpdateCrouchPivot();
        UpdateShoulderOffset();

        // When a modal UI is open or the game is paused, we do not read camera input. Nor while the
        // wake-up cinematic drives the rig (WakeUpCameraPan): the player's mouse would fight the pan.
        if (cinemachineInputAxisController == null) return;
        bool gameplayActive = !PauseManager.IsGameplayInputBlocked;
        // Nor while the player gets up off the floor: nothing but pause until the clip ends.
        bool standingUp = PlayerRegistry.Current != null && PlayerRegistry.Current.IsStandingUp;
        bool shouldEnable = gameplayActive && !WakeUpCinematicEvents.IsCameraLocked && !standingUp;
        if (cinemachineInputAxisController.enabled != shouldEnable)
            cinemachineInputAxisController.enabled = shouldEnable;

        // Gameplay cursor: locked + invisible while there is no modal UI and no pause.
        // When there IS a UI open, the UIStateManager owns the cursor (it releases it),
        // which is why we only force it while gameplay input is active. Doing it every
        // frame also recovers the lock if the OS dropped it (alt-tab, click outside window).
        if (gameplayActive)
        {
            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible) Cursor.visible = false;
        }
    }

    void AplyConfig()
    {
        cinemachineCamera.Lens.FieldOfView = cameraConfig.WalkFov;
        cinemachineOrbitalFollow.VerticalAxis.Range = new Vector2(-cameraConfig.MaxVerticalAngle, cameraConfig.MaxVerticalAngle);
        Vector3 temp = cameraConfig.ShoulderOffset;
        cinemachineRotationComposer.TargetOffset.Set(temp.x, temp.y, temp.z);
    }

    /// <summary>
    /// The wake-up cinematic frames the player from straight behind, not over the shoulder, and
    /// hands the shoulder offset back gradually (<see cref="WakeUpCameraPan.ShoulderWeight"/>).
    /// Only on a rig that has the pan; any other rig keeps the offset AplyConfig set once.
    /// </summary>
    private void UpdateShoulderOffset()
    {
        if (wakeUpPan == null || cinemachineRotationComposer == null || cameraConfig == null) return;
        cinemachineRotationComposer.TargetOffset = cameraConfig.ShoulderOffset * wakeUpPan.ShoulderWeight;
    }

    /// <summary>
    /// Reads the standing height straight off the rig instead of hardcoding it, so re-authoring
    /// the pivot in the prefab does not silently desync the crouched height.
    /// </summary>
    private void CachePivot()
    {
        pivot = cinemachineCamera != null ? cinemachineCamera.Follow : null;

        if (pivot == null)
        {
            Debug.LogWarning($"[{nameof(PlayerCameraController)}] '{name}' has no Tracking Target " +
                             $"on its {nameof(CinemachineCamera)}. The camera will not dip when " +
                             $"the player crouches.", this);
            return;
        }

        standingPivotHeight = pivot.localPosition.y;
    }

    /// <summary>
    /// Slides the tracking pivot between the standing and crouched heights. Moving the target
    /// itself and not <c>OrbitalFollow.TargetOffset</c> on purpose: the rig has no separate
    /// LookAt target, so the same transform is both the orbit centre and the aim point, and
    /// offsetting only the follow would drop the camera while it kept aiming at the old height.
    /// </summary>
    private void UpdateCrouchPivot()
    {
        if (pivot == null || cameraConfig == null) return;

        // Read off the asset every frame instead of caching in Start: it costs nothing and it
        // means Crouch Pivot Drop can be dragged in the inspector during Play mode and framed by
        // eye against the real containers, which is the only sane way to pick that number.
        float drop = cameraConfig.CrouchPivotDrop;
        float damping = cameraConfig.CrouchPivotDamping;

        // IsCrouch and not the FSM state: the flag is the input truth and flips on the same frame
        // as the keypress, while the state transition lands one frame later. Interacting states
        // clear the flag themselves, so the pivot comes back up with them.
        bool crouching = player != null && player.IsCrouch;
        float target = crouching ? standingPivotHeight - drop : standingPivotHeight;

        Vector3 local = pivot.localPosition;

        // The wake-up cinematic raises the pivot from the floor to the head on its own timing:
        // placed exactly, no damping, or the rise would lag behind a pan synced to the voice line.
        if (wakeUpPan != null && wakeUpPan.TryGetPivotOffset(out float wakeUpOffset))
        {
            local.y = standingPivotHeight + wakeUpOffset;
            pivotVelocity = 0f;
            pivot.localPosition = local;
            return;
        }

        // Scaled deltaTime on purpose: while paused the framing holds wherever it was, same as
        // CameraSprintEffect.
        local.y = Mathf.SmoothDamp(local.y, target, ref pivotVelocity, damping,
                                   Mathf.Infinity, Time.deltaTime);

        // SmoothDamp only ever approaches its target, so settle it by hand — otherwise the rig
        // idles a hair off the authored height forever.
        if (Mathf.Abs(target - local.y) < 0.001f)
        {
            local.y = target;
            pivotVelocity = 0f;
        }

        pivot.localPosition = local;

        // Written in Update and not LateUpdate because CinemachineBrain drives the rig from its
        // own LateUpdate: setting it here means the dip is already in place when the brain reads
        // the target, instead of showing up one frame late.
    }
}
