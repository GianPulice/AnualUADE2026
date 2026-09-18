using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Pulls the camera away from the player and widens the lens while sprinting, so the extra speed
/// reads on screen and not only in the run animation — and narrows the lens while crouching.
///
/// Drives <c>CinemachineOrbitalFollow.RadialAxis</c>, which Cinemachine applies as a plain
/// multiplier over the orbit spline. Scaling <c>Orbits</c> instead would look identical but
/// invalidates Cinemachine's spline cache, and rebuilding it allocates three arrays — every frame,
/// for an effect that changes every frame.
///
/// This is the only thing that writes the lens FOV after Start, and it has to stay that way: it
/// writes every frame, so a second component setting the FOV would just be overwritten. The walk,
/// sprint and crouch FOVs themselves live in <see cref="SO_CameraConfig"/>, read through the
/// <see cref="PlayerCameraController"/> on the same rig.
///
/// Place this on the same GameObject as the CinemachineOrbitalFollow (the player's camera rig).
/// </summary>
[RequireComponent(typeof(CinemachineOrbitalFollow))]
public class CameraSprintEffect : MonoBehaviour
{
    [Header("Pull back")]
    [Tooltip("Camera distance while sprinting, as a multiple of the normal orbit distance. " +
             "1 disables the pull back.")]
    [SerializeField, Min(1f)] private float _sprintDistance = 1.25f;

    [Header("Response")]
    [Tooltip("Roughly how long the camera takes to settle into the sprint framing.")]
    [SerializeField, Min(0f)] private float _easeInTime = 0.3f;

    [Tooltip("Roughly how long the camera takes to settle back once sprint is released. Slower " +
             "than the ease in, otherwise stopping reads as a snap.")]
    [SerializeField, Min(0f)] private float _easeOutTime = 0.5f;

    private CinemachineOrbitalFollow _orbital;
    private CinemachineCamera _camera;
    private PlayerCameraController _controller;
    private PlayerStateManager _player;

    private float _sprint01;
    private float _sprintVelocity;

    private float _crouch01;
    private float _crouchVelocity;

    private void Awake()
    {
        _orbital    = GetComponent<CinemachineOrbitalFollow>();
        _camera     = GetComponent<CinemachineCamera>();
        _controller = GetComponent<PlayerCameraController>();
    }

    // The registry rather than GetComponentInParent: the rig happens to be parented under the
    // player today, but Cinemachine cameras are routinely pulled out of the character hierarchy
    // and this keeps working if that happens.
    private void OnEnable()  => PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
    private void OnDisable() => PlayerRegistry.Unsubscribe(HandlePlayerRegistered);

    private void HandlePlayerRegistered(PlayerStateManager player) => _player = player;

    private void LateUpdate()
    {
        SO_CameraConfig config = _controller != null ? _controller.Config : null;

        // SpeedMultiplier is the runtime truth for sprinting: PlayerMovingState raises it above 1
        // only while the sprint button is held, and crouching drives it below 1. Reading the state
        // instead of the button keeps this component out of the input layer, and the framing stays
        // put in the states that ignore sprint (crouch, hidden, interacting, disabled).
        bool sprinting = _player != null && _player.SpeedMultiplier > 1.01f;
        float sprintTarget = sprinting ? 1f : 0f;

        // IsCrouch and not SpeedMultiplier, same flag PlayerCameraController dips the pivot on, so
        // the zoom and the dip start on the same frame and use the same damping.
        bool crouching = _player != null && _player.IsCrouch;
        float crouchTarget = crouching ? 1f : 0f;
        float crouchTime = config != null ? config.CrouchPivotDamping : _easeInTime;

        // Scaled deltaTime on purpose: while paused the framing holds wherever it was.
        _sprint01 = Ease(_sprint01, sprintTarget, ref _sprintVelocity,
                         sprinting ? _easeInTime : _easeOutTime);
        _crouch01 = Ease(_crouch01, crouchTarget, ref _crouchVelocity, crouchTime);

        ApplyFraming(config);
    }

    private static float Ease(float current, float target, ref float velocity, float time)
    {
        current = Mathf.SmoothDamp(current, target, ref velocity, time, Mathf.Infinity, Time.deltaTime);

        // SmoothDamp only ever approaches its target, so settle it by hand. Besides keeping the
        // rig from idling a hair away from its authored framing, this stops the radial writes:
        // Cinemachine reads a changing axis value as user input, so a value that never quite
        // arrives would hold the axis in "being touched" forever and suppress auto-recentering
        // if the rig is ever configured to use it (all three axes have it off today).
        if (Mathf.Abs(target - current) < 0.001f)
        {
            current = target;
            velocity = 0f;
        }

        return current;
    }

    private void ApplyFraming(SO_CameraConfig config)
    {
        // The radial axis clamps itself to its own Range, so a rig left at the default [1, 1]
        // would silently swallow the whole effect.
        _orbital.RadialAxis.Range.x = Mathf.Min(_orbital.RadialAxis.Range.x, 1f);
        _orbital.RadialAxis.Range.y = Mathf.Max(_orbital.RadialAxis.Range.y, _sprintDistance);
        _orbital.RadialAxis.Value   = Mathf.Lerp(1f, _sprintDistance, _sprint01);

        // Without a config there is nothing to ease between, so the lens is left as it is.
        if (_camera == null || config == null) return;

        // Walk is the resting FOV and sprint / crouch are pulls away from it. Summed rather than
        // lerped in sequence because both can be partway at once — going straight from a sprint
        // into a crouch eases one out while the other eases in, with no jump in between.
        // Read off the asset every frame so the three FOVs can be tuned live in Play mode.
        float walk = config.WalkFov;
        _camera.Lens.FieldOfView = walk
                                 + (config.SprintFov - walk) * _sprint01
                                 + (config.CrouchFov - walk) * _crouch01;
    }
}
